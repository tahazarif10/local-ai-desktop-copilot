using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace LocalCopilot_App.Services;

/// <summary>
/// SDK-generated UI Automation COM boundary. Every COM interface is created,
/// used, and explicitly released on the dedicated MTA worker thread.
/// </summary>
internal sealed class UiAutomationNativeClient : IDisposable
{
    private const int GenericFailure =
        unchecked((int)0x80004005);

    private const int BoundingRectanglePropertyId = 30001;
    private const int ControlTypePropertyId = 30003;
    private const int HasKeyboardFocusPropertyId = 30008;
    private const int IsKeyboardFocusablePropertyId = 30009;
    private const int IsEnabledPropertyId = 30010;
    private const int IsControlElementPropertyId = 30016;
    private const int IsContentElementPropertyId = 30017;
    private const int IsPasswordPropertyId = 30019;
    private const int IsOffscreenPropertyId = 30022;

    private static readonly Guid CUIAutomation8ClassId =
        new("E22AD333-B25F-460C-83D0-0581107395C9");

    private static readonly PatternProperty[] PatternProperties =
    [
        new(30027, UiAutomationPatternAvailability.Dock),
        new(30028, UiAutomationPatternAvailability.ExpandCollapse),
        new(30029, UiAutomationPatternAvailability.GridItem),
        new(30030, UiAutomationPatternAvailability.Grid),
        new(30031, UiAutomationPatternAvailability.Invoke),
        new(30032, UiAutomationPatternAvailability.MultipleView),
        new(30033, UiAutomationPatternAvailability.RangeValue),
        new(30034, UiAutomationPatternAvailability.Scroll),
        new(30035, UiAutomationPatternAvailability.ScrollItem),
        new(30036, UiAutomationPatternAvailability.SelectionItem),
        new(30037, UiAutomationPatternAvailability.Selection),
        new(30038, UiAutomationPatternAvailability.Table),
        new(30039, UiAutomationPatternAvailability.TableItem),
        new(30040, UiAutomationPatternAvailability.Text),
        new(30041, UiAutomationPatternAvailability.Toggle),
        new(30042, UiAutomationPatternAvailability.Transform),
        new(30043, UiAutomationPatternAvailability.Value),
        new(30044, UiAutomationPatternAvailability.Window)
    ];

    private static readonly int[] CachedPropertyIds =
    [
        BoundingRectanglePropertyId,
        ControlTypePropertyId,
        HasKeyboardFocusPropertyId,
        IsKeyboardFocusablePropertyId,
        IsEnabledPropertyId,
        IsControlElementPropertyId,
        IsContentElementPropertyId,
        IsPasswordPropertyId,
        IsOffscreenPropertyId,
        .. PatternProperties.Select(property => property.PropertyId)
    ];

    private IUIAutomation2? _automation;
    private IUIAutomationTreeWalker? _controlViewWalker;
    private IUIAutomationCacheRequest? _cacheRequest;

    private UiAutomationNativeClient(
        IUIAutomation2 automation,
        IUIAutomationTreeWalker controlViewWalker,
        IUIAutomationCacheRequest cacheRequest)
    {
        _automation = automation;
        _controlViewWalker = controlViewWalker;
        _cacheRequest = cacheRequest;
    }

    public static int InitializeMta()
    {
        return CoInitializeEx(nint.Zero, coInit: 0);
    }

    public static void UninitializeMta()
    {
        CoUninitialize();
    }

    public static int TryCreate(
        uint connectionTimeoutMilliseconds,
        uint transactionTimeoutMilliseconds,
        out UiAutomationNativeClient? client)
    {
        client = null;

        IUIAutomation2? automation = null;
        IUIAutomationTreeWalker? walker = null;
        IUIAutomationCacheRequest? cacheRequest = null;
        IUIAutomationCondition? controlViewCondition = null;
        object? automationObject = null;

        try
        {
            Type automationType =
                Type.GetTypeFromCLSID(
                    CUIAutomation8ClassId,
                    throwOnError: true)!;

            automationObject =
                Activator.CreateInstance(
                    automationType);

            automation =
                automationObject as IUIAutomation2 ??
                throw new InvalidCastException(
                    "CUIAutomation8 does not expose IUIAutomation2.");

            automationObject = null;

            automation.ConnectionTimeout =
                connectionTimeoutMilliseconds;
            automation.TransactionTimeout =
                transactionTimeoutMilliseconds;

            walker = automation.ControlViewWalker;
            cacheRequest = automation.CreateCacheRequest();
            controlViewCondition = automation.ControlViewCondition;

            cacheRequest.TreeScope =
                TreeScope.TreeScope_Element;
            cacheRequest.TreeFilter = controlViewCondition;
            cacheRequest.AutomationElementMode =
                AutomationElementMode.AutomationElementMode_Full;

            foreach (int propertyId in CachedPropertyIds)
            {
                cacheRequest.AddProperty(
                    (UIA_PROPERTY_ID)propertyId);
            }

            if (CachedPropertyIds.Length !=
                UiAutomationSnapshotBudgets
                    .M3_2RequiredPropertyCount)
            {
                throw new InvalidOperationException(
                    "The structural cache schema is inconsistent.");
            }

            client =
                new UiAutomationNativeClient(
                    automation,
                    walker,
                    cacheRequest);

            automation = null;
            walker = null;
            cacheRequest = null;

            return 0;
        }
        catch (Exception ex)
        {
            return ex.HResult == 0
                ? GenericFailure
                : ex.HResult;
        }
        finally
        {
            ReleaseComObject(controlViewCondition);
            ReleaseComObject(cacheRequest);
            ReleaseComObject(walker);
            ReleaseComObject(automation);
            ReleaseComObject(automationObject);
        }
    }

    public int ElementFromHandle(
        nint hwnd,
        out object? element)
    {
        element = null;

        if (_automation is null)
        {
            return GenericFailure;
        }

        try
        {
            element =
                _automation.ElementFromHandle(
                    new HWND(hwnd));
            return 0;
        }
        catch (Exception ex)
        {
            element = null;
            return ex.HResult == 0
                ? GenericFailure
                : ex.HResult;
        }
    }

    public UiAutomationNativeSnapshotResult CaptureStructuralSnapshot(
        nint hwnd,
        UiAutomationSnapshotBudgets budgets,
        Func<bool> cancellationRequested)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        ArgumentNullException.ThrowIfNull(cancellationRequested);
        budgets.ValidateForNonTextSnapshot();

        if (_automation is null ||
            _controlViewWalker is null ||
            _cacheRequest is null)
        {
            return new UiAutomationNativeSnapshotResult(
                GenericFailure,
                Snapshot: null,
                Cancelled: false);
        }

        long traversalStarted = Stopwatch.GetTimestamp();

        TimeSpan Elapsed() =>
            Stopwatch.GetElapsedTime(traversalStarted);

        UiAutomationSnapshotBudgetTracker tracker =
            new(budgets);

        List<UiAutomationStructuralNode> nodes =
            new(capacity: Math.Min(64, budgets.MaxNodes));

        Queue<NativeNodeFrame> pending = new();

        IUIAutomationElement? unownedElement = null;
        bool cancelled = false;
        bool stopTraversal = false;

        try
        {
            unownedElement =
                _automation.ElementFromHandleBuildCache(
                    new HWND(hwnd),
                    _cacheRequest);

            if (unownedElement is null)
            {
                return new UiAutomationNativeSnapshotResult(
                    HResult: 0,
                    Snapshot: null,
                    Cancelled: false);
            }

            cancelled = cancellationRequested();

            if (cancelled)
            {
                return new UiAutomationNativeSnapshotResult(
                    HResult: 0,
                    Snapshot: null,
                    Cancelled: true);
            }

            if (tracker.TryReserveNode(
                    depth: 0,
                    elapsed: Elapsed()))
            {
                nodes.Add(
                    ReadNode(
                        unownedElement,
                        index: 0,
                        parentIndex: -1,
                        depth: 0));

                pending.Enqueue(
                    new NativeNodeFrame(
                        unownedElement,
                        NodeIndex: 0,
                        Depth: 0));

                unownedElement = null;
            }
            else
            {
                stopTraversal = true;
            }

            while (!stopTraversal &&
                   pending.TryDequeue(out NativeNodeFrame frame))
            {
                try
                {
                    cancelled = cancellationRequested();

                    if (cancelled ||
                        !tracker.MayContinue(Elapsed()))
                    {
                        stopTraversal = true;
                        continue;
                    }

                    if (frame.Depth >= budgets.MaxDepth)
                    {
                        tracker.MarkDepthBoundary();
                        continue;
                    }

                    unownedElement =
                        _controlViewWalker
                            .GetFirstChildElementBuildCache(
                                frame.Element,
                                _cacheRequest);

                    while (unownedElement is not null)
                    {
                        cancelled = cancellationRequested();

                        if (cancelled ||
                            !tracker.MayContinue(Elapsed()))
                        {
                            stopTraversal = true;
                            break;
                        }

                        int depth = checked(frame.Depth + 1);

                        if (!tracker.TryReserveNode(
                                depth,
                                Elapsed()))
                        {
                            stopTraversal = true;
                            break;
                        }

                        int nodeIndex = nodes.Count;

                        nodes.Add(
                            ReadNode(
                                unownedElement,
                                nodeIndex,
                                frame.NodeIndex,
                                depth));

                        IUIAutomationElement? next =
                            _controlViewWalker
                                .GetNextSiblingElementBuildCache(
                                    unownedElement,
                                    _cacheRequest);

                        pending.Enqueue(
                            new NativeNodeFrame(
                                unownedElement,
                                nodeIndex,
                                depth));

                        unownedElement = next;
                    }
                }
                finally
                {
                    ReleaseComObject(frame.Element);
                }
            }

            if (cancelled)
            {
                return new UiAutomationNativeSnapshotResult(
                    HResult: 0,
                    Snapshot: null,
                    Cancelled: true);
            }

            TimeSpan traversalElapsed = Elapsed();
            _ = tracker.MayContinue(traversalElapsed);

            UiAutomationStructuralSnapshot snapshot =
                new(
                    nodes,
                    budgets,
                    tracker.Truncation,
                    tracker.PropertyValueCount,
                    tracker.EstimatedResultBytes,
                    traversalElapsed);

            return new UiAutomationNativeSnapshotResult(
                HResult: 0,
                snapshot,
                Cancelled: false);
        }
        catch (Exception ex)
        {
            return new UiAutomationNativeSnapshotResult(
                ex.HResult == 0
                    ? GenericFailure
                    : ex.HResult,
                Snapshot: null,
                Cancelled: false);
        }
        finally
        {
            ReleaseComObject(unownedElement);

            while (pending.TryDequeue(
                out NativeNodeFrame remaining))
            {
                ReleaseComObject(remaining.Element);
            }
        }
    }

    public static void ReleaseElement(object? element)
    {
        ReleaseComObject(element);
    }

    public void Dispose()
    {
        ReleaseComObject(_cacheRequest);
        ReleaseComObject(_controlViewWalker);
        ReleaseComObject(_automation);

        _cacheRequest = null;
        _controlViewWalker = null;
        _automation = null;
    }

    private static UiAutomationStructuralNode ReadNode(
        IUIAutomationElement element,
        int index,
        int parentIndex,
        int depth)
    {
        UiAutomationPatternAvailability patterns =
            UiAutomationPatternAvailability.None;

        foreach (PatternProperty pattern in PatternProperties)
        {
            if (ReadBoolean(element, pattern.PropertyId))
            {
                patterns |= pattern.Pattern;
            }
        }

        return new UiAutomationStructuralNode(
            index,
            parentIndex,
            depth,
            ReadInt32(element, ControlTypePropertyId),
            ReadRectangle(element),
            ReadBoolean(element, IsControlElementPropertyId),
            ReadBoolean(element, IsContentElementPropertyId),
            ReadBoolean(element, IsEnabledPropertyId),
            ReadBoolean(element, IsKeyboardFocusablePropertyId),
            ReadBoolean(element, HasKeyboardFocusPropertyId),
            ReadBoolean(element, IsOffscreenPropertyId),
            ReadBoolean(element, IsPasswordPropertyId),
            patterns);
    }

    private static bool ReadBoolean(
        IUIAutomationElement element,
        int propertyId)
    {
        object? value =
            element.GetCachedPropertyValue(
                (UIA_PROPERTY_ID)propertyId);

        if (value is bool boolean)
        {
            return boolean;
        }

        RejectUnexpectedPropertyValue(value);
        return false;
    }

    private static int ReadInt32(
        IUIAutomationElement element,
        int propertyId)
    {
        object? value =
            element.GetCachedPropertyValue(
                (UIA_PROPERTY_ID)propertyId);

        if (value is int number)
        {
            return number;
        }

        RejectUnexpectedPropertyValue(value);
        return 0;
    }

    private static UiAutomationRectangle ReadRectangle(
        IUIAutomationElement element)
    {
        object? value =
            element.GetCachedPropertyValue(
                (UIA_PROPERTY_ID)BoundingRectanglePropertyId);

        if (value is double[] values &&
            values.Length == 4)
        {
            return UiAutomationRectangle.Sanitize(
                values[0],
                values[1],
                values[2],
                values[3]);
        }

        if (value is null)
        {
            return UiAutomationRectangle.Empty;
        }

        RejectUnexpectedPropertyValue(value);
        return UiAutomationRectangle.Empty;
    }

    private static void RejectUnexpectedPropertyValue(
        object? value)
    {
        ReleaseComObject(value);

        throw new InvalidOperationException(
            "A cached structural property returned an unexpected type.");
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(
        nint reserved,
        uint coInit);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();

    private readonly record struct NativeNodeFrame(
        IUIAutomationElement Element,
        int NodeIndex,
        int Depth);

    private readonly record struct PatternProperty(
        int PropertyId,
        UiAutomationPatternAvailability Pattern);
}

internal readonly record struct UiAutomationNativeSnapshotResult(
    int HResult,
    UiAutomationStructuralSnapshot? Snapshot,
    bool Cancelled);
