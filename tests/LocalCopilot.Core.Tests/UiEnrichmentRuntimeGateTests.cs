using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class UiEnrichmentRuntimeGateTests
{
    [TestMethod]
    public void MayDispatch_RequiresArmedCurrentUncancelledTextCapableEpoch()
    {
        UiEnrichmentRequest request = Request();

        Assert.IsFalse(
            UiEnrichmentRuntimeGate.MayDispatch(
                request,
                isArmed: false,
                Epoch(cancellationToken: CancellationToken.None)));

        Assert.IsFalse(
            UiEnrichmentRuntimeGate.MayDispatch(
                request,
                isArmed: true,
                currentEpoch: null));

        Assert.IsFalse(
            UiEnrichmentRuntimeGate.MayDispatch(
                request,
                isArmed: true,
                Epoch(
                    id: 8,
                    cancellationToken: CancellationToken.None)));

        Assert.IsFalse(
            UiEnrichmentRuntimeGate.MayDispatch(
                request,
                isArmed: true,
                Epoch(
                    capabilities:
                        PrivacyCapability.ObserveIdentity |
                        PrivacyCapability.ReadUiStructure,
                    cancellationToken: CancellationToken.None)));

        using CancellationTokenSource cancellation =
            new();
        cancellation.Cancel();

        Assert.IsFalse(
            UiEnrichmentRuntimeGate.MayDispatch(
                request,
                isArmed: true,
                Epoch(cancellationToken: cancellation.Token)));

        Assert.IsTrue(
            UiEnrichmentRuntimeGate.MayDispatch(
                request,
                isArmed: true,
                Epoch(cancellationToken: CancellationToken.None)));
    }

    [TestMethod]
    public void ApplyPublication_DisarmedRejectsOtherwiseCurrentResult()
    {
        UiAutomationProbeResult result =
            Result();

        UiAutomationProbeResult gated =
            UiEnrichmentRuntimeGate.ApplyPublication(
                Request(),
                result,
                Epoch(cancellationToken: CancellationToken.None),
                latestWorkerRequestId: result.RequestId,
                isArmed: false);

        Assert.AreEqual(
            UiAutomationProbeOutcome.Stale,
            gated.Outcome);
        Assert.AreEqual(
            UiAutomationProbeReason.PublicationRejected,
            gated.Reason);
        Assert.IsNull(gated.Snapshot);
        Assert.IsNull(gated.SemanticSnapshot);
    }

    [TestMethod]
    public void ApplyPublication_ArmedCurrentLatestResultPasses()
    {
        UiAutomationProbeResult result =
            Result();

        UiAutomationProbeResult gated =
            UiEnrichmentRuntimeGate.ApplyPublication(
                Request(),
                result,
                Epoch(cancellationToken: CancellationToken.None),
                latestWorkerRequestId: result.RequestId,
                isArmed: true);

        Assert.AreEqual(result, gated);
    }

    [TestMethod]
    public void ApplyPublication_RequestEpochMismatchRejectsResult()
    {
        UiAutomationProbeResult result =
            Result();

        UiAutomationProbeResult gated =
            UiEnrichmentRuntimeGate.ApplyPublication(
                Request(epochId: 8),
                result,
                Epoch(
                    id: 7,
                    cancellationToken: CancellationToken.None),
                latestWorkerRequestId: result.RequestId,
                isArmed: true);

        Assert.AreEqual(
            UiAutomationProbeOutcome.Stale,
            gated.Outcome);
        Assert.AreEqual(
            UiAutomationProbeReason.PublicationRejected,
            gated.Reason);
    }

    private static UiEnrichmentRequest Request(
        long epochId = 7) =>
        new(
            RequestId: 1,
            EpochId: epochId,
            SourceId: 1,
            Kind: UiEnrichmentTriggerKind.BackgroundChange,
            Classification: ChangeClassification.Meaningful);

    private static UiAutomationProbeResult Result() =>
        new(
            RequestId: 42,
            EpochId: 7,
            Outcome: UiAutomationProbeOutcome.Available,
            Reason: UiAutomationProbeReason.SnapshotCaptured,
            Elapsed: TimeSpan.FromMilliseconds(25),
            HResult: null,
            WorkerThreadId: 5,
            IdentityRevalidated: true);

    private static ContextEpoch Epoch(
        long id = 7,
        PrivacyCapability capabilities =
            PrivacyCapability.ObserveIdentity |
            PrivacyCapability.ReadUiStructure |
            PrivacyCapability.ReadUiText,
        CancellationToken cancellationToken = default) =>
        new(
            id,
            DateTimeOffset.UtcNow,
            TestData.Snapshot(),
            TestData.Allowed(capabilities: capabilities),
            cancellationToken);
}
