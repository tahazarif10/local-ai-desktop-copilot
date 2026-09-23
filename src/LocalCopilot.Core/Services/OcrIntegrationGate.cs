using System;
using System.Collections.Generic;

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
    InvalidRegionPlan,
    NotLatestRequest
}

public sealed class OcrIntegrationRequest
{
    public OcrIntegrationRequest(
        long requestId,
        long epochId,
        OcrExecutionTopology topology,
        int sourceWidth,
        int sourceHeight,
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

        if (!Enum.IsDefined(topology))
        {
            throw new ArgumentOutOfRangeException(nameof(topology));
        }

        if (sourceWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        }

        if (sourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceHeight));
        }

        RequestId = requestId;
        EpochId = epochId;
        Topology = topology;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        RegionPlan = regionPlan ??
            throw new ArgumentNullException(nameof(regionPlan));
    }

    public long RequestId { get; }

    public long EpochId { get; }

    public OcrExecutionTopology Topology { get; }

    public int SourceWidth { get; }

    public int SourceHeight { get; }

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

        if (!IsRegionPlanWithinHardCeilings(request))
        {
            return Reject(
                OcrIntegrationRejectionReason.InvalidRegionPlan,
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
        OcrExecutionTopology topology) =>
        topology switch
        {
            OcrExecutionTopology.ClientProcess =>
                PrivacyCapability.CapturePixels |
                PrivacyCapability.RunOcr,
            OcrExecutionTopology.LocalAiServer =>
                PrivacyCapability.CapturePixels |
                PrivacyCapability.RunOcr |
                PrivacyCapability.SendPixelsToLocalServer,
            _ => throw new ArgumentOutOfRangeException(nameof(topology))
        };

    private static bool IsRegionPlanWithinHardCeilings(
        OcrIntegrationRequest request)
    {
        IReadOnlyList<PlannedCaptureRegion> regions =
            request.RegionPlan.Regions;

        if (regions.Count == 0 ||
            regions.Count > RegionOfInterestPlannerOptions.HardMaxRegions)
        {
            return false;
        }

        long sourceArea =
            checked(
                (long)request.SourceWidth *
                request.SourceHeight);

        long totalArea = 0;

        foreach (PlannedCaptureRegion planned in regions)
        {
            CaptureRegion bounds = planned.Bounds;

            if (bounds.IsEmpty ||
                bounds.X < 0 ||
                bounds.Y < 0 ||
                planned.Source == RegionOfInterestSource.None)
            {
                return false;
            }

            long right =
                (long)bounds.X + bounds.Width;
            long bottom =
                (long)bounds.Y + bounds.Height;

            if (right > request.SourceWidth ||
                bottom > request.SourceHeight)
            {
                return false;
            }

            long area = bounds.AreaPixels;

            if ((double)area / sourceArea >
                RegionOfInterestPlannerOptions.HardMaxRegionAreaRatio)
            {
                return false;
            }

            totalArea = checked(totalArea + area);

            if ((double)totalArea / sourceArea >
                RegionOfInterestPlannerOptions.HardMaxTotalAreaRatio)
            {
                return false;
            }
        }

        return true;
    }

    private static OcrIntegrationGateDecision Reject(
        OcrIntegrationRejectionReason reason,
        PrivacyCapability required) =>
        new(
            Allowed: false,
            reason,
            required);
}
