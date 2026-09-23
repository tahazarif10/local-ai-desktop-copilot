using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class OcrIntegrationGateTests
{
    [TestMethod]
    public void ClientDispatch_RequiresCaptureAndRunOcrButNotServerEgress()
    {
        OcrIntegrationRequest request =
            Request(OcrExecutionTopology.ClientProcess);

        OcrIntegrationGateDecision allowed =
            OcrIntegrationGate.EvaluateDispatch(
                request,
                isArmed: true,
                Epoch(
                    PrivacyCapability.CapturePixels |
                    PrivacyCapability.RunOcr));

        Assert.IsTrue(allowed.Allowed);
        Assert.AreEqual(
            PrivacyCapability.CapturePixels |
            PrivacyCapability.RunOcr,
            allowed.RequiredCapabilities);
    }

    [TestMethod]
    public void ServerDispatch_RequiresExplicitPixelEgressCapability()
    {
        OcrIntegrationRequest request =
            Request(OcrExecutionTopology.LocalAiServer);

        OcrIntegrationGateDecision denied =
            OcrIntegrationGate.EvaluateDispatch(
                request,
                isArmed: true,
                Epoch(
                    PrivacyCapability.CapturePixels |
                    PrivacyCapability.RunOcr));

        Assert.IsFalse(denied.Allowed);
        Assert.AreEqual(
            OcrIntegrationRejectionReason.CapabilityDenied,
            denied.Reason);

        OcrIntegrationGateDecision allowed =
            OcrIntegrationGate.EvaluateDispatch(
                request,
                isArmed: true,
                Epoch(
                    PrivacyCapability.CapturePixels |
                    PrivacyCapability.RunOcr |
                    PrivacyCapability.SendPixelsToLocalServer));

        Assert.IsTrue(allowed.Allowed);
        Assert.IsTrue(
            allowed.RequiredCapabilities.HasFlag(
                PrivacyCapability.SendPixelsToLocalServer));
    }

    [TestMethod]
    public void Dispatch_RejectsDisarmedMissingStaleAndCancelledEpochs()
    {
        OcrIntegrationRequest request =
            Request(OcrExecutionTopology.ClientProcess);

        AssertDecision(
            OcrIntegrationRejectionReason.NotArmed,
            OcrIntegrationGate.EvaluateDispatch(
                request,
                isArmed: false,
                Epoch(AllClientCapabilities)));

        AssertDecision(
            OcrIntegrationRejectionReason.NoCurrentEpoch,
            OcrIntegrationGate.EvaluateDispatch(
                request,
                isArmed: true,
                currentEpoch: null));

        AssertDecision(
            OcrIntegrationRejectionReason.EpochMismatch,
            OcrIntegrationGate.EvaluateDispatch(
                request,
                isArmed: true,
                Epoch(AllClientCapabilities, id: 8)));

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        AssertDecision(
            OcrIntegrationRejectionReason.EpochCancelled,
            OcrIntegrationGate.EvaluateDispatch(
                request,
                isArmed: true,
                Epoch(
                    AllClientCapabilities,
                    cancellationToken: cancellation.Token)));
    }

    [TestMethod]
    public void Dispatch_RejectsEmptyRegionPlanWithoutWidening()
    {
        OcrIntegrationRequest request =
            new(
                requestId: 5,
                epochId: 7,
                topology: OcrExecutionTopology.ClientProcess,
                sourceWidth: 1000,
                sourceHeight: 800,
                regionPlan: EmptyPlan());

        OcrIntegrationGateDecision decision =
            OcrIntegrationGate.EvaluateDispatch(
                request,
                isArmed: true,
                Epoch(AllClientCapabilities));

        AssertDecision(
            OcrIntegrationRejectionReason.EmptyRegionPlan,
            decision);
    }

    [TestMethod]
    public void Dispatch_RejectsForgedPlanThatExceedsHardAreaCeiling()
    {
        RegionOfInterestPlan oversized =
            new(
                new[]
                {
                    new PlannedCaptureRegion(
                        new CaptureRegion(0, 0, 900, 800),
                        RegionOfInterestSource.ChangedRegion)
                },
                uiAutomationInputCount: 0,
                rejectedInvalid: 0,
                rejectedOutsideCapture: 0,
                rejectedUnassociated: 0,
                rejectedBudget: 0,
                deduplicated: 0,
                projectionUnavailable: false,
                usedChangeFallback: true);

        OcrIntegrationRequest request =
            new(
                requestId: 5,
                epochId: 7,
                topology: OcrExecutionTopology.ClientProcess,
                sourceWidth: 1000,
                sourceHeight: 800,
                regionPlan: oversized);

        OcrIntegrationGateDecision decision =
            OcrIntegrationGate.EvaluateDispatch(
                request,
                isArmed: true,
                Epoch(AllClientCapabilities));

        AssertDecision(
            OcrIntegrationRejectionReason.InvalidRegionPlan,
            decision);
    }

    [TestMethod]
    public void Dispatch_RejectsForgedPlanOutsideCaptureBounds()
    {
        RegionOfInterestPlan outside =
            new(
                new[]
                {
                    new PlannedCaptureRegion(
                        new CaptureRegion(950, 700, 100, 80),
                        RegionOfInterestSource.ChangedRegion)
                },
                uiAutomationInputCount: 0,
                rejectedInvalid: 0,
                rejectedOutsideCapture: 0,
                rejectedUnassociated: 0,
                rejectedBudget: 0,
                deduplicated: 0,
                projectionUnavailable: false,
                usedChangeFallback: true);

        OcrIntegrationRequest request =
            new(
                requestId: 5,
                epochId: 7,
                topology: OcrExecutionTopology.ClientProcess,
                sourceWidth: 1000,
                sourceHeight: 800,
                regionPlan: outside);

        OcrIntegrationGateDecision decision =
            OcrIntegrationGate.EvaluateDispatch(
                request,
                isArmed: true,
                Epoch(AllClientCapabilities));

        AssertDecision(
            OcrIntegrationRejectionReason.InvalidRegionPlan,
            decision);
    }

    [TestMethod]
    public void Request_RejectsUnknownTopology()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new OcrIntegrationRequest(
                requestId: 5,
                epochId: 7,
                topology: (OcrExecutionTopology)12345,
                sourceWidth: 1000,
                sourceHeight: 800,
                regionPlan: NonEmptyPlan()));
    }

    [TestMethod]
    public void Publication_RechecksContextCapabilitiesAndLatestRequest()
    {
        OcrIntegrationRequest request =
            Request(OcrExecutionTopology.LocalAiServer);

        OcrIntegrationGateDecision stale =
            OcrIntegrationGate.EvaluatePublication(
                request,
                latestRequestId: request.RequestId + 1,
                isArmed: true,
                Epoch(AllServerCapabilities));

        AssertDecision(
            OcrIntegrationRejectionReason.NotLatestRequest,
            stale);

        OcrIntegrationGateDecision revoked =
            OcrIntegrationGate.EvaluatePublication(
                request,
                latestRequestId: request.RequestId,
                isArmed: true,
                Epoch(AllClientCapabilities));

        AssertDecision(
            OcrIntegrationRejectionReason.CapabilityDenied,
            revoked);

        OcrIntegrationGateDecision allowed =
            OcrIntegrationGate.EvaluatePublication(
                request,
                latestRequestId: request.RequestId,
                isArmed: true,
                Epoch(AllServerCapabilities));

        Assert.IsTrue(allowed.Allowed);
    }

    [TestMethod]
    public void RequiredCapabilities_ServerAddsOnlyExplicitPixelEgress()
    {
        PrivacyCapability client =
            OcrIntegrationGate.RequiredCapabilities(
                OcrExecutionTopology.ClientProcess);

        PrivacyCapability server =
            OcrIntegrationGate.RequiredCapabilities(
                OcrExecutionTopology.LocalAiServer);

        Assert.AreEqual(
            PrivacyCapability.CapturePixels |
            PrivacyCapability.RunOcr,
            client);

        Assert.AreEqual(
            client |
            PrivacyCapability.SendPixelsToLocalServer,
            server);
    }

    private const PrivacyCapability AllClientCapabilities =
        PrivacyCapability.CapturePixels |
        PrivacyCapability.RunOcr;

    private const PrivacyCapability AllServerCapabilities =
        AllClientCapabilities |
        PrivacyCapability.SendPixelsToLocalServer;

    private static OcrIntegrationRequest Request(
        OcrExecutionTopology topology) =>
        new(
            requestId: 5,
            epochId: 7,
            topology: topology,
            sourceWidth: 1000,
            sourceHeight: 800,
            regionPlan: NonEmptyPlan());

    private static RegionOfInterestPlan NonEmptyPlan() =>
        new(
            new[]
            {
                new PlannedCaptureRegion(
                    new CaptureRegion(10, 20, 200, 80),
                    RegionOfInterestSource.ChangedRegion)
            },
            uiAutomationInputCount: 0,
            rejectedInvalid: 0,
            rejectedOutsideCapture: 0,
            rejectedUnassociated: 0,
            rejectedBudget: 0,
            deduplicated: 0,
            projectionUnavailable: false,
            usedChangeFallback: true);

    private static RegionOfInterestPlan EmptyPlan() =>
        new(
            Array.Empty<PlannedCaptureRegion>(),
            uiAutomationInputCount: 0,
            rejectedInvalid: 0,
            rejectedOutsideCapture: 0,
            rejectedUnassociated: 0,
            rejectedBudget: 0,
            deduplicated: 0,
            projectionUnavailable: false,
            usedChangeFallback: false);

    private static ContextEpoch Epoch(
        PrivacyCapability capabilities,
        long id = 7,
        CancellationToken cancellationToken = default) =>
        new(
            id,
            DateTimeOffset.UtcNow,
            TestData.Snapshot(),
            TestData.Allowed(capabilities: capabilities),
            cancellationToken);

    private static void AssertDecision(
        OcrIntegrationRejectionReason expected,
        OcrIntegrationGateDecision actual)
    {
        Assert.IsFalse(actual.Allowed);
        Assert.AreEqual(expected, actual.Reason);
    }
}
