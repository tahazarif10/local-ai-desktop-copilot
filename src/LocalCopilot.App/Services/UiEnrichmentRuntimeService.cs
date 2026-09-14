using LocalCopilot_App.Diagnostics;
using Microsoft.UI.Dispatching;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LocalCopilot_App.Services;

/// <summary>
/// Runtime owner that binds the accepted M3.4 admission policy to the existing
/// M3.3 semantic snapshot worker. It owns no persistent content: every semantic
/// result is publication-gated, observed only through content-free metadata,
/// and then cleared immediately.
/// </summary>
internal sealed class UiEnrichmentRuntimeService :
    IDisposable
{
    private static readonly TimeSpan BackgroundDebounce =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan SemanticRequestDeadline =
        TimeSpan.FromMilliseconds(2500);

    private readonly DispatcherQueue _uiDispatcher;
    private readonly ContextEpochManager _contextEpochManager;
    private readonly SensingOrchestrator _sensingOrchestrator;
    private readonly PersistentChangeDetectionService _persistentChangeDetectionService;
    private readonly UiAutomationProbeWorker _uiAutomationProbeWorker;
    private readonly UiEnrichmentOrchestrationPolicy _policy;
    private readonly object _lifecycleGate = new();

    private bool _stopped;
    private bool _disposed;
    private long _nextBackgroundSourceId;
    private long _nextQuestionSourceId;

    public UiEnrichmentRuntimeService(
        DispatcherQueue uiDispatcher,
        ContextEpochManager contextEpochManager,
        SensingOrchestrator sensingOrchestrator,
        PersistentChangeDetectionService persistentChangeDetectionService,
        UiAutomationProbeWorker uiAutomationProbeWorker)
    {
        _uiDispatcher =
            uiDispatcher ??
            throw new ArgumentNullException(nameof(uiDispatcher));
        _contextEpochManager =
            contextEpochManager ??
            throw new ArgumentNullException(nameof(contextEpochManager));
        _sensingOrchestrator =
            sensingOrchestrator ??
            throw new ArgumentNullException(nameof(sensingOrchestrator));
        _persistentChangeDetectionService =
            persistentChangeDetectionService ??
            throw new ArgumentNullException(nameof(persistentChangeDetectionService));
        _uiAutomationProbeWorker =
            uiAutomationProbeWorker ??
            throw new ArgumentNullException(nameof(uiAutomationProbeWorker));
        _policy =
            new UiEnrichmentOrchestrationPolicy(BackgroundDebounce);

        _persistentChangeDetectionService.SampleReady +=
            PersistentChangeDetectionService_SampleReady;
        _sensingOrchestrator.StatusChanged +=
            SensingOrchestrator_StatusChanged;

        DiagnosticLog.Write(
            "UIENRICH.START",
            $"backgroundDebounceMs={BackgroundDebounce.TotalMilliseconds:0} " +
            $"requestDeadlineMs={SemanticRequestDeadline.TotalMilliseconds:0}");
    }

    public UiEnrichmentAdmissionDecision RequestUserQuestion()
    {
        EnsureUiThread();

        ContextEpoch? epoch =
            _contextEpochManager.Current;

        long sourceId =
            Interlocked.Increment(ref _nextQuestionSourceId);

        if (epoch is null)
        {
            UiEnrichmentAdmissionDecision rejected =
                new(
                    UiEnrichmentAdmissionOutcome.Rejected,
                    UiEnrichmentRejectionReason.NoCurrentEpoch,
                    Request: null,
                    ReplacedPending: null,
                    UiEnrichmentInvalidation.Empty);

            LogAdmission(
                UiEnrichmentTriggerKind.UserQuestion,
                epochId: 0,
                sourceId,
                classification: null,
                rejected);

            return rejected;
        }

        UiEnrichmentTrigger trigger =
            new(
                epoch.Id,
                sourceId,
                UiEnrichmentTriggerKind.UserQuestion);

        return AdmitOnUiThread(trigger);
    }

    public void Stop(
        string reason)
    {
        lock (_lifecycleGate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
        }

        _sensingOrchestrator.StatusChanged -=
            SensingOrchestrator_StatusChanged;
        _persistentChangeDetectionService.SampleReady -=
            PersistentChangeDetectionService_SampleReady;

        UiEnrichmentInvalidation invalidated =
            _policy.Stop();

        LogInvalidation(
            string.IsNullOrWhiteSpace(reason)
                ? "runtime_stop"
                : reason,
            invalidated);

        DiagnosticLog.Write(
            "UIENRICH.STOP",
            $"reason={(string.IsNullOrWhiteSpace(reason) ? "runtime_stop" : reason)}");
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop("dispose");
        GC.SuppressFinalize(this);
    }

    private void SensingOrchestrator_StatusChanged(
        SensingOrchestratorUpdate update)
    {
        if (_uiDispatcher.HasThreadAccess)
        {
            ApplyOrchestratorStatusOnUiThread(update);
            return;
        }

        bool queued =
            _uiDispatcher.TryEnqueue(
                DispatcherQueuePriority.High,
                () => ApplyOrchestratorStatusOnUiThread(update));

        if (!queued)
        {
            DiagnosticLog.Write(
                "UIENRICH.UI_QUEUE_REJECT",
                "source=orchestrator_status");
        }
    }

    private void PersistentChangeDetectionService_SampleReady(
        PersistentChangeSample sample)
    {
        long sourceId =
            Interlocked.Increment(ref _nextBackgroundSourceId);

        if (_uiDispatcher.HasThreadAccess)
        {
            AdmitBackgroundOnUiThread(sample, sourceId);
            return;
        }

        bool queued =
            _uiDispatcher.TryEnqueue(
                () => AdmitBackgroundOnUiThread(sample, sourceId));

        if (!queued)
        {
            DiagnosticLog.Write(
                "UIENRICH.UI_QUEUE_REJECT",
                $"source=background_change epoch={sample.EpochId}");
        }
    }

    private void ApplyOrchestratorStatusOnUiThread(
        SensingOrchestratorUpdate update)
    {
        if (IsStopped())
        {
            return;
        }

        UiEnrichmentOrchestrationState state =
            _policy.GetState();

        if (!update.Armed)
        {
            InvalidateOnUiThread(
                string.IsNullOrWhiteSpace(update.Reason)
                    ? "orchestrator_disarmed"
                    : update.Reason);
            return;
        }

        if (update.EpochId > 0 &&
            state.EpochId > 0 &&
            state.EpochId != update.EpochId)
        {
            InvalidateOnUiThread("epoch_changed");
        }
    }

    private void AdmitBackgroundOnUiThread(
        PersistentChangeSample sample,
        long sourceId)
    {
        if (IsStopped())
        {
            return;
        }

        UiEnrichmentTrigger trigger =
            new(
                sample.EpochId,
                sourceId,
                UiEnrichmentTriggerKind.BackgroundChange,
                sample.Change.Classification);

        AdmitOnUiThread(trigger);
    }

    private UiEnrichmentAdmissionDecision AdmitOnUiThread(
        UiEnrichmentTrigger trigger)
    {
        EnsureUiThread();

        UiEnrichmentAdmissionDecision decision =
            _policy.Admit(
                trigger,
                _sensingOrchestrator.IsArmed,
                _contextEpochManager.Current);

        LogAdmission(
            trigger.Kind,
            trigger.EpochId,
            trigger.SourceId,
            trigger.Classification,
            decision);

        if (decision.Invalidated.Count > 0)
        {
            LogInvalidation(
                "admission_revalidation",
                decision.Invalidated);
        }

        if (decision.ReplacedPending is not null)
        {
            DiagnosticLog.Write(
                "UIENRICH.REPLACE_PENDING",
                $"request={decision.ReplacedPending.RequestId} " +
                $"epoch={decision.ReplacedPending.EpochId} " +
                $"kind={decision.ReplacedPending.Kind} " +
                $"source={decision.ReplacedPending.SourceId}");
        }

        if (decision.Outcome == UiEnrichmentAdmissionOutcome.DispatchNow &&
            decision.Request is not null)
        {
            DispatchOnUiThread(decision.Request);
        }

        return decision;
    }

    private void DispatchOnUiThread(
        UiEnrichmentRequest request)
    {
        EnsureUiThread();

        if (IsStopped())
        {
            return;
        }

        ContextEpoch? epoch =
            _contextEpochManager.Current;

        if (!UiEnrichmentRuntimeGate.MayDispatch(
                request,
                _sensingOrchestrator.IsArmed,
                epoch))
        {
            DiagnosticLog.Write(
                "UIENRICH.DISPATCH_REJECT",
                $"request={request.RequestId} " +
                $"epoch={request.EpochId} " +
                $"kind={request.Kind} " +
                "reason=context_revalidation");

            CompleteAndDispatchNextOnUiThread(request.RequestId);
            return;
        }

        UiAutomationProbeOperation operation =
            _uiAutomationProbeWorker.CaptureSemanticSnapshot(
                epoch!,
                SemanticRequestDeadline);

        DiagnosticLog.Write(
            "UIENRICH.DISPATCH",
            $"request={request.RequestId} " +
            $"workerRequest={operation.RequestId} " +
            $"epoch={request.EpochId} " +
            $"kind={request.Kind} " +
            $"source={request.SourceId}");

        _ = ObserveWorkerCompletionAsync(request, operation);
    }

    private async Task ObserveWorkerCompletionAsync(
        UiEnrichmentRequest request,
        UiAutomationProbeOperation operation)
    {
        UiAutomationProbeResult result;

        try
        {
            result =
                await operation.Completion.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            QueueDispatchFailure(request, ex);
            return;
        }

        bool queued =
            _uiDispatcher.TryEnqueue(
                DispatcherQueuePriority.High,
                () => HandleWorkerCompletionOnUiThread(
                    request,
                    operation.RequestId,
                    result));

        if (queued)
        {
            return;
        }

        result.SemanticSnapshot?.Dispose();

        UiEnrichmentInvalidation invalidated =
            _policy.Invalidate();

        LogInvalidation(
            "publication_ui_queue_rejected",
            invalidated);

        DiagnosticLog.Write(
            "UIENRICH.UI_QUEUE_REJECT",
            $"source=worker_completion request={request.RequestId}");
    }

    private void QueueDispatchFailure(
        UiEnrichmentRequest request,
        Exception exception)
    {
        bool queued =
            _uiDispatcher.TryEnqueue(
                DispatcherQueuePriority.High,
                () =>
                {
                    if (IsStopped())
                    {
                        return;
                    }

                    DiagnosticLog.WriteException(
                        "UIENRICH.DISPATCH_ERROR",
                        exception,
                        $"request={request.RequestId} " +
                        $"epoch={request.EpochId} " +
                        $"kind={request.Kind}");

                    CompleteAndDispatchNextOnUiThread(request.RequestId);
                });

        if (queued)
        {
            return;
        }

        UiEnrichmentInvalidation invalidated =
            _policy.Invalidate();

        LogInvalidation(
            "dispatch_error_ui_queue_rejected",
            invalidated);
    }

    private void HandleWorkerCompletionOnUiThread(
        UiEnrichmentRequest request,
        long workerRequestId,
        UiAutomationProbeResult result)
    {
        EnsureUiThread();

        if (IsStopped())
        {
            result.SemanticSnapshot?.Dispose();

            DiagnosticLog.Write(
                "UIENRICH.RESULT_DROP",
                $"request={request.RequestId} reason=runtime_stopped");

            return;
        }

        try
        {
            UiAutomationProbeResult publishable =
                UiEnrichmentRuntimeGate.ApplyPublication(
                    request,
                    result,
                    _contextEpochManager.Current,
                    workerRequestId,
                    _sensingOrchestrator.IsArmed);

            LogResult(
                request,
                publishable);

            // Slice 3 proves bounded automatic acquisition and ordering. The
            // event/memory owner is introduced later; retaining semantic
            // content here would silently create a new memory boundary.
        }
        finally
        {
            result.SemanticSnapshot?.Dispose();
        }

        CompleteAndDispatchNextOnUiThread(request.RequestId);
    }

    private void CompleteAndDispatchNextOnUiThread(
        long requestId)
    {
        EnsureUiThread();

        if (IsStopped())
        {
            return;
        }

        UiEnrichmentCompletionDecision completion =
            _policy.Complete(
                requestId,
                _sensingOrchestrator.IsArmed,
                _contextEpochManager.Current);

        DiagnosticLog.Write(
            "UIENRICH.COMPLETE",
            $"request={requestId} " +
            $"outcome={completion.Outcome} " +
            $"reason={completion.RejectionReason} " +
            $"invalidated={completion.Invalidated.Count} " +
            $"nextRequest={completion.Dispatch?.RequestId ?? 0}");

        if (completion.Invalidated.Count > 0)
        {
            LogInvalidation(
                "completion_revalidation",
                completion.Invalidated);
        }

        if (completion.Outcome == UiEnrichmentCompletionOutcome.DispatchNext &&
            completion.Dispatch is not null)
        {
            DispatchOnUiThread(completion.Dispatch);
        }
    }

    private void InvalidateOnUiThread(
        string reason)
    {
        EnsureUiThread();

        UiEnrichmentInvalidation invalidated =
            _policy.Invalidate();

        LogInvalidation(
            string.IsNullOrWhiteSpace(reason)
                ? "unspecified"
                : reason,
            invalidated);
    }

    private bool IsStopped()
    {
        lock (_lifecycleGate)
        {
            return _stopped;
        }
    }

    private void EnsureUiThread()
    {
        if (!_uiDispatcher.HasThreadAccess)
        {
            throw new InvalidOperationException(
                "M3.4 UI enrichment runtime state must be accessed on the UI thread.");
        }
    }

    private static void LogAdmission(
        UiEnrichmentTriggerKind kind,
        long epochId,
        long sourceId,
        ChangeClassification? classification,
        UiEnrichmentAdmissionDecision decision)
    {
        DiagnosticLog.Write(
            "UIENRICH.ADMIT",
            $"epoch={epochId} " +
            $"source={sourceId} " +
            $"kind={kind} " +
            $"classification={classification?.ToString() ?? "none"} " +
            $"outcome={decision.Outcome} " +
            $"reason={decision.RejectionReason} " +
            $"request={decision.Request?.RequestId ?? 0} " +
            $"replaced={decision.ReplacedPending?.RequestId ?? 0} " +
            $"invalidated={decision.Invalidated.Count}");
    }

    private static void LogInvalidation(
        string reason,
        UiEnrichmentInvalidation invalidated)
    {
        if (invalidated.Count == 0)
        {
            return;
        }

        DiagnosticLog.Write(
            "UIENRICH.INVALIDATE",
            $"reason={reason} " +
            $"count={invalidated.Count} " +
            $"activeRequest={invalidated.Active?.RequestId ?? 0} " +
            $"pendingRequest={invalidated.Pending?.RequestId ?? 0}");
    }

    private static void LogResult(
        UiEnrichmentRequest request,
        UiAutomationProbeResult result)
    {
        UiAutomationSemanticSnapshot? semantic =
            result.SemanticSnapshot;

        DiagnosticLog.Write(
            "UIENRICH.RESULT",
            $"request={request.RequestId} " +
            $"workerRequest={result.RequestId} " +
            $"epoch={request.EpochId} " +
            $"kind={request.Kind} " +
            $"outcome={result.Outcome} " +
            $"reason={result.Reason} " +
            $"elapsedMs={result.Elapsed.TotalMilliseconds:0.000} " +
            $"selectedNodes={semantic?.Nodes.Count ?? 0} " +
            $"strings={semantic?.StringCount ?? 0} " +
            $"utf8Bytes={semantic?.Utf8Bytes ?? 0} " +
            $"estimatedBytes={semantic?.EstimatedResultBytes ?? 0} " +
            $"content=redacted");
    }
}
