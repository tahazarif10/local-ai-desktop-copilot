using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace LocalCopilot_App.Services;

public enum UiAutomationSemanticContentKind
{
    Name,
    Value,
    VisibleText
}

public enum UiAutomationSemanticProvenance
{
    ForegroundControlView
}

public enum UiAutomationSemanticSensitivity
{
    PotentiallySensitive
}

public sealed record UiAutomationSemanticBudgets(
    int MaxSelectedNodes,
    int MaxStrings,
    int MaxCharactersPerString,
    int MaxVisibleTextRanges,
    int MaxUtf8Bytes,
    TimeSpan MaxElapsed,
    int MaxResultBytes,
    TimeSpan TimeToLive)
{
    public static UiAutomationSemanticBudgets M3_3Default { get; } =
        new(
            MaxSelectedNodes: 32,
            MaxStrings: 64,
            MaxCharactersPerString: 1024,
            MaxVisibleTextRanges: 32,
            MaxUtf8Bytes: 16 * 1024,
            MaxElapsed: TimeSpan.FromMilliseconds(800),
            MaxResultBytes: 24 * 1024,
            TimeToLive: TimeSpan.FromSeconds(5));

    public void Validate()
    {
        if (MaxSelectedNodes <= 0 || MaxSelectedNodes > 256)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxSelectedNodes));
        }

        if (MaxVisibleTextRanges <= 0 ||
            MaxVisibleTextRanges > 256)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxVisibleTextRanges));
        }

        int maximumPossibleStrings =
            checked(
                (MaxSelectedNodes * 2) +
                MaxVisibleTextRanges);

        if (MaxStrings <= 0 ||
            MaxStrings > maximumPossibleStrings)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxStrings));
        }

        if (MaxCharactersPerString <= 0 ||
            MaxCharactersPerString > 4096)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxCharactersPerString));
        }

        if (MaxUtf8Bytes <= 0 || MaxUtf8Bytes > 64 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxUtf8Bytes));
        }

        if (MaxElapsed <= TimeSpan.Zero ||
            MaxElapsed > TimeSpan.FromSeconds(5))
        {
            throw new ArgumentOutOfRangeException(nameof(MaxElapsed));
        }

        if (MaxResultBytes <
                UiAutomationSemanticSizeEstimator.HeaderBytes ||
            MaxResultBytes > 128 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxResultBytes));
        }

        if (TimeToLive <= TimeSpan.Zero ||
            TimeToLive > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(TimeToLive));
        }
    }
}

public static class UiAutomationSemanticSizeEstimator
{
    public const int HeaderBytes = 256;

    public const int NodeBytes = 160;

    public const int ValueBytes = 32;

    public static int EstimateBytes(
        int nodeCount,
        int stringCount,
        int utf8Bytes)
    {
        if (nodeCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeCount));
        }

        if (stringCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stringCount));
        }

        if (utf8Bytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(utf8Bytes));
        }

        return checked(
            HeaderBytes +
            (nodeCount * NodeBytes) +
            (stringCount * ValueBytes) +
            utf8Bytes);
    }
}

public static class UiAutomationSemanticCandidateSelector
{
    private const int WindowControlTypeId = 50032;

    public static IReadOnlyList<int> SelectStructuralIndexes(
        IReadOnlyList<UiAutomationStructuralNode> nodes,
        int maxSelectedNodes)
    {
        return Select(
            nodes,
            maxSelectedNodes).SelectedIndexes;
    }

    public static UiAutomationSemanticCandidateSelection Select(
        IReadOnlyList<UiAutomationStructuralNode> nodes,
        int maxSelectedNodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        if (maxSelectedNodes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSelectedNodes));
        }

        int excludedNonContentCount =
            nodes.Count(node => !node.IsContentElement);

        int excludedOffscreenCount =
            nodes.Count(
                node =>
                    node.IsContentElement &&
                    node.IsOffscreen);

        int excludedPasswordCount =
            nodes.Count(
                node =>
                    node.IsContentElement &&
                    !node.IsOffscreen &&
                    node.IsPassword);

        int[] selectedIndexes =
            nodes
                .Where(IsEligible)
                .OrderByDescending(node => node.HasKeyboardFocus)
                .ThenByDescending(
                    node => node.ControlTypeId == WindowControlTypeId)
                .ThenBy(node => node.Index)
                .Take(maxSelectedNodes)
                .Select(node => node.Index)
                .ToArray();

        int eligibleCount = checked(
            nodes.Count -
            excludedNonContentCount -
            excludedOffscreenCount -
            excludedPasswordCount);

        return new UiAutomationSemanticCandidateSelection(
            selectedIndexes,
            new UiAutomationSemanticSelectionMetrics(
                StructuralNodeCount: nodes.Count,
                EligibleCount: eligibleCount,
                SelectedCount: selectedIndexes.Length,
                ExcludedNonContentCount: excludedNonContentCount,
                ExcludedOffscreenCount: excludedOffscreenCount,
                ExcludedPasswordCount: excludedPasswordCount));
    }

    public static bool IsEligible(
        UiAutomationStructuralNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.IsContentElement &&
            !node.IsOffscreen &&
            !node.IsPassword;
    }
}

public sealed record UiAutomationSemanticCandidateSelection(
    IReadOnlyList<int> SelectedIndexes,
    UiAutomationSemanticSelectionMetrics Metrics);

public sealed record UiAutomationSemanticSelectionMetrics(
    int StructuralNodeCount,
    int EligibleCount,
    int SelectedCount,
    int ExcludedNonContentCount,
    int ExcludedOffscreenCount,
    int ExcludedPasswordCount);

public sealed class UiAutomationSensitiveText : IDisposable
{
    private char[]? _characters;

    internal UiAutomationSensitiveText(
        char[] characters,
        int utf8ByteCount)
    {
        ArgumentNullException.ThrowIfNull(characters);

        if (characters.Length == 0)
        {
            throw new ArgumentException(
                "Sensitive text cannot be empty.",
                nameof(characters));
        }

        if (utf8ByteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(utf8ByteCount));
        }

        _characters = characters;
        CharacterCount = characters.Length;
        Utf8ByteCount = utf8ByteCount;
    }

    public int CharacterCount { get; }

    public int Utf8ByteCount { get; }

    public bool IsDisposed => _characters is null;

    public ReadOnlyMemory<char> Characters =>
        _characters is not null
            ? _characters.AsMemory()
            : throw new ObjectDisposedException(
                nameof(UiAutomationSensitiveText));

    public void Dispose()
    {
        char[]? characters =
            Interlocked.Exchange(
                ref _characters,
                null);

        if (characters is not null)
        {
            Array.Clear(
                characters,
                index: 0,
                length: characters.Length);
        }

        GC.SuppressFinalize(this);
    }

    ~UiAutomationSensitiveText()
    {
        Dispose();
    }

    public override string ToString()
    {
        return IsDisposed
            ? "<disposed-ui-text>"
            : $"<sensitive-ui-text chars={CharacterCount} " +
              $"utf8Bytes={Utf8ByteCount}>";
    }
}

public sealed class UiAutomationSemanticValue : IDisposable
{
    internal UiAutomationSemanticValue(
        UiAutomationSemanticContentKind kind,
        UiAutomationSensitiveText content,
        bool wasTruncated)
    {
        Kind = kind;
        Content = content ??
            throw new ArgumentNullException(nameof(content));
        WasTruncated = wasTruncated;
    }

    public UiAutomationSemanticContentKind Kind { get; }

    public UiAutomationSensitiveText Content { get; }

    public bool WasTruncated { get; }

    public void Dispose()
    {
        Content.Dispose();
        GC.SuppressFinalize(this);
    }

    public override string ToString()
    {
        return
            $"{Kind}:<redacted chars={Content.CharacterCount} " +
            $"utf8Bytes={Content.Utf8ByteCount} " +
            $"truncated={WasTruncated}>";
    }
}

public sealed class UiAutomationSemanticBudgetTracker
{
    private readonly UiAutomationSemanticBudgets _budgets;
    private int _selectedNodeCount;
    private int _stringCount;
    private int _utf8Bytes;
    private int _visibleTextRangeCount;
    private int _estimatedResultBytes =
        UiAutomationSemanticSizeEstimator.HeaderBytes;
    private UiAutomationSnapshotTruncation _truncation;

    public UiAutomationSemanticBudgetTracker(
        UiAutomationSemanticBudgets budgets)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        budgets.Validate();
        _budgets = budgets;
    }

    public int SelectedNodeCount => _selectedNodeCount;

    public int StringCount => _stringCount;

    public int Utf8Bytes => _utf8Bytes;

    public int VisibleTextRangeCount => _visibleTextRangeCount;

    public int EstimatedResultBytes => _estimatedResultBytes;

    public UiAutomationSnapshotTruncation Truncation => _truncation;

    public bool CanReadAnotherString()
    {
        bool allowed = true;

        if (_stringCount >= _budgets.MaxStrings)
        {
            _truncation |=
                UiAutomationSnapshotTruncation.StringCountLimit;
            allowed = false;
        }

        if (_utf8Bytes >= _budgets.MaxUtf8Bytes)
        {
            _truncation |=
                UiAutomationSnapshotTruncation.StringByteLimit;
            allowed = false;
        }

        if (_estimatedResultBytes >
            _budgets.MaxResultBytes -
            UiAutomationSemanticSizeEstimator.ValueBytes - 1)
        {
            _truncation |=
                UiAutomationSnapshotTruncation.ResultByteLimit;
            allowed = false;
        }

        return allowed;
    }

    public void MarkProviderContentUnavailable()
    {
        _truncation |=
            UiAutomationSnapshotTruncation.ProviderContentUnavailable;
    }

    public void MarkSelectedNodeLimit()
    {
        _truncation |=
            UiAutomationSnapshotTruncation.SelectedNodeLimit;
    }

    public bool TryReserveSelectedNode(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        UiAutomationSnapshotTruncation rejection =
            UiAutomationSnapshotTruncation.None;

        if (_selectedNodeCount >= _budgets.MaxSelectedNodes)
        {
            rejection |=
                UiAutomationSnapshotTruncation.SelectedNodeLimit;
        }

        if (elapsed > _budgets.MaxElapsed)
        {
            rejection |=
                UiAutomationSnapshotTruncation.ElapsedLimit;
        }

        if (_estimatedResultBytes >
            _budgets.MaxResultBytes -
            UiAutomationSemanticSizeEstimator.NodeBytes)
        {
            rejection |=
                UiAutomationSnapshotTruncation.ResultByteLimit;
        }

        if (rejection != UiAutomationSnapshotTruncation.None)
        {
            _truncation |= rejection;
            return false;
        }

        _selectedNodeCount = checked(_selectedNodeCount + 1);
        _estimatedResultBytes = checked(
            _estimatedResultBytes +
            UiAutomationSemanticSizeEstimator.NodeBytes);

        return true;
    }

    public bool TryReserveVisibleTextRange()
    {
        if (_visibleTextRangeCount >=
            _budgets.MaxVisibleTextRanges)
        {
            _truncation |=
                UiAutomationSnapshotTruncation.TextRangeLimit;
            return false;
        }

        _visibleTextRangeCount = checked(
            _visibleTextRangeCount + 1);
        return true;
    }

    public bool TryCreateValue(
        UiAutomationSemanticContentKind kind,
        string? providerValue,
        out UiAutomationSemanticValue? value)
    {
        value = null;

        if (string.IsNullOrEmpty(providerValue))
        {
            return false;
        }

        if (_stringCount >= _budgets.MaxStrings)
        {
            _truncation |=
                UiAutomationSnapshotTruncation.StringCountLimit;
            return false;
        }

        int remainingUtf8Bytes =
            _budgets.MaxUtf8Bytes - _utf8Bytes;

        int remainingResultBytes =
            _budgets.MaxResultBytes -
            _estimatedResultBytes -
            UiAutomationSemanticSizeEstimator.ValueBytes;

        int byteLimit = Math.Min(
            remainingUtf8Bytes,
            remainingResultBytes);

        if (byteLimit <= 0)
        {
            if (remainingUtf8Bytes <= 0)
            {
                _truncation |=
                    UiAutomationSnapshotTruncation.StringByteLimit;
            }

            if (remainingResultBytes <= 0)
            {
                _truncation |=
                    UiAutomationSnapshotTruncation.ResultByteLimit;
            }

            return false;
        }

        ReadOnlySpan<char> source =
            providerValue.AsSpan();

        if (source.IsEmpty)
        {
            return false;
        }

        int leadingWhitespace = 0;

        while (leadingWhitespace < source.Length &&
               leadingWhitespace < _budgets.MaxCharactersPerString &&
               char.IsWhiteSpace(source[leadingWhitespace]))
        {
            leadingWhitespace++;
        }

        if (leadingWhitespace == source.Length)
        {
            return false;
        }

        if (leadingWhitespace ==
            _budgets.MaxCharactersPerString)
        {
            _truncation |=
                UiAutomationSnapshotTruncation.StringCharacterLimit;
            return false;
        }

        source = source[leadingWhitespace..];

        int characterLimit =
            Math.Min(
                source.Length,
                _budgets.MaxCharactersPerString);

        char[] bounded = new char[characterLimit];
        int sourceOffset = 0;
        int destinationOffset = 0;
        int acceptedUtf8Bytes = 0;
        bool truncated = false;

        while (sourceOffset < source.Length)
        {
            OperationStatus status =
                Rune.DecodeFromUtf16(
                    source[sourceOffset..],
                    out Rune rune,
                    out int consumed);

            if (status != OperationStatus.Done)
            {
                rune = Rune.ReplacementChar;
                consumed = Math.Max(consumed, 1);
            }

            int runeCharacters =
                rune.Utf16SequenceLength;
            int runeBytes =
                rune.Utf8SequenceLength;

            if (destinationOffset >
                    characterLimit - runeCharacters ||
                acceptedUtf8Bytes >
                    byteLimit - runeBytes)
            {
                truncated = true;

                if (destinationOffset >
                    characterLimit - runeCharacters)
                {
                    _truncation |=
                        UiAutomationSnapshotTruncation
                            .StringCharacterLimit;
                }

                if (acceptedUtf8Bytes >
                    byteLimit - runeBytes)
                {
                    if (remainingUtf8Bytes <=
                        remainingResultBytes)
                    {
                        _truncation |=
                            UiAutomationSnapshotTruncation
                                .StringByteLimit;
                    }

                    if (remainingResultBytes <=
                        remainingUtf8Bytes)
                    {
                        _truncation |=
                            UiAutomationSnapshotTruncation
                                .ResultByteLimit;
                    }
                }

                break;
            }

            int written =
                rune.EncodeToUtf16(
                    bounded.AsSpan(destinationOffset));

            destinationOffset = checked(
                destinationOffset + written);
            acceptedUtf8Bytes = checked(
                acceptedUtf8Bytes + runeBytes);
            sourceOffset = checked(
                sourceOffset + consumed);
        }

        if (sourceOffset < source.Length)
        {
            truncated = true;
        }

        if (destinationOffset == 0)
        {
            Array.Clear(
                bounded,
                index: 0,
                length: bounded.Length);
            return false;
        }

        while (destinationOffset > 0 &&
               char.IsWhiteSpace(bounded[destinationOffset - 1]))
        {
            destinationOffset--;
        }

        if (destinationOffset == 0)
        {
            Array.Clear(
                bounded,
                index: 0,
                length: bounded.Length);
            return false;
        }

        acceptedUtf8Bytes =
            Encoding.UTF8.GetByteCount(
                bounded.AsSpan(0, destinationOffset));

        if (destinationOffset != bounded.Length)
        {
            char[] exact = new char[destinationOffset];
            bounded.AsSpan(0, destinationOffset).CopyTo(exact);
            Array.Clear(
                bounded,
                index: 0,
                length: bounded.Length);
            bounded = exact;
        }

        UiAutomationSensitiveText text =
            new(
                bounded,
                acceptedUtf8Bytes);

        value =
            new UiAutomationSemanticValue(
                kind,
                text,
                truncated);

        _stringCount = checked(_stringCount + 1);
        _utf8Bytes = checked(_utf8Bytes + acceptedUtf8Bytes);
        _estimatedResultBytes = checked(
            _estimatedResultBytes +
            UiAutomationSemanticSizeEstimator.ValueBytes +
            acceptedUtf8Bytes);

        return true;
    }

    public bool MayContinue(TimeSpan elapsed)
    {
        if (elapsed <= _budgets.MaxElapsed)
        {
            return true;
        }

        _truncation |=
            UiAutomationSnapshotTruncation.ElapsedLimit;
        return false;
    }
}

public sealed class UiAutomationSemanticNode : IDisposable
{
    private readonly ReadOnlyCollection<UiAutomationSemanticValue> _values;

    public UiAutomationSemanticNode(
        int structuralIndex,
        int parentStructuralIndex,
        int depth,
        int controlTypeId,
        UiAutomationRectangle bounds,
        bool hasKeyboardFocus,
        bool isEnabled,
        bool isContentElement,
        bool isPassword,
        bool isOffscreen,
        bool isWindow,
        bool isDialog,
        bool? isReadOnly,
        IEnumerable<UiAutomationSemanticValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (structuralIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(structuralIndex));
        }

        if (parentStructuralIndex >= structuralIndex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parentStructuralIndex));
        }

        if (depth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth));
        }

        UiAutomationSemanticValue[] copied = values.ToArray();

        if (copied.Any(value => value is null) ||
            copied.Any(value => value.Content.IsDisposed) ||
            copied
                .Where(value =>
                    value.Kind !=
                    UiAutomationSemanticContentKind.VisibleText)
                .GroupBy(value => value.Kind)
                .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Semantic values must be non-null and unique by source.",
                nameof(values));
        }

        StructuralIndex = structuralIndex;
        ParentStructuralIndex = parentStructuralIndex;
        Depth = depth;
        ControlTypeId = controlTypeId;
        Bounds = bounds;
        HasKeyboardFocus = hasKeyboardFocus;
        IsEnabled = isEnabled;
        IsContentElement = isContentElement;
        IsPassword = isPassword;
        IsOffscreen = isOffscreen;
        IsWindow = isWindow;
        IsDialog = isDialog;
        IsReadOnly = isReadOnly;
        _values = Array.AsReadOnly(copied);
    }

    public int StructuralIndex { get; }

    public int ParentStructuralIndex { get; }

    public int Depth { get; }

    public int ControlTypeId { get; }

    public UiAutomationRectangle Bounds { get; }

    public bool HasKeyboardFocus { get; }

    public bool IsEnabled { get; }

    public bool IsContentElement { get; }

    public bool IsPassword { get; }

    public bool IsOffscreen { get; }

    public bool IsWindow { get; }

    public bool IsDialog { get; }

    public bool? IsReadOnly { get; }

    public IReadOnlyList<UiAutomationSemanticValue> Values => _values;

    public void Dispose()
    {
        foreach (UiAutomationSemanticValue value in _values)
        {
            value.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    public override string ToString()
    {
        return
            $"<semantic-node index={StructuralIndex} " +
            $"values={_values.Count} content=redacted>";
    }
}

public sealed class UiAutomationSemanticSnapshot : IDisposable
{
    private readonly ReadOnlyCollection<UiAutomationSemanticNode> _nodes;
    private int _disposed;

    public UiAutomationSemanticSnapshot(
        long epochId,
        DateTimeOffset capturedUtc,
        DateTimeOffset expiresUtc,
        IEnumerable<UiAutomationSemanticNode> nodes,
        UiAutomationSemanticBudgets budgets,
        UiAutomationSnapshotTruncation truncation,
        int visibleTextRangeCount,
        int estimatedResultBytes,
        TimeSpan semanticElapsed,
        UiAutomationSemanticSelectionMetrics? selection = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(budgets);
        budgets.Validate();

        if (epochId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(epochId));
        }

        if (capturedUtc == default ||
            expiresUtc <= capturedUtc ||
            expiresUtc - capturedUtc > budgets.TimeToLive)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresUtc));
        }

        UiAutomationSemanticNode[] copiedNodes = nodes.ToArray();

        UiAutomationSemanticSelectionMetrics effectiveSelection =
            selection ??
            new UiAutomationSemanticSelectionMetrics(
                StructuralNodeCount: copiedNodes.Length,
                EligibleCount: copiedNodes.Length,
                SelectedCount: copiedNodes.Length,
                ExcludedNonContentCount: 0,
                ExcludedOffscreenCount: 0,
                ExcludedPasswordCount: 0);

        if (copiedNodes.Any(node => node is null) ||
            copiedNodes.Length > budgets.MaxSelectedNodes ||
            copiedNodes.Any(
                node =>
                    !node.IsContentElement ||
                    node.IsPassword ||
                    node.IsOffscreen ||
                    node.Values.Any(
                        value => value.Content.IsDisposed)) ||
            copiedNodes.Select(node => node.StructuralIndex).Distinct().Count() !=
                copiedNodes.Length)
        {
            throw new ArgumentException(
                "Semantic nodes violate the selected-node contract.",
                nameof(nodes));
        }

        if (effectiveSelection.StructuralNodeCount < 0 ||
            effectiveSelection.EligibleCount < 0 ||
            effectiveSelection.SelectedCount != copiedNodes.Length ||
            effectiveSelection.ExcludedNonContentCount < 0 ||
            effectiveSelection.ExcludedOffscreenCount < 0 ||
            effectiveSelection.ExcludedPasswordCount < 0 ||
            effectiveSelection.SelectedCount >
                effectiveSelection.EligibleCount ||
            effectiveSelection.StructuralNodeCount !=
                effectiveSelection.EligibleCount +
                effectiveSelection.ExcludedNonContentCount +
                effectiveSelection.ExcludedOffscreenCount +
                effectiveSelection.ExcludedPasswordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(selection));
        }

        int stringCount = copiedNodes.Sum(node => node.Values.Count);
        int utf8Bytes = copiedNodes.Sum(
            node => node.Values.Sum(value => value.Content.Utf8ByteCount));

        if (stringCount > budgets.MaxStrings ||
            utf8Bytes > budgets.MaxUtf8Bytes ||
            visibleTextRangeCount < 0 ||
            visibleTextRangeCount > budgets.MaxVisibleTextRanges)
        {
            throw new ArgumentOutOfRangeException(nameof(nodes));
        }

        int expectedBytes =
            UiAutomationSemanticSizeEstimator.EstimateBytes(
                copiedNodes.Length,
                stringCount,
                utf8Bytes);

        if (estimatedResultBytes != expectedBytes ||
            estimatedResultBytes > budgets.MaxResultBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(estimatedResultBytes));
        }

        if (semanticElapsed < TimeSpan.Zero ||
            (semanticElapsed > budgets.MaxElapsed &&
             !truncation.HasFlag(
                 UiAutomationSnapshotTruncation.ElapsedLimit)))
        {
            throw new ArgumentOutOfRangeException(nameof(semanticElapsed));
        }

        EpochId = epochId;
        CapturedUtc = capturedUtc;
        ExpiresUtc = expiresUtc;
        Budgets = budgets;
        Truncation = truncation;
        VisibleTextRangeCount = visibleTextRangeCount;
        EstimatedResultBytes = estimatedResultBytes;
        SemanticElapsed = semanticElapsed;
        Selection = effectiveSelection;
        StringCount = stringCount;
        Utf8Bytes = utf8Bytes;
        _nodes = Array.AsReadOnly(copiedNodes);
    }

    public long EpochId { get; }

    public DateTimeOffset CapturedUtc { get; }

    public DateTimeOffset ExpiresUtc { get; }

    public UiAutomationSemanticProvenance Provenance =>
        UiAutomationSemanticProvenance.ForegroundControlView;

    public UiAutomationSemanticSensitivity Sensitivity =>
        UiAutomationSemanticSensitivity.PotentiallySensitive;

    public IReadOnlyList<UiAutomationSemanticNode> Nodes => _nodes;

    public UiAutomationSemanticBudgets Budgets { get; }

    public UiAutomationSnapshotTruncation Truncation { get; }

    public int VisibleTextRangeCount { get; }

    public int EstimatedResultBytes { get; }

    public TimeSpan SemanticElapsed { get; }

    public UiAutomationSemanticSelectionMetrics Selection { get; }

    public int StringCount { get; }

    public int Utf8Bytes { get; }

    public bool IsDisposed =>
        Volatile.Read(ref _disposed) != 0;

    public bool IsExpired(DateTimeOffset utcNow) =>
        utcNow >= ExpiresUtc;

    public int NameCount => Count(UiAutomationSemanticContentKind.Name);

    public int ValueCount => Count(UiAutomationSemanticContentKind.Value);

    public int VisibleTextCount =>
        Count(UiAutomationSemanticContentKind.VisibleText);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (UiAutomationSemanticNode node in _nodes)
        {
            node.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    public override string ToString()
    {
        return
            $"<semantic-snapshot nodes={_nodes.Count} " +
            $"strings={StringCount} utf8Bytes={Utf8Bytes} " +
            $"content=redacted disposed={IsDisposed}>";
    }

    private int Count(UiAutomationSemanticContentKind kind) =>
        _nodes.Sum(
            node => node.Values.Count(value => value.Kind == kind));
}
