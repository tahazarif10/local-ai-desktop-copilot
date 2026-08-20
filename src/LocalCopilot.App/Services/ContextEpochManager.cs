using LocalCopilot_App.Diagnostics;
using System;
using System.Threading;

namespace LocalCopilot_App.Services;

public sealed record ContextEpoch(
    long Id,
    DateTimeOffset StartedAt,
    ForegroundWindowSnapshot Snapshot,
    PrivacyEvaluation Privacy,
    CancellationToken CancellationToken);

public sealed class ContextEpochManager :
    IDisposable
{
    private long
        _nextEpochId;

    private ContextEpoch?
        _current;

    private CancellationTokenSource?
        _currentCancellation;

    private bool
        _disposed;

    public ContextEpoch? Current =>
        _current;

    public ContextEpoch GetOrAdvance(
        ForegroundWindowSnapshot snapshot,
        PrivacyEvaluation privacy)
    {
        ArgumentNullException.ThrowIfNull(
            snapshot);

        ArgumentNullException.ThrowIfNull(
            privacy);

        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(ContextEpochManager));
        }

        if (_current is not null &&
            IsSameContext(
                _current,
                snapshot,
                privacy))
        {
            DiagnosticLog.Write(
                "EPOCH.REUSE",
                $"epoch={_current.Id} " +
                $"hwnd=0x{snapshot.Handle.ToInt64():X} " +
                $"pid={snapshot.ProcessId} " +
                $"process={snapshot.ProcessName} " +
                $"privacy={privacy.Disposition} " +
                $"rule={privacy.RuleId}");

            return _current;
        }

        ContextEpoch? previous =
            _current;

        CancelCurrent(
            "advance");

        CancellationTokenSource cancellation =
            new();

        long id =
            Interlocked.Increment(
                ref _nextEpochId);

        ContextEpoch epoch =
            new(
                id,
                DateTimeOffset.Now,
                snapshot,
                privacy,
                cancellation.Token);

        _currentCancellation =
            cancellation;

        _current =
            epoch;

        DiagnosticLog.Write(
            "EPOCH.ADVANCE",
            $"epoch={epoch.Id} " +
            $"previousEpoch={previous?.Id ?? 0} " +
            $"hwnd=0x{snapshot.Handle.ToInt64():X} " +
            $"pid={snapshot.ProcessId} " +
            $"process={snapshot.ProcessName} " +
            $"privacy={privacy.Disposition} " +
            $"rule={privacy.RuleId} " +
            $"reason={privacy.Reason}");

        return epoch;
    }

    public void Reset()
    {
        if (_disposed)
            return;

        DiagnosticLog.Write(
            "EPOCH.RESET",
            $"epoch={_current?.Id ?? 0}");

        CancelCurrent(
            "reset");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        DiagnosticLog.Write(
            "EPOCH.DISPOSE",
            $"epoch={_current?.Id ?? 0}");

        CancelCurrent(
            "dispose");

        _disposed =
            true;
    }

    private void CancelCurrent(
        string reason)
    {
        ContextEpoch? previous =
            _current;

        CancellationTokenSource? cancellation =
            _currentCancellation;

        _current =
            null;

        _currentCancellation =
            null;

        if (cancellation is null)
            return;

        DiagnosticLog.Write(
            "EPOCH.CANCEL_PREVIOUS",
            $"epoch={previous?.Id ?? 0} " +
            $"reason={reason}");

        try
        {
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private static bool IsSameContext(
        ContextEpoch current,
        ForegroundWindowSnapshot snapshot,
        PrivacyEvaluation privacy)
    {
        return
            current.Snapshot.Handle ==
                snapshot.Handle &&
            current.Snapshot.ProcessId ==
                snapshot.ProcessId &&
            string.Equals(
                current.Snapshot.ProcessName,
                snapshot.ProcessName,
                StringComparison.OrdinalIgnoreCase) &&
            current.Privacy.Disposition ==
                privacy.Disposition &&
            string.Equals(
                current.Privacy.RuleId,
                privacy.RuleId,
                StringComparison.Ordinal) &&
            string.Equals(
                current.Privacy.Reason,
                privacy.Reason,
                StringComparison.Ordinal);
    }
}