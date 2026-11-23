namespace DotaChecker.Core.Diagnostics;

/// <summary>Классификация результата одиночной пробы.</summary>
public enum ProbeOutcome
{
    /// <summary>Получен Echo Reply от конечного узла.</summary>
    EchoReply = 0,

    /// <summary>Получен ICMP Time Exceeded от промежуточного маршрутизатора.</summary>
    TimeExceeded = 1,

    /// <summary>Получен ICMP Destination Unreachable.</summary>
    DestinationUnreachable = 2,

    /// <summary>Ответа нет. Это НЕ доказательство потери транзитного трафика.</summary>
    NoReply = 3,

    /// <summary>Локальная/сетевая ошибка отправки.</summary>
    Error = 4,
}
