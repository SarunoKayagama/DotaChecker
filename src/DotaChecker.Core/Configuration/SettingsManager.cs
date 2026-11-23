using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotaChecker.Core.Configuration;

/// <summary>Загрузка и сохранение настроек приложения.</summary>
public interface ISettingsManager
{
    AppSettings Settings { get; }

    string SettingsPath { get; }

    void Save();

    void Reload();

    /// <summary>Вернуть значения по умолчанию и сразу сохранить их.</summary>
    void Reset();
}

/// <summary>
/// Хранит настройки в %AppData%\Dota Checker\settings.json.
/// Запись идёт через временный файл, чтобы сбой не оставил полузаписанный файл.
/// </summary>
public sealed class SettingsManager : ISettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public AppSettings Settings { get; private set; } = new();

    public string SettingsPath { get; }

    public SettingsManager(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? BuildDefaultPath();
        Load();
    }

    public void Reload() => Load();

    public void Reset()
    {
        // Изменять тот же экземпляр: на него уже ссылаются движок мониторинга и DI.
        var defaults = new AppSettings();
        foreach (var property in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanWrite)
            {
                property.SetValue(Settings, property.GetValue(defaults));
            }
        }

        Save();
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Settings, JsonOptions));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                Settings = new AppSettings();
                return;
            }

            var json = File.ReadAllText(SettingsPath);
            Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // Повреждённый файл настроек не должен мешать запуску: используем значения по умолчанию.
            Settings = new AppSettings();
        }
    }

    private static string BuildDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Dota Checker", "settings.json");
    }
}
