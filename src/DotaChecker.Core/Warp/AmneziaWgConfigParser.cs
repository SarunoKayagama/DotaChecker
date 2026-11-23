using System.Globalization;

namespace DotaChecker.Core.Warp;

/// <summary>
/// Разбор .conf только в публичные поля. PrivateKey/PresharedKey/Never хранятся,
/// но их значения никогда не попадают в модель, логи и отчёты.
/// </summary>
public sealed class AmneziaWgConfigParser
{
    private static readonly HashSet<string> SecretKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "PrivateKey", "PresharedKey",
    };

    private static readonly HashSet<string> AmneziaKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "JC", "JMin", "JMax", "S1", "S2", "S3", "S4",
    };

    public AmneziaWgProfileInfo Parse(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        if (!File.Exists(path))
        {
            return new AmneziaWgProfileInfo(name, path, false, false, null, 0, 0, 0, false, false,
                new[] { "Файл конфигурации не найден." });
        }

        var warnings = new List<string>();
        var hasInterface = false;
        var hasPeer = false;
        var hasPrivateKey = false;
        var hasAmnezia = false;
        string? endpoint = null;
        var addresses = 0;
        var allowedIps = 0;
        var mtu = 0;

        try
        {
            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                {
                    continue;
                }

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    var section = line[1..^1];
                    hasInterface |= section.Equals("Interface", StringComparison.OrdinalIgnoreCase);
                    hasPeer |= section.Equals("Peer", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    warnings.Add("Строка без '=' пропущена (значение не сохраняется).");
                    continue;
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();

                if (SecretKeys.Contains(key))
                {
                    hasPrivateKey = true;
                    continue;
                }

                if (AmneziaKeys.Contains(key))
                {
                    hasAmnezia = true;
                    continue;
                }

                if (key.Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
                {
                    endpoint = value;
                }
                else if (key.Equals("Address", StringComparison.OrdinalIgnoreCase))
                {
                    addresses += value.Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
                }
                else if (key.Equals("AllowedIPs", StringComparison.OrdinalIgnoreCase))
                {
                    allowedIps += value.Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
                }
                else if (key.Equals("MTU", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMtu))
                {
                    mtu = parsedMtu;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            warnings.Add("Нет доступа к файлу конфигурации.");
        }

        if (!hasInterface)
        {
            warnings.Add("Секция [Interface] не найдена.");
        }

        if (!hasPeer)
        {
            warnings.Add("Секция [Peer] не найдена.");
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            warnings.Add("Публичный Endpoint не найден.");
        }

        return new AmneziaWgProfileInfo(
            name,
            path,
            true,
            hasInterface && hasPeer && !string.IsNullOrWhiteSpace(endpoint),
            endpoint,
            addresses,
            allowedIps,
            mtu,
            hasPrivateKey,
            hasAmnezia,
            warnings.ToArray());
    }
}
