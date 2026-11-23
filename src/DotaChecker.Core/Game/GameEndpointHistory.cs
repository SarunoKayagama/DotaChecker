using System.Text.Json;
using DotaChecker.Core.Models;

namespace DotaChecker.Core.Game;

/// <summary>
/// JSON-история наблюдаемых endpoint. Факты и классификация хранятся раздельно,
/// поэтому при изменении правил классификатора категории можно пересчитать.
/// </summary>
public sealed class GameEndpointHistory
{
    private const int SchemaVersion = 2;
    private const int MaxClassificationHistory = 20;
    private const int MaxObservationTimes = 50;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly int _maxEntries;
    private readonly TimeSpan _staleAfter;
    private Document _document;

    public GameEndpointHistory(
        string path,
        int maxEntries = 200,
        TimeSpan? staleAfter = null)
    {
        _path = path;
        _maxEntries = Math.Max(1, maxEntries);
        _staleAfter = staleAfter ?? TimeSpan.FromDays(30);
        _document = new Document();
        Load();
    }

    public string Path => _path;

    public IReadOnlyList<GameEndpointRecord> Records()
    {
        lock (_gate)
        {
            return _document.Entries
                .Select(ToRecord)
                .OrderByDescending(record => record.LastSeen)
                .ToArray();
        }
    }

    public IReadOnlyList<GameEndpointHistoryEntry> Entries()
    {
        lock (_gate)
        {
            return _document.Entries.Select(entry => new GameEndpointHistoryEntry(
                ToRecord(entry),
                ToClassification(entry.Classification),
                entry.StoredAt)).ToArray();
        }
    }

    public void UpsertObservation(GameEndpointObservation observation)
    {
        lock (_gate)
        {
            var existing = _document.Entries.FirstOrDefault(entry => entry.Key == observation.Key);
            if (existing is null)
            {
                existing = new EntryDto { Key = observation.Key };
                _document.Entries.Add(existing);
            }

            existing.RemoteAddress = observation.RemoteAddress.ToString();
            existing.RemotePort = observation.RemotePort;
            existing.LocalAddress = observation.LocalAddress?.ToString();
            existing.LocalPort = observation.LocalPort;
            existing.Protocol = (int)observation.Protocol;
            existing.ProcessId = observation.ProcessId;
            if (!string.IsNullOrWhiteSpace(observation.ProcessName))
            {
                existing.ProcessName = observation.ProcessName;
            }

            if (!string.IsNullOrWhiteSpace(observation.ProcessPath))
            {
                existing.ProcessPath = observation.ProcessPath;
            }

            existing.Source = (int)observation.Source;
            existing.FirstSeen = existing.FirstSeen == default
                ? observation.FirstSeen
                : Min(existing.FirstSeen, observation.FirstSeen);
            existing.LastSeen = Max(existing.LastSeen, observation.LastSeen);
            existing.SendCount = Math.Max(existing.SendCount, observation.SendCount);
            existing.ReceiveCount = Math.Max(existing.ReceiveCount, observation.ReceiveCount);
            existing.ObservationCount += observation.ObservationCount;
            existing.ConnectionId = observation.ConnectionId != 0 ? observation.ConnectionId : existing.ConnectionId;
            existing.SizeSum = Math.Max(existing.SizeSum, observation.ObservedSizeFieldSum);
            existing.DataSizeSum = Math.Max(existing.DataSizeSum, observation.ObservedDataSizeFieldSum);
            existing.ObservationTimes.Add(observation.LastSeen);
            while (existing.ObservationTimes.Count > MaxObservationTimes)
            {
                existing.ObservationTimes.RemoveAt(0);
            }

            if (observation.ProcessStartTime.HasValue)
            {
                var start = observation.ProcessStartTime.Value;
                if (!existing.ProcessStartTimes.Any(value => Min(value, start) == Max(value, start)))
                {
                    existing.ProcessStartTimes.Add(start);
                }
            }
        }
    }

    public void Reclassify(GameEndpointClassifier classifier)
    {
        IReadOnlyList<GameEndpointRecord> snapshot;
        lock (_gate)
        {
            snapshot = _document.Entries.Select(ToRecord).ToArray();
        }

        foreach (var entry in snapshot)
        {
            var classification = classifier.Classify(entry, snapshot);
            lock (_gate)
            {
                var target = _document.Entries.FirstOrDefault(item => item.Key == entry.Key);
                if (target is null)
                {
                    continue;
                }

                var dto = new ClassificationDto
                {
                    Category = (int)classification.Category,
                    Evidence = (int)classification.Evidence,
                    Reasons = classification.Reasons.ToArray(),
                    ClassifiedAt = classification.ClassifiedAt,
                    ClassifierVersion = classification.ClassifierVersion,
                };
                target.Classification = dto;

                var last = target.ClassificationHistory.LastOrDefault();
                if (last is null || last.Category != dto.Category || last.Evidence != dto.Evidence ||
                    last.ClassifierVersion != dto.ClassifierVersion)
                {
                    target.ClassificationHistory.Add(dto);
                    while (target.ClassificationHistory.Count > MaxClassificationHistory)
                    {
                        target.ClassificationHistory.RemoveAt(0);
                    }
                }

                target.StoredAt = DateTimeOffset.UtcNow;
            }
        }
    }

    public bool Exclude(string key) => SetManualFlag(key, excluded: true);

    public bool Include(string key) => SetManualFlag(key, excluded: false);

    public int RemoveStale(DateTimeOffset now)
    {
        lock (_gate)
        {
            var threshold = now - _staleAfter;
            var removed = _document.Entries.RemoveAll(entry => entry.LastSeen < threshold);

            if (_document.Entries.Count > _maxEntries)
            {
                var excess = _document.Entries.Count - _maxEntries;
                foreach (var entry in _document.Entries.OrderBy(item => item.LastSeen).Take(excess).ToArray())
                {
                    _document.Entries.Remove(entry);
                }

                removed += excess;
            }

            return removed;
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_document, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }
    }

    public void Reload() => Load();

    private bool SetManualFlag(string key, bool excluded)
    {
        lock (_gate)
        {
            var entry = _document.Entries.FirstOrDefault(item => item.Key == key);
            if (entry is null)
            {
                return false;
            }

            entry.ManuallyExcluded = excluded;
            entry.ManuallyIncluded = !excluded;
            entry.StoredAt = DateTimeOffset.UtcNow;
            return true;
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                _document = new Document();
                return;
            }

            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(_path), JsonOptions);
            _document = document ?? new Document();
            _document.SchemaVersion = SchemaVersion;
            _document.Entries ??= new List<EntryDto>();
            foreach (var entry in _document.Entries)
            {
                entry.ClassificationHistory ??= new List<ClassificationDto>();
                entry.ObservationTimes ??= new List<DateTimeOffset>();
                entry.ProcessStartTimes ??= new List<DateTimeOffset>();
            }
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // Не уничтожаем исходные данные: сохраняем повреждённый файл отдельно.
            try
            {
                if (File.Exists(_path))
                {
                    var backup = $"{_path}.corrupt.{DateTimeOffset.UtcNow.UtcTicks}";
                    File.Copy(_path, backup, overwrite: false);
                }
            }
            catch (IOException)
            {
                // Не мешаем запуску, если копию сохранить не удалось.
            }

            _document = new Document();
        }
    }

    private static GameEndpointRecord ToRecord(EntryDto entry) => new()
    {
        Key = entry.Key,
        RemoteAddress = ParseAddress(entry.RemoteAddress),
        RemotePort = entry.RemotePort,
        LocalAddress = ParseAddress(entry.LocalAddress),
        LocalPort = entry.LocalPort,
        Protocol = (TransportProtocol)entry.Protocol,
        ProcessId = entry.ProcessId,
        ProcessName = entry.ProcessName,
        ProcessPath = entry.ProcessPath,
        Source = (EndpointDetectionSource)entry.Source,
        FirstSeen = entry.FirstSeen,
        LastSeen = entry.LastSeen,
        SendCount = entry.SendCount,
        ReceiveCount = entry.ReceiveCount,
        ObservationCount = entry.ObservationCount,
        ProcessStartTimes = entry.ProcessStartTimes.ToList(),
        ConnectionId = entry.ConnectionId,
        ObservedSizeFieldSum = entry.SizeSum,
        ObservedDataSizeFieldSum = entry.DataSizeSum,
        Classification = ToClassification(entry.Classification),
        ClassificationHistory = entry.ClassificationHistory
            .Select(ToClassification)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList(),
        ObservationTimes = entry.ObservationTimes.ToList(),
        ManuallyExcluded = entry.ManuallyExcluded,
        ManuallyIncluded = entry.ManuallyIncluded,
    };

    private static GameEndpointClassification? ToClassification(ClassificationDto? dto) =>
        dto is null
            ? null
            : new GameEndpointClassification(
                (GameEndpointCategory)dto.Category,
                (EvidenceLevel)dto.Evidence,
                dto.Reasons ?? Array.Empty<string>(),
                dto.ClassifiedAt,
                dto.ClassifierVersion ?? GameEndpointClassifier.Version);

    private static System.Net.IPAddress ParseAddress(string? text) =>
        System.Net.IPAddress.TryParse(text ?? string.Empty, out var address)
            ? address
            : System.Net.IPAddress.None;

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left >= right ? left : right;

    private sealed class Document
    {
        public int SchemaVersion { get; set; } = 2;

        public List<EntryDto> Entries { get; set; } = new();
    }

    private sealed class EntryDto
    {
        public string Key { get; set; } = string.Empty;
        public string RemoteAddress { get; set; } = string.Empty;
        public int RemotePort { get; set; }
        public string? LocalAddress { get; set; }
        public int LocalPort { get; set; }
        public int Protocol { get; set; } = (int)TransportProtocol.Udp;
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string ProcessPath { get; set; } = string.Empty;
        public int Source { get; set; }
        public DateTimeOffset FirstSeen { get; set; }
        public DateTimeOffset LastSeen { get; set; }
        public long SendCount { get; set; }
        public long ReceiveCount { get; set; }
        public int ObservationCount { get; set; }
        public List<DateTimeOffset> ProcessStartTimes { get; set; } = new();
        public long ConnectionId { get; set; }
        public long SizeSum { get; set; }
        public long DataSizeSum { get; set; }
        public bool ManuallyExcluded { get; set; }
        public bool ManuallyIncluded { get; set; }
        public DateTimeOffset StoredAt { get; set; }
        public ClassificationDto? Classification { get; set; }
        public List<ClassificationDto> ClassificationHistory { get; set; } = new();
        public List<DateTimeOffset> ObservationTimes { get; set; } = new();
    }

    private sealed class ClassificationDto
    {
        public int Category { get; set; }
        public int Evidence { get; set; }
        public string[] Reasons { get; set; } = Array.Empty<string>();
        public DateTimeOffset ClassifiedAt { get; set; }
        public string ClassifierVersion { get; set; } = GameEndpointClassifier.Version;
    }
}
