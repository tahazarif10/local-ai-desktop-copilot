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
    private long _nextEpochId;

    private ContextEpoch?
        _current;

    private CancellationTokenSource?
        _currentCancellation;

    private bool _disposed;

    public ContextEpoch? Current =>
        _current;

    public ContextEpoch Advance(
        ForegroundWindowSnapshot snapshot,
        PrivacyEvaluation privacy)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(ContextEpochManager));
        }

        ContextEpoch? previous =
            _current;

        if (_currentCancellation is not null)
        {
            DiagnosticLog.Write(
                "EPOCH.CANCEL_PREVIOUS",
                $"epoch={previous?.Id ?? 0}");

            _currentCancellation.Cancel();
            _currentCancellation.Dispose();
            _currentCancellation = null;
        }

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
            $"reason={privacy.Reason}");

        return epoch;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed =
            true;

        DiagnosticLog.Write(
            "EPOCH.DISPOSE",
            $"epoch={_current?.Id ?? 0}");

        if (_currentCancellation is not null)
        {
            _currentCancellation.Cancel();
            _currentCancellation.Dispose();
            _currentCancellation = null;
        }

        _current =
            null;
    }
}