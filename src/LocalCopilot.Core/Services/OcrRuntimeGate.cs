using System;

namespace LocalCopilot_App.Services;

public static class OcrRuntimeGate
{
    public static PrivacyCapability RequiredCapabilities(
        OcrExecutionTopology topology) =>
        topology switch
        {
            OcrExecutionTopology.ClientLocal =>
                PrivacyCapability.CapturePixels |
                PrivacyCapability.RunOcr,

            OcrExecutionTopology.LocalAiServer =>
                PrivacyCapability.CapturePixels |
                PrivacyCapability.RunOcr |
                PrivacyCapability.SendPixelsToLocalServer,

            _ => throw new ArgumentOutOfRangeException(nameof(topology))
        };

    public static OcrDispatchDecision MayDispatch(
        OcrDispatchRequest request,
        bool isArmed,
        ContextEpoch? currentEpoch)
    {
        ArgumentNullException.ThrowIfNull(request);

        PrivacyCapability required =
            RequiredCapabilities(request.Topology);

        if (!isArmed)
        {
            return Reject(
                OcrDispatchRejectionReason.NotArmed,
                required);
        }

        if (currentEpoch is null)
        {
            return Reject(
                OcrDispatchRejectionReason.NoCurrentEpoch,
                required);
        }

        if (currentEpoch.Id != request.EpochId)
        {
            return Reject(
                OcrDispatchRejectionReason.EpochMismatch,
                required);
        }

        if (currentEpoch.CancellationToken.IsCancellationRequested)
        {
            return Reject(
                OcrDispatchRejectionReason.EpochCancelled,
                required);
        }

        if (!currentEpoch.Privacy.Allows(required))
        {
            return Reject(
                OcrDispatchRejectionReason.CapabilityDenied,
                required);
        }

        if (request.Region.Bounds.IsEmpty)
        {
            return Reject(
                OcrDispatchRejectionReason.EmptyRegion,
                required);
        }

        return new OcrDispatchDecision(
            Allowed: true,
            OcrDispatchRejectionReason.None,
            required);
    }

    public static OcrPublicationDecision MayPublish(
        OcrDispatchRequest request,
        long latestRequestId,
        bool isArmed,
        ContextEpoch? currentEpoch)
    {
        ArgumentNullException.ThrowIfNull(request);

        OcrDispatchDecision dispatch =
            MayDispatch(
                request,
                isArmed,
                currentEpoch);

        if (!dispatch.Allowed)
        {
            return new OcrPublicationDecision(
                OcrPublicationOutcome.Stale,
                dispatch.RejectionReason);
        }

        if (latestRequestId != request.RequestId)
        {
            return new OcrPublicationDecision(
                OcrPublicationOutcome.Stale,
                OcrDispatchRejectionReason.Superseded);
        }

        return new OcrPublicationDecision(
            OcrPublicationOutcome.Publishable,
            OcrDispatchRejectionReason.None);
    }

    private static OcrDispatchDecision Reject(
        OcrDispatchRejectionReason reason,
        PrivacyCapability required) =>
        new(
            Allowed: false,
            reason,
            required);
}
