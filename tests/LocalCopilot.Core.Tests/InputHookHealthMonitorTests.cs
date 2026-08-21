using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class InputHookHealthMonitorTests
{
    [TestMethod]
    public void Constructor_InvalidValues_Throw()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new InputHookHealthMonitor(
                0,
                1_000_000));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new InputHookHealthMonitor(
                1,
                0));
    }

    [TestMethod]
    public void RecordCallback_ClassifiesActivityErrorsThreadsAndDurations()
    {
        InputHookHealthMonitor monitor =
            new(
                installThreadId: 42,
                timestampFrequency: 1_000_000);

        monitor.RecordCallback(
            InputHookKind.Keyboard,
            InputActivityKind.KeyboardActivity,
            elapsedTicks: 100,
            callbackThreadId: 42,
            callbackFailed: false,
            subscriberFailed: false);

        monitor.RecordCallback(
            InputHookKind.Mouse,
            InputActivityKind.MouseClick,
            elapsedTicks: 101,
            callbackThreadId: 42,
            callbackFailed: false,
            subscriberFailed: false);

        monitor.RecordCallback(
            InputHookKind.Mouse,
            InputActivityKind.MouseWheel,
            elapsedTicks: 500,
            callbackThreadId: 42,
            callbackFailed: false,
            subscriberFailed: false);

        monitor.RecordCallback(
            InputHookKind.Mouse,
            activityKind: null,
            elapsedTicks: 1_000,
            callbackThreadId: 42,
            callbackFailed: false,
            subscriberFailed: false);

        monitor.RecordCallback(
            InputHookKind.Keyboard,
            activityKind: null,
            elapsedTicks: 5_000,
            callbackThreadId: 42,
            callbackFailed: false,
            subscriberFailed: false);

        monitor.RecordCallback(
            InputHookKind.Keyboard,
            activityKind: null,
            elapsedTicks: 20_000,
            callbackThreadId: 99,
            callbackFailed: true,
            subscriberFailed: true);

        monitor.RecordCallback(
            InputHookKind.Mouse,
            activityKind: null,
            elapsedTicks: 20_001,
            callbackThreadId: 42,
            callbackFailed: false,
            subscriberFailed: false);

        InputHookHealthSnapshot snapshot =
            monitor.Snapshot();

        Assert.AreEqual(
            3,
            snapshot.KeyboardCallbacks);

        Assert.AreEqual(
            4,
            snapshot.MouseCallbacks);

        Assert.AreEqual(
            7,
            snapshot.TotalCallbacks);

        Assert.AreEqual(
            3,
            snapshot.TotalActivities);

        Assert.AreEqual(
            1,
            snapshot.CallbackErrors);

        Assert.AreEqual(
            1,
            snapshot.SubscriberErrors);

        Assert.AreEqual(
            1,
            snapshot.ThreadMismatches);

        Assert.AreEqual(
            1,
            snapshot.UpTo100Microseconds);

        Assert.AreEqual(
            2,
            snapshot.UpTo500Microseconds);

        Assert.AreEqual(
            1,
            snapshot.UpTo1Millisecond);

        Assert.AreEqual(
            1,
            snapshot.UpTo5Milliseconds);

        Assert.AreEqual(
            1,
            snapshot.UpTo20Milliseconds);

        Assert.AreEqual(
            1,
            snapshot.Over20Milliseconds);

        Assert.AreEqual(
            20_001d,
            snapshot.MaximumCallbackMicroseconds,
            0.001d);

        Assert.AreEqual(
            46_702d /
            7d,
            snapshot.AverageCallbackMicroseconds,
            0.001d);
    }

    [TestMethod]
    public void Snapshot_NoCallbacks_IsZeroed()
    {
        InputHookHealthSnapshot snapshot =
            new InputHookHealthMonitor(
                42,
                1_000_000)
            .Snapshot();

        Assert.AreEqual(
            0,
            snapshot.TotalCallbacks);

        Assert.AreEqual(
            0d,
            snapshot.AverageCallbackMicroseconds);

        Assert.AreEqual(
            0d,
            snapshot.MaximumCallbackMicroseconds);
    }

    [TestMethod]
    public void RecordCallback_ConcurrentUpdatesRemainExact()
    {
        InputHookHealthMonitor monitor =
            new(
                installThreadId: 42,
                timestampFrequency: 1_000_000);

        Parallel.For(
            0,
            1_000,
            index =>
            {
                monitor.RecordCallback(
                    index % 2 == 0
                        ? InputHookKind.Keyboard
                        : InputHookKind.Mouse,
                    activityKind: null,
                    elapsedTicks: 10,
                    callbackThreadId: 42,
                    callbackFailed: false,
                    subscriberFailed: false);
            });

        InputHookHealthSnapshot snapshot =
            monitor.Snapshot();

        Assert.AreEqual(
            500,
            snapshot.KeyboardCallbacks);

        Assert.AreEqual(
            500,
            snapshot.MouseCallbacks);

        Assert.AreEqual(
            1_000,
            snapshot.TotalCallbacks);

        Assert.AreEqual(
            1_000,
            snapshot.UpTo100Microseconds);
    }
}
