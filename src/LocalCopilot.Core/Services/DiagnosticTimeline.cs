using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace LocalCopilot_App.Services;

public sealed class DiagnosticTimeline
{
    private const int DefaultCapacity =
        256;

    private static readonly TimeSpan DefaultRetention =
        TimeSpan.FromSeconds(
            5);

    private readonly object _gate =
        new();

    private readonly Queue<InputActivityEvent> _events =
        new();

    private readonly int _capacity;

    private readonly long _retentionTicks;

    private long _currentEpochId;

    internal int EventCount
    {
        get
        {
            lock (_gate)
            {
                return _events.Count;
            }
        }
    }

    public DiagnosticTimeline(
        int capacity = DefaultCapacity,
        TimeSpan? retention = null)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity));
        }

        TimeSpan effectiveRetention =
            retention ??
            DefaultRetention;

        if (effectiveRetention <=
            TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retention));
        }

        _capacity =
            capacity;

        _retentionTicks =
            ToStopwatchTicks(
                effectiveRetention);
    }

    public void BeginEpoch(
        long epochId)
    {
        if (epochId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(epochId));
        }

        lock (_gate)
        {
            if (_currentEpochId ==
                epochId)
            {
                return;
            }

            _events.Clear();

            _currentEpochId =
                epochId;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _events.Clear();

            _currentEpochId =
                0;
        }
    }

    public void Record(
        InputActivityEvent activity)
    {
        ArgumentNullException.ThrowIfNull(
            activity);

        lock (_gate)
        {
            if (
                _currentEpochId <= 0 ||
                activity.EpochId !=
                    _currentEpochId)
            {
                return;
            }

            PruneExpired(
                activity.TimestampTicks);

            while (_events.Count >=
                _capacity)
            {
                _events.Dequeue();
            }

            _events.Enqueue(
                activity);
        }
    }

    public bool TryReadMostRecent(
        long epochId,
        long observedAtTicks,
        TimeSpan lookback,
        out InputActivityEvent? activity)
    {
        if (lookback <=
            TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lookback));
        }

        activity =
            null;

        long lookbackTicks =
            ToStopwatchTicks(
                lookback);

        lock (_gate)
        {
            if (
                epochId <= 0 ||
                epochId !=
                    _currentEpochId)
            {
                return false;
            }

            PruneExpired(
                observedAtTicks);

            foreach (InputActivityEvent candidate in
                _events)
            {
                long ageTicks =
                    observedAtTicks -
                    candidate.TimestampTicks;

                if (
                    candidate.EpochId ==
                        epochId &&
                    ageTicks >= 0 &&
                    ageTicks <=
                        lookbackTicks)
                {
                    activity =
                        candidate;
                }
            }

            return true;
        }
    }

    private void PruneExpired(
        long nowTicks)
    {
        while (_events.Count > 0)
        {
            InputActivityEvent oldest =
                _events.Peek();

            long ageTicks =
                nowTicks -
                oldest.TimestampTicks;

            if (
                ageTicks >= 0 &&
                ageTicks <=
                    _retentionTicks)
            {
                break;
            }

            _events.Dequeue();
        }
    }

    private static long ToStopwatchTicks(
        TimeSpan duration)
    {
        return checked(
            (long)Math.Ceiling(
                duration.TotalSeconds *
                Stopwatch.Frequency));
    }
}
