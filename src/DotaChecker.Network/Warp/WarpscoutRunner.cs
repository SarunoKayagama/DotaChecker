using System.Diagnostics;
using System.IO;
using DotaChecker.Core.Warp;

namespace DotaChecker.Network.Warp;

/// <summary>Запуск WARPSCOUT как внешнего процесса; не переносит Go-код и не трогает сеть сам.</summary>
public sealed class WarpscoutRunner
{
    private readonly WarpscoutReportParser _parser = new();

    public async Task<WarpscoutResult> RunAsync(
        WarpscoutConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var binaryPath = ResolveBinary(configuration.BinaryPath);
        if (string.IsNullOrWhiteSpace(binaryPath) || !File.Exists(binaryPath))
        {
            return Failure(WarpscoutExecutionStatus.BinaryNotFound, "Указанный warpscout.exe не найден.", started);
        }

        var version = await GetVersionAsync(binaryPath, cancellationToken).ConfigureAwait(false);

        var arguments = configuration.BuildArguments().ToArray();
        var execution = await ExecuteAsync(
                binaryPath,
                arguments,
                configuration.TimeoutSeconds,
                cancellationToken)
            .ConfigureAwait(false);

        if (execution.Status != WarpscoutExecutionStatus.Success)
        {
            return new WarpscoutResult(
                execution.Status,
                started,
                version,
                Array.Empty<WarpscoutEndpoint>(),
                configuration.ReportPath ?? string.Empty,
                configuration.ConfigPath ?? string.Empty,
                execution.Error,
                DateTimeOffset.UtcNow - started,
                execution.Log);
        }

        var reportText = execution.StandardOutput;
        if (!string.IsNullOrWhiteSpace(configuration.ReportPath) && File.Exists(configuration.ReportPath))
        {
            reportText = await File.ReadAllTextAsync(configuration.ReportPath).ConfigureAwait(false);
        }

        var parsed = _parser.Parse(reportText, version);
        if (!parsed.IsRecognized)
        {
            return new WarpscoutResult(
                WarpscoutExecutionStatus.ParseFailed,
                started,
                version,
                Array.Empty<WarpscoutEndpoint>(),
                configuration.ReportPath ?? string.Empty,
                configuration.ConfigPath ?? string.Empty,
                string.Join("; ", parsed.Warnings),
                DateTimeOffset.UtcNow - started,
                execution.Log);
        }

        return new WarpscoutResult(
            WarpscoutExecutionStatus.Success,
            started,
            version,
            parsed.Endpoints,
            configuration.ReportPath ?? string.Empty,
            configuration.ConfigPath ?? string.Empty,
            string.Join("; ", parsed.Warnings),
            DateTimeOffset.UtcNow - started,
            execution.Log);
    }

    public async Task<string> GetVersionAsync(string binaryPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(binaryPath))
        {
            return string.Empty;
        }

        var execution = await ExecuteAsync(binaryPath, new[] { "version" }, 15, cancellationToken).ConfigureAwait(false);
        if (execution.Status != WarpscoutExecutionStatus.Success)
        {
            return string.Empty;
        }

        return execution.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0) ?? string.Empty;
    }

    private static async Task<ProcessExecution> ExecuteAsync(
        string binaryPath,
        IReadOnlyList<string> arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var binaryDirectory = System.IO.Path.GetDirectoryName(binaryPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        // WARPSCOUT ищет warpscout-account.json рядом со своим .exe.
        if (!string.IsNullOrWhiteSpace(binaryDirectory))
        {
            startInfo.WorkingDirectory = binaryDirectory;
        }
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new ProcessExecution(WarpscoutExecutionStatus.Failed, string.Empty, "Не удалось запустить процесс.", string.Empty);
            }

            // Параллельное чтение исключает deadlock при полном буфере stdout/stderr.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return new ProcessExecution(WarpscoutExecutionStatus.Timeout, string.Empty, "Таймаут WARPSCOUT.", string.Empty);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                return new ProcessExecution(WarpscoutExecutionStatus.Cancelled, string.Empty, "Операция отменена.", string.Empty);
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var log = $"exit={process.ExitCode}{Environment.NewLine}{stderr}";
            var status = process.ExitCode == 0
                ? WarpscoutExecutionStatus.Success
                : WarpscoutExecutionStatus.Failed;
            return new ProcessExecution(status, stdout, stderr, log);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return new ProcessExecution(WarpscoutExecutionStatus.Failed, string.Empty, exception.Message, string.Empty);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Процесс уже завершён.
        }
    }

    private static string ResolveBinary(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return Directory.Exists(path) ? Path.Combine(path, "warpscout.exe") : path;
    }

    private static WarpscoutResult Failure(WarpscoutExecutionStatus status, string error, DateTimeOffset started) =>
        new(status, started, string.Empty, Array.Empty<WarpscoutEndpoint>(), string.Empty, string.Empty, error,
            DateTimeOffset.UtcNow - started, error);

    private sealed record ProcessExecution(
        WarpscoutExecutionStatus Status,
        string StandardOutput,
        string Error,
        string Log);
}
