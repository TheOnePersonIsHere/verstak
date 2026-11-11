namespace Engine.Core.Abstractions;

public interface ISettingsService
{
    string FilePath { get; }
    bool GetBool(string key, bool defaultValue = false);
    void SetBool(string key, bool value);
    int GetInt(string key, int defaultValue = 0);
    void SetInt(string key, int value);
    float GetFloat(string key, float defaultValue = 0f);
    void SetFloat(string key, float value);
    string GetString(string key, string defaultValue = "");
    void SetString(string key, string value);
    void Save();
}
