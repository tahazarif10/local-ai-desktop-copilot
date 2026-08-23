using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class UiAutomationSemanticSnapshotTests
{
    [TestMethod]
    public void M3_3Default_IsIndependentAndBounded()
    {
        UiAutomationSemanticBudgets budgets =
            UiAutomationSemanticBudgets.M3_3Default;

        budgets.Validate();

        Assert.AreEqual(32, budgets.MaxSelectedNodes);
        Assert.AreEqual(64, budgets.MaxStrings);
        Assert.AreEqual(1024, budgets.MaxCharactersPerString);
        Assert.AreEqual(32, budgets.MaxVisibleTextRanges);
        Assert.AreEqual(16 * 1024, budgets.MaxUtf8Bytes);
        Assert.AreEqual(
            TimeSpan.FromMilliseconds(800),
            budgets.MaxElapsed);
        Assert.AreEqual(24 * 1024, budgets.MaxResultBytes);
        Assert.AreEqual(TimeSpan.FromSeconds(5), budgets.TimeToLive);
    }

    [TestMethod]
    public void CandidateSelection_ExcludesSensitiveOrOffscreenAndPrioritizes()
    {
        UiAutomationStructuralNode[] nodes =
        [
            StructuralNode(0, content: false),
            StructuralNode(1, content: true),
            StructuralNode(2, content: true, offscreen: true),
            StructuralNode(3, content: true, password: true),
            StructuralNode(4, content: true, controlTypeId: 50032),
            StructuralNode(5, content: true, focused: true),
            StructuralNode(6, content: true)
        ];

        IReadOnlyList<int> selected =
            UiAutomationSemanticCandidateSelector
                .SelectStructuralIndexes(
                    nodes,
                    maxSelectedNodes: 3);

        Assert.AreSequenceEqual(
            new[] { 5, 4, 1 },
            selected);
    }

    [TestMethod]
    public void TryCreateValue_TruncatesOnRuneBoundaryAndCountsUtf8()
    {
        UiAutomationSemanticBudgets budgets =
            TinyBudgets(
                maxCharactersPerString: 3,
                maxUtf8Bytes: 16);

        UiAutomationSemanticBudgetTracker tracker =
            new(budgets);

        Assert.IsTrue(
            tracker.TryReserveSelectedNode(TimeSpan.Zero));

        Assert.IsTrue(
            tracker.TryCreateValue(
                UiAutomationSemanticContentKind.Name,
                "  a😀b  ",
                out UiAutomationSemanticValue? value));

        using (UiAutomationSemanticValue accepted = value!)
        {
            Assert.AreEqual("a😀", new string(accepted.Content.Characters.Span));
            Assert.AreEqual(3, accepted.Content.CharacterCount);
            Assert.AreEqual(5, accepted.Content.Utf8ByteCount);
            Assert.IsTrue(accepted.WasTruncated);
        }

        Assert.AreEqual(1, tracker.StringCount);
        Assert.AreEqual(5, tracker.Utf8Bytes);
        Assert.IsTrue(
            tracker.Truncation.HasFlag(
                UiAutomationSnapshotTruncation.StringCharacterLimit));
    }

    [TestMethod]
    public void TryCreateValue_EnforcesUtf8AndStringCountIndependently()
    {
        UiAutomationSemanticBudgets budgets =
            TinyBudgets(
                maxStrings: 1,
                maxUtf8Bytes: 4);

        UiAutomationSemanticBudgetTracker tracker =
            new(budgets);

        Assert.IsTrue(
            tracker.TryReserveSelectedNode(TimeSpan.Zero));

        Assert.IsTrue(
            tracker.TryCreateValue(
                UiAutomationSemanticContentKind.Name,
                "ab😀",
                out UiAutomationSemanticValue? first));

        using (UiAutomationSemanticValue accepted = first!)
        {
            Assert.AreEqual("ab", new string(accepted.Content.Characters.Span));
            Assert.IsTrue(accepted.WasTruncated);
        }

        Assert.IsFalse(
            tracker.TryCreateValue(
                UiAutomationSemanticContentKind.Value,
                "c",
                out UiAutomationSemanticValue? second));

        Assert.IsNull(second);
        Assert.AreEqual(2, tracker.Utf8Bytes);
        Assert.IsTrue(
            tracker.Truncation.HasFlag(
                UiAutomationSnapshotTruncation.StringByteLimit));
        Assert.IsTrue(
            tracker.Truncation.HasFlag(
                UiAutomationSnapshotTruncation.StringCountLimit));
    }

    [TestMethod]
    public void Tracker_EnforcesSelectedNodeRangeElapsedAndResultLimits()
    {
        UiAutomationSemanticBudgets budgets =
            TinyBudgets(
                maxSelectedNodes: 1,
                maxResultBytes:
                    UiAutomationSemanticSizeEstimator.HeaderBytes +
                    UiAutomationSemanticSizeEstimator.NodeBytes);

        UiAutomationSemanticBudgetTracker tracker =
            new(budgets);

        Assert.IsTrue(
            tracker.TryReserveSelectedNode(TimeSpan.Zero));
        Assert.IsFalse(
            tracker.TryReserveSelectedNode(TimeSpan.Zero));
        Assert.IsTrue(tracker.TryReserveVisibleTextRange());
        Assert.IsFalse(tracker.TryReserveVisibleTextRange());
        Assert.IsFalse(
            tracker.MayContinue(
                budgets.MaxElapsed + TimeSpan.FromTicks(1)));

        Assert.IsFalse(
            tracker.TryCreateValue(
                UiAutomationSemanticContentKind.Name,
                "x",
                out _));

        Assert.IsTrue(
            tracker.Truncation.HasFlag(
                UiAutomationSnapshotTruncation.SelectedNodeLimit));
        Assert.IsTrue(
            tracker.Truncation.HasFlag(
                UiAutomationSnapshotTruncation.TextRangeLimit));
        Assert.IsTrue(
            tracker.Truncation.HasFlag(
                UiAutomationSnapshotTruncation.ElapsedLimit));
        Assert.IsTrue(
            tracker.Truncation.HasFlag(
                UiAutomationSnapshotTruncation.ResultByteLimit));
    }

    [TestMethod]
    public void Snapshot_AttachesProvenanceExpiryAndAggregateCounts()
    {
        DateTimeOffset captured =
            new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

        UiAutomationSemanticBudgets budgets =
            TinyBudgets(maxStrings: 3);

        UiAutomationSemanticBudgetTracker tracker =
            new(budgets);

        Assert.IsTrue(
            tracker.TryReserveSelectedNode(TimeSpan.Zero));

        UiAutomationSemanticValue name =
            CreateValue(
                tracker,
                UiAutomationSemanticContentKind.Name,
                "Editor");

        UiAutomationSemanticValue value =
            CreateValue(
                tracker,
                UiAutomationSemanticContentKind.Value,
                "Read only");

        UiAutomationSemanticValue text =
            CreateValue(
                tracker,
                UiAutomationSemanticContentKind.VisibleText,
                "Visible");

        Assert.IsTrue(tracker.TryReserveVisibleTextRange());

        UiAutomationSemanticNode node =
            SemanticNode([name, value, text]);

        using UiAutomationSemanticSnapshot snapshot =
            new(
                epochId: 7,
                captured,
                captured + budgets.TimeToLive,
                [node],
                budgets,
                tracker.Truncation,
                tracker.VisibleTextRangeCount,
                tracker.EstimatedResultBytes,
                TimeSpan.FromMilliseconds(5));

        Assert.AreEqual(7L, snapshot.EpochId);
        Assert.AreEqual(
            UiAutomationSemanticProvenance.ForegroundControlView,
            snapshot.Provenance);
        Assert.AreEqual(
            UiAutomationSemanticSensitivity.PotentiallySensitive,
            snapshot.Sensitivity);
        Assert.AreEqual(3, snapshot.StringCount);
        Assert.AreEqual(1, snapshot.NameCount);
        Assert.AreEqual(1, snapshot.ValueCount);
        Assert.AreEqual(1, snapshot.VisibleTextCount);
        Assert.IsFalse(snapshot.IsExpired(captured));
        Assert.IsTrue(snapshot.IsExpired(snapshot.ExpiresUtc));
        Assert.DoesNotContain("Editor", snapshot.ToString());
        Assert.Contains("content=redacted", snapshot.ToString());
    }

    [TestMethod]
    public void SemanticNode_AllowsMultipleIndependentlyBoundedVisibleRanges()
    {
        UiAutomationSemanticBudgets budgets =
            TinyBudgets(
                maxStrings: 4,
                maxVisibleTextRanges: 2);

        UiAutomationSemanticBudgetTracker tracker =
            new(budgets);

        Assert.IsTrue(
            tracker.TryReserveSelectedNode(TimeSpan.Zero));
        Assert.IsTrue(tracker.TryReserveVisibleTextRange());
        Assert.IsTrue(tracker.TryReserveVisibleTextRange());

        using UiAutomationSemanticNode node =
            SemanticNode(
                [
                    CreateValue(
                        tracker,
                        UiAutomationSemanticContentKind.Name,
                        "Editor"),
                    CreateValue(
                        tracker,
                        UiAutomationSemanticContentKind.Value,
                        "Read only"),
                    CreateValue(
                        tracker,
                        UiAutomationSemanticContentKind.VisibleText,
                        "First"),
                    CreateValue(
                        tracker,
                        UiAutomationSemanticContentKind.VisibleText,
                        "Second")
                ]);

        Assert.HasCount(4, node.Values);
        Assert.AreEqual(
            2,
            node.Values.Count(
                value =>
                    value.Kind ==
                    UiAutomationSemanticContentKind.VisibleText));
    }

    [TestMethod]
    public void Dispose_ClearsPreviouslyExposedMemoryAndIsIdempotent()
    {
        DateTimeOffset captured = DateTimeOffset.UtcNow;
        UiAutomationSemanticBudgets budgets = TinyBudgets();
        UiAutomationSemanticBudgetTracker tracker = new(budgets);

        Assert.IsTrue(
            tracker.TryReserveSelectedNode(TimeSpan.Zero));

        UiAutomationSemanticValue value =
            CreateValue(
                tracker,
                UiAutomationSemanticContentKind.Name,
                "secret");

        ReadOnlyMemory<char> exposed = value.Content.Characters;

        UiAutomationSemanticSnapshot snapshot =
            new(
                9,
                captured,
                captured + budgets.TimeToLive,
                [SemanticNode([value])],
                budgets,
                tracker.Truncation,
                visibleTextRangeCount: 0,
                tracker.EstimatedResultBytes,
                TimeSpan.Zero);

        snapshot.Dispose();
        snapshot.Dispose();

        Assert.IsTrue(snapshot.IsDisposed);
        Assert.IsTrue(exposed.Span.ToArray().All(character => character == '\0'));
        Assert.ThrowsExactly<ObjectDisposedException>(
            () => _ = value.Content.Characters);
        Assert.AreEqual("<disposed-ui-text>", value.Content.ToString());
    }

    [TestMethod]
    public void Snapshot_RejectsOffscreenSelectedNode()
    {
        DateTimeOffset captured = DateTimeOffset.UtcNow;
        UiAutomationSemanticBudgets budgets = TinyBudgets();

        Assert.ThrowsExactly<ArgumentException>(
            () => new UiAutomationSemanticSnapshot(
                1,
                captured,
                captured + budgets.TimeToLive,
                [SemanticNode([], isOffscreen: true)],
                budgets,
                UiAutomationSnapshotTruncation.None,
                visibleTextRangeCount: 0,
                UiAutomationSemanticSizeEstimator.EstimateBytes(1, 0, 0),
                TimeSpan.Zero));
    }

    private static UiAutomationSemanticBudgets TinyBudgets(
        int maxSelectedNodes = 1,
        int maxStrings = 1,
        int maxCharactersPerString = 32,
        int maxVisibleTextRanges = 1,
        int maxUtf8Bytes = 64,
        int? maxResultBytes = null) =>
        new(
            maxSelectedNodes,
            maxStrings,
            maxCharactersPerString,
            maxVisibleTextRanges,
            maxUtf8Bytes,
            MaxElapsed: TimeSpan.FromMilliseconds(20),
            MaxResultBytes:
                maxResultBytes ??
                UiAutomationSemanticSizeEstimator.EstimateBytes(
                    maxSelectedNodes,
                    maxStrings,
                    maxUtf8Bytes),
            TimeToLive: TimeSpan.FromSeconds(1));

    private static UiAutomationSemanticValue CreateValue(
        UiAutomationSemanticBudgetTracker tracker,
        UiAutomationSemanticContentKind kind,
        string content)
    {
        Assert.IsTrue(
            tracker.TryCreateValue(
                kind,
                content,
                out UiAutomationSemanticValue? value));
        Assert.IsNotNull(value);
        return value!;
    }

    private static UiAutomationSemanticNode SemanticNode(
        IEnumerable<UiAutomationSemanticValue> values,
        bool isOffscreen = false) =>
        new(
            structuralIndex: 1,
            parentStructuralIndex: 0,
            depth: 1,
            controlTypeId: 50004,
            UiAutomationRectangle.Empty,
            hasKeyboardFocus: false,
            isEnabled: true,
            isOffscreen,
            isWindow: false,
            isDialog: false,
            isReadOnly: true,
            values);

    private static UiAutomationStructuralNode StructuralNode(
        int index,
        bool content,
        bool offscreen = false,
        bool password = false,
        bool focused = false,
        int controlTypeId = 50004) =>
        new(
            index,
            index == 0 ? -1 : 0,
            index == 0 ? 0 : 1,
            controlTypeId,
            UiAutomationRectangle.Empty,
            IsControlElement: true,
            IsContentElement: content,
            IsEnabled: true,
            IsKeyboardFocusable: focused,
            HasKeyboardFocus: focused,
            IsOffscreen: offscreen,
            IsPassword: password,
            UiAutomationPatternAvailability.None);
}
