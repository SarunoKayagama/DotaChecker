using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using DotaChecker.Core.Warp;

namespace DotaChecker.Network.Warp;

/// <summary>
/// Только безопасные операции: поиск профилей, разбор конфигурации, чтение состояния.
/// Установка/удаление туннелей, изменение маршрутов, DNS и правил не выполняется.
/// </summary>
public sealed class AmneziaWgController
{
    private readonly AmneziaWgConfigParser _parser = new();

    public string AwgExecutablePath { get; init; } =
        @"C:\Program Files\AmneziaWG\awg.exe";

    public string ManagerExecutablePath { get; init; } =
        @"C:\Program Files\AmneziaWG\amneziawg.exe";

    public static bool IsElevated =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);

    public bool ComponentsPresent =>
        File.Exists(AwgExecutablePath) || File.Exists(ManagerExecutablePath);

    public IReadOnlyList<AmneziaWgProfileInfo> FindProfiles(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Array.Empty<AmneziaWgProfileInfo>();
        }

        return Directory.GetFiles(directory, "*.conf", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(_parser.Parse)
            .ToArray();
    }

    public AmneziaWgProfileInfo ReadProfile(string path) => _parser.Parse(path);

    public async Task<AmneziaWgTunnelStatus> GetTunnelStatusAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var serviceState = ReadServiceState(name);
        if (!IsElevated)
        {
            return new AmneziaWgTunnelStatus(
                name, AmneziaWgTunnelState.PermissionDenied, serviceState, string.Empty, string.Empty,
                "awg show требует прав администратора. Туннель не изменялся.");
        }

        if (!File.Exists(AwgExecutablePath))
        {
            return new AmneziaWgTunnelStatus(
                name, AmneziaWgTunnelState.Error, serviceState, string.Empty, string.Empty, "awg.exe не найден.");
        }

        var execution = await RunReadOnlyAsync(AwgExecutablePath, new[] { "show", name }, cancellationToken)
            .ConfigureAwait(false);
        if (!execution.Success)
        {
            return new AmneziaWgTunnelStatus(
                name, AmneziaWgTunnelState.Error, serviceState, string.Empty, string.Empty, execution.Error);
        }

        var handshake = FindValue(execution.Output, "latest handshake");
        var transfer = FindValue(execution.Output, "transfer");
        var state = serviceState.Contains("Running", StringComparison.OrdinalIgnoreCase)
            ? AmneziaWgTunnelState.Running
            : AmneziaWgTunnelState.Stopped;

        return new AmneziaWgTunnelStatus(name, state, serviceState, handshake, transfer, string.Empty);
    }

    /// <summary>Описание будущей операции без её выполнения.</summary>
    public AmneziaWgOperationPlan BuildActivationPlan(string profilePath) =>
        new AmneziaWgOperationPlan(
            profilePath,
            true,
            true,
            new[]
            {
                $"{ManagerExecutablePath} /installtunnelservice \"{profilePath}\"",
                $"sc start AmneziaWGTunnel${System.IO.Path.GetFileNameWithoutExtension(profilePath)}",
            },
            "Только план: команды не выполняются DotaChecker.");

    private static string ReadServiceState(string name)
    {
        try
        {
            using var controller = new ServiceController($"AmneziaWGTunnel${name}");
            return controller.Status.ToString();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return "NotFound";
        }
    }

    private static string FindValue(string output, string marker)
    {
        foreach (var line in output.Split('\n'))
        {
            var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                return line.Trim();
            }
        }

        return string.Empty;
    }

    private static async Task<(bool Success, string Output, string Error)> RunReadOnlyAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return (process.ExitCode == 0, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return (false, string.Empty, exception.Message);
        }
    }
}

public sealed record AmneziaWgOperationPlan(
    string ProfilePath,
    bool RequiresUserConfirmation,
    bool RequiresElevation,
    IReadOnlyList<string> Commands,
    string Explanation);
