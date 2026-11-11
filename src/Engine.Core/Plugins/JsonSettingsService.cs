using System.Text.Json;
using Engine.Core.Abstractions;
using Engine.Core.Events;

namespace Engine.Core.Plugins;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IEventBus _eventBus;
    private readonly Dictionary<string, JsonElement> _values = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public JsonSettingsService(IEventBus eventBus, string? filePath = null)
    {
        _eventBus = eventBus;
        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? GetDefaultSettingsPath()
            : Path.GetFullPath(filePath);
        Load();
    }

    public string FilePath { get; }

    public bool GetBool(string key, bool defaultValue = false)
    {
        lock (_sync)
        {
            if (_values.TryGetValue(key, out JsonElement el) &&
                (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False))
            {
                return el.GetBoolean();
            }

            return defaultValue;
        }
    }

    public void SetBool(string key, bool value)
    {
        bool changed;
        lock (_sync)
        {
            changed = !(_values.TryGetValue(key, out JsonElement el) &&
                        (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False) &&
                        el.GetBoolean() == value);
            _values[key] = JsonSerializer.SerializeToElement(value, JsonOptions);
        }

        if (changed)
        {
            Save();
            _eventBus.Publish(new SettingChangedEvent(key));
        }
    }

    public int GetInt(string key, int defaultValue = 0)
    {
        lock (_sync)
        {
            if (_values.TryGetValue(key, out JsonElement el) &&
                el.ValueKind == JsonValueKind.Number &&
                el.TryGetInt32(out int v))
            {
                return v;
            }

            return defaultValue;
        }
    }

    public void SetInt(string key, int value)
    {
        bool changed;
        lock (_sync)
        {
            changed = !(_values.TryGetValue(key, out JsonElement el) &&
                        el.ValueKind == JsonValueKind.Number &&
                        el.TryGetInt32(out int existing) &&
                        existing == value);
            _values[key] = JsonSerializer.SerializeToElement(value, JsonOptions);
        }

        if (changed)
        {
            Save();
            _eventBus.Publish(new SettingChangedEvent(key));
        }
    }

    public float GetFloat(string key, float defaultValue = 0f)
    {
        lock (_sync)
        {
            if (_values.TryGetValue(key, out JsonElement el) &&
                el.ValueKind == JsonValueKind.Number &&
                el.TryGetSingle(out float v))
            {
                return v;
            }

            return defaultValue;
        }
    }

    public void SetFloat(string key, float value)
    {
        bool changed;
        lock (_sync)
        {
            changed = !(_values.TryGetValue(key, out JsonElement el) &&
                        el.ValueKind == JsonValueKind.Number &&
                        el.TryGetSingle(out float existing) &&
                        MathF.Abs(existing - value) < 1e-5f);
            _values[key] = JsonSerializer.SerializeToElement(value, JsonOptions);
        }

        if (changed)
        {
            Save();
            _eventBus.Publish(new SettingChangedEvent(key));
        }
    }

    public string GetString(string key, string defaultValue = "")
    {
        lock (_sync)
        {
            if (_values.TryGetValue(key, out JsonElement el) &&
                el.ValueKind == JsonValueKind.String)
            {
                return el.GetString() ?? defaultValue;
            }

            return defaultValue;
        }
    }

    public void SetString(string key, string value)
    {
        bool changed;
        lock (_sync)
        {
            changed = !(_values.TryGetValue(key, out JsonElement el) &&
                        el.ValueKind == JsonValueKind.String &&
                        string.Equals(el.GetString(), value, StringComparison.Ordinal));
            _values[key] = JsonSerializer.SerializeToElement(value ?? string.Empty, JsonOptions);
        }

        if (changed)
        {
            Save();
            _eventBus.Publish(new SettingChangedEvent(key));
        }
    }

    public void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json;
            lock (_sync)
            {
                json = JsonSerializer.Serialize(_values, JsonOptions);
            }

            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Keep workbench responsive even if user settings directory is read-only (e.g. sandboxed CI).
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            string json = File.ReadAllText(FilePath);
            Dictionary<string, JsonElement>? loaded = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions);
            if (loaded == null)
            {
                return;
            }

            lock (_sync)
            {
                _values.Clear();
                foreach (KeyValuePair<string, JsonElement> kvp in loaded)
                {
                    _values[kvp.Key] = kvp.Value;
                }
            }
        }
        catch
        {
            // Ignore malformed user settings file and recreate cleanly on next save.
        }
    }

    private static string GetDefaultSettingsPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Path.GetTempPath();
        }

        return Path.Combine(appData, "WorkbenchEngine", "settings.json");
    }
}
