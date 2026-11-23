namespace DotaChecker.Core.Configuration;

/// <summary>
/// Устойчивые пользовательские настройки. Хранятся в JSON и не содержат
/// состояние сессии — сброс статистики выполняется отдельно и вручную.
/// </summary>
public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    /// <summary>Период отправки проб одному узлу, мс.</summary>
    public int ProbeIntervalMs { get; set; } = 1000;

    /// <summary>Таймаут ожидания ответа на одну пробу, мс.</summary>
    public int ProbeTimeoutMs { get; set; } = 2000;

    /// <summary>Максимальное число одновременно выполняемых проверок.</summary>
    public int MaxConcurrentProbes { get; set; } = 8;

    /// <summary>Период повторной трассировки для поиска изменений маршрута, с.</summary>
    public int RouteRefreshSeconds { get; set; } = 120;

    /// <summary>Максимальный номер TTL при обнаружении маршрута.</summary>
    public int MaxHops { get; set; } = 30;

    /// <summary>Минимальное число проб, ниже которого анализ не делается.</summary>
    public int MinSamplesForDiagnosis { get; set; } = 20;

    /// <summary>Порог значимой потери, %.</summary>
    public double LossThresholdPct { get; set; } = 5.0;

    /// <summary>Порт назначения для UDP-трассировки.</summary>
    public int UdpDestinationPort { get; set; } = 33434;

    /// <summary>Порт назначения для TCP-трассировки.</summary>
    public int TcpDestinationPort { get; set; } = 443;

    /// <summary>Выполнять ли reverse DNS для найденных узлов.</summary>
    public bool ReverseDnsEnabled { get; set; } = true;

    /// <summary>
    /// Таймаут TCP-пробы. Windows отдаёт RST только после ретрансмиссий SYN (~2 с),
    /// поэтому значение ниже 3000 мс приводит к неверной классификации закрытого порта.
    /// </summary>
    public int TcpProbeTimeoutMs { get; set; } = 3500;

    /// <summary>Путь к warpscout.exe, заданный пользователем. Не задаётся жёстко.</summary>
    public string WarpscoutBinaryPath { get; set; } = string.Empty;

    /// <summary>Таймаут сканирования WARPSCOUT, с.</summary>
    public int WarpscoutTimeoutSeconds { get; set; } = 600;

    /// <summary>Каталог с .conf профилями AmneziaWG (только чтение).</summary>
    public string AmneziaWgConfigDirectory { get; set; } = string.Empty;
}
