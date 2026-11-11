using Engine.Core.Abstractions;

namespace Engine.Core.Events;

public sealed class EventBus : IEventBus
{
    private readonly object _sync = new();
    private readonly Dictionary<Type, List<Delegate>> _subscribers = new();

    public void Publish<T>(T evt)
    {
        Delegate[] snapshot;
        lock (_sync)
        {
            if (!_subscribers.TryGetValue(typeof(T), out List<Delegate>? handlers) || handlers.Count == 0)
            {
                return;
            }

            snapshot = handlers.ToArray();
        }

        foreach (Delegate handler in snapshot)
        {
            if (handler is Action<T> typedHandler)
            {
                typedHandler(evt);
            }
        }
    }

    public void Subscribe<T>(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_sync)
        {
            if (!_subscribers.TryGetValue(typeof(T), out List<Delegate>? handlers))
            {
                handlers = new List<Delegate>();
                _subscribers[typeof(T)] = handlers;
            }

            if (!handlers.Contains(handler))
            {
                handlers.Add(handler);
            }
        }
    }

    public void Unsubscribe<T>(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_sync)
        {
            if (_subscribers.TryGetValue(typeof(T), out List<Delegate>? handlers))
            {
                handlers.Remove(handler);
                if (handlers.Count == 0)
                {
                    _subscribers.Remove(typeof(T));
                }
            }
        }
    }
}
