using Microsoft.UI.Dispatching;
using System;

namespace LocalCopilot_App.Services;

internal sealed record ApplicationComposition(
    DesktopCopilotCoordinator Coordinator,
    UiEnrichmentRuntimeService UiEnrichmentRuntimeService);

public static class ApplicationCompositionRoot
{
    internal static ApplicationComposition Create(
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

        ContextEpochManager contextEpochManager =
            new();

        SensingOrchestrator sensingOrchestrator =
            new(
                persistentChangeDetectionService,
                uiDispatcher);

        UiAutomationProbeWorker uiAutomationProbeWorker =
            new(
                foregroundWindowService);

        DesktopCopilotCoordinator coordinator =
            new(
                unchecked(
                    (uint)Environment.ProcessId),
                uiDispatcher,
                foregroundWindowService,
                new ForegroundWindowObserver(),
                PrivacyPolicy.CreateDefault(),
                contextEpochManager,
                new ChangeDetectionProbeService(
                    foregroundWindowService),
                persistentChangeDetectionService,
                sensingOrchestrator,
                diagnosticTimeline,
                new InputActivityTracker(),
                new ChangeCorrelationService(
                    diagnosticTimeline),
                uiAutomationProbeWorker);

        UiEnrichmentRuntimeService uiEnrichmentRuntimeService =
            new(
                uiDispatcher,
                contextEpochManager,
                sensingOrchestrator,
                persistentChangeDetectionService,
                uiAutomationProbeWorker);

        return new ApplicationComposition(
            coordinator,
            uiEnrichmentRuntimeService);
    }
}
