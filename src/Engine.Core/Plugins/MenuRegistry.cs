using Engine.Core.Abstractions;

namespace Engine.Core.Plugins;

public sealed class MenuRegistry : IMenuService
{
    private readonly List<MenuItemDefinition> _items = new();
    private readonly object _sync = new();
    private int _version;

    public int Version
    {
        get
        {
            lock (_sync)
            {
                return _version;
            }
        }
    }

    public IReadOnlyList<MenuItemDefinition> Items
    {
        get
        {
            lock (_sync)
            {
                return _items.ToArray();
            }
        }
    }

    public void Register(MenuItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Path);

        lock (_sync)
        {
            int idx = _items.FindIndex(x => string.Equals(x.Id, item.Id, StringComparison.Ordinal));
            if (idx >= 0)
            {
                _items[idx] = item;
            }
            else
            {
                _items.Add(item);
            }

            _version++;
        }
    }

    public void AddSeparator(string menuPath, string id, int order = 0)
    {
        Register(new MenuItemDefinition(
            Id: id,
            Path: menuPath,
            Callback: null,
            Order: order,
            IsSeparator: true));
    }

    public void Unregister(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        lock (_sync)
        {
            if (_items.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal)) > 0)
            {
                _version++;
            }
        }
    }

    public void UnregisterByPrefix(string idPrefix)
    {
        if (string.IsNullOrWhiteSpace(idPrefix))
        {
            return;
        }

        lock (_sync)
        {
            if (_items.RemoveAll(x => x.Id.StartsWith(idPrefix, StringComparison.Ordinal)) > 0)
            {
                _version++;
            }
        }
    }
}
