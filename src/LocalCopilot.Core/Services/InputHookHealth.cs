using System;
using System.Diagnostics;
using System.Threading;

namespace LocalCopilot_App.Services;

public enum InputHookKind
{
    Keyboard,
    Mouse
}

public sealed record InputHookHealthSnapshot(
    uint InstallThreadId,
    long KeyboardCallbacks,
    long MouseCallbacks,
    long KeyboardActivities,
    long MouseClickActivities,
    long MouseWheelActivities,
    long CallbackErrors,
    long SubscriberErrors,
    long ThreadMismatches,
    long UpTo100Microseconds,
    long UpTo500Microseconds,
    long UpTo1Millisecond,
    long UpTo5Milliseconds,
    long UpTo20Milliseconds,
    long Over20Milliseconds,
    double AverageCallbackMicroseconds,
    double MaximumCallbackMicroseconds)
{
    public long TotalCallbacks =>
        KeyboardCallbacks +
        MouseCallbacks;

    public long TotalActivities =>
        KeyboardActivities +
        MouseClickActivities +
        MouseWheelActivities;
}

public sealed class InputHookHealthMonitor
{
    private readonly long _timestampFrequency;

    private readonly long _upTo100MicrosecondsTicks;

    private readonly long _upTo500MicrosecondsTicks;

    private readonly long _upTo1MillisecondTicks;

    private readonly long _upTo5MillisecondsTicks;

    private readonly long _upTo20MillisecondsTicks;

    private long _keyboardCallbacks;

    private long _mouseCallbacks;

    private long _keyboardActivities;

    private long _mouseClickActivities;

    private long _mouseWheelActivities;

    private long _callbackErrors;

    private long _subscriberErrors;

    private long _threadMismatches;

    private long _upTo100Microseconds;

    private long _upTo500Microseconds;

    private long _upTo1Millisecond;

    private long _upTo5Milliseconds;

    private long _upTo20Milliseconds;

    private long _over20Milliseconds;

    private long _totalCallbackTicks;

    private long _maximumCallbackTicks;

    public InputHookHealthMonitor(
        uint installThreadId)
        : this(
            installThreadId,
            Stopwatch.Frequency)
    {
    }

    internal InputHookHealthMonitor(
        uint installThreadId,
        long timestampFrequency)
    {
        if (installThreadId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(installThreadId));
        }

        if (timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestampFrequency));
        }

        InstallThreadId =
            installThreadId;

        _timestampFrequency =
            timestampFrequency;

        _upTo100MicrosecondsTicks =
            ToTimestampTicks(
                100);

        _upTo500MicrosecondsTicks =
            ToTimestampTicks(
                500);

        _upTo1MillisecondTicks =
            ToTimestampTicks(
                1_000);

        _upTo5MillisecondsTicks =
            ToTimestampTicks(
                5_000);

        _upTo20MillisecondsTicks =
            ToTimestampTicks(
                20_000);
    }

    public uint InstallThreadId
    {
        get;
    }

    public void RecordCallback(
        InputHookKind hookKind,
        InputActivityKind? activityKind,
        long elapsedTicks,
        uint callbackThreadId,
        bool callbackFailed,
        bool subscriberFailed)
    {
        long safeElapsedTicks =
            Math.Max(
                0,
                elapsedTicks);

        if (hookKind ==
            InputHookKind.Keyboard)
        {
            Interlocked.Increment(
                ref _keyboardCallbacks);
        }
        else
        {
            Interlocked.Increment(
                ref _mouseCallbacks);
        }

        switch (activityKind)
        {
            case InputActivityKind.KeyboardActivity:
                Interlocked.Increment(
                    ref _keyboardActivities);
                break;

            case InputActivityKind.MouseClick:
                Interlocked.Increment(
                    ref _mouseClickActivities);
                break;

            case InputActivityKind.MouseWheel:
                Interlocked.Increment(
                    ref _mouseWheelActivities);
                break;
        }

        if (callbackFailed)
        {
            Interlocked.Increment(
                ref _callbackErrors);
        }

        if (subscriberFailed)
        {
            Interlocked.Increment(
                ref _subscriberErrors);
        }

        if (callbackThreadId !=
            InstallThreadId)
        {
            Interlocked.Increment(
                ref _threadMismatches);
        }

        Interlocked.Add(
            ref _totalCallbackTicks,
            safeElapsedTicks);

        SetMaximum(
            ref _maximumCallbackTicks,
            safeElapsedTicks);

        if (safeElapsedTicks <=
            _upTo100MicrosecondsTicks)
        {
            Interlocked.Increment(
                ref _upTo100Microseconds);
        }
        else if (safeElapsedTicks <=
            _upTo500MicrosecondsTicks)
        {
            Interlocked.Increment(
                ref _upTo500Microseconds);
        }
        else if (safeElapsedTicks <=
            _upTo1MillisecondTicks)
        {
            Interlocked.Increment(
                ref _upTo1Millisecond);
        }
        else if (safeElapsedTicks <=
            _upTo5MillisecondsTicks)
        {
            Interlocked.Increment(
                ref _upTo5Milliseconds);
        }
        else if (safeElapsedTicks <=
            _upTo20MillisecondsTicks)
        {
            Interlocked.Increment(
                ref _upTo20Milliseconds);
        }
        else
        {
            Interlocked.Increment(
                ref _over20Milliseconds);
        }
    }

    public InputHookHealthSnapshot Snapshot()
    {
        long keyboardCallbacks =
            Interlocked.Read(
                ref _keyboardCallbacks);

        long mouseCallbacks =
            Interlocked.Read(
                ref _mouseCallbacks);

        long totalCallbacks =
            keyboardCallbacks +
            mouseCallbacks;

        long totalCallbackTicks =
            Interlocked.Read(
                ref _totalCallbackTicks);

        return new InputHookHealthSnapshot(
            InstallThreadId,
            keyboardCallbacks,
            mouseCallbacks,
            Interlocked.Read(
                ref _keyboardActivities),
            Interlocked.Read(
                ref _mouseClickActivities),
            Interlocked.Read(
                ref _mouseWheelActivities),
            Interlocked.Read(
                ref _callbackErrors),
            Interlocked.Read(
                ref _subscriberErrors),
            Interlocked.Read(
                ref _threadMismatches),
            Interlocked.Read(
                ref _upTo100Microseconds),
            Interlocked.Read(
                ref _upTo500Microseconds),
            Interlocked.Read(
                ref _upTo1Millisecond),
            Interlocked.Read(
                ref _upTo5Milliseconds),
            Interlocked.Read(
                ref _upTo20Milliseconds),
            Interlocked.Read(
                ref _over20Milliseconds),
            totalCallbacks == 0
                ? 0
                : ToMicroseconds(
                      totalCallbackTicks) /
                  totalCallbacks,
            ToMicroseconds(
                Interlocked.Read(
                    ref _maximumCallbackTicks)));
    }

    private long ToTimestampTicks(
        long microseconds)
    {
        return checked(
            (long)Math.Ceiling(
                microseconds *
                _timestampFrequency /
                1_000_000d));
    }

    private double ToMicroseconds(
        long timestampTicks)
    {
        return
            timestampTicks *
            1_000_000d /
            _timestampFrequency;
    }

    private static void SetMaximum(
        ref long target,
        long candidate)
    {
        long observed =
            Interlocked.Read(
                ref target);

        while (candidate >
            observed)
        {
            long original =
                Interlocked.CompareExchange(
                    ref target,
                    candidate,
                    observed);

            if (original ==
                observed)
            {
                return;
            }

            observed =
                original;
        }
    }
}
