using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class ContextEpochManagerTests
{
    [TestMethod]
    public void GetOrAdvance_FirstContext_CreatesEpochOne()
    {
        using ContextEpochManager manager =
            new();

        ContextEpoch epoch = manager.GetOrAdvance(
            TestData.Snapshot(),
            TestData.Allowed());

        Assert.AreEqual(
            1L,
            epoch.Id);
        Assert.AreSame(
            epoch,
            manager.Current);
        Assert.IsFalse(
            epoch.CancellationToken.IsCancellationRequested);
    }

    [TestMethod]
    public void GetOrAdvance_SameIdentityAndPrivacy_ReusesEpoch()
    {
        using ContextEpochManager manager =
            new();

        ContextEpoch first = manager.GetOrAdvance(
            TestData.Snapshot(
                processName: "Editor",
                windowTitle: "First title"),
            TestData.Allowed());

        ContextEpoch second = manager.GetOrAdvance(
            TestData.Snapshot(
                processName: "editor",
                windowTitle: "Changed title"),
            TestData.Allowed());

        Assert.AreSame(
            first,
            second);
        Assert.AreEqual(
            1L,
            second.Id);
        Assert.IsFalse(
            first.CancellationToken.IsCancellationRequested);
    }

    [TestMethod]
    public void GetOrAdvance_ChangedHandle_CancelsAndAdvances()
    {
        using ContextEpochManager manager =
            new();

        ContextEpoch first = manager.GetOrAdvance(
            TestData.Snapshot(handle: (nint)0x1000),
            TestData.Allowed());

        ContextEpoch second = manager.GetOrAdvance(
            TestData.Snapshot(handle: (nint)0x2000),
            TestData.Allowed());

        Assert.AreEqual(
            2L,
            second.Id);
        Assert.IsTrue(
            first.CancellationToken.IsCancellationRequested);
        Assert.IsFalse(
            second.CancellationToken.IsCancellationRequested);
        Assert.AreSame(
            second,
            manager.Current);
    }

    [TestMethod]
    public void GetOrAdvance_ChangedProcessIdentity_Advances()
    {
        using ContextEpochManager manager =
            new();

        ContextEpoch first = manager.GetOrAdvance(
            TestData.Snapshot(
                processId: 42,
                processName: "editor"),
            TestData.Allowed());

        ContextEpoch second = manager.GetOrAdvance(
            TestData.Snapshot(
                processId: 43,
                processName: "browser"),
            TestData.Allowed());

        Assert.AreEqual(
            2L,
            second.Id);
        Assert.IsTrue(
            first.CancellationToken.IsCancellationRequested);
    }

    [TestMethod]
    public void GetOrAdvance_ChangedPrivacyReason_Advances()
    {
        using ContextEpochManager manager =
            new();

        ContextEpoch first = manager.GetOrAdvance(
            TestData.Snapshot(),
            TestData.Allowed(
                ruleId: "rule",
                reason: "reason-a"));

        ContextEpoch second = manager.GetOrAdvance(
            TestData.Snapshot(),
            TestData.Allowed(
                ruleId: "rule",
                reason: "reason-b"));

        Assert.AreEqual(
            2L,
            second.Id);
        Assert.IsTrue(
            first.CancellationToken.IsCancellationRequested);
    }

    [TestMethod]
    public void GetOrAdvance_ChangedPrivacyDisposition_Advances()
    {
        using ContextEpochManager manager =
            new();

        ContextEpoch first = manager.GetOrAdvance(
            TestData.Snapshot(),
            TestData.Allowed());

        ContextEpoch second = manager.GetOrAdvance(
            TestData.Snapshot(),
            TestData.Blocked());

        Assert.AreEqual(
            2L,
            second.Id);
        Assert.IsTrue(
            first.CancellationToken.IsCancellationRequested);
    }

    [TestMethod]
    public void GetOrAdvance_ChangedCapabilitySet_CancelsAndAdvances()
    {
        using ContextEpochManager manager = new();

        ContextEpoch first = manager.GetOrAdvance(
            TestData.Snapshot(),
            TestData.Allowed(
                capabilities:
                    PrivacyCapability.ObserveIdentity |
                    PrivacyCapability.ReadWindowTitle));

        ContextEpoch second = manager.GetOrAdvance(
            TestData.Snapshot(),
            TestData.Allowed(
                capabilities:
                    PrivacyCapability.ObserveIdentity |
                    PrivacyCapability.CapturePixels));

        Assert.AreEqual(2L, second.Id);
        Assert.IsTrue(first.CancellationToken.IsCancellationRequested);
    }

    [TestMethod]
    public void GetOrAdvance_ChangedPolicyRevision_CancelsAndAdvances()
    {
        using ContextEpochManager manager = new();

        ContextEpoch first = manager.GetOrAdvance(
            TestData.Snapshot(),
            TestData.Allowed(policyRevision: 1));

        ContextEpoch second = manager.GetOrAdvance(
            TestData.Snapshot(),
            TestData.Allowed(policyRevision: 2));

        Assert.AreEqual(2L, second.Id);
        Assert.IsTrue(first.CancellationToken.IsCancellationRequested);
    }

    [TestMethod]
    public void Reset_CancelsCurrentAndPreservesMonotonicIds()
    {
        using ContextEpochManager manager =
            new();

        ContextEpoch first = manager.GetOrAdvance(
            TestData.Snapshot(),
            TestData.Allowed());

        manager.Reset();

        Assert.IsNull(
            manager.Current);
        Assert.IsTrue(
            first.CancellationToken.IsCancellationRequested);

        ContextEpoch second = manager.GetOrAdvance(
            TestData.Snapshot(),
            TestData.Allowed());

        Assert.AreEqual(
            2L,
            second.Id);
    }

    [TestMethod]
    public void Dispose_CancelsCurrentAndRejectsNewEpochs()
    {
        ContextEpochManager manager =
            new();

        ContextEpoch epoch = manager.GetOrAdvance(
            TestData.Snapshot(),
            TestData.Allowed());

        manager.Dispose();
        manager.Dispose();

        Assert.IsNull(
            manager.Current);
        Assert.IsTrue(
            epoch.CancellationToken.IsCancellationRequested);

        Assert.ThrowsExactly<ObjectDisposedException>(
            () => manager.GetOrAdvance(
                TestData.Snapshot(),
                TestData.Allowed()));
    }

    [TestMethod]
    public void GetOrAdvance_NullArguments_Throw()
    {
        using ContextEpochManager manager =
            new();

        Assert.ThrowsExactly<ArgumentNullException>(
            () => manager.GetOrAdvance(
                null!,
                TestData.Allowed()));

        Assert.ThrowsExactly<ArgumentNullException>(
            () => manager.GetOrAdvance(
                TestData.Snapshot(),
                null!));
    }
}
