using Microsoft.UI.Dispatching;
using System;

namespace LocalCopilot_App.Services;

internal sealed record ApplicationComposition(
    DesktopCopilotCoordinator Coordinator,
    UiEnrichmentRuntimeService UiEnrichmentRuntimeService,
    OcrEnrichmentRuntimeService? OcrEnrichmentRuntimeService);

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

        OcrEnrichmentRuntimeService? ocrEnrichmentRuntimeService =
            null;

        if (LocalCopilot_App.Diagnostics.DiagnosticLog.IsOcrEnabled &&
            OcrRuntimeConfiguration.TryLoad(
                out OcrRuntimeConfiguration? ocrConfiguration))
        {
            OcrServerTransport transport =
                ocrConfiguration!.CreateTransport();

            try
            {
                ocrEnrichmentRuntimeService =
                    new OcrEnrichmentRuntimeService(
                        contextEpochManager,
                        sensingOrchestrator,
                        persistentChangeDetectionService,
                        foregroundWindowService,
                        transport);
            }
            catch
            {
                transport.Dispose();
                throw;
            }
        }

        return new ApplicationComposition(
            coordinator,
            uiEnrichmentRuntimeService,
            ocrEnrichmentRuntimeService);
    }
}
