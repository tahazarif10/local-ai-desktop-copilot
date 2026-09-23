using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class RegionOfInterestPlannerTests
{
    [TestMethod]
    public void DefaultOptions_AreBoundedBelowFullFrame()
    {
        RegionOfInterestPlannerOptions options =
            RegionOfInterestPlannerOptions.M4_1Default;

        options.Validate();

        Assert.AreEqual(4, options.MaxRegions);
        Assert.AreEqual(16, options.PaddingPixels);
        Assert.AreEqual(24, options.AssociationMarginPixels);
        Assert.AreEqual(0.25, options.MaxRegionAreaRatio, 0.000001);
        Assert.AreEqual(0.40, options.MaxTotalAreaRatio, 0.000001);
        Assert.IsLessThan(1.0, options.MaxRegionAreaRatio);
        Assert.IsLessThan(1.0, options.MaxTotalAreaRatio);
    }

    [TestMethod]
    public void Plan_ChangeRegion_MapsOutwardToSourcePixels()
    {
        RegionOfInterestPlanner planner =
            new(NoPaddingOptions());

        RegionOfInterestPlan plan =
            planner.Plan(
                Input(
                    RegionOfInterestPlanningIntent.BackgroundChange,
                    sourceWidth: 1000,
                    sourceHeight: 500,
                    changedRegion:
                        new ChangeRegion(10, 5, 20, 10),
                    changeMapWidth: 100,
                    changeMapHeight: 50));

        Assert.HasCount(1, plan.Regions);
        Assert.AreEqual(
            new CaptureRegion(100, 50, 200, 100),
            plan.Regions[0].Bounds);
        Assert.AreEqual(
            RegionOfInterestSource.ChangedRegion,
            plan.Regions[0].Source);
        Assert.IsTrue(plan.UsedChangeFallback);
    }

    [TestMethod]
    public void Plan_PartialChangeRegion_ClipsBeforeMapping()
    {
        RegionOfInterestPlanner planner =
            new(NoPaddingOptions());

        RegionOfInterestPlan plan =
            planner.Plan(
                Input(
                    RegionOfInterestPlanningIntent.BackgroundChange,
                    sourceWidth: 1000,
                    sourceHeight: 500,
                    changedRegion:
                        new ChangeRegion(-10, -5, 30, 15),
                    changeMapWidth: 100,
                    changeMapHeight: 50));

        Assert.HasCount(1, plan.Regions);
        Assert.AreEqual(
            new CaptureRegion(0, 0, 200, 100),
            plan.Regions[0].Bounds);
    }

    [TestMethod]
    public void Plan_UiAutomationBounds_MapThroughExplicitScreenProjection()
    {
        RegionOfInterestPlanner planner =
            new(NoPaddingOptions());

        RegionOfInterestPlan plan =
            planner.Plan(
                Input(
                    RegionOfInterestPlanningIntent.UserQuestion,
                    sourceWidth: 1000,
                    sourceHeight: 500,
                    captureScreenBounds:
                        new UiAutomationRectangle(
                            100,
                            200,
                            1000,
                            500),
                    uiAutomationBounds:
                    [
                        new UiAutomationRectangle(
                            200,
                            250,
                            200,
                            100)
                    ]));

        Assert.HasCount(1, plan.Regions);
        Assert.AreEqual(
            new CaptureRegion(100, 50, 200, 100),
            plan.Regions[0].Bounds);
        Assert.AreEqual(
            RegionOfInterestSource.UiAutomation,
            plan.Regions[0].Source);
        Assert.IsFalse(plan.UsedChangeFallback);
    }

    [TestMethod]
    public void Plan_BackgroundChange_UsesOnlyAssociatedUiAutomationBounds()
    {
        RegionOfInterestPlannerOptions options =
            NoPaddingOptions() with
            {
                AssociationMarginPixels = 10
            };

        RegionOfInterestPlanner planner =
            new(options);

        RegionOfInterestPlan plan =
            planner.Plan(
                Input(
                    RegionOfInterestPlanningIntent.BackgroundChange,
                    sourceWidth: 1000,
                    sourceHeight: 500,
                    changedRegion:
                        new ChangeRegion(10, 10, 20, 10),
                    changeMapWidth: 100,
                    changeMapHeight: 50,
                    captureScreenBounds:
                        new UiAutomationRectangle(
                            0,
                            0,
                            1000,
                            500),
                    uiAutomationBounds:
                    [
                        new UiAutomationRectangle(
                            150,
                            110,
                            100,
                            80),
                        new UiAutomationRectangle(
                            800,
                            350,
                            100,
                            80)
                    ]));

        Assert.HasCount(1, plan.Regions);
        Assert.AreEqual(
            RegionOfInterestSource.UiAutomation,
            plan.Regions[0].Source);
        Assert.AreEqual(1, plan.RejectedUnassociated);
        Assert.IsFalse(plan.UsedChangeFallback);
    }

    [TestMethod]
    public void Plan_BackgroundChange_FallsBackToChangeWhenUiAutomationIsUnavailable()
    {
        RegionOfInterestPlanner planner =
            new(NoPaddingOptions());

        RegionOfInterestPlan plan =
            planner.Plan(
                Input(
                    RegionOfInterestPlanningIntent.BackgroundChange,
                    sourceWidth: 1000,
                    sourceHeight: 500,
                    changedRegion:
                        new ChangeRegion(10, 5, 20, 10),
                    changeMapWidth: 100,
                    changeMapHeight: 50,
                    uiAutomationBounds:
                    [
                        new UiAutomationRectangle(
                            200,
                            200,
                            100,
                            100)
                    ]));

        Assert.HasCount(1, plan.Regions);
        Assert.AreEqual(
            RegionOfInterestSource.ChangedRegion,
            plan.Regions[0].Source);
        Assert.IsTrue(plan.ProjectionUnavailable);
        Assert.IsTrue(plan.UsedChangeFallback);
    }

    [TestMethod]
    public void Plan_UserQuestion_CanUseUiAutomationWithoutChangeRegion()
    {
        RegionOfInterestPlanner planner =
            new(NoPaddingOptions());

        RegionOfInterestPlan plan =
            planner.Plan(
                Input(
                    RegionOfInterestPlanningIntent.UserQuestion,
                    sourceWidth: 800,
                    sourceHeight: 600,
                    captureScreenBounds:
                        new UiAutomationRectangle(
                            -100,
                            50,
                            800,
                            600),
                    uiAutomationBounds:
                    [
                        new UiAutomationRectangle(
                            0,
                            150,
                            200,
                            100)
                    ]));

        Assert.HasCount(1, plan.Regions);
        Assert.AreEqual(
            new CaptureRegion(100, 100, 200, 100),
            plan.Regions[0].Bounds);
    }

    [TestMethod]
    public void Plan_BackgroundWithoutChange_DoesNotWidenToUiAutomationOrFullFrame()
    {
        RegionOfInterestPlanner planner =
            new(NoPaddingOptions());

        RegionOfInterestPlan plan =
            planner.Plan(
                Input(
                    RegionOfInterestPlanningIntent.BackgroundChange,
                    sourceWidth: 800,
                    sourceHeight: 600,
                    captureScreenBounds:
                        new UiAutomationRectangle(
                            0,
                            0,
                            800,
                            600),
                    uiAutomationBounds:
                    [
                        new UiAutomationRectangle(
                            0,
                            0,
                            100,
                            100)
                    ]));

        Assert.IsTrue(plan.IsEmpty);
        Assert.IsFalse(plan.UsedChangeFallback);
    }

    [TestMethod]
    public void Plan_OversizedFallback_IsRejectedInsteadOfCroppingOrUsingFullFrame()
    {
        RegionOfInterestPlanner planner =
            new(
                NoPaddingOptions() with
                {
                    MaxRegionAreaRatio = 0.25,
                    MaxTotalAreaRatio = 0.40
                });

        RegionOfInterestPlan plan =
            planner.Plan(
                Input(
                    RegionOfInterestPlanningIntent.BackgroundChange,
                    sourceWidth: 1000,
                    sourceHeight: 1000,
                    changedRegion:
                        new ChangeRegion(0, 0, 80, 100),
                    changeMapWidth: 100,
                    changeMapHeight: 100));

        Assert.IsTrue(plan.IsEmpty);
        Assert.AreEqual(1, plan.RejectedBudget);
        Assert.IsFalse(plan.UsedChangeFallback);
    }

    [TestMethod]
    public void Plan_MaxRegionCount_IsHard()
    {
        RegionOfInterestPlanner planner =
            new(
                NoPaddingOptions() with
                {
                    MaxRegions = 2
                });

        RegionOfInterestPlan plan =
            planner.Plan(
                Input(
                    RegionOfInterestPlanningIntent.UserQuestion,
                    sourceWidth: 1000,
                    sourceHeight: 1000,
                    captureScreenBounds:
                        new UiAutomationRectangle(
                            0,
                            0,
                            1000,
                            1000),
                    uiAutomationBounds:
                    [
                        new UiAutomationRectangle(
                            0,
                            0,
                            100,
                            100),
                        new UiAutomationRectangle(
                            200,
                            0,
                            100,
                            100),
                        new UiAutomationRectangle(
                            400,
                            0,
                            100,
                            100)
                    ]));

        Assert.HasCount(2, plan.Regions);
        Assert.AreEqual(1, plan.RejectedBudget);
    }

    [TestMethod]
    public void Plan_TotalAreaBudget_IsConservativeAcrossRegions()
    {
        RegionOfInterestPlanner planner =
            new(
                NoPaddingOptions() with
                {
                    MaxRegionAreaRatio = 0.25,
                    MaxTotalAreaRatio = 0.30
                });

        RegionOfInterestPlan plan =
            planner.Plan(
                Input(
                    RegionOfInterestPlanningIntent.UserQuestion,
                    sourceWidth: 100,
                    sourceHeight: 100,
                    captureScreenBounds:
                        new UiAutomationRectangle(
                            0,
                            0,
                            100,
                            100),
                    uiAutomationBounds:
                    [
                        new UiAutomationRectangle(
                            0,
                            0,
                            40,
                            50),
                        new UiAutomationRectangle(
                            60,
                            0,
                            40,
                            50)
                    ]));

        Assert.HasCount(1, plan.Regions);
        Assert.AreEqual(1, plan.RejectedBudget);
        Assert.AreEqual(2000L, plan.TotalAreaPixels);
    }

    [TestMethod]
    public void Plan_NestedUiAutomationCandidates_AreDeduplicated()
    {
        RegionOfInterestPlanner planner =
            new(NoPaddingOptions());

        RegionOfInterestPlan plan =
            planner.Plan(
                Input(
                    RegionOfInterestPlanningIntent.UserQuestion,
                    sourceWidth: 1000,
                    sourceHeight: 1000,
                    captureScreenBounds:
                        new UiAutomationRectangle(
                            0,
                            0,
                            1000,
                            1000),
                    uiAutomationBounds:
                    [
                        new UiAutomationRectangle(
                            100,
                            100,
                            200,
                            200),
                        new UiAutomationRectangle(
                            125,
                            125,
                            50,
                            50)
                    ]));

        Assert.HasCount(1, plan.Regions);
        Assert.AreEqual(1, plan.Deduplicated);
    }

    [TestMethod]
    public void Plan_InvalidAndOutsideUiAutomationBounds_AreRejected()
    {
        RegionOfInterestPlanner planner =
            new(NoPaddingOptions());

        RegionOfInterestPlan plan =
            planner.Plan(
                Input(
                    RegionOfInterestPlanningIntent.UserQuestion,
                    sourceWidth: 1000,
                    sourceHeight: 1000,
                    captureScreenBounds:
                        new UiAutomationRectangle(
                            0,
                            0,
                            1000,
                            1000),
                    uiAutomationBounds:
                    [
                        new UiAutomationRectangle(
                            double.NaN,
                            0,
                            10,
                            10),
                        new UiAutomationRectangle(
                            2000,
                            2000,
                            10,
                            10)
                    ]));

        Assert.IsTrue(plan.IsEmpty);
        Assert.AreEqual(1, plan.RejectedInvalid);
        Assert.AreEqual(1, plan.RejectedOutsideCapture);
    }

    [TestMethod]
    public void Plan_Padding_ClampsToCaptureBounds()
    {
        RegionOfInterestPlanner planner =
            new(
                NoPaddingOptions() with
                {
                    PaddingPixels = 20
                });

        RegionOfInterestPlan plan =
            planner.Plan(
                Input(
                    RegionOfInterestPlanningIntent.UserQuestion,
                    sourceWidth: 100,
                    sourceHeight: 100,
                    captureScreenBounds:
                        new UiAutomationRectangle(
                            0,
                            0,
                            100,
                            100),
                    uiAutomationBounds:
                    [
                        new UiAutomationRectangle(
                            0,
                            0,
                            10,
                            10)
                    ]));

        Assert.HasCount(1, plan.Regions);
        Assert.AreEqual(
            new CaptureRegion(0, 0, 30, 30),
            plan.Regions[0].Bounds);
    }

    [TestMethod]
    public void Plan_InvalidCaptureProjection_Throws()
    {
        RegionOfInterestPlanner planner =
            new(NoPaddingOptions());

        RegionOfInterestPlanningInput input =
            Input(
                RegionOfInterestPlanningIntent.UserQuestion,
                sourceWidth: 100,
                sourceHeight: 100,
                captureScreenBounds:
                    new UiAutomationRectangle(
                        0,
                        0,
                        0,
                        100));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => planner.Plan(input));
    }

    [TestMethod]
    public void Options_HardCeilings_RejectFullFrameCompatibleBudgets()
    {
        RegionOfInterestPlannerOptions valid =
            NoPaddingOptions();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => (
                valid with
                {
                    MaxRegions =
                        RegionOfInterestPlannerOptions
                            .HardMaxRegions + 1
                }).Validate());

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => (
                valid with
                {
                    MaxRegionAreaRatio = 1.0
                }).Validate());

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => (
                valid with
                {
                    MaxTotalAreaRatio = 1.0
                }).Validate());
    }

    private static RegionOfInterestPlanningInput Input(
        RegionOfInterestPlanningIntent intent,
        int sourceWidth,
        int sourceHeight,
        ChangeRegion? changedRegion = null,
        int changeMapWidth = 0,
        int changeMapHeight = 0,
        UiAutomationRectangle? captureScreenBounds = null,
        IReadOnlyList<UiAutomationRectangle>? uiAutomationBounds = null) =>
        new(
            intent,
            sourceWidth,
            sourceHeight,
            changedRegion,
            changeMapWidth,
            changeMapHeight,
            captureScreenBounds,
            uiAutomationBounds);

    private static RegionOfInterestPlannerOptions
        NoPaddingOptions() =>
        new(
            PaddingPixels: 0,
            AssociationMarginPixels: 0,
            MaxRegions: 4,
            MaxRegionAreaRatio: 0.50,
            MaxTotalAreaRatio: 0.75);
}
