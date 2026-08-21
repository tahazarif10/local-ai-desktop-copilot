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

        ForegroundWindowService foregroundWindowService =
            new();

        PersistentChangeDetectionService
            persistentChangeDetectionService =
                new(
                    foregroundWindowService);

        DiagnosticTimeline diagnosticTimeline =
            new();

        return new DesktopCopilotCoordinator(
            unchecked(
                (uint)Environment.ProcessId),
            uiDispatcher,
            foregroundWindowService,
            new ForegroundWindowObserver(),
            PrivacyPolicy.CreateDefault(),
            new ContextEpochManager(),
            new ChangeDetectionProbeService(
                foregroundWindowService),
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
