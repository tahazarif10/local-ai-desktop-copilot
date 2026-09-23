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
    EmptyRegion,
    Superseded
}

public sealed record OcrDispatchRequest
{
    public OcrDispatchRequest(
        long requestId,
        long epochId,
        PlannedCaptureRegion region,
        OcrExecutionTopology topology)
    {
        if (requestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestId));
        }

        if (epochId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(epochId));
        }

        ArgumentNullException.ThrowIfNull(region);

        RequestId = requestId;
        EpochId = epochId;
        Region = region;
        Topology = topology;
    }

    public long RequestId { get; }

    public long EpochId { get; }

    public PlannedCaptureRegion Region { get; }

    public OcrExecutionTopology Topology { get; }
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
