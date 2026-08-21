using LocalCopilot_App.Diagnostics;
using Microsoft.UI.Dispatching;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Graphics.Capture;

namespace LocalCopilot_App.Services;

public sealed class DesktopCopilotCoordinator :
    IDisposable
{
    private readonly uint
        _ownProcessId;

    private readonly DispatcherQueue
        _uiDispatcher;

    private readonly ForegroundWindowService
        _foregroundWindowService;

    private readonly ForegroundWindowObserver
        _foregroundWindowObserver;

    private readonly PrivacyPolicy
        _privacyPolicy;

    private readonly ContextEpochManager
        _contextEpochManager;

    private readonly ChangeDetectionProbeService
        _changeDetectionProbeService;

    private readonly PersistentChangeDetectionService
        _persistentChangeDetectionService;

    private readonly SensingOrchestrator
        _sensingOrchestrator;

    private readonly DiagnosticTimeline
        _diagnosticTimeline;

    private readonly InputActivityTracker
        _inputActivityTracker;

    private readonly ChangeCorrelationService
        _changeCorrelationService;

    private readonly ApplicationLifecycleGate
        _lifecycle =
            new();

    private readonly object
        _viewGate =
            new();

    private DesktopCopilotViewState
        _viewState =
            DesktopCopilotViewState.Initial;

    private IDesktopCopilotView?
        _view;

    private ContextEpoch?
        _currentEpoch;

    private bool
        _subscriptionsAttached;

    internal DesktopCopilotCoordinator(
        uint ownProcessId,
        DispatcherQueue uiDispatcher,
        ForegroundWindowService foregroundWindowService,
        ForegroundWindowObserver foregroundWindowObserver,
        PrivacyPolicy privacyPolicy,
        ContextEpochManager contextEpochManager,
        ChangeDetectionProbeService changeDetectionProbeService,
        PersistentChangeDetectionService persistentChangeDetectionService,
        SensingOrchestrator sensingOrchestrator,
        DiagnosticTimeline diagnosticTimeline,
        InputActivityTracker inputActivityTracker,
        ChangeCorrelationService changeCorrelationService)
    {
        _ownProcessId =
            ownProcessId;

        _uiDispatcher =
            uiDispatcher ??
            throw new ArgumentNullException(
                nameof(uiDispatcher));

        _foregroundWindowService =
            foregroundWindowService ??
            throw new ArgumentNullException(
                nameof(foregroundWindowService));

        _foregroundWindowObserver =
            foregroundWindowObserver ??
            throw new ArgumentNullException(
                nameof(foregroundWindowObserver));

        _privacyPolicy =
            privacyPolicy ??
            throw new ArgumentNullException(
                nameof(privacyPolicy));

        _contextEpochManager =
            contextEpochManager ??
            throw new ArgumentNullException(
                nameof(contextEpochManager));

        _changeDetectionProbeService =
            changeDetectionProbeService ??
            throw new ArgumentNullException(
                nameof(changeDetectionProbeService));

        _persistentChangeDetectionService =
            persistentChangeDetectionService ??
            throw new ArgumentNullException(
                nameof(persistentChangeDetectionService));

        _sensingOrchestrator =
            sensingOrchestrator ??
            throw new ArgumentNullException(
                nameof(sensingOrchestrator));

        _diagnosticTimeline =
            diagnosticTimeline ??
            throw new ArgumentNullException(
                nameof(diagnosticTimeline));

        _inputActivityTracker =
            inputActivityTracker ??
            throw new ArgumentNullException(
                nameof(inputActivityTracker));

        _changeCorrelationService =
            changeCorrelationService ??
            throw new ArgumentNullException(
                nameof(changeCorrelationService));

        DiagnosticLog.Write(
            "COORD.CTOR",
            $"ownPid={_ownProcessId} " +
            $"HasThreadAccess={_uiDispatcher.HasThreadAccess}");
    }

    public bool IsRunning =>
        _lifecycle.IsRunning;

    public void Start()
    {
        EnsureUiThread(
            "start");

        if (!_lifecycle.TryStart())
        {
            DiagnosticLog.Write(
                "COORD.START_REUSE",
                $"state={_lifecycle.State}");

            return;
        }

        DiagnosticLog.Write(
            "COORD.START",
            $"HasThreadAccess={_uiDispatcher.HasThreadAccess}");

        AttachServiceSubscriptions();

        try
        {
            _foregroundWindowObserver.Start();

            DiagnosticLog.Write(
                "COORD.OBSERVER_START",
                $"observerRunning={_foregroundWindowObserver.IsRunning}");

            ForegroundWindowObservation? observation =
                _foregroundWindowService.GetCurrent(
                    _privacyPolicy,
                    _ownProcessId);

            if (observation is null)
            {
                DiagnosticLog.Write(
                    "COORD.INITIAL",
                    "Initial observation=null.");

                return;
            }

            DiagnosticLog.Write(
                "COORD.INITIAL",
                $"hwnd=0x{observation.Snapshot.Handle.ToInt64():X} " +
                $"process={observation.Snapshot.ProcessName} " +
                $"privacy={observation.Privacy.Disposition}");

            ApplyObservation(
                observation);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                "COORD.START_ERROR",
                ex.ToString());

            UpdateViewState(
                state =>
                    state with
                    {
                        ProcessName =
                            "Observer start failed",
                        WindowTitle =
                            ex.Message
                    });

            Stop(
                "start_failed");
        }
    }

    public void Stop(
        string reason)
    {
        EnsureUiThread(
            "stop");

        if (!_lifecycle.TryStop())
        {
            return;
        }

        string normalizedReason =
            string.IsNullOrWhiteSpace(
                reason)
                ? "application_stop"
                : reason;

        DiagnosticLog.Write(
            "COORD.STOP",
            $"reason={normalizedReason} " +
            $"HasThreadAccess={_uiDispatcher.HasThreadAccess}");

        bool observerStopped =
            _foregroundWindowObserver.Stop();

        DiagnosticLog.Write(
            "COORD.OBSERVER_STOP",
            $"success={observerStopped}");

        _sensingOrchestrator.Disarm(
            normalizedReason);

        StopInputTracking(
            normalizedReason);

        _contextEpochManager.Reset();

        _currentEpoch =
            null;

        _changeDetectionProbeService.ResetAll();

        DetachServiceSubscriptions();
    }

    public void AttachView(
        IDesktopCopilotView view)
    {
        ArgumentNullException.ThrowIfNull(
            view);

        EnsureUiThread(
            "attach_view");

        DesktopCopilotViewState state;

        lock (_viewGate)
        {
            _view =
                view;

            state =
                _viewState;
        }

        RenderView(
            view,
            state);

        DiagnosticLog.Write(
            "COORD.VIEW_ATTACH",
            $"state={_lifecycle.State}");
    }

    public void DetachView(
        IDesktopCopilotView view)
    {
        ArgumentNullException.ThrowIfNull(
            view);

        EnsureUiThread(
            "detach_view");

        lock (_viewGate)
        {
            if (ReferenceEquals(
                    _view,
                    view))
            {
                _view =
                    null;
            }
        }

        DiagnosticLog.Write(
            "COORD.VIEW_DETACH",
            $"state={_lifecycle.State}");
    }

    public void ProbeCaptureTarget()
    {
        EnsureUiThread(
            "target_probe");

        ContextEpoch? epoch =
            GetAllowedEpoch(
                "target_probe");

        if (epoch is null)
        {
            SetCaptureTargetStatus(
                _currentEpoch is not null &&
                !_currentEpoch.Privacy.AllowsSensing
                    ? "Blocked by privacy policy."
                    : "No allowed capture target.");

            return;
        }

        bool captureSupported =
            GraphicsCaptureSession.IsSupported();

        DiagnosticLog.Write(
            "CAPTURE.SUPPORT",
            $"supported={captureSupported}");

        if (!captureSupported)
        {
            SetCaptureTargetStatus(
                "Windows Graphics Capture is not supported.");

            DiagnosticLog.Write(
                "CAPTURE.PROBE_REJECT",
                "reason=graphics_capture_not_supported");

            return;
        }

        ForegroundWindowSnapshot snapshot =
            epoch.Snapshot;

        DiagnosticLog.Write(
            "CAPTURE.PROBE_BEGIN",
            $"epoch={epoch.Id} " +
            $"hwnd=0x{snapshot.Handle.ToInt64():X} " +
            $"pid={snapshot.ProcessId} " +
            $"process={snapshot.ProcessName}");

        try
        {
            GraphicsCaptureItem item =
                GraphicsCaptureItemFactory.CreateForWindow(
                    snapshot.Handle);

            SetCaptureTargetStatus(
                $"OK | {item.Size.Width} x {item.Size.Height}" +
                $" | {item.DisplayName}");

            DiagnosticLog.Write(
                "CAPTURE.PROBE_OK",
                $"epoch={epoch.Id} " +
                $"hwnd=0x{snapshot.Handle.ToInt64():X} " +
                $"size={item.Size.Width}x{item.Size.Height}");
        }
        catch (Exception ex)
        {
            SetCaptureTargetStatus(
                $"Capture target failed: {ex.Message}");

            DiagnosticLog.Write(
                "CAPTURE.PROBE_ERROR",
                ex.ToString());
        }
    }

    public async Task CaptureOneFrameAsync()
    {
        EnsureUiThread(
            "single_frame");

        ContextEpoch? epoch =
            GetAllowedEpoch(
                "single_frame");

        if (epoch is null)
        {
            SetCapturedFrameStatus(
                _currentEpoch is not null &&
                !_currentEpoch.Privacy.AllowsSensing
                    ? "Blocked by privacy policy."
                    : "No allowed capture target.");

            return;
        }

        ForegroundWindowSnapshot snapshot =
            epoch.Snapshot;

        DiagnosticLog.Write(
            "CAPTURE.FRAME_BEGIN",
            $"epoch={epoch.Id} " +
            $"hwnd=0x{snapshot.Handle.ToInt64():X} " +
            $"pid={snapshot.ProcessId} " +
            $"process={snapshot.ProcessName}");

        try
        {
            GraphicsCaptureItem item =
                GraphicsCaptureItemFactory.CreateForWindow(
                    snapshot.Handle);

            DiagnosticLog.Write(
                "CAPTURE.FRAME_ITEM",
                $"epoch={epoch.Id} " +
                $"itemSize={item.Size.Width}x{item.Size.Height}");

            SingleFrameCaptureInfo frame =
                await SingleFrameCaptureService.CaptureAsync(
                    item,
                    TimeSpan.FromSeconds(5),
                    2560);

            if (epoch.CancellationToken.IsCancellationRequested ||
                !ReferenceEquals(
                    _currentEpoch,
                    epoch))
            {
                DiagnosticLog.Write(
                    "CAPTURE.FRAME_STALE_DROP",
                    $"epoch={epoch.Id} " +
                    $"currentEpoch={_currentEpoch?.Id ?? 0}");

                SetCapturedFrameStatus(
                    "Ignored stale capture result.");

                return;
            }

            DiagnosticLog.Write(
                "CAPTURE.FRAME_OK",
                $"epoch={epoch.Id} " +
                $"content={frame.ContentWidth}x{frame.ContentHeight} " +
                $"surface={frame.SurfaceWidth}x{frame.SurfaceHeight} " +
                $"frameMs={frame.FrameMilliseconds:0.0}");

            DiagnosticLog.Write(
                "CAPTURE.RESIZE_OK",
                $"epoch={epoch.Id} " +
                $"source={frame.ContentWidth}x{frame.ContentHeight} " +
                $"output={frame.OutputWidth}x{frame.OutputHeight} " +
                $"scale={frame.ScaleFactor:0.0000} " +
                $"resizeMs={frame.ResizeMilliseconds:0.0}");

            DiagnosticLog.Write(
                "CAPTURE.BITMAP_OK",
                $"epoch={epoch.Id} " +
                $"bitmap={frame.OutputWidth}x{frame.OutputHeight} " +
                $"format={frame.BitmapPixelFormat} " +
                $"stride={frame.PlaneStride} " +
                $"cpuBytes={frame.CpuBytes} " +
                $"copyMs={frame.CopyMilliseconds:0.0} " +
                $"totalMs={frame.TotalMilliseconds:0.0}");

            SetCapturedFrameStatus(
                $"RAM OK | " +
                $"{frame.OutputWidth} x {frame.OutputHeight} | " +
                $"{frame.CpuBytes / 1024.0 / 1024.0:0.0} MB | " +
                $"{frame.TotalMilliseconds:0.0} ms");
        }
        catch (Exception ex)
        {
            SetCapturedFrameStatus(
                $"RAM capture failed: {ex.Message}");

            DiagnosticLog.Write(
                "CAPTURE.BITMAP_ERROR",
                ex.ToString());
        }
    }

    public async Task RunChangeDetectionSampleAsync(
        int profileWidth)
    {
        EnsureUiThread(
            $"change_{profileWidth}");

        ContextEpoch? epoch =
            GetAllowedEpoch(
                $"change_{profileWidth}");

        if (epoch is null)
        {
            SetChangeDetectionStatus(
                _currentEpoch is not null &&
                !_currentEpoch.Privacy.AllowsSensing
                    ? "Blocked by privacy policy."
                    : "No allowed change-detection target.");

            return;
        }

        DiagnosticLog.Write(
            "CHANGE.SAMPLE_BEGIN",
            $"epoch={epoch.Id} " +
            $"profile={profileWidth} " +
            $"hwnd=0x{epoch.Snapshot.Handle.ToInt64():X} " +
            $"pid={epoch.Snapshot.ProcessId} " +
            $"process={epoch.Snapshot.ProcessName}");

        SetChangeDetectionStatus(
            $"Sampling {profileWidth}px...");

        try
        {
            ChangeProbeResult probe =
                await _changeDetectionProbeService.SampleAsync(
                    epoch,
                    profileWidth,
                    TimeSpan.FromSeconds(5));

            if (epoch.CancellationToken.IsCancellationRequested ||
                !ReferenceEquals(
                    _currentEpoch,
                    epoch))
            {
                DiagnosticLog.Write(
                    "CHANGE.SAMPLE_STALE",
                    $"epoch={epoch.Id} " +
                    $"profile={profileWidth} " +
                    $"currentEpoch={_currentEpoch?.Id ?? 0}");

                SetChangeDetectionStatus(
                    "Ignored stale change sample.");

                return;
            }

            ChangeResult change =
                probe.Change;

            ChangeDetectionCaptureFrame capture =
                probe.Capture;

            string region =
                change.ChangedRegion is null
                    ? "none"
                    : $"{change.ChangedRegion.X}," +
                      $"{change.ChangedRegion.Y}," +
                      $"{change.ChangedRegion.Width}," +
                      $"{change.ChangedRegion.Height}";

            DiagnosticLog.Write(
                "CHANGE.SAMPLE_OK",
                $"epoch={probe.EpochId} " +
                $"profile={probe.ProfileWidth} " +
                $"classification={change.Classification} " +
                $"reason={change.Reason} " +
                $"input={capture.SourceWidth}x{capture.SourceHeight} " +
                $"output={capture.OutputWidth}x{capture.OutputHeight} " +
                $"captureMs={capture.TotalMilliseconds:0.000} " +
                $"frameMs={capture.FrameMilliseconds:0.000} " +
                $"resizeMs={capture.ResizeMilliseconds:0.000} " +
                $"readbackMs={capture.ReadbackMilliseconds:0.000} " +
                $"lumaMs={capture.LuminanceMilliseconds:0.000} " +
                $"diffMs={change.DiffMilliseconds:0.000} " +
                $"totalMs={probe.TotalMilliseconds:0.000} " +
                $"changedPixelRatio={change.ChangedPixelRatio:0.000000} " +
                $"changedTileRatio={change.ChangedTileRatio:0.000000} " +
                $"globalDifference={change.MeanAbsoluteDifference:0.000000} " +
                $"changedPixels={change.ChangedPixelCount} " +
                $"changedTiles={change.ChangedTileCount}/{change.TotalTileCount} " +
                $"region={region}");

            SetChangeDetectionStatus(
                $"{profileWidth}px | " +
                $"{change.Classification} | " +
                $"pixels {change.ChangedPixelRatio:P2} | " +
                $"tiles {change.ChangedTileRatio:P2} | " +
                $"diff {change.DiffMilliseconds:0.0} ms | " +
                $"total {probe.TotalMilliseconds:0.0} ms");
        }
        catch (OperationCanceledException)
        {
            DiagnosticLog.Write(
                "CHANGE.SAMPLE_CANCELLED",
                $"epoch={epoch.Id} " +
                $"profile={profileWidth}");

            SetChangeDetectionStatus(
                "Change sample cancelled.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                "CHANGE.SAMPLE_ERROR",
                $"epoch={epoch.Id} " +
                $"profile={profileWidth} " +
                $"type={ex.GetType().Name} " +
                $"message={ex.Message}");

            SetChangeDetectionStatus(
                $"Change sample failed: {ex.Message}");
        }
    }

    public void Arm()
    {
        EnsureUiThread(
            "arm");

        if (_sensingOrchestrator.IsArmed)
        {
            SetOrchestratorStatus(
                "Already armed.");

            return;
        }

        if (_persistentChangeDetectionService
            .HasActiveSession)
        {
            SetOrchestratorStatus(
                "Stop the manual persistent session before arming.");

            DiagnosticLog.Write(
                "ORCH.ARM_REJECT",
                "reason=manual_session_active");

            return;
        }

        SetOrchestratorStatus(
            "ARMED | evaluating current context...");

        _sensingOrchestrator.Arm(
            _currentEpoch);

        RefreshInputTracking(
            _currentEpoch,
            "user_arm");
    }

    public void Disarm()
    {
        EnsureUiThread(
            "disarm");

        SetOrchestratorStatus(
            "Disarming...");

        _sensingOrchestrator.Disarm(
            "user_disarm");

        StopInputTracking(
            "user_disarm");

        SetOrchestratorStatus(
            "OFF");
    }

    public void StartManualPersistentSensing()
    {
        EnsureUiThread(
            "persistent_start");

        if (_sensingOrchestrator.IsArmed)
        {
            SetPersistentChangeStatus(
                "Disarm auto sensing before manual start.");

            DiagnosticLog.Write(
                "PERSIST.MANUAL_REJECT",
                "operation=start reason=orchestrator_armed");

            return;
        }

        ContextEpoch? epoch =
            GetAllowedEpoch(
                "persistent_start");

        if (epoch is null)
        {
            SetPersistentChangeStatus(
                _currentEpoch is not null &&
                !_currentEpoch.Privacy.AllowsSensing
                    ? "Blocked by privacy policy."
                    : "No allowed persistent target.");

            return;
        }

        if (_persistentChangeDetectionService.IsRunning)
        {
            SetPersistentChangeStatus(
                "Persistent sensing is already running.");

            return;
        }

        try
        {
            _persistentChangeDetectionService.Start(
                epoch,
                640,
                TimeSpan.FromMilliseconds(
                    500));

            SetPersistentChangeStatus(
                "Running | 640 px | 2 Hz | waiting for samples...");
        }
        catch (Exception ex)
        {
            SetPersistentChangeStatus(
                $"Persistent start failed: {ex.Message}");

            DiagnosticLog.Write(
                "PERSIST.START_ERROR",
                $"epoch={epoch.Id} " +
                $"type={ex.GetType().Name} " +
                $"message={ex.Message}");
        }
    }

    public void StopManualPersistentSensing()
    {
        EnsureUiThread(
            "persistent_stop");

        if (_sensingOrchestrator.IsArmed)
        {
            SetPersistentChangeStatus(
                "Use Disarm auto sensing to stop orchestrated capture.");

            DiagnosticLog.Write(
                "PERSIST.MANUAL_REJECT",
                "operation=stop reason=orchestrator_armed");

            return;
        }

        try
        {
            _persistentChangeDetectionService.Stop(
                "user_stop");

            SetPersistentChangeStatus(
                "Stopped by user.");
        }
        catch (Exception ex)
        {
            SetPersistentChangeStatus(
                $"Persistent stop failed: {ex.Message}");

            DiagnosticLog.Write(
                "PERSIST.STOP_ERROR",
                $"type={ex.GetType().Name} " +
                $"message={ex.Message}");
        }
    }

    public void Dispose()
    {
        EnsureUiThread(
            "dispose");

        Stop(
            "application_shutdown");

        if (!_lifecycle.TryDispose())
        {
            return;
        }

        DetachServiceSubscriptions();

        _inputActivityTracker.Dispose();
        _foregroundWindowObserver.Dispose();
        _contextEpochManager.Dispose();

        lock (_viewGate)
        {
            _view =
                null;
        }

        DiagnosticLog.Write(
            "COORD.DISPOSE",
            "Coordinator disposed.");

        GC.SuppressFinalize(
            this);
    }

    private void ForegroundWindowObserver_ForegroundWindowChanged(
        nint hwnd)
    {
        if (!_lifecycle.IsRunning)
        {
            return;
        }

        DiagnosticLog.Write(
            "COORD.EVENT_RECEIVED",
            $"hwnd=0x{hwnd.ToInt64():X} " +
            $"HasThreadAccess={_uiDispatcher.HasThreadAccess}");

        bool queued =
            _uiDispatcher.TryEnqueue(
                DispatcherQueuePriority.High,
                () =>
                {
                    if (!_lifecycle.IsRunning)
                    {
                        return;
                    }

                    DiagnosticLog.Write(
                        "COORD.QUEUE_EXECUTE",
                        $"hwnd=0x{hwnd.ToInt64():X} " +
                        $"HasThreadAccess={_uiDispatcher.HasThreadAccess}");

                    try
                    {
                        ForegroundWindowObservation? observation =
                            _foregroundWindowService.GetFromHandle(
                                hwnd,
                                _privacyPolicy,
                                _ownProcessId);

                        if (observation is null)
                        {
                            DiagnosticLog.Write(
                                "COORD.QUEUE_RESULT",
                                "observation=null");

                            return;
                        }

                        DiagnosticLog.Write(
                            "COORD.QUEUE_RESULT",
                            $"process={observation.Snapshot.ProcessName} " +
                            $"hwnd=0x{observation.Snapshot.Handle.ToInt64():X} " +
                            $"privacy={observation.Privacy.Disposition}");

                        ApplyObservation(
                            observation);
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Write(
                            "COORD.QUEUE_ERROR",
                            ex.ToString());
                    }
                });

        DiagnosticLog.Write(
            "COORD.ENQUEUE",
            $"hwnd=0x{hwnd.ToInt64():X} queued={queued}");
    }

    private void ApplyObservation(
        ForegroundWindowObservation observation)
    {
        ArgumentNullException.ThrowIfNull(
            observation);

        ForegroundWindowSnapshot snapshot =
            observation.Snapshot;

        PrivacyEvaluation privacy =
            observation.Privacy;

        ContextEpoch epoch =
            _contextEpochManager.GetOrAdvance(
                snapshot,
                privacy);

        bool contextChanged =
            !ReferenceEquals(
                _currentEpoch,
                epoch);

        _currentEpoch =
            epoch;

        _changeDetectionProbeService.ObserveContext(
            epoch);

        _sensingOrchestrator.ObserveContext(
            epoch);

        RefreshInputTracking(
            epoch,
            contextChanged
                ? "context_changed"
                : "context_reused");

        DiagnosticLog.Write(
            "CONTEXT.APPLY",
            $"epoch={epoch.Id} " +
            $"contextChanged={contextChanged} " +
            $"process={snapshot.ProcessName} " +
            $"pid={snapshot.ProcessId} " +
            $"hwnd=0x{snapshot.Handle.ToInt64():X} " +
            $"privacy={privacy.Disposition} " +
            $"rule={privacy.RuleId} " +
            $"reason={privacy.Reason}");

        UpdateViewState(
            state =>
            {
                DesktopCopilotViewState updated =
                    state with
                    {
                        ProcessName =
                            snapshot.ProcessName,
                        ProcessId =
                            snapshot.ProcessId.ToString(),
                        WindowHandle =
                            $"0x{snapshot.Handle.ToInt64():X}",
                        LastObserved =
                            DateTime.Now.ToString(
                                "HH:mm:ss.fff")
                    };

                if (!privacy.AllowsSensing)
                {
                    return updated with
                    {
                        WindowTitle =
                            "[Privacy blocked]",
                        CaptureTargetStatus =
                            "Blocked by privacy policy.",
                        CapturedFrameStatus =
                            "Blocked by privacy policy.",
                        ChangeDetectionStatus =
                            "Blocked by privacy policy.",
                        PersistentChangeStatus =
                            "Blocked by privacy policy."
                    };
                }

                updated =
                    updated with
                    {
                        WindowTitle =
                            string.IsNullOrWhiteSpace(
                                snapshot.WindowTitle)
                                ? "(no title)"
                                : snapshot.WindowTitle
                    };

                if (!contextChanged)
                {
                    return updated;
                }

                return updated with
                {
                    CaptureTargetStatus =
                        "Not tested",
                    CapturedFrameStatus =
                        "Not captured",
                    ChangeDetectionStatus =
                        "No change samples yet",
                    PersistentChangeStatus =
                        _persistentChangeDetectionService.IsRunning
                            ? updated.PersistentChangeStatus
                            : "Stopped | context changed"
                };
            });

        if (!privacy.AllowsSensing)
        {
            DiagnosticLog.Write(
                "COORD.PRIVACY_BLOCKED",
                $"epoch={epoch.Id} " +
                $"process={snapshot.ProcessName} " +
                $"rule={privacy.RuleId} " +
                $"reason={privacy.Reason}");

            return;
        }

        DiagnosticLog.Write(
            "COORD.DISPLAY_DONE",
            $"epoch={epoch.Id} " +
            $"process={snapshot.ProcessName}");
    }

    private ContextEpoch? GetAllowedEpoch(
        string operation)
    {
        ContextEpoch? epoch =
            _currentEpoch;

        DiagnosticLog.Write(
            "SENSING.GATE",
            $"operation={operation} " +
            $"epoch={epoch?.Id ?? 0} " +
            $"privacy={epoch?.Privacy.Disposition.ToString() ?? "None"}");

        if (epoch is null)
        {
            DiagnosticLog.Write(
                "SENSING.GATE_REJECT",
                $"operation={operation} reason=no_epoch");

            return null;
        }

        if (!epoch.Privacy.AllowsSensing)
        {
            DiagnosticLog.Write(
                "CAPTURE.PRIVACY_REJECT",
                $"operation={operation} " +
                $"epoch={epoch.Id} " +
                $"process={epoch.Snapshot.ProcessName} " +
                $"rule={epoch.Privacy.RuleId} " +
                $"reason={epoch.Privacy.Reason}");

            return null;
        }

        if (epoch.CancellationToken.IsCancellationRequested)
        {
            DiagnosticLog.Write(
                "SENSING.GATE_REJECT",
                $"operation={operation} " +
                $"epoch={epoch.Id} reason=cancelled");

            return null;
        }

        return epoch;
    }

    private void SensingOrchestrator_StatusChanged(
        SensingOrchestratorUpdate update)
    {
        bool queued =
            _uiDispatcher.TryEnqueue(
                DispatcherQueuePriority.Normal,
                () =>
                {
                    SetOrchestratorStatus(
                        $"{update.Phase} | " +
                        $"epoch {update.EpochId} | " +
                        $"{update.Reason}");
                });

        if (!queued)
        {
            DiagnosticLog.Write(
                "ORCH.UI_QUEUE_REJECT",
                $"epoch={update.EpochId} " +
                $"phase={update.Phase}");
        }
    }

    private void RefreshInputTracking(
        ContextEpoch? epoch,
        string reason)
    {
        if (
            !_sensingOrchestrator.IsArmed ||
            epoch is null ||
            !epoch.Privacy.AllowsSensing)
        {
            StopInputTracking(
                reason);

            return;
        }

        _diagnosticTimeline.BeginEpoch(
            epoch.Id);

        try
        {
            _inputActivityTracker.Start(
                epoch.Id);
        }
        catch (Exception ex)
        {
            _diagnosticTimeline.Reset();

            DiagnosticLog.Write(
                "INPUT.TRACKING_START_ERROR",
                $"epoch={epoch.Id} " +
                $"type={ex.GetType().Name} " +
                $"hresult=0x{ex.HResult:X8}");
        }
    }

    private void StopInputTracking(
        string reason)
    {
        _inputActivityTracker.Stop(
            reason);

        _diagnosticTimeline.Reset();
    }

    private void InputActivityTracker_ActivityObserved(
        InputActivityEvent activity)
    {
        _diagnosticTimeline.Record(
            activity);
    }

    private void PersistentChangeDetectionService_SampleReady(
        PersistentChangeSample sample)
    {
        _changeCorrelationService.Observe(
            sample);

        bool queued =
            _uiDispatcher.TryEnqueue(
                DispatcherQueuePriority.Normal,
                () =>
                {
                    ContextEpoch? epoch =
                        _currentEpoch;

                    if (
                        epoch is null ||
                        epoch.Id !=
                            sample.EpochId ||
                        !epoch.Privacy.AllowsSensing)
                    {
                        DiagnosticLog.Write(
                            "PERSIST.UI_STALE_DROP",
                            $"sampleEpoch={sample.EpochId} " +
                            $"currentEpoch={epoch?.Id ?? 0}");

                        return;
                    }

                    SetPersistentChangeStatus(
                        $"RUNNING | " +
                        $"{sample.Change.Classification} | " +
                        $"pixels {sample.Change.ChangedPixelRatio:P2} | " +
                        $"tiles {sample.Change.ChangedTileRatio:P2} | " +
                        $"process {sample.ProcessingMilliseconds:0.0} ms | " +
                        $"diff {sample.Change.DiffMilliseconds:0.0} ms | " +
                        $"frames {sample.FramesArrived} | " +
                        $"replaced {sample.FramesReplaced} | " +
                        $"samples {sample.SamplesProcessed} | " +
                        $"recreate {sample.FramePoolRecreates}");

                    DiagnosticLog.Write(
                        "PERSIST.UI_SAMPLE",
                        $"epoch={sample.EpochId} " +
                        $"classification={sample.Change.Classification} " +
                        $"samples={sample.SamplesProcessed}");
                });

        if (!queued)
        {
            DiagnosticLog.Write(
                "PERSIST.UI_QUEUE_REJECT",
                $"epoch={sample.EpochId} " +
                "event=sample");
        }
    }

    private void PersistentChangeDetectionService_SessionEnded(
        PersistentChangeSessionEnded ended)
    {
        bool queued =
            _uiDispatcher.TryEnqueue(
                DispatcherQueuePriority.Normal,
                () =>
                {
                    ContextEpoch? epoch =
                        _currentEpoch;

                    if (
                        epoch is null ||
                        epoch.Id !=
                            ended.EpochId)
                    {
                        DiagnosticLog.Write(
                            "PERSIST.END_UI_STALE",
                            $"endedEpoch={ended.EpochId} " +
                            $"currentEpoch={epoch?.Id ?? 0} " +
                            $"reason={ended.Reason}");

                        return;
                    }

                    SetPersistentChangeStatus(
                        ended.HadError
                            ? $"Stopped with error | {ended.ErrorType}: {ended.ErrorMessage}"
                            : $"Stopped | {ended.Reason} | " +
                              $"frames {ended.FramesArrived} | " +
                              $"replaced {ended.FramesReplaced} | " +
                              $"samples {ended.SamplesProcessed} | " +
                              $"recreate {ended.FramePoolRecreates}");
                });

        if (!queued)
        {
            DiagnosticLog.Write(
                "PERSIST.UI_QUEUE_REJECT",
                $"epoch={ended.EpochId} " +
                "event=session_end");
        }
    }

    private void AttachServiceSubscriptions()
    {
        if (_subscriptionsAttached)
        {
            return;
        }

        _sensingOrchestrator.StatusChanged +=
            SensingOrchestrator_StatusChanged;

        _persistentChangeDetectionService.SampleReady +=
            PersistentChangeDetectionService_SampleReady;

        _persistentChangeDetectionService.SessionEnded +=
            PersistentChangeDetectionService_SessionEnded;

        _inputActivityTracker.ActivityObserved +=
            InputActivityTracker_ActivityObserved;

        _foregroundWindowObserver.ForegroundWindowChanged +=
            ForegroundWindowObserver_ForegroundWindowChanged;

        _subscriptionsAttached =
            true;

        DiagnosticLog.Write(
            "COORD.SUBSCRIBE",
            "Service subscriptions attached.");
    }

    private void DetachServiceSubscriptions()
    {
        if (!_subscriptionsAttached)
        {
            return;
        }

        _foregroundWindowObserver.ForegroundWindowChanged -=
            ForegroundWindowObserver_ForegroundWindowChanged;

        _inputActivityTracker.ActivityObserved -=
            InputActivityTracker_ActivityObserved;

        _persistentChangeDetectionService.SessionEnded -=
            PersistentChangeDetectionService_SessionEnded;

        _persistentChangeDetectionService.SampleReady -=
            PersistentChangeDetectionService_SampleReady;

        _sensingOrchestrator.StatusChanged -=
            SensingOrchestrator_StatusChanged;

        _subscriptionsAttached =
            false;

        DiagnosticLog.Write(
            "COORD.UNSUBSCRIBE",
            "Service subscriptions detached.");
    }

    private void SetCaptureTargetStatus(
        string status)
    {
        UpdateViewState(
            state =>
                state with
                {
                    CaptureTargetStatus =
                        status
                });
    }

    private void SetCapturedFrameStatus(
        string status)
    {
        UpdateViewState(
            state =>
                state with
                {
                    CapturedFrameStatus =
                        status
                });
    }

    private void SetChangeDetectionStatus(
        string status)
    {
        UpdateViewState(
            state =>
                state with
                {
                    ChangeDetectionStatus =
                        status
                });
    }

    private void SetPersistentChangeStatus(
        string status)
    {
        UpdateViewState(
            state =>
                state with
                {
                    PersistentChangeStatus =
                        status
                });
    }

    private void SetOrchestratorStatus(
        string status)
    {
        UpdateViewState(
            state =>
                state with
                {
                    OrchestratorStatus =
                        status
                });
    }

    private void UpdateViewState(
        Func<DesktopCopilotViewState,
            DesktopCopilotViewState> update)
    {
        ArgumentNullException.ThrowIfNull(
            update);

        if (!_uiDispatcher.HasThreadAccess)
        {
            bool queued =
                _uiDispatcher.TryEnqueue(
                    DispatcherQueuePriority.Normal,
                    () =>
                    {
                        UpdateViewState(
                            update);
                    });

            if (!queued)
            {
                DiagnosticLog.Write(
                    "COORD.VIEW_QUEUE_REJECT",
                    $"state={_lifecycle.State}");
            }

            return;
        }

        IDesktopCopilotView? view;
        DesktopCopilotViewState state;

        lock (_viewGate)
        {
            _viewState =
                update(
                    _viewState);

            state =
                _viewState;

            view =
                _view;
        }

        if (view is not null)
        {
            RenderView(
                view,
                state);
        }
    }

    private static void RenderView(
        IDesktopCopilotView view,
        DesktopCopilotViewState state)
    {
        try
        {
            view.Render(
                state);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                "COORD.VIEW_RENDER_ERROR",
                $"type={ex.GetType().Name}");
        }
    }

    private void EnsureUiThread(
        string operation)
    {
        if (_uiDispatcher.HasThreadAccess)
        {
            return;
        }

        DiagnosticLog.Write(
            "COORD.THREAD_REJECT",
            $"operation={operation}");

        throw new InvalidOperationException(
            $"Coordinator operation '{operation}' requires the UI thread.");
    }
}
