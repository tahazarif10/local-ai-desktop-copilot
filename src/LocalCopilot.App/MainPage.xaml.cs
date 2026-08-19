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
    private readonly uint _ownProcessId;

    private readonly ForegroundWindowService
        _foregroundWindowService;

    private readonly ForegroundWindowObserver
        _foregroundWindowObserver;

    private readonly DispatcherQueue
        _uiDispatcher;

    private ForegroundWindowSnapshot?
        _lastSnapshot;

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
                $"snapshot hwnd=0x{snapshot.Handle.ToInt64():X} " +
                $"process={snapshot.ProcessName}");

            DisplaySnapshot(snapshot);
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

                        DisplaySnapshot(
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

    private void CaptureTargetProbeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DiagnosticLog.Write(
            "CAPTURE.PROBE_CLICK",
            $"hasSnapshot={_lastSnapshot is not null}");

        if (_lastSnapshot is null)
        {
            CaptureTargetStatusText.Text =
                "No external foreground window has been observed yet.";

            DiagnosticLog.Write(
                "CAPTURE.PROBE_REJECT",
                "reason=no_snapshot");

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

        DiagnosticLog.Write(
            "CAPTURE.PROBE_BEGIN",
            $"hwnd=0x{_lastSnapshot.Handle.ToInt64():X} " +
            $"pid={_lastSnapshot.ProcessId} " +
            $"process={_lastSnapshot.ProcessName} " +
            $"title=[{_lastSnapshot.WindowTitle}]");

        try
        {
            GraphicsCaptureItem item =
                GraphicsCaptureItemFactory.CreateForWindow(
                    _lastSnapshot.Handle);

            CaptureTargetStatusText.Text =
                $"OK | {item.Size.Width} x {item.Size.Height}" +
                $" | {item.DisplayName}";

            DiagnosticLog.Write(
                "CAPTURE.PROBE_OK",
                $"hwnd=0x{_lastSnapshot.Handle.ToInt64():X} " +
                $"size={item.Size.Width}x{item.Size.Height} " +
                $"displayName=[{item.DisplayName}]");
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
        DiagnosticLog.Write(
            "CAPTURE.FRAME_CLICK",
            $"hasSnapshot={_lastSnapshot is not null}");

        if (_lastSnapshot is null)
        {
            CapturedFrameStatusText.Text =
                "No capture target available.";

            DiagnosticLog.Write(
                "CAPTURE.FRAME_REJECT",
                "reason=no_snapshot");

            return;
        }

        DiagnosticLog.Write(
            "CAPTURE.FRAME_BEGIN",
            $"hwnd=0x{_lastSnapshot.Handle.ToInt64():X} " +
            $"pid={_lastSnapshot.ProcessId} " +
            $"process={_lastSnapshot.ProcessName} " +
            $"title=[{_lastSnapshot.WindowTitle}]");

        try
        {
            GraphicsCaptureItem item =
                GraphicsCaptureItemFactory.CreateForWindow(
                    _lastSnapshot.Handle);

            DiagnosticLog.Write(
                "CAPTURE.FRAME_ITEM",
                $"itemSize={item.Size.Width}x{item.Size.Height} " +
                $"displayName=[{item.DisplayName}]");

            SingleFrameCaptureInfo frame =
                await SingleFrameCaptureService.CaptureAsync(
                    item,
                    TimeSpan.FromSeconds(5),
                    2560);

            DiagnosticLog.Write(
                "CAPTURE.FRAME_OK",
                $"content={frame.ContentWidth}x{frame.ContentHeight} " +
                $"surface={frame.SurfaceWidth}x{frame.SurfaceHeight} " +
                $"frameMs={frame.FrameMilliseconds:0.0}");

            DiagnosticLog.Write(
                "CAPTURE.RESIZE_OK",
                $"source={frame.ContentWidth}x{frame.ContentHeight} " +
                $"output={frame.OutputWidth}x{frame.OutputHeight} " +
                $"scale={frame.ScaleFactor:0.0000} " +
                $"resizeMs={frame.ResizeMilliseconds:0.0}");

            DiagnosticLog.Write(
                "CAPTURE.BITMAP_OK",
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
    private void DisplaySnapshot(
        ForegroundWindowSnapshot snapshot)
    {
        _lastSnapshot =
            snapshot;

        DiagnosticLog.Write(
            "PAGE.DISPLAY",
            $"process={snapshot.ProcessName} " +
            $"pid={snapshot.ProcessId} " +
            $"hwnd=0x{snapshot.Handle.ToInt64():X} " +
            $"title=[{snapshot.WindowTitle}]");

        ProcessNameText.Text =
            snapshot.ProcessName;

        WindowTitleText.Text =
            string.IsNullOrWhiteSpace(
                snapshot.WindowTitle)
                ? "(no title)"
                : snapshot.WindowTitle;

        ProcessIdText.Text =
            snapshot.ProcessId.ToString();

        WindowHandleText.Text =
            $"0x{snapshot.Handle.ToInt64():X}";

        LastObservedText.Text =
            DateTime.Now.ToString(
                "HH:mm:ss.fff");

        DiagnosticLog.Write(
            "PAGE.DISPLAY_DONE",
            $"ProcessNameText={ProcessNameText.Text}");
    }
}
