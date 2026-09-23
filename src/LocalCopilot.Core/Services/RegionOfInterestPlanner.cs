using System;
using System.Collections.Generic;

namespace LocalCopilot_App.Services;

public sealed class RegionOfInterestPlanner
{
    private readonly RegionOfInterestPlannerOptions
        _options;

    public RegionOfInterestPlanner(
        RegionOfInterestPlannerOptions? options = null)
    {
        _options =
            options ??
            RegionOfInterestPlannerOptions.M4_1Default;

        _options.Validate();
    }

    public RegionOfInterestPlan Plan(
        RegionOfInterestPlanningInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        ValidateInput(input);

        IReadOnlyList<UiAutomationRectangle> uiBounds =
            input.UiAutomationBounds ??
            Array.Empty<UiAutomationRectangle>();

        int rejectedInvalid = 0;
        int rejectedOutsideCapture = 0;
        int rejectedUnassociated = 0;
        int rejectedBudget = 0;
        int deduplicated = 0;
        bool projectionUnavailable =
            uiBounds.Count > 0 &&
            input.CaptureScreenBounds is null;

        CaptureRegion? changeAnchor =
            TryMapChangeRegion(
                input.ChangedRegion,
                input.ChangeMapWidth,
                input.ChangeMapHeight,
                input.SourceWidth,
                input.SourceHeight);

        CaptureRegion? associationRegion =
            changeAnchor is null
                ? null
                : ExpandAndClamp(
                    changeAnchor.Value,
                    _options.AssociationMarginPixels,
                    input.SourceWidth,
                    input.SourceHeight);

        List<PlannedCaptureRegion> planned =
            new();

        long sourceArea =
            checked(
                (long)input.SourceWidth *
                input.SourceHeight);

        long totalArea = 0;

        if (input.Intent ==
                RegionOfInterestPlanningIntent.BackgroundChange &&
            changeAnchor is null)
        {
            return CreatePlan(
                planned,
                uiBounds.Count,
                rejectedInvalid,
                rejectedOutsideCapture,
                rejectedUnassociated,
                rejectedBudget,
                deduplicated,
                projectionUnavailable,
                usedChangeFallback: false);
        }

        if (input.CaptureScreenBounds is not null)
        {
            UiAutomationRectangle screenBounds =
                input.CaptureScreenBounds.Value;

            for (int index = 0;
                index < uiBounds.Count;
                index++)
            {
                UiAutomationRectangle uiRectangle =
                    uiBounds[index];

                UiRectangleMapOutcome mapping =
                    TryMapUiAutomationRectangle(
                        uiRectangle,
                        screenBounds,
                        input.SourceWidth,
                        input.SourceHeight,
                        out CaptureRegion mapped);

                if (mapping ==
                    UiRectangleMapOutcome.Invalid)
                {
                    rejectedInvalid++;
                    continue;
                }

                if (mapping ==
                    UiRectangleMapOutcome.OutsideCapture)
                {
                    rejectedOutsideCapture++;
                    continue;
                }

                CaptureRegion padded =
                    ExpandAndClamp(
                        mapped,
                        _options.PaddingPixels,
                        input.SourceWidth,
                        input.SourceHeight);

                if (input.Intent ==
                        RegionOfInterestPlanningIntent.BackgroundChange &&
                    associationRegion is not null &&
                    !Intersects(
                        padded,
                        associationRegion.Value))
                {
                    rejectedUnassociated++;
                    continue;
                }

                if (IsCoveredOrBroaderDuplicate(
                        planned,
                        padded))
                {
                    deduplicated++;
                    continue;
                }

                if (!CanReserve(
                        padded,
                        planned.Count,
                        totalArea,
                        sourceArea))
                {
                    rejectedBudget++;
                    continue;
                }

                planned.Add(
                    new PlannedCaptureRegion(
                        padded,
                        RegionOfInterestSource.UiAutomation));

                totalArea =
                    checked(
                        totalArea +
                        padded.AreaPixels);
            }
        }

        bool usedChangeFallback = false;

        if (planned.Count == 0 &&
            changeAnchor is not null)
        {
            CaptureRegion paddedChange =
                ExpandAndClamp(
                    changeAnchor.Value,
                    _options.PaddingPixels,
                    input.SourceWidth,
                    input.SourceHeight);

            if (CanReserve(
                    paddedChange,
                    planned.Count,
                    totalArea,
                    sourceArea))
            {
                planned.Add(
                    new PlannedCaptureRegion(
                        paddedChange,
                        RegionOfInterestSource.ChangedRegion));

                usedChangeFallback = true;
            }
            else
            {
                rejectedBudget++;
            }
        }

        return CreatePlan(
            planned,
            uiBounds.Count,
            rejectedInvalid,
            rejectedOutsideCapture,
            rejectedUnassociated,
            rejectedBudget,
            deduplicated,
            projectionUnavailable,
            usedChangeFallback);
    }

    private static void ValidateInput(
        RegionOfInterestPlanningInput input)
    {
        if (input.SourceWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.SourceWidth));
        }

        if (input.SourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.SourceHeight));
        }

        if (input.ChangedRegion is not null &&
            input.ChangeMapWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.ChangeMapWidth));
        }

        if (input.ChangedRegion is not null &&
            input.ChangeMapHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.ChangeMapHeight));
        }

        if (input.CaptureScreenBounds is not null)
        {
            UiAutomationRectangle bounds =
                input.CaptureScreenBounds.Value;

            if (!IsFinitePositiveRectangle(bounds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(input.CaptureScreenBounds));
            }
        }
    }

    private static CaptureRegion? TryMapChangeRegion(
        ChangeRegion? changedRegion,
        int changeMapWidth,
        int changeMapHeight,
        int sourceWidth,
        int sourceHeight)
    {
        if (changedRegion is null ||
            changedRegion.Width <= 0 ||
            changedRegion.Height <= 0)
        {
            return null;
        }

        long rawLeft =
            changedRegion.X;

        long rawTop =
            changedRegion.Y;

        long rawRight =
            rawLeft +
            changedRegion.Width;

        long rawBottom =
            rawTop +
            changedRegion.Height;

        long left =
            Math.Clamp(
                rawLeft,
                0L,
                changeMapWidth);

        long top =
            Math.Clamp(
                rawTop,
                0L,
                changeMapHeight);

        long right =
            Math.Clamp(
                rawRight,
                0L,
                changeMapWidth);

        long bottom =
            Math.Clamp(
                rawBottom,
                0L,
                changeMapHeight);

        if (right <= left ||
            bottom <= top)
        {
            return null;
        }

        int sourceLeft =
            ClampPixelCoordinate(
                Math.Floor(
                    left *
                    sourceWidth /
                    (double)changeMapWidth),
                sourceWidth);

        int sourceTop =
            ClampPixelCoordinate(
                Math.Floor(
                    top *
                    sourceHeight /
                    (double)changeMapHeight),
                sourceHeight);

        int sourceRight =
            ClampPixelCoordinate(
                Math.Ceiling(
                    right *
                    sourceWidth /
                    (double)changeMapWidth),
                sourceWidth);

        int sourceBottom =
            ClampPixelCoordinate(
                Math.Ceiling(
                    bottom *
                    sourceHeight /
                    (double)changeMapHeight),
                sourceHeight);

        return CreateRegion(
            sourceLeft,
            sourceTop,
            sourceRight,
            sourceBottom);
    }

    private static UiRectangleMapOutcome
        TryMapUiAutomationRectangle(
            UiAutomationRectangle rectangle,
            UiAutomationRectangle captureScreenBounds,
            int sourceWidth,
            int sourceHeight,
            out CaptureRegion mapped)
    {
        mapped =
            default;

        if (!IsFinitePositiveRectangle(rectangle))
        {
            return UiRectangleMapOutcome.Invalid;
        }

        double rectangleRight =
            rectangle.X +
            rectangle.Width;

        double rectangleBottom =
            rectangle.Y +
            rectangle.Height;

        double captureRight =
            captureScreenBounds.X +
            captureScreenBounds.Width;

        double captureBottom =
            captureScreenBounds.Y +
            captureScreenBounds.Height;

        if (!double.IsFinite(rectangleRight) ||
            !double.IsFinite(rectangleBottom) ||
            !double.IsFinite(captureRight) ||
            !double.IsFinite(captureBottom))
        {
            return UiRectangleMapOutcome.Invalid;
        }

        double left =
            Math.Max(
                rectangle.X,
                captureScreenBounds.X);

        double top =
            Math.Max(
                rectangle.Y,
                captureScreenBounds.Y);

        double right =
            Math.Min(
                rectangleRight,
                captureRight);

        double bottom =
            Math.Min(
                rectangleBottom,
                captureBottom);

        if (right <= left ||
            bottom <= top)
        {
            return UiRectangleMapOutcome.OutsideCapture;
        }

        double scaleX =
            sourceWidth /
            captureScreenBounds.Width;

        double scaleY =
            sourceHeight /
            captureScreenBounds.Height;

        int sourceLeft =
            ClampPixelCoordinate(
                Math.Floor(
                    (left - captureScreenBounds.X) *
                    scaleX),
                sourceWidth);

        int sourceTop =
            ClampPixelCoordinate(
                Math.Floor(
                    (top - captureScreenBounds.Y) *
                    scaleY),
                sourceHeight);

        int sourceRight =
            ClampPixelCoordinate(
                Math.Ceiling(
                    (right - captureScreenBounds.X) *
                    scaleX),
                sourceWidth);

        int sourceBottom =
            ClampPixelCoordinate(
                Math.Ceiling(
                    (bottom - captureScreenBounds.Y) *
                    scaleY),
                sourceHeight);

        CaptureRegion? candidate =
            CreateRegion(
                sourceLeft,
                sourceTop,
                sourceRight,
                sourceBottom);

        if (candidate is null)
        {
            return UiRectangleMapOutcome.OutsideCapture;
        }

        mapped =
            candidate.Value;

        return UiRectangleMapOutcome.Mapped;
    }

    private CaptureRegion ExpandAndClamp(
        CaptureRegion region,
        int paddingPixels,
        int sourceWidth,
        int sourceHeight)
    {
        long left =
            Math.Max(
                0L,
                (long)region.X -
                    paddingPixels);

        long top =
            Math.Max(
                0L,
                (long)region.Y -
                    paddingPixels);

        long right =
            Math.Min(
                sourceWidth,
                (long)region.Right +
                    paddingPixels);

        long bottom =
            Math.Min(
                sourceHeight,
                (long)region.Bottom +
                    paddingPixels);

        return new CaptureRegion(
            checked((int)left),
            checked((int)top),
            checked((int)(right - left)),
            checked((int)(bottom - top)));
    }

    private bool CanReserve(
        CaptureRegion region,
        int currentRegionCount,
        long currentTotalArea,
        long sourceArea)
    {
        if (region.IsEmpty ||
            currentRegionCount >=
                _options.MaxRegions)
        {
            return false;
        }

        double singleRatio =
            region.AreaPixels /
            (double)sourceArea;

        if (singleRatio >
            _options.MaxRegionAreaRatio)
        {
            return false;
        }

        long newTotalArea =
            checked(
                currentTotalArea +
                region.AreaPixels);

        double totalRatio =
            newTotalArea /
            (double)sourceArea;

        return totalRatio <=
            _options.MaxTotalAreaRatio;
    }

    private static bool IsCoveredOrBroaderDuplicate(
        IReadOnlyList<PlannedCaptureRegion> planned,
        CaptureRegion candidate)
    {
        for (int index = 0;
            index < planned.Count;
            index++)
        {
            CaptureRegion existing =
                planned[index].Bounds;

            if (Contains(
                    existing,
                    candidate) ||
                Contains(
                    candidate,
                    existing))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(
        CaptureRegion outer,
        CaptureRegion inner) =>
        outer.X <= inner.X &&
        outer.Y <= inner.Y &&
        outer.Right >= inner.Right &&
        outer.Bottom >= inner.Bottom;

    private static bool Intersects(
        CaptureRegion left,
        CaptureRegion right) =>
        left.X < right.Right &&
        left.Right > right.X &&
        left.Y < right.Bottom &&
        left.Bottom > right.Y;

    private static CaptureRegion? CreateRegion(
        int left,
        int top,
        int right,
        int bottom)
    {
        if (right <= left ||
            bottom <= top)
        {
            return null;
        }

        return new CaptureRegion(
            left,
            top,
            checked(right - left),
            checked(bottom - top));
    }

    private static int ClampPixelCoordinate(
        double value,
        int limit)
    {
        if (value <= 0)
        {
            return 0;
        }

        if (value >= limit)
        {
            return limit;
        }

        return checked((int)value);
    }

    private static bool IsFinitePositiveRectangle(
        UiAutomationRectangle rectangle) =>
        double.IsFinite(rectangle.X) &&
        double.IsFinite(rectangle.Y) &&
        double.IsFinite(rectangle.Width) &&
        double.IsFinite(rectangle.Height) &&
        rectangle.Width > 0 &&
        rectangle.Height > 0;

    private static RegionOfInterestPlan CreatePlan(
        IReadOnlyList<PlannedCaptureRegion> planned,
        int uiAutomationInputCount,
        int rejectedInvalid,
        int rejectedOutsideCapture,
        int rejectedUnassociated,
        int rejectedBudget,
        int deduplicated,
        bool projectionUnavailable,
        bool usedChangeFallback) =>
        new(
            planned,
            uiAutomationInputCount,
            rejectedInvalid,
            rejectedOutsideCapture,
            rejectedUnassociated,
            rejectedBudget,
            deduplicated,
            projectionUnavailable,
            usedChangeFallback);

    private enum UiRectangleMapOutcome
    {
        Mapped,
        Invalid,
        OutsideCapture
    }
}
