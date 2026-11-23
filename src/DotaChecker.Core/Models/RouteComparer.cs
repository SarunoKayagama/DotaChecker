using System.Net;

namespace DotaChecker.Core.Models;

public sealed record RouteComparison(bool HasChanges, bool HasStructuralChange, string Description);

/// <summary>Сравнение упорядоченной структуры маршрута, а не только пересечения адресов.</summary>
public static class RouteComparer
{
    public static RouteComparison Compare(RouteSnapshot oldRoute, RouteSnapshot newRoute)
    {
        if (oldRoute.Signature == newRoute.Signature)
        {
            return new RouteComparison(false, false, "Изменений в наборе адресов нет.");
        }

        var changed = new List<string>();
        var max = Math.Max(oldRoute.Hops.Count, newRoute.Hops.Count);
        if (oldRoute.Hops.Count != newRoute.Hops.Count)
        {
            changed.Add($"число hop: {oldRoute.Hops.Count} → {newRoute.Hops.Count}");
        }

        for (var i = 0; i < max; i++)
        {
            var oldSet = i < oldRoute.Hops.Count ? oldRoute.Hops[i].Responders : Array.Empty<IPAddress>();
            var newSet = i < newRoute.Hops.Count ? newRoute.Hops[i].Responders : Array.Empty<IPAddress>();
            var oldText = oldSet.Count == 0 ? "—" : string.Join(",", oldSet.OrderBy(x => x.ToString()));
            var newText = newSet.Count == 0 ? "—" : string.Join(",", newSet.OrderBy(x => x.ToString()));
            if (oldText != newText)
            {
                changed.Add($"hop {i + 1}: {oldText} → {newText}");
            }
        }

        // Частичное пересечение адресов НЕ считается основанием считать маршрут прежним.
        return new RouteComparison(true, true, string.Join("; ", changed));
    }
}

/// <summary>Упорядоченный стабильный fingerprint: количество hop и устойчивые адреса для каждого TTL.</summary>
public sealed record RouteFingerprint(int HopCount, IReadOnlyList<string> StableAddressesPerHop)
{
    public string Key => HopCount + ":" + string.Join("|", StableAddressesPerHop);
}

public enum RouteStabilityStatus
{
    Initialized,
    NoChange,
    EcmpVariation,
    Pending,
    Committed,
}

public sealed record RouteStabilityResult(
    RouteStabilityStatus Status,
    RouteFingerprint Fingerprint,
    int PendingDiscoveries,
    int CommittedLatencyDiscoveries);

/// <summary>
/// Стабилизация ECMP: адрес считается устойчивым, если встречается в ≥ minSeen
/// из последних window discovery. Реальное изменение фиксируется после confirm подряд
/// одинаковых fingerprint'ов. Измеряет задержку обнаружения в числах discovery.
/// </summary>
public sealed class RouteStabilityTracker
{
    private readonly int _window;
    private readonly int _minSeen;
    private readonly int _confirm;
    private readonly List<RouteSnapshot> _recent = new();

    private RouteFingerprint? _current;
    private RouteFingerprint? _pending;
    private int _pendingCount;
    private int _committedLatency;

    public RouteStabilityTracker(int window = 3, int minSeen = 2, int confirm = 3)
    {
        _window = Math.Max(1, window);
        _minSeen = Math.Clamp(minSeen, 1, _window);
        _confirm = Math.Max(1, confirm);
    }

    public RouteFingerprint? CurrentFingerprint => _current;

    public int CommittedLatencyDiscoveries => _committedLatency;

    public RouteStabilityResult OnDiscovery(RouteSnapshot route)
    {
        _recent.Add(route);
        while (_recent.Count > _window)
        {
            _recent.RemoveAt(0);
        }

        var fingerprint = BuildFingerprint();
        if (_current is null)
        {
            _current = fingerprint;
            return new RouteStabilityResult(RouteStabilityStatus.Initialized, fingerprint, 0, 0);
        }

        if (fingerprint.Key == _current.Key)
        {
            _pending = null;
            _pendingCount = 0;
            return new RouteStabilityResult(RouteStabilityStatus.NoChange, fingerprint, 0, 0);
        }

        if (_pending is null || _pending.Key != fingerprint.Key)
        {
            _pending = fingerprint;
            _pendingCount = 1;
            return new RouteStabilityResult(RouteStabilityStatus.Pending, fingerprint, 1, 0);
        }

        _pendingCount++;
        if (_pendingCount < _confirm)
        {
            return new RouteStabilityResult(RouteStabilityStatus.Pending, fingerprint, _pendingCount, 0);
        }

        var previous = _current;
        var isEcmpVariation = IsAddressSupersetOnly(previous, fingerprint);

        _current = fingerprint;
        _pending = null;
        _committedLatency = _pendingCount;

        return isEcmpVariation
            ? new RouteStabilityResult(RouteStabilityStatus.EcmpVariation, fingerprint, 0, _committedLatency)
            : new RouteStabilityResult(RouteStabilityStatus.Committed, fingerprint, 0, _committedLatency);
    }

    /// <summary>
    /// ECMP-вариация: на каждом TTL прежние адреса сохранились, добавились новые,
    /// число hop не изменилось. Если какой-то адрес исчез — это уже смена маршрута.
    /// </summary>
    private static bool IsAddressSupersetOnly(RouteFingerprint? previous, RouteFingerprint current)
    {
        if (previous is null || previous.HopCount != current.HopCount)
        {
            return false;
        }

        for (var i = 0; i < previous.StableAddressesPerHop.Count; i++)
        {
            var before = Split(previous.StableAddressesPerHop[i]);
            var after = Split(current.StableAddressesPerHop[i]);
            if (!before.IsSubsetOf(after))
            {
                return false;
            }
        }

        return true;
    }

    private static HashSet<string> Split(string text) =>
        text.Length == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    private RouteFingerprint BuildFingerprint()
    {
        var hopCount = _recent.Max(snapshot => snapshot.Hops.Count);
        // На первом discovery устойчивым считается всё, что observed: иначе
        // начальный fingerprint был бы пустым и вызвал ложную смену версии.
        var minSeen = Math.Min(_minSeen, _recent.Count);
        var stable = new List<string>(hopCount);

        for (var i = 0; i < hopCount; i++)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var snapshot in _recent)
            {
                if (i >= snapshot.Hops.Count)
                {
                    continue;
                }

                foreach (var address in snapshot.Hops[i].Responders)
                {
                    var key = address.ToString();
                    counts[key] = counts.TryGetValue(key, out var value) ? value + 1 : 1;
                }
            }

            var addresses = counts
                .Where(pair => pair.Value >= minSeen)
                .Select(pair => pair.Key)
                .OrderBy(text => text, StringComparer.Ordinal)
                .ToArray();
            stable.Add(string.Join(",", addresses));
        }

        return new RouteFingerprint(hopCount, stable);
    }
}
