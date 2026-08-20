using LocalCopilot_App.Diagnostics;
using LocalCopilot_App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Graphics.Capture;

namespace LocalCopilot_App;

public sealed partial class MainPage : Page
{
    private readonly uint
        _ownProcessId;

    private readonly ForegroundWindowService
        _foregroundWindowService;

    private readonly ForegroundWindowObserver
        _foregroundWindowObserver;

    private readonly DispatcherQueue
        _uiDispatcher;

    private readonly PrivacyPolicy
        _privacyPolicy;

    private readonly ContextEpochManager
        _contextEpochManager;

    private readonly ChangeDetectionProbeService
        _changeDetectionProbeService;

    private ForegroundWindowSnapshot?
        _lastSnapshot;

    private ContextEpoch?
        _currentEpoch;

    public MainPage()
    {
        DiagnosticLog.ResetSession();

        DiagnosticLog.Write(
            "PAGE.CTOR",
            "Before InitializeComponent.");

        InitializeComponent();

        DiagnosticLog.Write(
            "PAGE.CTOR",
            "After InitializeComponent.");

        _ownProcessId =
            unchecked(
                (uint)Environment.ProcessId);

        DiagnosticLog.Write(
            "PAGE.CTOR",
            $"ownPid={_ownProcessId}");

        _foregroundWindowService =
            new ForegroundWindowService();

        _foregroundWindowObserver =
            new ForegroundWindowObserver();

        _privacyPolicy =
            PrivacyPolicy.CreateDefault();

        _contextEpochManager =
            new ContextEpochManager();

        _changeDetectionProbeService =
            new ChangeDetectionProbeService();

        _uiDispatcher =
            DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "UI DispatcherQueue unavailable.");

        DiagnosticLog.Write(
            "PAGE.DISPATCHER",
            $"HasThreadAccess={_uiDispatcher.HasThreadAccess}");

        _foregroundWindowObserver.ForegroundWindowChanged +=
            ForegroundWindowObserver_ForegroundWindowChanged;

        Loaded +=
            MainPage_Loaded;

        Unloaded +=
            MainPage_Unloaded;

        DiagnosticLog.Write(
            "PAGE.CTOR",
            "Constructor complete.");
    }

    private void MainPage_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        DiagnosticLog.Write(
            "PAGE.LOADED",
            $"HasThreadAccess={_uiDispatcher.HasThreadAccess}");

        try
        {
            _foregroundWindowObserver.Start();

            DiagnosticLog.Write(
                "PAGE.LOADED",
                $"observerRunning={_foregroundWindowObserver.IsRunning}");

            ForegroundWindowObservation? observation =
                _foregroundWindowService.GetCurrent(
                    _privacyPolicy,
                    _ownProcessId);

            if (observation is null)
            {
                DiagnosticLog.Write(
                    "PAGE.INITIAL",
                    "Initial observation=null.");

                return;
            }

            ForegroundWindowSnapshot snapshot =
                observation.Snapshot;

            DiagnosticLog.Write(
                "PAGE.INITIAL",
                $"hwnd=0x{snapshot.Handle.ToInt64():X} " +
                $"process={snapshot.ProcessName} " +
                $"privacy={observation.Privacy.Disposition}");

            ApplyObservation(
                observation);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                "PAGE.LOADED_ERROR",
                ex.ToString());

            ProcessNameText.Text =
                "Observer start failed";

            WindowTitleText.Text =
                ex.Message;
        }
    }

    private void MainPage_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        DiagnosticLog.Write(
            "PAGE.UNLOADED",
            $"HasThreadAccess={_uiDispatcher.HasThreadAccess}");

        bool stopped =
            _foregroundWindowObserver.Stop();

        DiagnosticLog.Write(
            "PAGE.UNLOADED",
            $"observerStopped={stopped}");

        _contextEpochManager.Reset();

        _currentEpoch =
            null;

        _lastSnapshot =
            null;

        _changeDetectionProbeService.ResetAll();
    }

    private void ForegroundWindowObserver_ForegroundWindowChanged(
        nint hwnd)
    {
        DiagnosticLog.Write(
            "PAGE.EVENT_RECEIVED",
            $"hwnd=0x{hwnd.ToInt64():X} " +
            $"HasThreadAccess={_uiDispatcher.HasThreadAccess}");

        bool queued =
            _uiDispatcher.TryEnqueue(
                DispatcherQueuePriority.High,
                () =>
                {
                    DiagnosticLog.Write(
                        "PAGE.QUEUE_EXECUTE",
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
                                "PAGE.QUEUE_RESULT",
                                "observation=null");

                            return;
                        }

                        ForegroundWindowSnapshot snapshot =
                            observation.Snapshot;

                        DiagnosticLog.Write(
                            "PAGE.QUEUE_RESULT",
                            $"process={snapshot.ProcessName} " +
                            $"hwnd=0x{snapshot.Handle.ToInt64():X} " +
                            $"privacy={observation.Privacy.Disposition}");

                        ApplyObservation(
                            observation);
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Write(
                            "PAGE.QUEUE_ERROR",
                            ex.ToString());
                    }
                });

        DiagnosticLog.Write(
            "PAGE.ENQUEUE",
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

        _lastSnapshot =
            snapshot;

        _currentEpoch =
            epoch;

        _changeDetectionProbeService.ObserveContext(
            epoch);

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

        ProcessNameText.Text =
            snapshot.ProcessName;

        ProcessIdText.Text =
            snapshot.ProcessId.ToString();

        WindowHandleText.Text =
            $"0x{snapshot.Handle.ToInt64():X}";

        LastObservedText.Text =
            DateTime.Now.ToString(
                "HH:mm:ss.fff");

        if (!privacy.AllowsSensing)
        {
            WindowTitleText.Text =
                "[Privacy blocked]";

            CaptureTargetStatusText.Text =
                "Blocked by privacy policy.";

            CapturedFrameStatusText.Text =
                "Blocked by privacy policy.";

            ChangeDetectionStatusText.Text =
                "Blocked by privacy policy.";

            DiagnosticLog.Write(
                "PAGE.PRIVACY_BLOCKED",
                $"epoch={epoch.Id} " +
                $"process={snapshot.ProcessName} " +
                $"rule={privacy.RuleId} " +
                $"reason={privacy.Reason}");

            return;
        }

        if (contextChanged)
        {
            CaptureTargetStatusText.Text =
                "Not tested";

            CapturedFrameStatusText.Text =
                "Not captured";

            ChangeDetectionStatusText.Text =
                "No change samples yet";
        }

        WindowTitleText.Text =
            string.IsNullOrWhiteSpace(
                snapshot.WindowTitle)
                ? "(no title)"
                : snapshot.WindowTitle;

        DiagnosticLog.Write(
            "PAGE.DISPLAY_DONE",
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

    private void CaptureTargetProbeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ContextEpoch? epoch =
            GetAllowedEpoch(
                "target_probe");

        if (epoch is null)
        {
            CaptureTargetStatusText.Text =
                _currentEpoch is not null &&
                !_currentEpoch.Privacy.AllowsSensing
                    ? "Blocked by privacy policy."
                    : "No allowed capture target.";

            return;
        }

        bool captureSupported =
            GraphicsCaptureSession.IsSupported();

        DiagnosticLog.Write(
            "CAPTURE.SUPPORT",
            $"supported={captureSupported}");

        if (!captureSupported)
        {
            CaptureTargetStatusText.Text =
                "Windows Graphics Capture is not supported.";

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

            CaptureTargetStatusText.Text =
                $"OK | {item.Size.Width} x {item.Size.Height}" +
                $" | {item.DisplayName}";

            DiagnosticLog.Write(
                "CAPTURE.PROBE_OK",
                $"epoch={epoch.Id} " +
                $"hwnd=0x{snapshot.Handle.ToInt64():X} " +
                $"size={item.Size.Width}x{item.Size.Height}");
        }
        catch (Exception ex)
        {
            CaptureTargetStatusText.Text =
                $"Capture target failed: {ex.Message}";

            DiagnosticLog.Write(
                "CAPTURE.PROBE_ERROR",
                ex.ToString());
        }
    }

    private async void CaptureOneFrameButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ContextEpoch? epoch =
            GetAllowedEpoch(
                "single_frame");

        if (epoch is null)
        {
            CapturedFrameStatusText.Text =
                _currentEpoch is not null &&
                !_currentEpoch.Privacy.AllowsSensing
                    ? "Blocked by privacy policy."
                    : "No allowed capture target.";

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

                CapturedFrameStatusText.Text =
                    "Ignored stale capture result.";

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

            CapturedFrameStatusText.Text =
                $"RAM OK | " +
                $"{frame.OutputWidth} x {frame.OutputHeight} | " +
                $"{frame.CpuBytes / 1024.0 / 1024.0:0.0} MB | " +
                $"{frame.TotalMilliseconds:0.0} ms";
        }
        catch (Exception ex)
        {
            CapturedFrameStatusText.Text =
                $"RAM capture failed: {ex.Message}";

            DiagnosticLog.Write(
                "CAPTURE.BITMAP_ERROR",
                ex.ToString());
        }
    }
    private async void Change640Button_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RunChangeDetectionSampleAsync(
            640);
    }

    private async void Change960Button_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RunChangeDetectionSampleAsync(
            960);
    }

    private async Task RunChangeDetectionSampleAsync(
        int profileWidth)
    {
        ContextEpoch? epoch =
            GetAllowedEpoch(
                $"change_{profileWidth}");

        if (epoch is null)
        {
            ChangeDetectionStatusText.Text =
                _currentEpoch is not null &&
                !_currentEpoch.Privacy.AllowsSensing
                    ? "Blocked by privacy policy."
                    : "No allowed change-detection target.";

            return;
        }

        DiagnosticLog.Write(
            "CHANGE.SAMPLE_BEGIN",
            $"epoch={epoch.Id} " +
            $"profile={profileWidth} " +
            $"hwnd=0x{epoch.Snapshot.Handle.ToInt64():X} " +
            $"pid={epoch.Snapshot.ProcessId} " +
            $"process={epoch.Snapshot.ProcessName}");

        ChangeDetectionStatusText.Text =
            $"Sampling {profileWidth}px...";

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

                ChangeDetectionStatusText.Text =
                    "Ignored stale change sample.";

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

            ChangeDetectionStatusText.Text =
                $"{profileWidth}px | " +
                $"{change.Classification} | " +
                $"pixels {change.ChangedPixelRatio:P2} | " +
                $"tiles {change.ChangedTileRatio:P2} | " +
                $"diff {change.DiffMilliseconds:0.0} ms | " +
                $"total {probe.TotalMilliseconds:0.0} ms";
        }
        catch (OperationCanceledException)
        {
            DiagnosticLog.Write(
                "CHANGE.SAMPLE_CANCELLED",
                $"epoch={epoch.Id} " +
                $"profile={profileWidth}");

            ChangeDetectionStatusText.Text =
                "Change sample cancelled.";
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                "CHANGE.SAMPLE_ERROR",
                $"epoch={epoch.Id} " +
                $"profile={profileWidth} " +
                $"type={ex.GetType().Name} " +
                $"message={ex.Message}");

            ChangeDetectionStatusText.Text =
                $"Change sample failed: {ex.Message}";
        }
    }
}