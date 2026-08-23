using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class UiAutomationProbePublicationGateTests
{
    [TestMethod]
    public void Apply_SameCurrentEpochWithCapability_PreservesResult()
    {
        using ContextEpochManager manager = new();

        ContextEpoch epoch = manager.GetOrAdvance(
            TestData.Snapshot(),
            TestData.Allowed(
                capabilities:
                    PrivacyCapability.ObserveIdentity |
                    PrivacyCapability.ReadUiStructure));

        UiAutomationProbeResult result = Available(epoch.Id);

        UiAutomationProbeResult published =
            UiAutomationProbePublicationGate.Apply(
                result,
                epoch,
                latestRequestId: result.RequestId);

        Assert.AreSame(result, published);
        Assert.AreEqual(
            UiAutomationProbeOutcome.Available,
            published.Outcome);
        Assert.AreEqual(
            UiAutomationProbeReason.RootResolved,
            published.Reason);
    }

    [TestMethod]
    public void Apply_DifferentEpoch_ReturnsStale()
    {
        using ContextEpochManager manager = new();

        ContextEpoch first = manager.GetOrAdvance(
            TestData.Snapshot(handle: (nint)0x1000),
            TestData.Allowed(
                capabilities:
                    PrivacyCapability.ObserveIdentity |
                    PrivacyCapability.ReadUiStructure));

        ContextEpoch second = manager.GetOrAdvance(
            TestData.Snapshot(handle: (nint)0x2000),
            TestData.Allowed(
                capabilities:
                    PrivacyCapability.ObserveIdentity |
                    PrivacyCapability.ReadUiStructure));

        UiAutomationProbeResult published =
            UiAutomationProbePublicationGate.Apply(
                Available(first.Id),
                second,
                latestRequestId: 1);

        AssertStale(published);
    }

    [TestMethod]
    public void Apply_NoCurrentEpoch_ReturnsStale()
    {
        UiAutomationProbeResult published =
            UiAutomationProbePublicationGate.Apply(
                Available(epochId: 7),
                currentEpoch: null,
                latestRequestId: 1);

        AssertStale(published);
    }

    [TestMethod]
    public void Apply_CapabilityRevoked_ReturnsStale()
    {
        using ContextEpochManager manager = new();

        ContextEpoch epoch = manager.GetOrAdvance(
            TestData.Snapshot(),
            TestData.Allowed(
                capabilities:
                    PrivacyCapability.ObserveIdentity));

        UiAutomationProbeResult published =
            UiAutomationProbePublicationGate.Apply(
                Available(epoch.Id),
                epoch,
                latestRequestId: 1);

        AssertStale(published);
    }

    [TestMethod]
    public void Apply_CancelledEpoch_ReturnsStale()
    {
        using ContextEpochManager manager = new();

        ContextEpoch epoch = manager.GetOrAdvance(
            TestData.Snapshot(),
            TestData.Allowed(
                capabilities:
                    PrivacyCapability.ObserveIdentity |
                    PrivacyCapability.ReadUiStructure));

        manager.Reset();

        UiAutomationProbeResult published =
            UiAutomationProbePublicationGate.Apply(
                Available(epoch.Id),
                epoch,
                latestRequestId: 1);

        AssertStale(published);
    }

    [TestMethod]
    public void Apply_NullResult_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => UiAutomationProbePublicationGate.Apply(
                null!,
                currentEpoch: null,
                latestRequestId: 1));
    }

    [TestMethod]
    public void Apply_OlderRequestInSameEpoch_ReturnsStale()
    {
        using ContextEpochManager manager = new();

        ContextEpoch epoch = manager.GetOrAdvance(
            TestData.Snapshot(),
            TestData.Allowed(
                capabilities:
                    PrivacyCapability.ObserveIdentity |
                    PrivacyCapability.ReadUiStructure));

        UiAutomationProbeResult published =
            UiAutomationProbePublicationGate.Apply(
                Available(epoch.Id),
                epoch,
                latestRequestId: 2);

        AssertStale(published);
    }

    [TestMethod]
    public void Apply_RejectedResult_DropsStructuralSnapshot()
    {
        UiAutomationProbeResult result =
            Available(epochId: 7) with
            {
                Snapshot = EmptySnapshot()
            };

        UiAutomationProbeResult published =
            UiAutomationProbePublicationGate.Apply(
                result,
                currentEpoch: null,
                latestRequestId: result.RequestId);

        AssertStale(published);
        Assert.IsNull(published.Snapshot);
    }

    [TestMethod]
    public void Apply_CurrentStructuralSnapshot_PreservesSnapshot()
    {
        using ContextEpochManager manager = new();

        ContextEpoch epoch = manager.GetOrAdvance(
            TestData.Snapshot(),
            TestData.Allowed(
                capabilities:
                    PrivacyCapability.ObserveIdentity |
                    PrivacyCapability.ReadUiStructure));

        UiAutomationStructuralSnapshot snapshot =
            EmptySnapshot();

        UiAutomationProbeResult result =
            Available(epoch.Id) with
            {
                Reason =
                    UiAutomationProbeReason.SnapshotCaptured,
                Snapshot = snapshot
            };

        UiAutomationProbeResult published =
            UiAutomationProbePublicationGate.Apply(
                result,
                epoch,
                latestRequestId: result.RequestId);

        Assert.AreSame(result, published);
        Assert.AreSame(snapshot, published.Snapshot);
        Assert.AreEqual(
            UiAutomationProbeReason.SnapshotCaptured,
            published.Reason);
    }

    private static UiAutomationProbeResult Available(
        long epochId) =>
        new(
            1,
            epochId,
            UiAutomationProbeOutcome.Available,
            UiAutomationProbeReason.RootResolved,
            TimeSpan.FromMilliseconds(10),
            HResult: null,
            WorkerThreadId: 12,
            IdentityRevalidated: true);

    private static void AssertStale(
        UiAutomationProbeResult result)
    {
        Assert.AreEqual(
            UiAutomationProbeOutcome.Stale,
            result.Outcome);
        Assert.AreEqual(
            UiAutomationProbeReason.PublicationRejected,
            result.Reason);
    }

    private static UiAutomationStructuralSnapshot EmptySnapshot() =>
        new(
            Array.Empty<UiAutomationStructuralNode>(),
            UiAutomationSnapshotBudgets.M3_2Default,
            UiAutomationSnapshotTruncation.None,
            propertyValueCount: 0,
            estimatedResultBytes:
                UiAutomationSnapshotSizeEstimator.HeaderBytes,
            traversalElapsed: TimeSpan.Zero);
}
