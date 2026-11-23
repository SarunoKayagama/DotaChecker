using DotaChecker.Core.Warp;
using DotaChecker.Network.Warp;

namespace DotaChecker.Tests.Warp;

public sealed class AmneziaWgTests : IDisposable
{
    private readonly string _directory;

    public AmneziaWgTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "dota-checker-awg", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Parser_ExtractsPublicInfoOnly()
    {
        var path = WriteProfile("""
            [Interface]
            Address = 10.0.0.2/32
            PrivateKey = VERY_SECRET_VALUE_12345
            JC = 4
            JMin = 1
            JMax = 2

            [Peer]
            PublicKey = PUBLICKEYEXAMPLE
            Endpoint = 188.114.98.58:2408
            AllowedIPs = 0.0.0.0/0, ::/0
            PersistentKeepalive = 25
            """);

        var info = new AmneziaWgConfigParser().Parse(path);

        info.IsStructurallyValid.Should().BeTrue();
        info.PublicEndpoint.Should().Be("188.114.98.58:2408");
        info.AddressCount.Should().Be(1);
        info.AllowedIpCount.Should().Be(2);
        info.HasPrivateKeyField.Should().BeTrue();
        info.HasAmneziaSpecificFields.Should().BeTrue();
        info.ToString().Should().NotContain("VERY_SECRET_VALUE");
    }

    [Fact]
    public void Parser_MissingFile_IsSafe()
    {
        var info = new AmneziaWgConfigParser().Parse(Path.Combine(_directory, "absent.conf"));

        info.FileExists.Should().BeFalse();
        info.IsStructurallyValid.Should().BeFalse();
    }

    [Fact]
    public void Parser_InvalidConfig_HasWarnings()
    {
        var path = WriteProfile("PrivateKey = SECRET\n");

        var info = new AmneziaWgConfigParser().Parse(path);

        info.IsStructurallyValid.Should().BeFalse();
        info.Warnings.Should().Contain(warning => warning.Contains("[Interface]"));
        info.ToString().Should().NotContain("SECRET");
    }

    [Fact]
    public void Controller_FindsProfilesAndBuildsSafePlan()
    {
        WriteProfile("[Interface]\nAddress=10.0.0.2/32\n[Peer]\nEndpoint=1.2.3.4:443\n");
        var controller = new AmneziaWgController();

        var profiles = controller.FindProfiles(_directory);
        profiles.Should().ContainSingle();

        var plan = controller.BuildActivationPlan(profiles[0].FilePath);
        plan.RequiresUserConfirmation.Should().BeTrue();
        plan.RequiresElevation.Should().BeTrue();
        plan.Explanation.Should().Contain("не выполняются");
    }

    [Fact]
    public async Task Controller_WithoutElevation_ReportsPermissionDenied()
    {
        if (AmneziaWgController.IsElevated)
        {
            return;
        }

        var status = await new AmneziaWgController().GetTunnelStatusAsync("test");
        status.State.Should().Be(AmneziaWgTunnelState.PermissionDenied);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string WriteProfile(string content)
    {
        var path = Path.Combine(_directory, $"profile-{Guid.NewGuid():N}.conf");
        File.WriteAllText(path, content);
        return path;
    }
}
