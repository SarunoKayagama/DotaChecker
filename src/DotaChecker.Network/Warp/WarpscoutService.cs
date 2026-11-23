using System.IO;
using DotaChecker.Core.Configuration;
using DotaChecker.Core.Warp;

namespace DotaChecker.Network.Warp;

/// <summary>Сервис запуска WARPSCOUT и хранения последнего результата. Не знает о UI.</summary>
public sealed class WarpscoutService
{
    private readonly WarpscoutRunner _runner = new();
    private readonly AppSettings _settings;

    public WarpscoutService(AppSettings settings) => _settings = settings;

    public event Action<WarpscoutResult>? ResultPublished;

    public WarpscoutResult? LastResult { get; private set; }

    public WarpscoutConfiguration BuildConfiguration(WarpscoutConfiguration? overrideConfiguration = null)
    {
        var configuration = overrideConfiguration ?? new WarpscoutConfiguration();
        if (string.IsNullOrWhiteSpace(configuration.BinaryPath))
        {
            configuration = configuration with { BinaryPath = _settings.WarpscoutBinaryPath };
        }

        if (Directory.Exists(configuration.BinaryPath))
        {
            configuration = configuration with
            {
                BinaryPath = Path.Combine(configuration.BinaryPath, "warpscout.exe"),
            };
        }

        if (configuration.TimeoutSeconds <= 0)
        {
            configuration = configuration with { TimeoutSeconds = _settings.WarpscoutTimeoutSeconds };
        }

        // Консольный вывод WARPSCOUT — таблица с box drawing; стабильный формат
        // находится в report-файле. Всегда пишем отчёт и парсим его.
        if (string.IsNullOrWhiteSpace(configuration.ReportPath))
        {
            var directory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Dota Checker",
                "warpscout");
            System.IO.Directory.CreateDirectory(directory);
            configuration = configuration with
            {
                ReportPath = System.IO.Path.Combine(
                    directory,
                    $"warpscout-report-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.txt"),
            };
        }

        if (string.IsNullOrWhiteSpace(configuration.ConfigPath))
        {
            var configDirectory = _settings.AmneziaWgConfigDirectory;
            if (string.IsNullOrWhiteSpace(configDirectory))
            {
                configDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Dota Checker",
                    "WarpProfiles");
            }

            Directory.CreateDirectory(configDirectory);
            configuration = configuration with
            {
                ConfigPath = Path.Combine(
                    configDirectory,
                    $"warpscout-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.conf"),
            };
        }

        return configuration;
    }

    public async Task<WarpscoutResult> ScanAsync(
        WarpscoutConfiguration? overrideConfiguration = null,
        CancellationToken cancellationToken = default)
    {
        var configuration = BuildConfiguration(overrideConfiguration);
        var result = await _runner.RunAsync(configuration, cancellationToken).ConfigureAwait(false);
        LastResult = result;
        ResultPublished?.Invoke(result);
        return result;
    }

    public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) =>
        _runner.GetVersionAsync(_settings.WarpscoutBinaryPath, cancellationToken);
}
