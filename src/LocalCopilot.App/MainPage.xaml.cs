using LocalCopilot_App.Diagnostics;
using LocalCopilot_App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;

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

    private void DisplaySnapshot(
        ForegroundWindowSnapshot snapshot)
    {
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
