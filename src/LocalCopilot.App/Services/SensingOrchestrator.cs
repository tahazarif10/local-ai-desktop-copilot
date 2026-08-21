using LocalCopilot_App.Diagnostics;
using Microsoft.UI.Dispatching;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LocalCopilot_App.Services;

public enum SensingOrchestratorPhase
{
    Off,
    WaitingForContext,
    Settling,
    WaitingForIdle,
    Running,
    Blocked,
    TargetUnavailable,
    Error
}

public sealed record SensingOrchestratorUpdate(
    bool Armed,
    SensingOrchestratorPhase Phase,
    long EpochId,
    string Reason);

public sealed class SensingOrchestrator
{
    private const int
        ProfileWidth =
            640;

    private static readonly TimeSpan
        SampleInterval =
            TimeSpan.FromMilliseconds(
                500);

    private static readonly TimeSpan
        SettleDelay =
            TimeSpan.FromMilliseconds(
                200);

    private static readonly TimeSpan
        IdlePollInterval =
            TimeSpan.FromMilliseconds(
                25);

    private static readonly TimeSpan
        IdleWaitTimeout =
            TimeSpan.FromMilliseconds(
                1500);

    private readonly object
        _gate =
            new();

    private readonly PersistentChangeDetectionService
        _persistentService;

    private readonly DispatcherQueue
        _dispatcher;

    private bool
        _armed;

    private ContextEpoch?
        _targetEpoch;

    private long
        _generation;

    private CancellationTokenSource?
        _pendingSettle;

    public SensingOrchestrator(
        PersistentChangeDetectionService persistentService,
        DispatcherQueue dispatcher)
    {
        _persistentService =
            persistentService ??
            throw new ArgumentNullException(
                nameof(persistentService));

        _dispatcher =
            dispatcher ??
            throw new ArgumentNullException(
                nameof(dispatcher));
    }

    public event Action<SensingOrchestratorUpdate>?
        StatusChanged;

    public bool IsArmed
    {
        get
        {
            lock (_gate)
            {
                return _armed;
            }
        }
    }

    public void Arm(
        ContextEpoch? currentEpoch)
    {
        bool alreadyArmed;

        lock (_gate)
        {
            alreadyArmed =
                _armed;

            _armed =
                true;
        }

        DiagnosticLog.Write(
            alreadyArmed
                ? "ORCH.ARM_REUSE"
                : "ORCH.ARM",
            $"currentEpoch={currentEpoch?.Id ?? 0}");

        if (currentEpoch is null)
        {
            Publish(
                new SensingOrchestratorUpdate(
                    true,
                    SensingOrchestratorPhase.WaitingForContext,
                    0,
                    "waiting_for_context"));

            return;
        }

        ObserveContext(
            currentEpoch);
    }

    public void ObserveContext(
        ContextEpoch epoch)
    {
        ArgumentNullException.ThrowIfNull(
            epoch);

        CancellationTokenSource?
            previousPending =
                null;

        CancellationTokenSource?
            newPending =
                null;

        bool sameEpoch =
            false;

        bool blocked =
            false;

        long generation =
            0;

        lock (_gate)
        {
            if (!_armed)
                return;

            if (
                _targetEpoch is not null &&
                _targetEpoch.Id ==
                    epoch.Id)
            {
                sameEpoch =
                    true;
            }
            else
            {
                previousPending =
                    _pendingSettle;

                _pendingSettle =
                    null;

                _targetEpoch =
                    epoch;

                generation =
                    ++_generation;

                blocked =
                    !epoch.Privacy
                        .Allows(
                            PrivacyCapability.CapturePixels);

                if (!blocked)
                {
                    newPending =
                        CancellationTokenSource
                            .CreateLinkedTokenSource(
                                epoch.CancellationToken,
                                CancellationToken.None);

                    _pendingSettle =
                        newPending;
                }
            }
        }

        if (sameEpoch)
        {
            DiagnosticLog.Write(
                "ORCH.REUSE",
                $"epoch={epoch.Id}");

            return;
        }

        if (previousPending is not null)
        {
            try
            {
                previousPending.Cancel();
            previousPending.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (blocked)
        {
            DiagnosticLog.Write(
                "ORCH.BLOCKED",
                $"epoch={epoch.Id} " +
                $"process={epoch.Snapshot.ProcessName} " +
                $"rule={epoch.Privacy.RuleId}");

            Publish(
                new SensingOrchestratorUpdate(
                    true,
                    SensingOrchestratorPhase.Blocked,
                    epoch.Id,
                    "privacy_blocked"));

            return;
        }

        DiagnosticLog.Write(
            "ORCH.OBSERVE_ALLOWED",
            $"epoch={epoch.Id} " +
            $"hwnd=0x{epoch.Snapshot.Handle.ToInt64():X} " +
            $"pid={epoch.Snapshot.ProcessId} " +
            $"process={epoch.Snapshot.ProcessName}");

        _ =
            SettleAndStartAsync(
                epoch,
                generation,
                newPending!);
    }

    public void Disarm(
        string reason)
    {
        if (string.IsNullOrWhiteSpace(
                reason))
        {
            reason =
                "disarmed";
        }

        CancellationTokenSource?
            pending;

        lock (_gate)
        {
            _armed =
                false;

            _targetEpoch =
                null;

            _generation++;

            pending =
                _pendingSettle;

            _pendingSettle =
                null;
        }

        if (pending is not null)
        {
            try
            {
                pending.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        DiagnosticLog.Write(
            "ORCH.DISARM",
            $"reason={reason}");

        Publish(
            new SensingOrchestratorUpdate(
                false,
                SensingOrchestratorPhase.Off,
                0,
                reason));

        try
        {
            _persistentService.Stop(
                reason);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                "ORCH.STOP_ERROR",
                $"type={ex.GetType().Name}");

            Publish(
                new SensingOrchestratorUpdate(
                    false,
                    SensingOrchestratorPhase.Error,
                    0,
                    "stop_error"));
        }
    }

    private async Task SettleAndStartAsync(
        ContextEpoch epoch,
        long generation,
        CancellationTokenSource settleCancellation)
    {
        CancellationToken token =
            settleCancellation.Token;

        try
        {
            DiagnosticLog.Write(
                "ORCH.SETTLE_BEGIN",
                $"epoch={epoch.Id} " +
                $"delayMs={SettleDelay.TotalMilliseconds:0}");

            Publish(
                new SensingOrchestratorUpdate(
                    true,
                    SensingOrchestratorPhase.Settling,
                    epoch.Id,
                    "settling"));

            await Task.Delay(
                    SettleDelay,
                    token)
                .ConfigureAwait(
                    false);

            token.ThrowIfCancellationRequested();

            if (!IsCurrentTarget(
                    epoch,
                    generation))
            {
                DiagnosticLog.Write(
                    "ORCH.SETTLE_OBSOLETE",
                    $"epoch={epoch.Id}");

                return;
            }

            Stopwatch idleWait =
                Stopwatch.StartNew();

            bool waitingPublished =
                false;

            while (true)
            {
                token.ThrowIfCancellationRequested();

                if (!IsCurrentTarget(
                        epoch,
                        generation))
                {
                    DiagnosticLog.Write(
                        "ORCH.START_OBSOLETE",
                        $"epoch={epoch.Id}");

                    return;
                }

                if (_persistentService
                    .HasActiveSession)
                {
                    if (!waitingPublished)
                    {
                        waitingPublished =
                            true;

                        DiagnosticLog.Write(
                            "ORCH.WAIT_IDLE",
                            $"epoch={epoch.Id}");

                        Publish(
                            new SensingOrchestratorUpdate(
                                true,
                                SensingOrchestratorPhase.WaitingForIdle,
                                epoch.Id,
                                "waiting_for_previous_session"));
                    }

                    if (idleWait.Elapsed >=
                        IdleWaitTimeout)
                    {
                        DiagnosticLog.Write(
                            "ORCH.WAIT_IDLE_TIMEOUT",
                            $"epoch={epoch.Id} " +
                            $"waitMs={idleWait.Elapsed.TotalMilliseconds:0}");

                        PublishIfCurrent(
                            epoch,
                            generation,
                            new SensingOrchestratorUpdate(
                                true,
                                SensingOrchestratorPhase.Error,
                                epoch.Id,
                                "previous_session_cleanup_timeout"));

                        return;
                    }

                    await Task.Delay(
                            IdlePollInterval,
                            token)
                        .ConfigureAwait(
                            false);

                    continue;
                }

                bool started =
                    await TryStartOnDispatcherAsync(
                            epoch,
                            generation,
                            token)
                        .ConfigureAwait(
                            false);

                if (started)
                {
                    DiagnosticLog.Write(
                        "ORCH.START",
                        $"epoch={epoch.Id} " +
                        $"profile={ProfileWidth} " +
                        $"cadenceMs={SampleInterval.TotalMilliseconds:0}");

                    PublishIfCurrent(
                        epoch,
                        generation,
                        new SensingOrchestratorUpdate(
                            true,
                            SensingOrchestratorPhase.Running,
                            epoch.Id,
                            "running"));

                    return;
                }

                if (idleWait.Elapsed >=
                    IdleWaitTimeout)
                {
                    DiagnosticLog.Write(
                        "ORCH.START_TIMEOUT",
                        $"epoch={epoch.Id}");

                    PublishIfCurrent(
                        epoch,
                        generation,
                        new SensingOrchestratorUpdate(
                            true,
                            SensingOrchestratorPhase.Error,
                            epoch.Id,
                            "start_timeout"));

                    return;
                }

                await Task.Delay(
                        IdlePollInterval,
                        token)
                    .ConfigureAwait(
                        false);
            }
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
            DiagnosticLog.Write(
                "ORCH.SETTLE_CANCELLED",
                $"epoch={epoch.Id}");
        }
        catch (GraphicsCaptureTargetUnavailableException ex)
        {
            DiagnosticLog.Write(
                "ORCH.TARGET_UNAVAILABLE",
                $"epoch={epoch.Id} " +
                $"hwnd=0x{epoch.Snapshot.Handle.ToInt64():X} " +
                $"pid={epoch.Snapshot.ProcessId} " +
                $"process={epoch.Snapshot.ProcessName} " +
                $"hresult=0x{ex.HResult:X8}");

            PublishIfCurrent(
                epoch,
                generation,
                new SensingOrchestratorUpdate(
                    true,
                    SensingOrchestratorPhase.TargetUnavailable,
                    epoch.Id,
                    "capture_target_unavailable"));
        }
        catch (Exception ex)
        {
            DiagnosticLog.WriteException(
                "ORCH.START_ERROR",
                ex,
                $"epoch={epoch.Id}");

            PublishIfCurrent(
                epoch,
                generation,
                new SensingOrchestratorUpdate(
                    true,
                    SensingOrchestratorPhase.Error,
                    epoch.Id,
                    "start_error"));
        }
        finally
        {
            ClearPending(
                settleCancellation);
        }
    }

    private async Task<bool>
        TryStartOnDispatcherAsync(
            ContextEpoch epoch,
            long generation,
            CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        TaskCompletionSource<bool> completion =
            new(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        using CancellationTokenRegistration
            registration =
                token.Register(
                    () =>
                    {
                        completion.TrySetCanceled(
                            token);
                    });

        bool queued =
            _dispatcher.TryEnqueue(
                DispatcherQueuePriority.Normal,
                () =>
                {
                    if (
                        token.IsCancellationRequested ||
                        !IsCurrentTarget(
                            epoch,
                            generation))
                    {
                        completion.TrySetResult(
                            false);

                        return;
                    }

                    if (_persistentService
                        .HasActiveSession)
                    {
                        completion.TrySetResult(
                            false);

                        return;
                    }

                    try
                    {
                        _persistentService.Start(
                            epoch,
                            ProfileWidth,
                            SampleInterval);

                        completion.TrySetResult(
                            true);
                    }
                    catch (Exception ex)
                    {
                        completion.TrySetException(
                            ex);
                    }
                });

        if (!queued)
        {
            DiagnosticLog.Write(
                "ORCH.DISPATCH_REJECT",
                $"epoch={epoch.Id}");

            throw new InvalidOperationException(
                "UI dispatcher rejected orchestrator start.");
        }

        return await completion.Task
            .ConfigureAwait(
                false);
    }

    private bool IsCurrentTarget(
        ContextEpoch epoch,
        long generation)
    {
        lock (_gate)
        {
            return
                _armed &&
                _generation ==
                    generation &&
                ReferenceEquals(
                    _targetEpoch,
                    epoch) &&
                !epoch.CancellationToken
                    .IsCancellationRequested &&
                epoch.Privacy.Allows(
                    PrivacyCapability.CapturePixels);
        }
    }

    private void PublishIfCurrent(
        ContextEpoch epoch,
        long generation,
        SensingOrchestratorUpdate update)
    {
        if (!IsCurrentTarget(
                epoch,
                generation))
        {
            return;
        }

        Publish(
            update);
    }

    private void ClearPending(
        CancellationTokenSource cancellation)
    {
        lock (_gate)
        {
            if (ReferenceEquals(
                    _pendingSettle,
                    cancellation))
            {
                _pendingSettle =
                    null;
            }
        }

        cancellation.Dispose();
    }

    private void Publish(
        SensingOrchestratorUpdate update)
    {
        try
        {
            StatusChanged?.Invoke(
                update);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                "ORCH.STATUS_EVENT_ERROR",
                $"type={ex.GetType().Name}");
        }
    }
}
