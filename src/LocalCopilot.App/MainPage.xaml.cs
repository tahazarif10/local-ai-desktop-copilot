using LocalCopilot_App.Diagnostics;
using LocalCopilot_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace LocalCopilot_App;

public sealed partial class MainPage :
    Page,
    IDesktopCopilotView
{
    private readonly DesktopCopilotCoordinator
        _coordinator;

    public MainPage()
        : this(
            ((App)Application.Current)
                .Coordinator)
    {
    }

    internal MainPage(
        DesktopCopilotCoordinator coordinator)
    {
        _coordinator =
            coordinator ??
            throw new ArgumentNullException(
                nameof(coordinator));

        DiagnosticLog.Write(
            "PAGE.CTOR",
            "Before InitializeComponent.");

        InitializeComponent();

        DiagnosticLog.Write(
            "PAGE.CTOR",
            "After InitializeComponent.");

        Loaded +=
            MainPage_Loaded;

        Unloaded +=
            MainPage_Unloaded;
    }

    public void Render(
        DesktopCopilotViewState state)
    {
        ArgumentNullException.ThrowIfNull(
            state);

        ProcessNameText.Text =
            state.ProcessName;

        WindowTitleText.Text =
            state.WindowTitle;

        ProcessIdText.Text =
            state.ProcessId;

        WindowHandleText.Text =
            state.WindowHandle;

        LastObservedText.Text =
            state.LastObserved;

        CaptureTargetStatusText.Text =
            state.CaptureTargetStatus;

        CapturedFrameStatusText.Text =
            state.CapturedFrameStatus;

        ChangeDetectionStatusText.Text =
            state.ChangeDetectionStatus;

        PersistentChangeStatusText.Text =
            state.PersistentChangeStatus;

        OrchestratorStatusText.Text =
            state.OrchestratorStatus;
    }

    private void MainPage_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        DiagnosticLog.Write(
            "PAGE.LOADED",
            $"coordinatorRunning={_coordinator.IsRunning}");

        _coordinator.AttachView(
            this);
    }

    private void MainPage_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        DiagnosticLog.Write(
            "PAGE.UNLOADED",
            $"coordinatorRunning={_coordinator.IsRunning}");

        _coordinator.DetachView(
            this);
    }

    private void CaptureTargetProbeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _coordinator.ProbeCaptureTarget();
    }

    private async void CaptureOneFrameButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await _coordinator.CaptureOneFrameAsync();
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

    private Task RunChangeDetectionSampleAsync(
        int profileWidth)
    {
        return _coordinator
            .RunChangeDetectionSampleAsync(
                profileWidth);
    }

    private void ArmSensingOrchestratorButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _coordinator.Arm();
    }

    private void DisarmSensingOrchestratorButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _coordinator.Disarm();
    }

    private void StartPersistentChangeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _coordinator.StartManualPersistentSensing();
    }

    private void StopPersistentChangeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _coordinator.StopManualPersistentSensing();
    }
}
