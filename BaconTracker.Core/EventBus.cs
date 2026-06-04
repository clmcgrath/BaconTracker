using System;
using System.Collections.Generic;
using Serilog;

namespace BaconTracker.Core;

public static class EventBus
{
    private static readonly Dictionary<string, List<Action<object[]>>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    /// <summary>
    /// Subscribes a handler to a specific event.
    /// </summary>
    public static void Subscribe(string eventName, Action<object[]> handler)
    {
        if (handler == null) return;
        lock (_lock)
        {
            if (!_subscriptions.TryGetValue(eventName, out var list))
            {
                list = new List<Action<object[]>>();
                _subscriptions[eventName] = list;
            }
            list.Add(handler);
        }
    }

    /// <summary>
    /// Unsubscribes a handler from an event.
    /// </summary>
    public static void Unsubscribe(string eventName, Action<object[]> handler)
    {
        if (handler == null) return;
        lock (_lock)
        {
            if (_subscriptions.TryGetValue(eventName, out var list))
            {
                list.Remove(handler);
                if (list.Count == 0)
                {
                    _subscriptions.Remove(eventName);
                }
            }
        }
    }

    /// <summary>
    /// Publishes (dispatches) an event to all subscribers.
    /// </summary>
    public static void Publish(string eventName, params object[] args)
    {
        List<Action<object[]>> handlersCopy;
        lock (_lock)
        {
            if (!_subscriptions.TryGetValue(eventName, out var list))
                return;
            handlersCopy = new List<Action<object[]>>(list);
        }

        foreach (var handler in handlersCopy)
        {
            try
            {
                handler(args);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error executing handler for event {EventName}", eventName);
            }
        }
    }
}
