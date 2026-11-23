using DotaChecker.Core.Configuration;

namespace DotaChecker.Tests.Configuration;

public sealed class SettingsManagerTests : IDisposable
{
    private readonly string _directory;

    public SettingsManagerTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "dota-checker-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void NewManager_WithoutFile_UsesDefaults()
    {
        var manager = CreateManager();

        manager.Settings.Theme.Should().Be(ThemeMode.System);
        manager.Settings.ProbeIntervalMs.Should().Be(1000);
        manager.Settings.TcpProbeTimeoutMs.Should().BeGreaterThanOrEqualTo(3000);
    }

    [Fact]
    public void SaveThenReload_PreservesValues()
    {
        var manager = CreateManager();
        manager.Settings.Theme = ThemeMode.Dark;
        manager.Settings.ProbeIntervalMs = 750;
        manager.Settings.MaxConcurrentProbes = 4;
        manager.Save();

        var reloaded = CreateManager();

        reloaded.Settings.Theme.Should().Be(ThemeMode.Dark);
        reloaded.Settings.ProbeIntervalMs.Should().Be(750);
        reloaded.Settings.MaxConcurrentProbes.Should().Be(4);
    }

    [Fact]
    public void CorruptedFile_FallsBackToDefaults()
    {
        var manager = CreateManager();
        File.WriteAllText(manager.SettingsPath, "{ this is not json");

        var reloaded = CreateManager();

        reloaded.Settings.ProbeIntervalMs.Should().Be(1000);
    }

    [Fact]
    public void Reset_RestoresDefaultsAndSaves()
    {
        var manager = CreateManager();
        manager.Settings.ProbeIntervalMs = 100;
        manager.Save();

        manager.Reset();

        manager.Settings.ProbeIntervalMs.Should().Be(1000);
        CreateManager().Settings.ProbeIntervalMs.Should().Be(1000);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private SettingsManager CreateManager() =>
        new(Path.Combine(_directory, "settings.json"));
}
