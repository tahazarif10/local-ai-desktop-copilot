using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LocalCopilot_App.Services;

public enum RegionOfInterestPlanningIntent
{
    BackgroundChange,
    UserQuestion
}

[Flags]
public enum RegionOfInterestSource
{
    None = 0,
    ChangedRegion = 1 << 0,
    UiAutomation = 1 << 1
}

public readonly record struct CaptureRegion(
    int X,
    int Y,
    int Width,
    int Height)
{
    public bool IsEmpty =>
        Width <= 0 ||
        Height <= 0;

    public int Right =>
        checked(X + Width);

    public int Bottom =>
        checked(Y + Height);

    public long AreaPixels =>
        IsEmpty
            ? 0
            : checked((long)Width * Height);
}

public sealed record RegionOfInterestPlannerOptions(
    int PaddingPixels,
    int AssociationMarginPixels,
    int MaxRegions,
    double MaxRegionAreaRatio,
    double MaxTotalAreaRatio)
{
    public const int HardMaxRegions = 8;
    public const int HardMaxPaddingPixels = 256;
    public const int HardMaxAssociationMarginPixels = 512;
    public const double HardMaxRegionAreaRatio = 0.50;
    public const double HardMaxTotalAreaRatio = 0.75;

    public static RegionOfInterestPlannerOptions M4_1Default { get; } =
        new(
            PaddingPixels: 16,
            AssociationMarginPixels: 24,
            MaxRegions: 4,
            MaxRegionAreaRatio: 0.25,
            MaxTotalAreaRatio: 0.40);

    public void Validate()
    {
        if (PaddingPixels < 0 ||
            PaddingPixels > HardMaxPaddingPixels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PaddingPixels));
        }

        if (AssociationMarginPixels < 0 ||
            AssociationMarginPixels >
                HardMaxAssociationMarginPixels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AssociationMarginPixels));
        }

        if (MaxRegions <= 0 ||
            MaxRegions > HardMaxRegions)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxRegions));
        }

        if (MaxRegionAreaRatio <= 0.0 ||
            MaxRegionAreaRatio >
                HardMaxRegionAreaRatio)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxRegionAreaRatio));
        }

        if (MaxTotalAreaRatio <= 0.0 ||
            MaxTotalAreaRatio >
                HardMaxTotalAreaRatio ||
            MaxTotalAreaRatio <
                MaxRegionAreaRatio)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxTotalAreaRatio));
        }
    }
}

public sealed record RegionOfInterestPlanningInput(
    RegionOfInterestPlanningIntent Intent,
    int SourceWidth,
    int SourceHeight,
    ChangeRegion? ChangedRegion,
    int ChangeMapWidth,
    int ChangeMapHeight,
    UiAutomationRectangle? CaptureScreenBounds,
    IReadOnlyList<UiAutomationRectangle>? UiAutomationBounds);

public sealed record PlannedCaptureRegion(
    CaptureRegion Bounds,
    RegionOfInterestSource Source);

public sealed class RegionOfInterestPlan
{
    private readonly ReadOnlyCollection<PlannedCaptureRegion>
        _regions;

    public RegionOfInterestPlan(
        IEnumerable<PlannedCaptureRegion> regions,
        int uiAutomationInputCount,
        int rejectedInvalid,
        int rejectedOutsideCapture,
        int rejectedUnassociated,
        int rejectedBudget,
        int deduplicated,
        bool projectionUnavailable,
        bool usedChangeFallback)
    {
        ArgumentNullException.ThrowIfNull(regions);

        PlannedCaptureRegion[] copied =
            regions.ToArray();

        if (copied.Any(region =>
                region.Bounds.IsEmpty ||
                region.Source ==
                    RegionOfInterestSource.None))
        {
            throw new ArgumentException(
                "Planned regions must be non-empty and have provenance.",
                nameof(regions));
        }

        if (uiAutomationInputCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uiAutomationInputCount));
        }

        if (rejectedInvalid < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rejectedInvalid));
        }

        if (rejectedOutsideCapture < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rejectedOutsideCapture));
        }

        if (rejectedUnassociated < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rejectedUnassociated));
        }

        if (rejectedBudget < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rejectedBudget));
        }

        if (deduplicated < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deduplicated));
        }

        _regions =
            Array.AsReadOnly(copied);

        UiAutomationInputCount =
            uiAutomationInputCount;

        RejectedInvalid =
            rejectedInvalid;

        RejectedOutsideCapture =
            rejectedOutsideCapture;

        RejectedUnassociated =
            rejectedUnassociated;

        RejectedBudget =
            rejectedBudget;

        Deduplicated =
            deduplicated;

        ProjectionUnavailable =
            projectionUnavailable;

        UsedChangeFallback =
            usedChangeFallback;
    }

    public IReadOnlyList<PlannedCaptureRegion> Regions =>
        _regions;

    public int UiAutomationInputCount { get; }

    public int RejectedInvalid { get; }

    public int RejectedOutsideCapture { get; }

    public int RejectedUnassociated { get; }

    public int RejectedBudget { get; }

    public int Deduplicated { get; }

    public bool ProjectionUnavailable { get; }

    public bool UsedChangeFallback { get; }

    public bool IsEmpty =>
        _regions.Count == 0;

    public long TotalAreaPixels =>
        _regions.Sum(
            region =>
                region.Bounds.AreaPixels);
}
