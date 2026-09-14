using System;

namespace LocalCopilot_App.Services;

public static class UiEnrichmentRuntimeGate
{
    public static bool MayDispatch(
        UiEnrichmentRequest request,
        bool isArmed,
        ContextEpoch? currentEpoch)
    {
        ArgumentNullException.ThrowIfNull(request);

        return
            isArmed &&
            currentEpoch is not null &&
            currentEpoch.Id == request.EpochId &&
            !currentEpoch.CancellationToken.IsCancellationRequested &&
            currentEpoch.Privacy.Allows(
                PrivacyCapability.ReadUiStructure |
                PrivacyCapability.ReadUiText);
    }

    public static UiAutomationProbeResult ApplyPublication(
        UiEnrichmentRequest request,
        UiAutomationProbeResult result,
        ContextEpoch? currentEpoch,
        long latestWorkerRequestId,
        bool isArmed,
        DateTimeOffset? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        UiAutomationProbeResult publishable =
            UiAutomationProbePublicationGate.Apply(
                result,
                currentEpoch,
                latestWorkerRequestId,
                utcNow);

        bool mayPublish =
            isArmed &&
            publishable.EpochId == request.EpochId &&
            publishable.RequestId == latestWorkerRequestId;

        if (mayPublish)
        {
            return publishable;
        }

        publishable.SemanticSnapshot?.Dispose();

        return publishable with
        {
            Outcome = UiAutomationProbeOutcome.Stale,
            Reason = UiAutomationProbeReason.PublicationRejected,
            Snapshot = null,
            SemanticSnapshot = null
        };
    }
}
