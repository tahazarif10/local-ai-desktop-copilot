using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class ChangeCorrelationServiceTests
{
    [TestMethod]
    public void Observe_BaselineOrInsignificant_ReturnsNull()
    {
        DiagnosticTimeline timeline =
            new();
        timeline.BeginEpoch(7);

        ChangeCorrelationService service =
            CreateService(
                timeline,
                nowTicks: 100);

        Assert.IsNull(
            service.Observe(
                TestData.Sample(
                    ChangeClassification.Baseline)));
        Assert.IsNull(
            service.Observe(
                TestData.Sample(
                    ChangeClassification.Insignificant)));
    }

    [TestMethod]
    public void Observe_MeaningfulForInactiveEpoch_ReturnsNull()
    {
        DiagnosticTimeline timeline =
            new();
        timeline.BeginEpoch(8);

        ChangeCorrelationService service =
            CreateService(
                timeline,
                nowTicks: 100);

        Assert.IsNull(
            service.Observe(
                TestData.Sample(
                    ChangeClassification.Meaningful,
                    epochId: 7)));
    }

    [TestMethod]
    public void Observe_ActiveEpochWithoutActivity_ReturnsNoneTrigger()
    {
        DiagnosticTimeline timeline =
            new();
        timeline.BeginEpoch(7);

        ChangeCorrelationService service =
            CreateService(
                timeline,
                nowTicks: 100);

        ChangeCorrelationResult? result = service.Observe(
            TestData.Sample(
                ChangeClassification.Meaningful));

        Assert.IsNotNull(result);
        ChangeCorrelationResult actual =
            result!;

        Assert.AreEqual(
            7L,
            actual.EpochId);
        Assert.AreEqual(
            ChangeClassification.Meaningful,
            actual.Classification);
        Assert.IsNull(
            actual.PossibleTrigger);
        Assert.IsNull(
            actual.TriggerAgeMilliseconds);
    }

    [TestMethod]
    public void Observe_RecentActivity_ReturnsKindAndDeterministicAge()
    {
        DiagnosticTimeline timeline =
            new(
                retention: TimeSpan.FromSeconds(5));

        long now =
            TestData.StopwatchTicks(
                TimeSpan.FromSeconds(10));
        long age =
            TestData.StopwatchTicks(
                TimeSpan.FromMilliseconds(250));

        timeline.BeginEpoch(7);
        timeline.Record(
            new InputActivityEvent(
                7,
                InputActivityKind.MouseWheel,
                now - age));

        ChangeCorrelationService service =
            CreateService(
                timeline,
                now);

        ChangeCorrelationResult? result = service.Observe(
            TestData.Sample(
                ChangeClassification.Large));

        Assert.IsNotNull(result);
        ChangeCorrelationResult actual =
            result!;

        Assert.AreEqual(
            InputActivityKind.MouseWheel,
            actual.PossibleTrigger);

        double? triggerAgeMilliseconds =
            actual.TriggerAgeMilliseconds;

        Assert.IsNotNull(
            triggerAgeMilliseconds);
        Assert.AreEqual(
            250.0,
            triggerAgeMilliseconds.GetValueOrDefault(),
            0.1);
    }

    [TestMethod]
    public void Observe_ActivityOutsideLookback_ReturnsNoneTrigger()
    {
        DiagnosticTimeline timeline =
            new(
                retention: TimeSpan.FromSeconds(5));

        long now =
            TestData.StopwatchTicks(
                TimeSpan.FromSeconds(10));

        timeline.BeginEpoch(7);
        timeline.Record(
            new InputActivityEvent(
                7,
                InputActivityKind.MouseClick,
                now - TestData.StopwatchTicks(
                    TimeSpan.FromSeconds(3))));

        ChangeCorrelationService service =
            CreateService(
                timeline,
                now,
                lookback: TimeSpan.FromSeconds(2));

        ChangeCorrelationResult? result = service.Observe(
            TestData.Sample(
                ChangeClassification.Meaningful));

        Assert.IsNotNull(result);
        ChangeCorrelationResult actual =
            result!;

        Assert.IsNull(
            actual.PossibleTrigger);
        Assert.IsNull(
            actual.TriggerAgeMilliseconds);
    }

    [TestMethod]
    public void Constructor_InvalidDependenciesOrLookback_Throw()
    {
        DiagnosticTimeline timeline =
            new();

        Assert.ThrowsExactly<ArgumentNullException>(
            () => new ChangeCorrelationService(
                null!));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new ChangeCorrelationService(
                timeline,
                TimeSpan.Zero));

        Assert.ThrowsExactly<ArgumentNullException>(
            () => new ChangeCorrelationService(
                timeline,
                TimeSpan.FromSeconds(1),
                null!));
    }

    [TestMethod]
    public void Observe_NullSample_Throws()
    {
        DiagnosticTimeline timeline =
            new();

        ChangeCorrelationService service =
            CreateService(
                timeline,
                nowTicks: 100);

        Assert.ThrowsExactly<ArgumentNullException>(
            () => service.Observe(null!));
    }

    private static ChangeCorrelationService CreateService(
        DiagnosticTimeline timeline,
        long nowTicks,
        TimeSpan? lookback = null) =>
        new(
            timeline,
            lookback,
            () => nowTicks);
}
