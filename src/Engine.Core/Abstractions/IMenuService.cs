namespace Engine.Core.Abstractions;

public sealed record MenuItemDefinition(
    string Id,
    string Path,
    Action? Callback,
    string? Shortcut = null,
    Func<bool>? IsChecked = null,
    Func<bool>? IsEnabled = null,
    int Order = 0,
    bool IsSeparator = false);

public interface IMenuService
{
    int Version { get; }
    IReadOnlyList<MenuItemDefinition> Items { get; }
    void Register(MenuItemDefinition item);
    void AddSeparator(string menuPath, string id, int order = 0);
    void Unregister(string id);
    void UnregisterByPrefix(string idPrefix);
}
