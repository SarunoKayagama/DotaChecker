namespace DotaChecker.Core.Warp;

public enum WarpscoutExecutionStatus
{
    NotRun,
    Running,
    Success,
    Failed,
    Cancelled,
    Timeout,
    BinaryNotFound,
    ParseFailed,
}

public sealed record WarpscoutConfiguration
{
    public string BinaryPath { get; set; } = string.Empty;

    public string Protocol { get; set; } = "awg";

    public bool TunnelPingCheck { get; set; } = true;

    public bool BestOnly { get; set; }

    public string? ExcludeNode { get; set; }

    public string? Country { get; set; }

    public int TimeoutSeconds { get; set; } = 600;

    public string? ReportPath { get; set; }

    /// <summary>Куда WARPSCOUT сразу запишет готовый .conf лучшего endpoint.</summary>
    public string? ConfigPath { get; set; }

    public IReadOnlyList<string> BuildArguments()
    {
        var arguments = new List<string> { "scan", "-p", Protocol };
        if (TunnelPingCheck)
        {
            arguments.Add("-P");
        }

        if (!string.IsNullOrWhiteSpace(ExcludeNode))
        {
            arguments.Add("-exclude-node");
            arguments.Add(ExcludeNode);
        }

        if (!string.IsNullOrWhiteSpace(Country))
        {
            arguments.Add("-country");
            arguments.Add(Country);
        }

        if (BestOnly)
        {
            arguments.Add("-best");
        }

        // DotaChecker не меняет DNS: конфиг не должен содержать строку DNS.
        arguments.Add("-no-dns");

        if (!string.IsNullOrWhiteSpace(ConfigPath))
        {
            arguments.Add("-conf");
            arguments.Add(ConfigPath);
            // Конфиг не должен менять маршруты или DNS.
            arguments.Add("-table-off");
        }

        if (!string.IsNullOrWhiteSpace(ReportPath))
        {
            arguments.Add("-o");
            arguments.Add(ReportPath);
        }
        else
        {
            arguments.Add("-no-report");
        }

        return arguments;
    }
}

public sealed record WarpscoutEndpoint(
    string Endpoint,
    double? EndpointPingMs,
    double? TunnelPingMs,
    double? LossPct,
    string SeenAs,
    string Node,
    string NodeLocation);

public sealed record WarpscoutResult(
    WarpscoutExecutionStatus Status,
    DateTimeOffset ExecutedAt,
    string BinaryVersion,
    IReadOnlyList<WarpscoutEndpoint> Endpoints,
    string ReportPath,
    string ConfigPath,
    string ErrorMessage,
    TimeSpan Duration,
    string DiagnosticLog);
