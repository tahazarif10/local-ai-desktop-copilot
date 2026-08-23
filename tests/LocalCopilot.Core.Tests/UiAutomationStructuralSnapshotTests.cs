using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class UiAutomationStructuralSnapshotTests
{
    [TestMethod]
    public void M3_2Default_IsBoundedAndTextFree()
    {
        UiAutomationSnapshotBudgets budgets =
            UiAutomationSnapshotBudgets.M3_2Default;

        budgets.ValidateForNonTextSnapshot();

        Assert.AreEqual(256, budgets.MaxNodes);
        Assert.AreEqual(8, budgets.MaxDepth);
        Assert.AreEqual(
            TimeSpan.FromMilliseconds(1200),
            budgets.MaxElapsed);
        Assert.AreEqual(27, budgets.MaxPropertiesPerNode);
        Assert.AreEqual(0, budgets.MaxStringCount);
        Assert.AreEqual(0, budgets.MaxStringBytes);
        Assert.AreEqual(32 * 1024, budgets.MaxResultBytes);

        Assert.IsEmpty(
            typeof(UiAutomationStructuralNode)
                .GetProperties()
                .Where(property =>
                    property.PropertyType == typeof(string)));
    }

    [TestMethod]
    public void Validate_NonZeroStringBudget_IsRejected()
    {
        UiAutomationSnapshotBudgets budgets =
            UiAutomationSnapshotBudgets.M3_2Default with
            {
                MaxStringBytes = 1
            };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => budgets.ValidateForNonTextSnapshot());
    }

    [TestMethod]
    public void TryReserveNode_ExactLimits_AreAccepted()
    {
        UiAutomationSnapshotBudgets budgets =
            TinyBudgets(
                maxNodes: 1,
                maxDepth: 0,
                maxElapsed: TimeSpan.FromMilliseconds(10),
                maxPropertyValues: 27,
                maxResultBytes:
                    UiAutomationSnapshotSizeEstimator
                        .EstimateBytes(1));

        UiAutomationSnapshotBudgetTracker tracker =
            new(budgets);

        Assert.IsTrue(
            tracker.TryReserveNode(
                depth: 0,
                elapsed: budgets.MaxElapsed));
        Assert.AreEqual(1, tracker.NodeCount);
        Assert.AreEqual(27, tracker.PropertyValueCount);
        Assert.AreEqual(
            UiAutomationSnapshotTruncation.None,
            tracker.Truncation);
    }

    [TestMethod]
    public void TryReserveNode_NodeLimit_IsHard()
    {
        UiAutomationSnapshotBudgetTracker tracker =
            new(
                TinyBudgets(
                    maxNodes: 1,
                    maxDepth: 2,
                    maxElapsed: TimeSpan.FromSeconds(1),
                    maxPropertyValues: 54,
                    maxResultBytes:
                        UiAutomationSnapshotSizeEstimator
                            .EstimateBytes(2)));

        Assert.IsTrue(
            tracker.TryReserveNode(0, TimeSpan.Zero));
        Assert.IsFalse(
            tracker.TryReserveNode(1, TimeSpan.Zero));
        Assert.AreEqual(1, tracker.NodeCount);
        Assert.IsTrue(
            tracker.Truncation.HasFlag(
                UiAutomationSnapshotTruncation.NodeLimit));
    }

    [TestMethod]
    public void TryReserveNode_DepthLimit_IsHard()
    {
        UiAutomationSnapshotBudgetTracker tracker =
            new(
                TinyBudgets(
                    maxNodes: 2,
                    maxDepth: 0,
                    maxElapsed: TimeSpan.FromSeconds(1),
                    maxPropertyValues: 54,
                    maxResultBytes:
                        UiAutomationSnapshotSizeEstimator
                            .EstimateBytes(2)));

        Assert.IsFalse(
            tracker.TryReserveNode(1, TimeSpan.Zero));
        Assert.AreEqual(0, tracker.NodeCount);
        Assert.IsTrue(
            tracker.Truncation.HasFlag(
                UiAutomationSnapshotTruncation.DepthLimit));
    }

    [TestMethod]
    public void MayContinue_ElapsedLimit_IsHard()
    {
        UiAutomationSnapshotBudgets budgets =
            TinyBudgets(
                maxNodes: 2,
                maxDepth: 1,
                maxElapsed: TimeSpan.FromMilliseconds(10),
                maxPropertyValues: 54,
                maxResultBytes:
                    UiAutomationSnapshotSizeEstimator
                        .EstimateBytes(2));

        UiAutomationSnapshotBudgetTracker tracker =
            new(budgets);

        Assert.IsTrue(
            tracker.MayContinue(budgets.MaxElapsed));
        Assert.IsFalse(
            tracker.MayContinue(
                budgets.MaxElapsed + TimeSpan.FromTicks(1)));
        Assert.IsTrue(
            tracker.Truncation.HasFlag(
                UiAutomationSnapshotTruncation.ElapsedLimit));
    }

    [TestMethod]
    public void MarkDepthBoundary_IsConservativeAndExplicit()
    {
        UiAutomationSnapshotBudgetTracker tracker =
            new(
                TinyBudgets(
                    maxNodes: 1,
                    maxDepth: 0,
                    maxElapsed: TimeSpan.FromSeconds(1),
                    maxPropertyValues: 27,
                    maxResultBytes:
                        UiAutomationSnapshotSizeEstimator
                            .EstimateBytes(1)));

        tracker.MarkDepthBoundary();

        Assert.IsTrue(
            tracker.Truncation.HasFlag(
                UiAutomationSnapshotTruncation.DepthLimit));
    }

    [TestMethod]
    public void TryReserveNode_PropertyValueLimit_IsHard()
    {
        UiAutomationSnapshotBudgetTracker tracker =
            new(
                TinyBudgets(
                    maxNodes: 2,
                    maxDepth: 1,
                    maxElapsed: TimeSpan.FromSeconds(1),
                    maxPropertyValues: 27,
                    maxResultBytes:
                        UiAutomationSnapshotSizeEstimator
                            .EstimateBytes(2)));

        Assert.IsTrue(
            tracker.TryReserveNode(0, TimeSpan.Zero));
        Assert.IsFalse(
            tracker.TryReserveNode(1, TimeSpan.Zero));
        Assert.IsTrue(
            tracker.Truncation.HasFlag(
                UiAutomationSnapshotTruncation
                    .PropertyValueLimit));
    }

    [TestMethod]
    public void TryReserveNode_ResultByteLimit_IsHard()
    {
        UiAutomationSnapshotBudgetTracker tracker =
            new(
                TinyBudgets(
                    maxNodes: 2,
                    maxDepth: 1,
                    maxElapsed: TimeSpan.FromSeconds(1),
                    maxPropertyValues: 54,
                    maxResultBytes:
                        UiAutomationSnapshotSizeEstimator
                            .EstimateBytes(1)));

        Assert.IsTrue(
            tracker.TryReserveNode(0, TimeSpan.Zero));
        Assert.IsFalse(
            tracker.TryReserveNode(1, TimeSpan.Zero));
        Assert.IsTrue(
            tracker.Truncation.HasFlag(
                UiAutomationSnapshotTruncation.ResultByteLimit));
    }

    [TestMethod]
    public void Snapshot_CopiesNodesAndDerivesSummary()
    {
        UiAutomationStructuralNode[] source =
        [
            Node(0, -1, 0, isContent: false),
            Node(1, 0, 1, isContent: true)
        ];

        UiAutomationStructuralSnapshot snapshot =
            new(
                source,
                UiAutomationSnapshotBudgets.M3_2Default,
                UiAutomationSnapshotTruncation.NodeLimit,
                propertyValueCount: 54,
                estimatedResultBytes:
                    UiAutomationSnapshotSizeEstimator
                        .EstimateBytes(2),
                traversalElapsed:
                    TimeSpan.FromMilliseconds(5));

        source[1] = Node(1, 0, 1, isContent: false);

        Assert.AreEqual(
            UiAutomationSnapshotView.Control,
            snapshot.View);
        Assert.HasCount(2, snapshot.Nodes);
        Assert.AreEqual(1, snapshot.ContentNodeCount);
        Assert.AreEqual(1, snapshot.MaxDepthObserved);
        Assert.AreEqual(0, snapshot.StringCount);
        Assert.AreEqual(0, snapshot.StringBytes);
        Assert.IsFalse(snapshot.IsComplete);
    }

    [TestMethod]
    public void Snapshot_InvalidTopology_IsRejected()
    {
        UiAutomationStructuralNode[] nodes =
        [
            Node(0, -1, 0, isContent: false),
            Node(1, 0, 2, isContent: true)
        ];

        Assert.ThrowsExactly<ArgumentException>(
            () => new UiAutomationStructuralSnapshot(
                nodes,
                UiAutomationSnapshotBudgets.M3_2Default,
                UiAutomationSnapshotTruncation.None,
                propertyValueCount: 54,
                estimatedResultBytes:
                    UiAutomationSnapshotSizeEstimator
                        .EstimateBytes(2),
                traversalElapsed: TimeSpan.Zero));
    }

    [TestMethod]
    public void Snapshot_EmptyResult_ReportsNoObservedDepth()
    {
        UiAutomationStructuralSnapshot snapshot =
            new(
                Array.Empty<UiAutomationStructuralNode>(),
                UiAutomationSnapshotBudgets.M3_2Default,
                UiAutomationSnapshotTruncation.ElapsedLimit,
                propertyValueCount: 0,
                estimatedResultBytes:
                    UiAutomationSnapshotSizeEstimator.HeaderBytes,
                traversalElapsed:
                    UiAutomationSnapshotBudgets
                        .M3_2Default
                        .MaxElapsed +
                    TimeSpan.FromTicks(1));

        Assert.IsEmpty(snapshot.Nodes);
        Assert.AreEqual(-1, snapshot.MaxDepthObserved);
    }

    [TestMethod]
    public void Snapshot_NodeBudgetViolation_IsRejected()
    {
        UiAutomationSnapshotBudgets budgets =
            TinyBudgets(
                maxNodes: 1,
                maxDepth: 1,
                maxElapsed: TimeSpan.FromSeconds(1),
                maxPropertyValues: 54,
                maxResultBytes:
                    UiAutomationSnapshotSizeEstimator
                        .EstimateBytes(2));

        UiAutomationStructuralNode[] nodes =
        [
            Node(0, -1, 0, isContent: false),
            Node(1, 0, 1, isContent: true)
        ];

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new UiAutomationStructuralSnapshot(
                nodes,
                budgets,
                UiAutomationSnapshotTruncation.NodeLimit,
                propertyValueCount: 54,
                estimatedResultBytes:
                    UiAutomationSnapshotSizeEstimator
                        .EstimateBytes(2),
                traversalElapsed: TimeSpan.Zero));
    }

    [TestMethod]
    public void Snapshot_ElapsedOverrunRequiresTruncationFlag()
    {
        UiAutomationSnapshotBudgets budgets =
            TinyBudgets(
                maxNodes: 1,
                maxDepth: 0,
                maxElapsed: TimeSpan.FromMilliseconds(10),
                maxPropertyValues: 27,
                maxResultBytes:
                    UiAutomationSnapshotSizeEstimator
                        .EstimateBytes(1));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new UiAutomationStructuralSnapshot(
                [Node(0, -1, 0, isContent: false)],
                budgets,
                UiAutomationSnapshotTruncation.None,
                propertyValueCount: 27,
                estimatedResultBytes:
                    UiAutomationSnapshotSizeEstimator
                        .EstimateBytes(1),
                traversalElapsed: TimeSpan.FromMilliseconds(11)));
    }

    [TestMethod]
    public void Rectangle_NonFiniteOrNegativeSize_IsSanitized()
    {
        Assert.AreEqual(
            UiAutomationRectangle.Empty,
            UiAutomationRectangle.Sanitize(
                double.NaN,
                0,
                1,
                1));

        Assert.AreEqual(
            new UiAutomationRectangle(-10, -20, 0, 0),
            UiAutomationRectangle.Sanitize(
                -10,
                -20,
                -30,
                -40));
    }

    private static UiAutomationSnapshotBudgets TinyBudgets(
        int maxNodes,
        int maxDepth,
        TimeSpan maxElapsed,
        int maxPropertyValues,
        int maxResultBytes) =>
        new(
            maxNodes,
            maxDepth,
            maxElapsed,
            MaxPropertiesPerNode: 27,
            maxPropertyValues,
            MaxStringCount: 0,
            MaxStringBytes: 0,
            maxResultBytes);

    private static UiAutomationStructuralNode Node(
        int index,
        int parentIndex,
        int depth,
        bool isContent) =>
        new(
            index,
            parentIndex,
            depth,
            ControlTypeId: 50000,
            UiAutomationRectangle.Empty,
            IsControlElement: true,
            IsContentElement: isContent,
            IsEnabled: true,
            IsKeyboardFocusable: false,
            HasKeyboardFocus: false,
            IsOffscreen: false,
            IsPassword: false,
            UiAutomationPatternAvailability.None);
}
