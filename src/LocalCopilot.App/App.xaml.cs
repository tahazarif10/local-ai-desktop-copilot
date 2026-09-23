using LocalCopilot_App.Diagnostics;
using LocalCopilot_App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace LocalCopilot_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window?
        _window;

    private DesktopCopilotCoordinator?
        _coordinator;

    private UiEnrichmentRuntimeService?
        _uiEnrichmentRuntimeService;

    private OcrEnrichmentRuntimeService?
        _ocrEnrichmentRuntimeService;

    internal DesktopCopilotCoordinator Coordinator =>
        _coordinator ??
        throw new InvalidOperationException(
            "Application coordinator is not initialized.");

    internal UiEnrichmentRuntimeService UiEnrichmentRuntimeService =>
        _uiEnrichmentRuntimeService ??
        throw new InvalidOperationException(
            "UI enrichment runtime is not initialized.");
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        DiagnosticLog.Initialize(
            Environment.GetCommandLineArgs());

        DiagnosticLog.ResetSession();

        DispatcherQueue uiDispatcher =
            DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "UI DispatcherQueue unavailable.");

        ApplicationComposition composition =
            ApplicationCompositionRoot.Create(
                uiDispatcher);

        DesktopCopilotCoordinator coordinator =
            composition.Coordinator;

        _coordinator =
            coordinator;

        _uiEnrichmentRuntimeService =
            composition.UiEnrichmentRuntimeService;

        _ocrEnrichmentRuntimeService =
            composition.OcrEnrichmentRuntimeService;

        MainWindow window =
            new MainWindow(
                coordinator);

        _window =
            window;

        window.Closed +=
            MainWindow_Closed;

        coordinator.Start();

        window.Activate();
    }

    private void MainWindow_Closed(
        object sender,
        WindowEventArgs args)
    {
        if (_window is not null)
        {
            _window.Closed -=
                MainWindow_Closed;
        }

        // Stop content-bearing enrichment before the coordinator tears down
        // capture, epoch, and shared UIA ownership.
        _ocrEnrichmentRuntimeService?.Dispose();
        _ocrEnrichmentRuntimeService =
            null;

        _uiEnrichmentRuntimeService?.Dispose();
        _uiEnrichmentRuntimeService =
            null;

        _coordinator?.Dispose();

        _coordinator =
            null;

        _window =
            null;
    }
}
