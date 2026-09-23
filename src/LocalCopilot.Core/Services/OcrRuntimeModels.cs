using System;

namespace LocalCopilot_App.Services;

public enum OcrExecutionTopology
{
    ClientLocal,
    LocalAiServer
}

public enum OcrDispatchRejectionReason
{
    None,
    NotArmed,
    NoCurrentEpoch,
    EpochMismatch,
    EpochCancelled,
    CapabilityDenied,
    EmptyRegion
}

public sealed record OcrDispatchRequest(
    long RequestId,
    long EpochId,
    PlannedCaptureRegion Region,
    OcrExecutionTopology Topology)
{
    public OcrDispatchRequest
    {
        if (RequestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RequestId));
        }

        if (EpochId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(EpochId));
        }

        ArgumentNullException.ThrowIfNull(Region);
    }
}

public sealed record OcrDispatchDecision(
    bool Allowed,
    OcrDispatchRejectionReason RejectionReason,
    PrivacyCapability RequiredCapabilities);

public enum OcrPublicationOutcome
{
    Publishable,
    Stale
}

public sealed record OcrPublicationDecision(
    OcrPublicationOutcome Outcome,
    OcrDispatchRejectionReason RejectionReason);
