using System;

namespace LocalCopilot_App.Services;

public enum OcrExecutionTopology
{
    ClientProcess,
    LocalAiServer
}

public enum OcrIntegrationRejectionReason
{
    None,
    NotArmed,
    NoCurrentEpoch,
    EpochMismatch,
    EpochCancelled,
    CapabilityDenied,
    EmptyRegionPlan,
    NotLatestRequest
}

public sealed class OcrIntegrationRequest
{
    public OcrIntegrationRequest(
        long requestId,
        long epochId,
        OcrExecutionTopology topology,
        RegionOfInterestPlan regionPlan)
    {
        if (requestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestId));
        }

        if (epochId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(epochId));
        }

        RequestId = requestId;
        EpochId = epochId;
        Topology = topology;
        RegionPlan = regionPlan ??
            throw new ArgumentNullException(nameof(regionPlan));
    }

    public long RequestId { get; }

    public long EpochId { get; }

    public OcrExecutionTopology Topology { get; }

    public RegionOfInterestPlan RegionPlan { get; }
}

public sealed record OcrIntegrationGateDecision(
    bool Allowed,
    OcrIntegrationRejectionReason Reason,
    PrivacyCapability RequiredCapabilities);

public static class OcrIntegrationGate
{
    public static OcrIntegrationGateDecision EvaluateDispatch(
        OcrIntegrationRequest request,
        bool isArmed,
        ContextEpoch? currentEpoch)
    {
        ArgumentNullException.ThrowIfNull(request);

        PrivacyCapability required =
            RequiredCapabilities(request.Topology);

        if (!isArmed)
        {
            return Reject(
                OcrIntegrationRejectionReason.NotArmed,
                required);
        }

        if (currentEpoch is null)
        {
            return Reject(
                OcrIntegrationRejectionReason.NoCurrentEpoch,
                required);
        }

        if (currentEpoch.Id != request.EpochId)
        {
            return Reject(
                OcrIntegrationRejectionReason.EpochMismatch,
                required);
        }

        if (currentEpoch.CancellationToken.IsCancellationRequested)
        {
            return Reject(
                OcrIntegrationRejectionReason.EpochCancelled,
                required);
        }

        if (!currentEpoch.Privacy.Allows(required))
        {
            return Reject(
                OcrIntegrationRejectionReason.CapabilityDenied,
                required);
        }

        if (request.RegionPlan.IsEmpty)
        {
            return Reject(
                OcrIntegrationRejectionReason.EmptyRegionPlan,
                required);
        }

        return new OcrIntegrationGateDecision(
            Allowed: true,
            OcrIntegrationRejectionReason.None,
            required);
    }

    public static OcrIntegrationGateDecision EvaluatePublication(
        OcrIntegrationRequest request,
        long latestRequestId,
        bool isArmed,
        ContextEpoch? currentEpoch)
    {
        OcrIntegrationGateDecision dispatch =
            EvaluateDispatch(
                request,
                isArmed,
                currentEpoch);

        if (!dispatch.Allowed)
        {
            return dispatch;
        }

        if (request.RequestId != latestRequestId)
        {
            return Reject(
                OcrIntegrationRejectionReason.NotLatestRequest,
                dispatch.RequiredCapabilities);
        }

        return dispatch;
    }

    public static PrivacyCapability RequiredCapabilities(
        OcrExecutionTopology topology)
    {
        PrivacyCapability required =
            PrivacyCapability.CapturePixels |
            PrivacyCapability.RunOcr;

        if (topology == OcrExecutionTopology.LocalAiServer)
        {
            required |=
                PrivacyCapability.SendPixelsToLocalServer;
        }

        return required;
    }

    private static OcrIntegrationGateDecision Reject(
        OcrIntegrationRejectionReason reason,
        PrivacyCapability required) =>
        new(
            Allowed: false,
            reason,
            required);
}
