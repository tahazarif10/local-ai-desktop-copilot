using LocalCopilot_App.Diagnostics;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;

namespace LocalCopilot_App.Services;

internal sealed class OcrEnrichmentRuntimeService :
    IDisposable
{
    private static readonly TimeSpan CaptureTimeout =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan RequestDeadline =
        TimeSpan.FromSeconds(15);

    private static readonly TimeSpan ShutdownJoinTimeout =
        TimeSpan.FromSeconds(20);

    private readonly object _gate = new();
    private readonly ContextEpochManager _contextEpochManager;
    private readonly SensingOrchestrator _sensingOrchestrator;
    private readonly PersistentChangeDetectionService _persistentChangeDetectionService;
    private readonly ForegroundWindowService _foregroundWindowService;
    private readonly RegionOfInterestPlanner _regionPlanner;
    private readonly OcrServerTransport _transport;
    private readonly CancellationTokenSource _stopCancellation = new();

    private RuntimeWork? _active;
    private RuntimeWork? _pending;
    private Task? _activeTask;
    private long _nextRequestId;
    private long _latestRequestId;
    private bool _stopped;
    private bool _disposed;
    private int _ownedResourcesDisposed;

    public OcrEnrichmentRuntimeService(
        ContextEpochManager contextEpochManager,
        SensingOrchestrator sensingOrchestrator,
        PersistentChangeDetectionService persistentChangeDetectionService,
        ForegroundWindowService foregroundWindowService,
        OcrServerTransport transport)
    {
        _contextEpochManager =
            contextEpochManager ??
            throw new ArgumentNullException(nameof(contextEpochManager));
        _sensingOrchestrator =
            sensingOrchestrator ??
            throw new ArgumentNullException(nameof(sensingOrchestrator));
        _persistentChangeDetectionService =
            persistentChangeDetectionService ??
            throw new ArgumentNullException(nameof(persistentChangeDetectionService));
        _foregroundWindowService =
            foregroundWindowService ??
            throw new ArgumentNullException(nameof(foregroundWindowService));
        _transport =
            transport ??
            throw new ArgumentNullException(nameof(transport));
        _regionPlanner =
            new RegionOfInterestPlanner(
                RegionOfInterestPlannerOptions.M4_1Default);

        _persistentChangeDetectionService.SampleReady +=
            PersistentChangeDetectionService_SampleReady;

        DiagnosticLog.Write(
            "OCR.RUNTIME_START",
            $"topology={OcrExecutionTopology.LocalAiServer} " +
            $"maxRegions={OcrTransportLimits.M4_2_3Default.MaxRegions} " +
            $"deadlineMs={RequestDeadline.TotalMilliseconds:0} " +
            "content=redacted");
    }

    private void PersistentChangeDetectionService_SampleReady(
        PersistentChangeSample sample)
    {
        if (sample.Change.Classification is not
            (ChangeClassification.Meaningful or ChangeClassification.Large))
        {
            return;
        }

        ContextEpoch? epoch =
            _contextEpochManager.Current;

        if (epoch is null ||
            epoch.Id != sample.EpochId ||
            epoch.CancellationToken.IsCancellationRequested)
        {
            return;
        }

        RegionOfInterestPlan plan;
        try
        {
            plan =
                _regionPlanner.Plan(
                    new RegionOfInterestPlanningInput(
                        RegionOfInterestPlanningIntent.BackgroundChange,
                        sample.SourceWidth,
                        sample.SourceHeight,
                        sample.Change.ChangedRegion,
                        sample.Change.Width,
                        sample.Change.Height,
                        CaptureScreenBounds: null,
                        UiAutomationBounds: null));
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException(
                "OCR.PLAN_ERROR",
                exception,
                $"epoch={sample.EpochId}");
            return;
        }

        long requestId =
            Interlocked.Increment(
                ref _nextRequestId);

        OcrIntegrationRequest request =
            new(
                requestId,
                sample.EpochId,
                OcrExecutionTopology.LocalAiServer,
                sample.SourceWidth,
                sample.SourceHeight,
                plan);

        OcrIntegrationGateDecision admission =
            OcrIntegrationGate.EvaluateDispatch(
                request,
                _sensingOrchestrator.IsArmed,
                epoch);

        if (!admission.Allowed)
        {
            DiagnosticLog.Write(
                "OCR.ADMIT_REJECT",
                $"request={requestId} epoch={sample.EpochId} " +
                $"reason={admission.Reason}");
            return;
        }

        RuntimeWork work =
            new(
                request,
                epoch);

        RuntimeWork? replaced = null;
        bool dispatchNow = false;

        lock (_gate)
        {
            if (_stopped)
                return;

            _latestRequestId = requestId;

            if (_active is null)
            {
                _active = work;
                dispatchNow = true;
            }
            else
            {
                replaced = _pending;
                _pending = work;
            }
        }

        if (replaced is not null)
        {
            DiagnosticLog.Write(
                "OCR.REPLACE_PENDING",
                $"replaced={replaced.Request.RequestId} " +
                $"replacement={requestId} epoch={sample.EpochId}");
        }

        if (dispatchNow)
        {
            StartWork(
                work);
        }
        else
        {
            DiagnosticLog.Write(
                "OCR.QUEUE_PENDING",
                $"request={requestId} epoch={sample.EpochId}");
        }
    }

    private async Task ExecuteAndAdvanceAsync(
        RuntimeWork work)
    {
        try
        {
            await ExecuteAsync(work)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            DiagnosticLog.Write(
                "OCR.REQUEST_CANCELLED",
                $"request={work.Request.RequestId} " +
                $"epoch={work.Request.EpochId}");
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException(
                "OCR.REQUEST_ERROR",
                exception,
                $"request={work.Request.RequestId} " +
                $"epoch={work.Request.EpochId}");
        }
        finally
        {
            RuntimeWork? next = null;

            lock (_gate)
            {
                if (ReferenceEquals(_active, work))
                {
                    _active = null;
                    _activeTask = null;

                    if (!_stopped)
                    {
                        next = _pending;
                        _pending = null;
                        _active = next;
                    }
                }
            }

            if (next is not null)
            {
                StartWork(
                    next);
            }
        }
    }

    private async Task ExecuteAsync(
        RuntimeWork work)
    {
        OcrIntegrationRequest request =
            work.Request;
        ContextEpoch epoch =
            work.Epoch;

        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                epoch.CancellationToken,
                _stopCancellation.Token);

        OcrIntegrationGateDecision beforeCapture =
            OcrIntegrationGate.EvaluateDispatch(
                request,
                _sensingOrchestrator.IsArmed,
                _contextEpochManager.Current);

        if (!beforeCapture.Allowed)
        {
            LogDrop(
                request,
                "before_capture",
                beforeCapture.Reason);
            return;
        }

        if (!_foregroundWindowService.IsCurrent(
                epoch.Snapshot))
        {
            DiagnosticLog.Write(
                "OCR.DROP",
                $"request={request.RequestId} epoch={request.EpochId} " +
                "stage=before_capture reason=identity_changed");
            return;
        }

        Stopwatch elapsed =
            Stopwatch.StartNew();

        GraphicsCaptureItem item =
            GraphicsCaptureItemFactory.CreateForWindow(
                epoch.Snapshot.Handle);

        using OcrRegionCaptureResult capture =
            await OcrRegionCaptureService.CaptureAsync(
                    item,
                    request,
                    CaptureTimeout,
                    cancellation.Token)
                .ConfigureAwait(false);

        if (!_foregroundWindowService.IsCurrent(
                epoch.Snapshot))
        {
            DiagnosticLog.Write(
                "OCR.DROP",
                $"request={request.RequestId} epoch={request.EpochId} " +
                "stage=before_transport reason=identity_changed");
            return;
        }

        using OcrTransportRequestPayload payload =
            new(
                request.RequestId,
                request.EpochId,
                DateTimeOffset.UtcNow + RequestDeadline,
                capture.Regions);

        // Revalidate the complete OCR + pixel-egress capability set directly
        // before the transport serializes any ROI bytes.
        OcrIntegrationGateDecision beforeTransport =
            OcrIntegrationGate.EvaluateDispatch(
                request,
                _sensingOrchestrator.IsArmed,
                _contextEpochManager.Current);

        if (!beforeTransport.Allowed)
        {
            LogDrop(
                request,
                "before_transport",
                beforeTransport.Reason);
            return;
        }

        using OcrTransportResponsePayload response =
            await _transport.SendAsync(
                    payload,
                    cancellation.Token)
                .ConfigureAwait(false);

        long latest =
            Interlocked.Read(
                ref _latestRequestId);

        OcrIntegrationGateDecision publication =
            OcrIntegrationGate.EvaluatePublication(
                request,
                latest,
                _sensingOrchestrator.IsArmed,
                _contextEpochManager.Current);

        if (!publication.Allowed)
        {
            LogDrop(
                request,
                "publication",
                publication.Reason);
            return;
        }

        if (response.Status != OcrTransportResponseStatus.Ok)
        {
            DiagnosticLog.Write(
                "OCR.RESULT_UNAVAILABLE",
                $"request={request.RequestId} epoch={request.EpochId} " +
                $"status={response.Status} " +
                $"elapsedMs={elapsed.Elapsed.TotalMilliseconds:0.000}");
            return;
        }

        int textCount =
            response.Texts.Count;
        int characterCount = 0;
        int utf8Bytes = 0;

        foreach (OcrTransportTextResult text in response.Texts)
        {
            characterCount =
                checked(
                    characterCount +
                    text.Text.CharacterCount);

            utf8Bytes =
                checked(
                    utf8Bytes +
                    text.Text.Utf8ByteCount);
        }

        elapsed.Stop();

        DiagnosticLog.Write(
            "OCR.RESULT",
            $"request={request.RequestId} epoch={request.EpochId} " +
            $"regions={capture.Regions.Count} texts={textCount} " +
            $"characters={characterCount} utf8Bytes={utf8Bytes} " +
            $"captureMs={capture.TotalMilliseconds:0.000} " +
            $"elapsedMs={elapsed.Elapsed.TotalMilliseconds:0.000} " +
            "content=redacted");

        // M4.2.3 proves bounded OCR acquisition and transport. A retained
        // semantic-event owner does not exist until M5, so OCR text is
        // deliberately disposed at the end of this method.
    }

    private static void LogDrop(
        OcrIntegrationRequest request,
        string stage,
        OcrIntegrationRejectionReason reason)
    {
        DiagnosticLog.Write(
            "OCR.DROP",
            $"request={request.RequestId} epoch={request.EpochId} " +
            $"stage={stage} reason={reason}");
    }

    private void StartWork(
        RuntimeWork work)
    {
        Task task =
            ExecuteAndAdvanceAsync(
                work);

        lock (_gate)
        {
            if (ReferenceEquals(
                    _active,
                    work))
            {
                _activeTask =
                    task;
            }
        }
    }

    private bool StopAndJoin(
        string reason,
        out Task? unfinishedTask)
    {
        RuntimeWork? pending;
        Task? activeTask;

        lock (_gate)
        {
            if (_stopped)
            {
                activeTask =
                    _activeTask;

                unfinishedTask =
                    activeTask is not null &&
                    !activeTask.IsCompleted
                        ? activeTask
                        : null;

                return
                    unfinishedTask is null;
            }

            _stopped = true;
            pending = _pending;
            _pending = null;
            activeTask = _activeTask;
        }

        try
        {
            _stopCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        bool joined =
            activeTask is null ||
            activeTask.IsCompleted;

        if (!joined)
        {
            try
            {
                joined =
                    activeTask!.Wait(
                        ShutdownJoinTimeout);
            }
            catch (AggregateException)
            {
                joined =
                    activeTask!.IsCompleted;
            }
        }

        unfinishedTask =
            joined
                ? null
                : activeTask;

        DiagnosticLog.Write(
            "OCR.RUNTIME_STOP",
            $"reason={(string.IsNullOrWhiteSpace(reason) ? "runtime_stop" : reason)} " +
            $"pendingDropped={(pending is null ? 0 : 1)} " +
            $"joined={joined}");

        return joined;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        _persistentChangeDetectionService.SampleReady -=
            PersistentChangeDetectionService_SampleReady;

        bool joined =
            StopAndJoin(
                "dispose",
                out Task? unfinishedTask);

        if (joined)
        {
            DisposeOwnedResources();
        }
        else if (unfinishedTask is not null)
        {
            _ =
                unfinishedTask.ContinueWith(
                    _ => DisposeOwnedResources(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
        }

        GC.SuppressFinalize(this);
    }

    private void DisposeOwnedResources()
    {
        if (Interlocked.Exchange(
                ref _ownedResourcesDisposed,
                1) != 0)
        {
            return;
        }

        _transport.Dispose();
        _stopCancellation.Dispose();
    }

    private sealed record RuntimeWork(
        OcrIntegrationRequest Request,
        ContextEpoch Epoch);
}
