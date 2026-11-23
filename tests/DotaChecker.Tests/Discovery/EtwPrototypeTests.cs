using DotaChecker.Network.Discovery;
using DotaChecker.Network.Discovery.Models;

namespace DotaChecker.Tests.Discovery;

/// <summary>
/// Реальный запуск ETW. Не запускается в обычном прогоне: требует администратора
/// и не является подтверждением полей без elevate-прогона на машине с Dota 2.
/// Запуск: dotnet test --filter Category=EtwIntegration --logger "console;verbosity=normal"
/// </summary>
[Trait("Category", "EtwIntegration")]
public sealed class EtwPrototypeTests
{
    [Fact]
    public async Task ProbeReportsRealStatus()
    {
        await using var probe = new UdpActivityProbe();
        var status = probe.Start(new[] { Environment.ProcessId });

        Console.WriteLine($"ETW STATUS={status} REASON={probe.StatusReason}");
        Console.WriteLine($"PAYLOAD_VALIDATED={probe.PayloadValidated} EVENTS={probe.TotalEvents}");
        foreach (var sample in probe.Samples)
        {
            Console.WriteLine($"SAMPLE {sample}");
        }

        status.Should().BeOneOf(
            EtAvailability.Available,
            EtAvailability.PermissionDenied,
            EtAvailability.ProviderUnavailable,
            EtAvailability.Unavailable,
            EtAvailability.Error);

        await probe.StopAsync();
    }
}
