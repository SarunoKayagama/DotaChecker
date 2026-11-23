using DotaChecker.Core.Configuration;
using DotaChecker.Core.Warp;
using DotaChecker.Network.Warp;

namespace DotaChecker.Tests.Warp;

public sealed class WarpscoutTests
{
    private readonly WarpscoutReportParser _parser = new();

    [Fact]
    public void ConfigBuilder_UsesSupportedFlags()
    {
        var config = new WarpscoutConfiguration
        {
            BinaryPath = "warpscout.exe",
            Protocol = "awg",
            TunnelPingCheck = true,
            BestOnly = true,
            ExcludeNode = "DME",
            Country = "DE,NL",
            ReportPath = "report.txt",
            ConfigPath = "warp.conf",
        };

        var arguments = config.BuildArguments();

        arguments.Should().Contain(new[] { "scan", "-p", "awg", "-P", "-exclude-node", "DME", "-country", "DE,NL", "-best", "-conf", "warp.conf", "-table-off", "-o", "report.txt", "-no-dns" });
    }

    [Fact]
    public void Parser_ReadsKnownReport()
    {
        const string report = """
            # WARP endpoints: 1 working / 2 probed
            ENDPOINT               ENDPOINT PING  TUN PING  LOSS    SEEN AS      NODE  NODE LOCATION
            188.114.98.58:2408     12ms           8ms       0%      Germany      FRA   Frankfurt, Germany
            162.159.192.1:2408     bad            -         -       Russia       DME   Moscow, Russia
            """;

        var result = _parser.Parse(report, "0.16.1");

        result.IsRecognized.Should().BeTrue();
        result.Endpoints.Should().HaveCount(2);
        result.Endpoints[0].EndpointPingMs.Should().Be(12);
        result.Endpoints[0].TunnelPingMs.Should().Be(8);
        result.Endpoints[0].LossPct.Should().Be(0);
        result.Endpoints[0].Node.Should().Be("FRA");
        result.Endpoints[1].EndpointPingMs.Should().BeNull();
    }

    [Fact]
    public void Parser_EmptyAndUnknownFormat_AreNotRecognized()
    {
        _parser.Parse(string.Empty, "0.16.1").IsRecognized.Should().BeFalse();
        _parser.Parse("random output", "0.16.1").IsRecognized.Should().BeFalse();
    }

    [Fact]
    public void Parser_UnknownVersion_WarnsButStillParses()
    {
        const string report = """
            ENDPOINT               ENDPOINT PING  NODE
            188.114.98.58:2408     12ms           FRA
            """;

        var result = _parser.Parse(report, "9.9.9");

        result.IsRecognized.Should().BeTrue();
        result.Warnings.Should().Contain(warning => warning.Contains("не проверен", StringComparison.Ordinal));
    }

    [Fact]
    public void Service_AcceptsDirectoryAndCreatesReportPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dota-checker-warpcfg", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settings = new AppSettings
            {
                WarpscoutBinaryPath = directory,
                WarpscoutTimeoutSeconds = 120,
            };
            var service = new WarpscoutService(settings);

            var configuration = service.BuildConfiguration(new WarpscoutConfiguration { TimeoutSeconds = 0 });

            configuration.BinaryPath.Should().Be(Path.Combine(directory, "warpscout.exe"));
            configuration.TimeoutSeconds.Should().Be(120);
            configuration.ReportPath.Should().EndWith(".txt");
            configuration.ConfigPath.Should().EndWith(".conf");
            configuration.BuildArguments().Should().Contain(new[] { "-o", "-conf", "-table-off", "-no-dns" });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Runner_MissingBinary_ReturnsBinaryNotFound()
    {
        var runner = new WarpscoutRunner();
        var result = await runner.RunAsync(new WarpscoutConfiguration
        {
            BinaryPath = Path.Combine(Path.GetTempPath(), "missing-warpscout.exe"),
        });

        result.Status.Should().Be(WarpscoutExecutionStatus.BinaryNotFound);
        result.Endpoints.Should().BeEmpty();
    }

    [Fact]
    public async Task Runner_ProcessFailure_IsReported()
    {
        var runner = new WarpscoutRunner();
        var result = await runner.RunAsync(new WarpscoutConfiguration
        {
            BinaryPath = @"C:\Windows\System32\cmd.exe",
            TimeoutSeconds = 10,
            ReportPath = string.Empty,
        });

        result.Status.Should().BeOneOf(
            WarpscoutExecutionStatus.Failed,
            WarpscoutExecutionStatus.ParseFailed);
    }
}
