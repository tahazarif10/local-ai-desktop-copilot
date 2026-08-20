using LocalCopilot_App.Diagnostics;
using LocalCopilot_App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
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

            ForegroundWindowSnapshot? snapshot =
                _foregroundWindowService.GetCurrent(
                    _ownProcessId);

            if (snapshot is null)
            {
                DiagnosticLog.Write(
                    "PAGE.INITIAL",
                    "Initial snapshot=null.");

                return;
            }

            DiagnosticLog.Write(
                "PAGE.INITIAL",
                $"hwnd=0x{snapshot.Handle.ToInt64():X} " +
                $"process={snapshot.ProcessName}");

            ApplySnapshot(
                snapshot);
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

        _contextEpochManager.Dispose();
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
                        ForegroundWindowSnapshot? snapshot =
                            _foregroundWindowService.GetFromHandle(
                                hwnd,
                                _ownProcessId);

                        if (snapshot is null)
                        {
                            DiagnosticLog.Write(
                                "PAGE.QUEUE_RESULT",
                                "snapshot=null");

                            return;
                        }

                        DiagnosticLog.Write(
                            "PAGE.QUEUE_RESULT",
                            $"process={snapshot.ProcessName} " +
                            $"hwnd=0x{snapshot.Handle.ToInt64():X}");

                        ApplySnapshot(
                            snapshot);
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

    private void ApplySnapshot(
        ForegroundWindowSnapshot snapshot)
    {
        PrivacyEvaluation privacy =
            _privacyPolicy.Evaluate(
                snapshot);

        ContextEpoch epoch =
            _contextEpochManager.Advance(
                snapshot,
                privacy);

        _lastSnapshot =
            snapshot;

        _currentEpoch =
            epoch;

        DiagnosticLog.Write(
            "CONTEXT.APPLY",
            $"epoch={epoch.Id} " +
            $"process={snapshot.ProcessName} " +
            $"pid={snapshot.ProcessId} " +
            $"hwnd=0x{snapshot.Handle.ToInt64():X} " +
            $"privacy={privacy.Disposition} " +
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

            DiagnosticLog.Write(
                "PRIVACY.BLOCK",
                $"epoch={epoch.Id} " +
                $"process={snapshot.ProcessName} " +
                $"reason={privacy.Reason}");

            return;
        }

        WindowTitleText.Text =
            string.IsNullOrWhiteSpace(
                snapshot.WindowTitle)
                ? "(no title)"
                : snapshot.WindowTitle;

        DiagnosticLog.Write(
            "PRIVACY.ALLOW",
            $"epoch={epoch.Id} " +
            $"process={snapshot.ProcessName}");

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
}