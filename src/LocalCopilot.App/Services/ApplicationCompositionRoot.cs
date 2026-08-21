using Microsoft.UI.Dispatching;
using System;

namespace LocalCopilot_App.Services;

public static class ApplicationCompositionRoot
{
    public static DesktopCopilotCoordinator Create(
        DispatcherQueue uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(
            uiDispatcher);

        PersistentChangeDetectionService
            persistentChangeDetectionService =
                new();

        DiagnosticTimeline diagnosticTimeline =
            new();

        return new DesktopCopilotCoordinator(
            unchecked(
                (uint)Environment.ProcessId),
            uiDispatcher,
            new ForegroundWindowService(),
            new ForegroundWindowObserver(),
            PrivacyPolicy.CreateDefault(),
            new ContextEpochManager(),
            new ChangeDetectionProbeService(),
            persistentChangeDetectionService,
            new SensingOrchestrator(
                persistentChangeDetectionService,
                uiDispatcher),
            diagnosticTimeline,
            new InputActivityTracker(),
            new ChangeCorrelationService(
                diagnosticTimeline));
    }
}
