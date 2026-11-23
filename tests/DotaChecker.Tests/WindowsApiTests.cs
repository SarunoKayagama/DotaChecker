using DotaChecker.Core.Models;
using DotaChecker.Network.Connections;

namespace DotaChecker.Tests;

[Trait("Category", "Integration")]
public sealed class WindowsApiTests
{
    [Fact]
    public async Task ConnectionMonitorReadsTcpAndUdpTables()
    {
        await using var monitor = new ConnectionMonitor();
        IReadOnlyList<ConnectionInfo>? snapshot = null;
        Exception? failure = null;
        monitor.Updated += rows => snapshot = rows;
        monitor.Failed += exception => failure = exception;

        await monitor.RefreshOnceAsync();

        failure.Should().BeNull(failure?.ToString());
        snapshot.Should().NotBeNull();
        snapshot!.Should().NotBeEmpty();
        snapshot.Should().Contain(row => row.Protocol == TransportProtocol.Tcp);
        snapshot.Should().Contain(row => row.Protocol == TransportProtocol.Udp);
        snapshot.Should().Contain(row => row.ProcessId > 0);
        snapshot.Should().OnlyContain(row => row.ProcessName.Length > 0);
    }
}
