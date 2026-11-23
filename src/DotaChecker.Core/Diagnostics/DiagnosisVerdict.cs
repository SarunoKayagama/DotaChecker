namespace DotaChecker.Core.Diagnostics;

/// <summary>
/// Диагностический verdict. EvidenceScore — не вероятность правильности вывода.
/// Confirmed используется только для контролируемых экспериментов или однозначно
/// установленных событий; в обычном мониторинге он не выставляется.
/// </summary>
public enum DiagnosisVerdict
{
    /// <summary>Данных недостаточно для вывода.</summary>
    InsufficientData = 0,

    /// <summary>Подавление ICMP-ответов узлом: потеря не переносится на последующие узлы.</summary>
    IcmpResponseSuppression = 1,

    /// <summary>Подозрение на конкретный сегмент; слабее Confirmed и не является доказанным.</summary>
    SuspectedSegment = 2,

    /// <summary>Вероятная проблема без однозначной локализации (например, на стороне цели).</summary>
    Probable = 3,

    /// <summary>Вероятная проблема в области из нескольких узлов.</summary>
    ProbableRegion = 4,

    /// <summary>Только для контролируемого эксперимента / однозначно установленного события.</summary>
    Confirmed = 5,
}
