using System.Globalization;
using System.Text.RegularExpressions;

namespace DotaChecker.Core.Warp;

/// <summary>
/// Version-aware parser фиксированно-шириного отчёта WARPSCOUT. Заголовок колонок
/// используется как источник имени полей: при неизвестном формате отчёт не интерпретируется.
/// </summary>
public sealed class WarpscoutReportParser
{
    private static readonly Regex ColumnSplit = new(@"\s{2,}", RegexOptions.Compiled);
    private static readonly HashSet<string> KnownVersions = new(StringComparer.Ordinal)
    {
        "0.12.0", "0.13.0", "0.13.1", "0.13.2", "0.13.3", "0.13.4",
        "0.14.0", "0.15.0", "0.16.0", "0.16.1",
    };

    public WarpscoutParseResult Parse(string report, string binaryVersion)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(report))
        {
            return new WarpscoutParseResult(false, binaryVersion, Array.Empty<WarpscoutEndpoint>(),
                new[] { "Отчёт пуст." });
        }

        if (!KnownVersions.Contains(binaryVersion.Trim()) && !string.IsNullOrWhiteSpace(binaryVersion))
        {
            warnings.Add($"Версия WARPSCOUT '{binaryVersion}' не проверена парсером; результаты помечены как предварительные.");
        }

        var lines = report.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r', '\n'))
            .ToArray();
        var headerIndex = Array.FindIndex(lines, IsHeaderLine);
        if (headerIndex < 0)
        {
            return new WarpscoutParseResult(false, binaryVersion, Array.Empty<WarpscoutEndpoint>(),
                warnings.Append("Строка заголовка ENDPOINT не найдена.").ToArray());
        }

        var headers = ColumnSplit.Split(lines[headerIndex])
            .Select(value => value.Trim())
            .ToArray();
        if (!headers.Contains("ENDPOINT"))
        {
            return new WarpscoutParseResult(false, binaryVersion, Array.Empty<WarpscoutEndpoint>(),
                warnings.Append("Колонка ENDPOINT отсутствует.").ToArray());
        }

        var endpoints = new List<WarpscoutEndpoint>();
        foreach (var line in lines.Skip(headerIndex + 1))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith('─'))
            {
                continue;
            }

            var cells = ColumnSplit.Split(line.Trim()).Select(value => value.Trim()).ToArray();
            if (cells.Length == 0 || !cells[0].Contains(':'))
            {
                continue;
            }

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < Math.Min(cells.Length, headers.Length); i++)
            {
                fields[headers[i]] = cells[i];
            }

            endpoints.Add(new WarpscoutEndpoint(
                fields.GetValueOrDefault("ENDPOINT", cells[0]),
                ParseMs(fields.GetValueOrDefault("ENDPOINT PING")),
                ParseMs(fields.GetValueOrDefault("TUN PING")),
                ParsePercent(fields.GetValueOrDefault("LOSS")),
                fields.GetValueOrDefault("SEEN AS", string.Empty),
                fields.GetValueOrDefault("NODE", string.Empty),
                fields.GetValueOrDefault("NODE LOCATION", string.Empty)));
        }

        if (endpoints.Count == 0)
        {
            warnings.Add("Строки endpoints не найдены.");
        }

        return new WarpscoutParseResult(true, binaryVersion, endpoints, warnings.ToArray());
    }

    private static bool IsHeaderLine(string line) =>
        !line.StartsWith('#') && line.Contains("ENDPOINT", StringComparison.Ordinal) &&
        line.Contains("NODE", StringComparison.Ordinal);

    private static double? ParseMs(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Replace("ms", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static double? ParsePercent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.TrimEnd('%').Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}

public sealed record WarpscoutParseResult(
    bool IsRecognized,
    string BinaryVersion,
    IReadOnlyList<WarpscoutEndpoint> Endpoints,
    IReadOnlyList<string> Warnings);
