using System.Net;
using System.Text.Json;

namespace DotaChecker.Core.Game;

/// <summary>Ограниченная история сводок мониторинга endpoint.</summary>
public sealed class EndpointMonitoringHistory
{
    private const int MaxSummaries = 50;
    private readonly string _path;
    private readonly object _gate = new();
    private List<EndpointMonitoringSummary> _summaries = new();

    public EndpointMonitoringHistory(string path)
    {
        _path = path;
        Load();
    }

    public IReadOnlyList<EndpointMonitoringSummary> Summaries()
    {
        lock (_gate)
        {
            return _summaries.ToArray();
        }
    }

    public void Upsert(EndpointMonitoringSummary summary)
    {
        lock (_gate)
        {
            _summaries.RemoveAll(item => item.EndpointKey == summary.EndpointKey);
            _summaries.Insert(0, summary);
            while (_summaries.Count > MaxSummaries)
            {
                _summaries.RemoveAt(_summaries.Count - 1);
            }
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
            File.WriteAllText(temporary, JsonSerializer.Serialize(_summaries, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, overwrite: true);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            _summaries = JsonSerializer.Deserialize<List<EndpointMonitoringSummary>>(
                File.ReadAllText(_path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<EndpointMonitoringSummary>();
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            try
            {
                if (File.Exists(_path))
                {
                    File.Copy(_path, $"{_path}.corrupt.{DateTimeOffset.UtcNow.UtcTicks}", overwrite: false);
                }
            }
            catch (IOException)
            {
                // Игнорируем: запуск важнее копии повреждённого файла.
            }

            _summaries = new List<EndpointMonitoringSummary>();
        }
    }
}
