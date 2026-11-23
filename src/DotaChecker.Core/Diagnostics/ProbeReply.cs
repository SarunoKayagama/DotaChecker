using System.Net;

namespace DotaChecker.Core.Diagnostics;

/// <summary>Результат одной пробы.</summary>
/// <param name="Responder">Адрес, который реально ответил; при отсутствии ответа — адрес назначения.</param>
/// <param name="Outcome">Классификация ответа.</param>
/// <param name="RttMs">RTT в мс; 0, если ответа нет.</param>
/// <param name="Status">Исходный статус провайдера (например, IPStatus), для диагностики.</param>
public sealed record ProbeReply(
    IPAddress Responder,
    ProbeOutcome Outcome,
    double RttMs,
    string Status);
