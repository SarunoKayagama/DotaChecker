namespace DotaChecker.Core.Diagnostics;

/// <summary>
/// Транспорт диагностических проб. Реализация не зависит от UI.
/// Слоты должны быть независимыми: каждый слот может отправлять пробы параллельно.
/// </summary>
public interface IProbeTransport : IAsyncDisposable
{
    Task<ProbeReply> SendAsync(ProbeRequest request, CancellationToken cancellationToken);
}
