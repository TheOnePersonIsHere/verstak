using Engine.Core.Abstractions;
using Engine.Core.Events;

namespace Engine.Core.Plugins;

public sealed class PluginHost : IPluginManager
{
    private sealed class PluginSlot
    {
        public required IEnginePlugin Plugin { get; init; }
        public required bool CanDisable { get; init; }
        public bool IsEnabled { get; set; }
        public bool IsLoaded { get; set; }
        public bool IsWindowVisible { get; set; }
    }

    private readonly PluginContext _context;
    private readonly List<PluginSlot> _slots = new();
    private bool _hostLoaded;

    public PluginHost(PluginContext context)
    {
        _context = context;
        _context.Plugins = this;
    }

    public IReadOnlyList<IEnginePlugin> Plugins => _slots.Select(s => s.Plugin).ToArray();

    public IReadOnlyList<PluginEntryInfo> Entries => _slots
        .Select(s => new PluginEntryInfo(s.Plugin.Id, s.Plugin.DisplayName, s.IsEnabled, s.IsWindowVisible, s.CanDisable))
        .ToArray();

    public void RegisterPlugin(IEnginePlugin plugin)
    {
        RegisterPlugin(plugin, canDisable: true, defaultWindowVisible: true);
    }

    public void RegisterPlugin(IEnginePlugin plugin, bool canDisable = true, bool defaultWindowVisible = true)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        if (_slots.Any(s => string.Equals(s.Plugin.Id, plugin.Id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Plugin with id '{plugin.Id}' is already registered.");
        }

        bool enabled = !canDisable || _context.Settings.GetBool($"plugin.{plugin.Id}.enabled", true);
        bool visible = _context.Settings.GetBool($"plugin.{plugin.Id}.visible", defaultWindowVisible);

        var slot = new PluginSlot
        {
            Plugin = plugin,
            CanDisable = canDisable,
            IsEnabled = enabled,
            IsWindowVisible = visible
        };

        _slots.Add(slot);

        if (_hostLoaded && slot.IsEnabled)
        {
            LoadSlot(slot);
        }
    }

    public void LoadAll()
    {
        if (_hostLoaded)
        {
            return;
        }

        _hostLoaded = true;
        foreach (PluginSlot slot in _slots)
        {
            if (slot.IsEnabled && !slot.IsLoaded)
            {
                LoadSlot(slot);
            }
        }
    }

    public bool IsPluginEnabled(string pluginId)
    {
        PluginSlot? slot = FindSlot(pluginId);
        return slot != null && slot.IsEnabled;
    }

    public void SetPluginEnabled(string pluginId, bool enabled)
    {
        PluginSlot? slot = FindSlot(pluginId);
        if (slot == null)
        {
            return;
        }

        if (!slot.CanDisable && !enabled)
        {
            return;
        }

        if (slot.IsEnabled == enabled)
        {
            return;
        }

        slot.IsEnabled = enabled;
        _context.Settings.SetBool($"plugin.{pluginId}.enabled", enabled);

        if (_hostLoaded)
        {
            if (enabled && !slot.IsLoaded)
            {
                LoadSlot(slot);
            }
            else if (!enabled && slot.IsLoaded)
            {
                UnloadSlot(slot);
            }
        }

        _context.EventBus.Publish(new PluginStateChangedEvent(pluginId, slot.IsEnabled, slot.IsWindowVisible));
    }

    public bool IsWindowVisible(string pluginId)
    {
        PluginSlot? slot = FindSlot(pluginId);
        return slot == null || slot.IsWindowVisible;
    }

    public void SetWindowVisible(string pluginId, bool visible)
    {
        PluginSlot? slot = FindSlot(pluginId);
        if (slot == null || slot.IsWindowVisible == visible)
        {
            return;
        }

        slot.IsWindowVisible = visible;
        _context.Settings.SetBool($"plugin.{pluginId}.visible", visible);
        _context.EventBus.Publish(new PluginStateChangedEvent(pluginId, slot.IsEnabled, slot.IsWindowVisible));
    }

    public void Update(float deltaTime)
    {
        foreach (PluginSlot slot in _slots)
        {
            if (slot.IsEnabled && slot.IsLoaded)
            {
                slot.Plugin.OnUpdate(deltaTime);
            }
        }
    }

    public void RenderUI()
    {
        foreach (PluginSlot slot in _slots)
        {
            if (slot.IsEnabled && slot.IsLoaded)
            {
                slot.Plugin.OnRenderUI();
            }
        }
    }

    public void UnloadAll()
    {
        if (!_hostLoaded)
        {
            return;
        }

        for (int i = _slots.Count - 1; i >= 0; i--)
        {
            if (_slots[i].IsLoaded)
            {
                UnloadSlot(_slots[i]);
            }
        }

        _hostLoaded = false;
    }

    private void LoadSlot(PluginSlot slot)
    {
        try
        {
            slot.Plugin.OnLoaded(_context);
            slot.IsLoaded = true;
            _context.EventBus.Publish(new EngineLogEvent("PluginHost", "Info", $"Loaded plugin '{slot.Plugin.DisplayName}' ({slot.Plugin.Id})"));
        }
        catch (Exception ex)
        {
            slot.IsLoaded = false;
            _context.EventBus.Publish(new EngineLogEvent("PluginHost", "Error", $"Failed to load plugin '{slot.Plugin.Id}': {ex.Message}"));
        }
    }

    private void UnloadSlot(PluginSlot slot)
    {
        try
        {
            slot.Plugin.OnUnloaded();
        }
        catch (Exception ex)
        {
            _context.EventBus.Publish(new EngineLogEvent("PluginHost", "Error", $"Error unloading '{slot.Plugin.Id}': {ex.Message}"));
        }
        finally
        {
            slot.IsLoaded = false;
        }
    }

    private PluginSlot? FindSlot(string pluginId)
    {
        return _slots.FirstOrDefault(s => string.Equals(s.Plugin.Id, pluginId, StringComparison.Ordinal));
    }
}
