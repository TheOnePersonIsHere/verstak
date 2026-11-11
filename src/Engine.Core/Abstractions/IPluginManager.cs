namespace Engine.Core.Abstractions;

public sealed record PluginEntryInfo(
    string Id,
    string DisplayName,
    bool IsEnabled,
    bool IsWindowVisible,
    bool CanDisable);

public interface IPluginManager
{
    IReadOnlyList<PluginEntryInfo> Entries { get; }
    bool IsPluginEnabled(string pluginId);
    void SetPluginEnabled(string pluginId, bool enabled);
    bool IsWindowVisible(string pluginId);
    void SetWindowVisible(string pluginId, bool visible);
}
