using Engine.Core.Abstractions;

namespace Engine.Core.Plugins;

public sealed class ScriptAssemblyScope : IScriptScope, IScopedEventBus
{
    private readonly record struct SubscriptionRecord(Type EventType, Delegate Handler, Action UnsubscribeAction);

    private readonly object _sync = new();
    private readonly IEventBus _innerBus;
    private readonly IGraphicsService _graphics;
    private readonly List<SubscriptionRecord> _subscriptions = new();
    private readonly HashSet<string> _trackedSurfaces = new(StringComparer.Ordinal);
    private readonly List<IExecutableNode> _trackedNodes = new();
    private bool _disposed;

    public ScriptAssemblyScope(IEventBus innerBus, IGraphicsService graphics)
    {
        _innerBus = innerBus ?? throw new ArgumentNullException(nameof(innerBus));
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
    }

    public int ActiveSubscriptionCount
    {
        get
        {
            lock (_sync)
            {
                return _subscriptions.Count;
            }
        }
    }

    public int TrackedSurfaceCount
    {
        get
        {
            lock (_sync)
            {
                return _trackedSurfaces.Count;
            }
        }
    }

    public int TrackedNodeCount
    {
        get
        {
            lock (_sync)
            {
                return _trackedNodes.Count;
            }
        }
    }

    public bool IsDisposed => _disposed;

    public void RegisterNode(IExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        lock (_sync)
        {
            if (_disposed || _trackedNodes.Contains(node))
            {
                return;
            }

            _trackedNodes.Add(node);
        }
    }

    public void Publish<T>(T evt)
    {
        _innerBus.Publish(evt);
    }

    public void Subscribe<T>(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            for (int i = 0; i < _subscriptions.Count; i++)
            {
                SubscriptionRecord existing = _subscriptions[i];
                if (existing.EventType == typeof(T) && Equals(existing.Handler, handler))
                {
                    return;
                }
            }

            _innerBus.Subscribe(handler);
            _subscriptions.Add(new SubscriptionRecord(
                typeof(T),
                handler,
                () => _innerBus.Unsubscribe(handler)));
        }
    }

    void IScriptScope.Subscribe<T>(Action<T> handler)
    {
        Subscribe(handler);
    }

    public void Unsubscribe<T>(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_sync)
        {
            for (int i = _subscriptions.Count - 1; i >= 0; i--)
            {
                SubscriptionRecord record = _subscriptions[i];
                if (record.EventType == typeof(T) && Equals(record.Handler, handler))
                {
                    _subscriptions.RemoveAt(i);
                    break;
                }
            }
        }

        _innerBus.Unsubscribe(handler);
    }

    public void TrackSurface(string surfaceName)
    {
        if (string.IsNullOrWhiteSpace(surfaceName))
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _trackedSurfaces.Add(surfaceName);
        }
    }

    public void Dispose()
    {
        Dispose(onNodeUnloadError: null);
    }

    public void Dispose(Action<IExecutableNode, Exception>? onNodeUnloadError)
    {
        SubscriptionRecord[] subs;
        string[] surfaces;
        IExecutableNode[] nodes;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            subs = _subscriptions.ToArray();
            _subscriptions.Clear();

            surfaces = _trackedSurfaces.ToArray();
            _trackedSurfaces.Clear();

            nodes = _trackedNodes.ToArray();
            _trackedNodes.Clear();
        }

        for (int i = 0; i < subs.Length; i++)
        {
            try
            {
                subs[i].UnsubscribeAction();
            }
            catch
            {
                // Ensure remaining handlers and surfaces are cleaned up.
            }
        }

        for (int i = nodes.Length - 1; i >= 0; i--)
        {
            IExecutableNode node = nodes[i];
            try
            {
                node.OnUnload();
            }
            catch (Exception ex)
            {
                onNodeUnloadError?.Invoke(node, ex);
            }
        }

        for (int i = 0; i < surfaces.Length; i++)
        {
            try
            {
                _graphics.ReleaseSurface(surfaces[i]);
            }
            catch
            {
                // Ignore cleanup failure for missing GPU context during shutdown.
            }
        }
    }
}

public sealed class PassThroughScopedEventBus : IScriptScope, IScopedEventBus
{
    private readonly IEventBus _innerBus;
    private readonly IGraphicsService? _graphics;
    private readonly HashSet<string> _trackedSurfaces = new(StringComparer.Ordinal);

    public PassThroughScopedEventBus(IEventBus innerBus, IGraphicsService? graphics = null)
    {
        _innerBus = innerBus ?? throw new ArgumentNullException(nameof(innerBus));
        _graphics = graphics;
    }

    public void Publish<T>(T evt) => _innerBus.Publish(evt);

    public void Subscribe<T>(Action<T> handler) => _innerBus.Subscribe(handler);

    void IScriptScope.Subscribe<T>(Action<T> handler) => _innerBus.Subscribe(handler);

    public void Unsubscribe<T>(Action<T> handler) => _innerBus.Unsubscribe(handler);

    public void TrackSurface(string surfaceName)
    {
        if (!string.IsNullOrWhiteSpace(surfaceName))
        {
            _trackedSurfaces.Add(surfaceName);
        }
    }

    public void Dispose()
    {
        if (_graphics != null)
        {
            foreach (string surface in _trackedSurfaces)
            {
                _graphics.ReleaseSurface(surface);
            }
        }

        _trackedSurfaces.Clear();
    }
}
