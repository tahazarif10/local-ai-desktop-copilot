using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class OcrRuntimeGateTests
{
    [TestMethod]
    public void RequiredCapabilities_ClientLocal_DoesNotRequireLanEgress()
    {
        PrivacyCapability required =
            OcrRuntimeGate.RequiredCapabilities(
                OcrExecutionTopology.ClientLocal);

        Assert.AreEqual(
            PrivacyCapability.CapturePixels |
            PrivacyCapability.RunOcr,
            required);
    }

    [TestMethod]
    public void RequiredCapabilities_LocalAiServer_RequiresExplicitPixelEgress()
    {
        PrivacyCapability required =
            OcrRuntimeGate.RequiredCapabilities(
                OcrExecutionTopology.LocalAiServer);

        Assert.AreEqual(
            PrivacyCapability.CapturePixels |
            PrivacyCapability.RunOcr |
            PrivacyCapability.SendPixelsToLocalServer,
            required);
    }

    [TestMethod]
    public void MayDispatch_ServerTopology_AllowsOnlyWhenAllCapabilitiesMatchCurrentEpoch()
    {
        ContextEpoch epoch =
            Epoch(
                7,
                PrivacyCapability.ObserveIdentity |
                PrivacyCapability.CapturePixels |
                PrivacyCapability.RunOcr |
                PrivacyCapability.SendPixelsToLocalServer);

        OcrDispatchDecision decision =
            OcrRuntimeGate.MayDispatch(
                Request(
                    requestId: 1,
                    epochId: 7,
                    OcrExecutionTopology.LocalAiServer),
                isArmed: true,
                epoch);

        Assert.IsTrue(decision.Allowed);
        Assert.AreEqual(
            OcrDispatchRejectionReason.None,
            decision.RejectionReason);
    }

    [TestMethod]
    public void MayDispatch_ServerTopology_FailsClosedWithoutSendPixelsCapability()
    {
        ContextEpoch epoch =
            Epoch(
                7,
                PrivacyCapability.ObserveIdentity |
                PrivacyCapability.CapturePixels |
                PrivacyCapability.RunOcr);

        OcrDispatchDecision decision =
            OcrRuntimeGate.MayDispatch(
                Request(
                    requestId: 1,
                    epochId: 7,
                    OcrExecutionTopology.LocalAiServer),
                isArmed: true,
                epoch);

        Assert.IsFalse(decision.Allowed);
        Assert.AreEqual(
            OcrDispatchRejectionReason.CapabilityDenied,
            decision.RejectionReason);
    }

    [TestMethod]
    public void MayDispatch_ClientLocal_DoesNotBorrowSendPixelsRequirement()
    {
        ContextEpoch epoch =
            Epoch(
                7,
                PrivacyCapability.ObserveIdentity |
                PrivacyCapability.CapturePixels |
                PrivacyCapability.RunOcr);

        OcrDispatchDecision decision =
            OcrRuntimeGate.MayDispatch(
                Request(
                    requestId: 1,
                    epochId: 7,
                    OcrExecutionTopology.ClientLocal),
                isArmed: true,
                epoch);

        Assert.IsTrue(decision.Allowed);
    }

    [TestMethod]
    public void MayDispatch_RejectsDisarmedMissingMismatchedAndCancelledEpochs()
    {
        ContextEpoch epoch =
            Epoch(
                7,
                PrivacyCapability.CapturePixels |
                PrivacyCapability.RunOcr);

        Assert.AreEqual(
            OcrDispatchRejectionReason.NotArmed,
            OcrRuntimeGate.MayDispatch(
                Request(1, 7, OcrExecutionTopology.ClientLocal),
                false,
                epoch).RejectionReason);

        Assert.AreEqual(
            OcrDispatchRejectionReason.NoCurrentEpoch,
            OcrRuntimeGate.MayDispatch(
                Request(1, 7, OcrExecutionTopology.ClientLocal),
                true,
                null).RejectionReason);

        Assert.AreEqual(
            OcrDispatchRejectionReason.EpochMismatch,
            OcrRuntimeGate.MayDispatch(
                Request(1, 8, OcrExecutionTopology.ClientLocal),
                true,
                epoch).RejectionReason);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        ContextEpoch cancelled =
            Epoch(
                7,
                PrivacyCapability.CapturePixels |
                PrivacyCapability.RunOcr,
                cancellation.Token);

        Assert.AreEqual(
            OcrDispatchRejectionReason.EpochCancelled,
            OcrRuntimeGate.MayDispatch(
                Request(1, 7, OcrExecutionTopology.ClientLocal),
                true,
                cancelled).RejectionReason);
    }

    [TestMethod]
    public void MayDispatch_RejectsEmptyRegion()
    {
        ContextEpoch epoch =
            Epoch(
                7,
                PrivacyCapability.CapturePixels |
                PrivacyCapability.RunOcr);

        OcrDispatchRequest request =
            new(
                1,
                7,
                new PlannedCaptureRegion(
                    new CaptureRegion(0, 0, 0, 20),
                    RegionOfInterestSource.ChangedRegion),
                OcrExecutionTopology.ClientLocal);

        OcrDispatchDecision decision =
            OcrRuntimeGate.MayDispatch(
                request,
                true,
                epoch);

        Assert.IsFalse(decision.Allowed);
        Assert.AreEqual(
            OcrDispatchRejectionReason.EmptyRegion,
            decision.RejectionReason);
    }

    [TestMethod]
    public void MayPublish_RequiresLatestRequestAndRevalidatesEpochCapabilities()
    {
        ContextEpoch epoch =
            Epoch(
                7,
                PrivacyCapability.CapturePixels |
                PrivacyCapability.RunOcr);

        OcrDispatchRequest request =
            Request(
                11,
                7,
                OcrExecutionTopology.ClientLocal);

        Assert.AreEqual(
            OcrPublicationOutcome.Publishable,
            OcrRuntimeGate.MayPublish(
                request,
                latestRequestId: 11,
                isArmed: true,
                epoch).Outcome);

        OcrPublicationDecision superseded =
            OcrRuntimeGate.MayPublish(
                request,
                latestRequestId: 12,
                isArmed: true,
                epoch);

        Assert.AreEqual(
            OcrPublicationOutcome.Stale,
            superseded.Outcome);
        Assert.AreEqual(
            OcrDispatchRejectionReason.Superseded,
            superseded.RejectionReason);

        ContextEpoch revoked =
            Epoch(
                7,
                PrivacyCapability.CapturePixels);

        OcrPublicationDecision denied =
            OcrRuntimeGate.MayPublish(
                request,
                latestRequestId: 11,
                isArmed: true,
                revoked);

        Assert.AreEqual(
            OcrPublicationOutcome.Stale,
            denied.Outcome);
        Assert.AreEqual(
            OcrDispatchRejectionReason.CapabilityDenied,
            denied.RejectionReason);
    }

    [TestMethod]
    public void Request_RejectsInvalidIdentifiersAndNullRegion()
    {
        PlannedCaptureRegion region =
            new(
                new CaptureRegion(0, 0, 10, 10),
                RegionOfInterestSource.ChangedRegion);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new OcrDispatchRequest(
                0,
                1,
                region,
                OcrExecutionTopology.ClientLocal));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new OcrDispatchRequest(
                1,
                0,
                region,
                OcrExecutionTopology.ClientLocal));

        Assert.ThrowsExactly<ArgumentNullException>(
            () => new OcrDispatchRequest(
                1,
                1,
                null!,
                OcrExecutionTopology.ClientLocal));
    }

    private static OcrDispatchRequest Request(
        long requestId,
        long epochId,
        OcrExecutionTopology topology) =>
        new(
            requestId,
            epochId,
            new PlannedCaptureRegion(
                new CaptureRegion(10, 20, 100, 40),
                RegionOfInterestSource.ChangedRegion),
            topology);

    private static ContextEpoch Epoch(
        long id,
        PrivacyCapability capabilities,
        CancellationToken cancellationToken = default) =>
        new(
            id,
            DateTimeOffset.UtcNow,
            TestData.Snapshot(),
            TestData.Allowed(capabilities: capabilities),
            cancellationToken);
}
