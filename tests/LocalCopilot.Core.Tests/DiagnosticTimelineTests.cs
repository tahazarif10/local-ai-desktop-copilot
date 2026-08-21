using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class DiagnosticTimelineTests
{
    [TestMethod]
    public void Constructor_InvalidBounds_Throw()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new DiagnosticTimeline(
                capacity: 0));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new DiagnosticTimeline(
                retention: TimeSpan.Zero));
    }

    [TestMethod]
    public void BeginEpoch_InvalidId_Throws()
    {
        DiagnosticTimeline timeline =
            new();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => timeline.BeginEpoch(0));
    }

    [TestMethod]
    public void Record_BeforeEpochOrForWrongEpoch_IsIgnored()
    {
        DiagnosticTimeline timeline =
            new();

        timeline.Record(
            new InputActivityEvent(
                1,
                InputActivityKind.MouseClick,
                10));

        timeline.BeginEpoch(2);
        timeline.Record(
            new InputActivityEvent(
                1,
                InputActivityKind.MouseClick,
                20));

        Assert.AreEqual(
            0,
            timeline.EventCount);
    }

    [TestMethod]
    public void BeginEpoch_SameIdPreservesEvents_NewIdClearsEvents()
    {
        DiagnosticTimeline timeline =
            new();

        timeline.BeginEpoch(1);
        timeline.Record(
            new InputActivityEvent(
                1,
                InputActivityKind.MouseClick,
                10));

        timeline.BeginEpoch(1);

        Assert.AreEqual(
            1,
            timeline.EventCount);

        timeline.BeginEpoch(2);

        Assert.AreEqual(
            0,
            timeline.EventCount);
    }

    [TestMethod]
    public void Record_CapacityDropsOldestEvents()
    {
        DiagnosticTimeline timeline =
            new(
                capacity: 2,
                retention: TimeSpan.FromMinutes(1));

        timeline.BeginEpoch(1);

        timeline.Record(
            new InputActivityEvent(
                1,
                InputActivityKind.MouseClick,
                10));
        timeline.Record(
            new InputActivityEvent(
                1,
                InputActivityKind.MouseWheel,
                20));
        timeline.Record(
            new InputActivityEvent(
                1,
                InputActivityKind.KeyboardActivity,
                30));

        Assert.AreEqual(
            2,
            timeline.EventCount);

        bool active = timeline.TryReadMostRecent(
            1,
            30,
            TimeSpan.FromMinutes(1),
            out InputActivityEvent? activity);

        Assert.IsTrue(active);
        Assert.IsNotNull(activity);
        Assert.AreEqual(
            InputActivityKind.KeyboardActivity,
            activity!.Kind);
    }

    [TestMethod]
    public void TryReadMostRecent_DistinguishesInactiveFromActiveWithoutActivity()
    {
        DiagnosticTimeline timeline =
            new();

        bool inactive = timeline.TryReadMostRecent(
            epochId: 1,
            observedAtTicks: 100,
            lookback: TimeSpan.FromSeconds(1),
            out InputActivityEvent? inactiveActivity);

        timeline.BeginEpoch(1);

        bool active = timeline.TryReadMostRecent(
            epochId: 1,
            observedAtTicks: 100,
            lookback: TimeSpan.FromSeconds(1),
            out InputActivityEvent? activeActivity);

        Assert.IsFalse(inactive);
        Assert.IsNull(inactiveActivity);
        Assert.IsTrue(active);
        Assert.IsNull(activeActivity);
    }

    [TestMethod]
    public void TryReadMostRecent_ReturnsLatestEligibleActivity()
    {
        DiagnosticTimeline timeline =
            new(
                retention: TimeSpan.FromSeconds(10));

        long now =
            TestData.StopwatchTicks(
                TimeSpan.FromSeconds(10));

        timeline.BeginEpoch(7);
        timeline.Record(
            new InputActivityEvent(
                7,
                InputActivityKind.MouseClick,
                now - TestData.StopwatchTicks(
                    TimeSpan.FromMilliseconds(400))));
        timeline.Record(
            new InputActivityEvent(
                7,
                InputActivityKind.MouseWheel,
                now - TestData.StopwatchTicks(
                    TimeSpan.FromMilliseconds(100))));

        bool active = timeline.TryReadMostRecent(
            7,
            now,
            TimeSpan.FromSeconds(1),
            out InputActivityEvent? activity);

        Assert.IsTrue(active);
        Assert.IsNotNull(activity);
        Assert.AreEqual(
            InputActivityKind.MouseWheel,
            activity!.Kind);
    }

    [TestMethod]
    public void TryReadMostRecent_LookbackBoundary_IsInclusive()
    {
        DiagnosticTimeline timeline =
            new(
                retention: TimeSpan.FromSeconds(10));

        TimeSpan lookback =
            TimeSpan.FromSeconds(2);
        long now =
            TestData.StopwatchTicks(
                TimeSpan.FromSeconds(10));

        timeline.BeginEpoch(7);
        timeline.Record(
            new InputActivityEvent(
                7,
                InputActivityKind.KeyboardActivity,
                now - TestData.StopwatchTicks(
                    lookback)));

        bool active = timeline.TryReadMostRecent(
            7,
            now,
            lookback,
            out InputActivityEvent? activity);

        Assert.IsTrue(active);
        Assert.IsNotNull(activity);
        Assert.AreEqual(
            InputActivityKind.KeyboardActivity,
            activity!.Kind);
    }

    [TestMethod]
    public void TryReadMostRecent_RetentionBoundaryThenExpiry()
    {
        TimeSpan retention =
            TimeSpan.FromSeconds(1);
        DiagnosticTimeline timeline =
            new(
                retention: retention);

        long recordedAt =
            TestData.StopwatchTicks(
                TimeSpan.FromSeconds(10));
        long retentionTicks =
            TestData.StopwatchTicks(
                retention);

        timeline.BeginEpoch(7);
        timeline.Record(
            new InputActivityEvent(
                7,
                InputActivityKind.MouseClick,
                recordedAt));

        timeline.TryReadMostRecent(
            7,
            recordedAt + retentionTicks,
            TimeSpan.FromSeconds(2),
            out InputActivityEvent? atBoundary);

        Assert.IsNotNull(atBoundary);

        timeline.TryReadMostRecent(
            7,
            recordedAt + retentionTicks + 1,
            TimeSpan.FromSeconds(2),
            out InputActivityEvent? expired);

        Assert.IsNull(expired);
        Assert.AreEqual(
            0,
            timeline.EventCount);
    }

    [TestMethod]
    public void Reset_ClearsEpochAndEvents()
    {
        DiagnosticTimeline timeline =
            new();

        timeline.BeginEpoch(7);
        timeline.Record(
            new InputActivityEvent(
                7,
                InputActivityKind.MouseClick,
                10));

        timeline.Reset();

        Assert.AreEqual(
            0,
            timeline.EventCount);

        bool active = timeline.TryReadMostRecent(
            7,
            10,
            TimeSpan.FromSeconds(1),
            out _);

        Assert.IsFalse(active);
    }

    [TestMethod]
    public void TryReadMostRecent_NonPositiveLookback_Throws()
    {
        DiagnosticTimeline timeline =
            new();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => timeline.TryReadMostRecent(
                1,
                100,
                TimeSpan.Zero,
                out _));
    }
}
