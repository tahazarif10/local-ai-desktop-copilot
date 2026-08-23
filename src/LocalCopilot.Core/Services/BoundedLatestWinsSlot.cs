using System;

namespace LocalCopilot_App.Services;

/// <summary>
/// Holds at most one pending item. A newer publication replaces the older
/// pending item; an item already taken by a consumer is outside this slot.
/// </summary>
public sealed class BoundedLatestWinsSlot<T>
    where T : class
{
    private readonly object _gate = new();
    private T? _pending;
    private bool _completed;

    public bool TryPublish(
        T item,
        out T? replaced)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (_gate)
        {
            if (_completed)
            {
                replaced = null;
                return false;
            }

            replaced = _pending;
            _pending = item;
            return true;
        }
    }

    public bool TryTake(
        out T? item)
    {
        lock (_gate)
        {
            item = _pending;
            _pending = null;
            return item is not null;
        }
    }

    public T? Complete()
    {
        lock (_gate)
        {
            if (_completed)
            {
                return null;
            }

            _completed = true;

            T? pending = _pending;
            _pending = null;
            return pending;
        }
    }
}
