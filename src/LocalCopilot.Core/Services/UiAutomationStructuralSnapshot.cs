using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LocalCopilot_App.Services;

[Flags]
public enum UiAutomationPatternAvailability : ulong
{
    None = 0,
    Dock = 1UL << 0,
    ExpandCollapse = 1UL << 1,
    GridItem = 1UL << 2,
    Grid = 1UL << 3,
    Invoke = 1UL << 4,
    MultipleView = 1UL << 5,
    RangeValue = 1UL << 6,
    Scroll = 1UL << 7,
    ScrollItem = 1UL << 8,
    SelectionItem = 1UL << 9,
    Selection = 1UL << 10,
    Table = 1UL << 11,
    TableItem = 1UL << 12,
    Text = 1UL << 13,
    Toggle = 1UL << 14,
    Transform = 1UL << 15,
    Value = 1UL << 16,
    Window = 1UL << 17
}

[Flags]
public enum UiAutomationSnapshotTruncation
{
    None = 0,
    NodeLimit = 1 << 0,
    DepthLimit = 1 << 1,
    ElapsedLimit = 1 << 2,
    PropertyValueLimit = 1 << 3,
    StringCountLimit = 1 << 4,
    StringByteLimit = 1 << 5,
    ResultByteLimit = 1 << 6,
    SelectedNodeLimit = 1 << 7,
    TextRangeLimit = 1 << 8,
    StringCharacterLimit = 1 << 9
}

public enum UiAutomationSnapshotView
{
    Control
}

public readonly record struct UiAutomationRectangle(
    double X,
    double Y,
    double Width,
    double Height)
{
    public static UiAutomationRectangle Empty { get; } =
        new(0, 0, 0, 0);

    public static UiAutomationRectangle Sanitize(
        double x,
        double y,
        double width,
        double height)
    {
        if (!double.IsFinite(x) ||
            !double.IsFinite(y) ||
            !double.IsFinite(width) ||
            !double.IsFinite(height))
        {
            return Empty;
        }

        return new UiAutomationRectangle(
            x,
            y,
            Math.Max(0, width),
            Math.Max(0, height));
    }
}

public sealed record UiAutomationStructuralNode(
    int Index,
    int ParentIndex,
    int Depth,
    int ControlTypeId,
    UiAutomationRectangle Bounds,
    bool IsControlElement,
    bool IsContentElement,
    bool IsEnabled,
    bool IsKeyboardFocusable,
    bool HasKeyboardFocus,
    bool IsOffscreen,
    bool IsPassword,
    UiAutomationPatternAvailability AvailablePatterns);

public sealed record UiAutomationSnapshotBudgets(
    int MaxNodes,
    int MaxDepth,
    TimeSpan MaxElapsed,
    int MaxPropertiesPerNode,
    int MaxPropertyValues,
    int MaxStringCount,
    int MaxStringBytes,
    int MaxResultBytes)
{
    public const int M3_2RequiredPropertyCount = 27;

    public static UiAutomationSnapshotBudgets M3_2Default { get; } =
        new(
            MaxNodes: 256,
            MaxDepth: 8,
            MaxElapsed: TimeSpan.FromMilliseconds(1200),
            MaxPropertiesPerNode: M3_2RequiredPropertyCount,
            MaxPropertyValues: 256 * M3_2RequiredPropertyCount,
            MaxStringCount: 0,
            MaxStringBytes: 0,
            MaxResultBytes: 32 * 1024);

    public void ValidateForNonTextSnapshot(
        int requiredPropertyCount = M3_2RequiredPropertyCount)
    {
        if (MaxNodes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxNodes));
        }

        if (MaxDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDepth));
        }

        if (MaxElapsed <= TimeSpan.Zero ||
            MaxElapsed > TimeSpan.FromSeconds(10))
        {
            throw new ArgumentOutOfRangeException(nameof(MaxElapsed));
        }

        if (requiredPropertyCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredPropertyCount));
        }

        if (MaxPropertiesPerNode < requiredPropertyCount)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPropertiesPerNode));
        }

        if (MaxPropertyValues < requiredPropertyCount)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPropertyValues));
        }

        if (MaxStringCount != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxStringCount));
        }

        if (MaxStringBytes != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxStringBytes));
        }

        if (MaxResultBytes < UiAutomationSnapshotSizeEstimator.HeaderBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxResultBytes));
        }
    }
}

public static class UiAutomationSnapshotSizeEstimator
{
    public const int HeaderBytes = 128;

    public const int NodeBytes = 96;

    public static int EstimateBytes(int nodeCount)
    {
        if (nodeCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeCount));
        }

        return checked(HeaderBytes + (nodeCount * NodeBytes));
    }
}

public sealed class UiAutomationSnapshotBudgetTracker
{
    private readonly UiAutomationSnapshotBudgets _budgets;
    private readonly int _propertyCountPerNode;
    private int _nodeCount;
    private int _propertyValueCount;
    private int _estimatedResultBytes =
        UiAutomationSnapshotSizeEstimator.HeaderBytes;
    private UiAutomationSnapshotTruncation _truncation;

    public UiAutomationSnapshotBudgetTracker(
        UiAutomationSnapshotBudgets budgets,
        int propertyCountPerNode =
            UiAutomationSnapshotBudgets.M3_2RequiredPropertyCount)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        budgets.ValidateForNonTextSnapshot(propertyCountPerNode);

        _budgets = budgets;
        _propertyCountPerNode = propertyCountPerNode;
    }

    public int NodeCount => _nodeCount;

    public int PropertyValueCount => _propertyValueCount;

    public int EstimatedResultBytes => _estimatedResultBytes;

    public UiAutomationSnapshotTruncation Truncation => _truncation;

    public bool TryReserveNode(int depth, TimeSpan elapsed)
    {
        if (depth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth));
        }

        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        UiAutomationSnapshotTruncation rejection =
            UiAutomationSnapshotTruncation.None;

        if (_nodeCount >= _budgets.MaxNodes)
        {
            rejection |= UiAutomationSnapshotTruncation.NodeLimit;
        }

        if (depth > _budgets.MaxDepth)
        {
            rejection |= UiAutomationSnapshotTruncation.DepthLimit;
        }

        if (elapsed > _budgets.MaxElapsed)
        {
            rejection |= UiAutomationSnapshotTruncation.ElapsedLimit;
        }

        if (_propertyValueCount >
            _budgets.MaxPropertyValues - _propertyCountPerNode)
        {
            rejection |= UiAutomationSnapshotTruncation.PropertyValueLimit;
        }

        long nextEstimatedBytes =
            UiAutomationSnapshotSizeEstimator.HeaderBytes +
            ((long)_nodeCount + 1L) *
            UiAutomationSnapshotSizeEstimator.NodeBytes;

        if (nextEstimatedBytes > _budgets.MaxResultBytes)
        {
            rejection |= UiAutomationSnapshotTruncation.ResultByteLimit;
        }

        if (rejection != UiAutomationSnapshotTruncation.None)
        {
            _truncation |= rejection;
            return false;
        }

        _nodeCount = checked(_nodeCount + 1);
        _propertyValueCount = checked(
            _propertyValueCount + _propertyCountPerNode);
        _estimatedResultBytes = checked((int)nextEstimatedBytes);

        return true;
    }

    public void MarkDepthBoundary()
    {
        _truncation |= UiAutomationSnapshotTruncation.DepthLimit;
    }

    public bool MayContinue(TimeSpan elapsed)
    {
        if (elapsed <= _budgets.MaxElapsed)
        {
            return true;
        }

        _truncation |= UiAutomationSnapshotTruncation.ElapsedLimit;
        return false;
    }
}

public sealed class UiAutomationStructuralSnapshot
{
    private readonly ReadOnlyCollection<UiAutomationStructuralNode> _nodes;

    public UiAutomationStructuralSnapshot(
        IEnumerable<UiAutomationStructuralNode> nodes,
        UiAutomationSnapshotBudgets budgets,
        UiAutomationSnapshotTruncation truncation,
        int propertyValueCount,
        int estimatedResultBytes,
        TimeSpan traversalElapsed)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(budgets);
        budgets.ValidateForNonTextSnapshot();

        UiAutomationStructuralNode[] copiedNodes = nodes.ToArray();
        ValidateTopology(copiedNodes);

        if (copiedNodes.Length > budgets.MaxNodes ||
            copiedNodes.Any(node => node.Depth > budgets.MaxDepth))
        {
            throw new ArgumentOutOfRangeException(nameof(nodes));
        }

        if (propertyValueCount != checked(
                copiedNodes.Length *
                UiAutomationSnapshotBudgets.M3_2RequiredPropertyCount) ||
            propertyValueCount > budgets.MaxPropertyValues)
        {
            throw new ArgumentOutOfRangeException(nameof(propertyValueCount));
        }

        int expectedBytes =
            UiAutomationSnapshotSizeEstimator.EstimateBytes(copiedNodes.Length);

        if (estimatedResultBytes != expectedBytes ||
            estimatedResultBytes > budgets.MaxResultBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedResultBytes));
        }

        if (traversalElapsed < TimeSpan.Zero ||
            (traversalElapsed > budgets.MaxElapsed &&
             !truncation.HasFlag(
                 UiAutomationSnapshotTruncation.ElapsedLimit)))
        {
            throw new ArgumentOutOfRangeException(nameof(traversalElapsed));
        }

        _nodes = Array.AsReadOnly(copiedNodes);
        Budgets = budgets;
        Truncation = truncation;
        PropertyValueCount = propertyValueCount;
        EstimatedResultBytes = estimatedResultBytes;
        TraversalElapsed = traversalElapsed;
    }

    public UiAutomationSnapshotView View => UiAutomationSnapshotView.Control;

    public IReadOnlyList<UiAutomationStructuralNode> Nodes => _nodes;

    public UiAutomationSnapshotBudgets Budgets { get; }

    public UiAutomationSnapshotTruncation Truncation { get; }

    public int PropertyValueCount { get; }

    public int StringCount => 0;

    public int StringBytes => 0;

    public int EstimatedResultBytes { get; }

    public TimeSpan TraversalElapsed { get; }

    public bool IsComplete => Truncation == UiAutomationSnapshotTruncation.None;

    public int MaxDepthObserved =>
        _nodes.Count == 0 ? -1 : _nodes.Max(node => node.Depth);

    public int ContentNodeCount =>
        _nodes.Count(node => node.IsContentElement);

    private static void ValidateTopology(
        IReadOnlyList<UiAutomationStructuralNode> nodes)
    {
        for (int index = 0; index < nodes.Count; index++)
        {
            UiAutomationStructuralNode node = nodes[index];

            if (node.Index != index)
            {
                throw new ArgumentException(
                    "Node indexes must be dense and ordered.",
                    nameof(nodes));
            }

            if (index == 0)
            {
                if (node.ParentIndex != -1 || node.Depth != 0)
                {
                    throw new ArgumentException(
                        "The first node must be the root.",
                        nameof(nodes));
                }

                continue;
            }

            if (node.ParentIndex < 0 || node.ParentIndex >= index)
            {
                throw new ArgumentException(
                    "A node parent must precede the node.",
                    nameof(nodes));
            }

            if (node.Depth != nodes[node.ParentIndex].Depth + 1)
            {
                throw new ArgumentException(
                    "A node depth must follow its parent depth.",
                    nameof(nodes));
            }
        }
    }
}
