using System;

namespace LocalCopilot_App.Services;

public enum ChangeClassification
{
    Baseline,
    Insignificant,
    Meaningful,
    Large
}

public sealed record ChangeRegion(
    int X,
    int Y,
    int Width,
    int Height);

public sealed record ChangeDetectorOptions(
    byte PixelDifferenceThreshold,
    int TileSize,
    double TileChangedPixelRatioThreshold,
    double MeaningfulChangedPixelRatio,
    double MeaningfulChangedTileRatio,
    double LargeChangedPixelRatio,
    double LargeChangedTileRatio)
{
    public static ChangeDetectorOptions CreateDefault() =>
        new(
            PixelDifferenceThreshold: 12,
            TileSize: 32,
            TileChangedPixelRatioThreshold: 0.03,
            MeaningfulChangedPixelRatio: 0.002,
            MeaningfulChangedTileRatio: 0.02,
            LargeChangedPixelRatio: 0.10,
            LargeChangedTileRatio: 0.25);

    public void Validate()
    {
        if (PixelDifferenceThreshold == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PixelDifferenceThreshold));
        }

        if (TileSize < 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TileSize));
        }

        ValidateRatio(
            TileChangedPixelRatioThreshold,
            nameof(TileChangedPixelRatioThreshold));

        ValidateRatio(
            MeaningfulChangedPixelRatio,
            nameof(MeaningfulChangedPixelRatio));

        ValidateRatio(
            MeaningfulChangedTileRatio,
            nameof(MeaningfulChangedTileRatio));

        ValidateRatio(
            LargeChangedPixelRatio,
            nameof(LargeChangedPixelRatio));

        ValidateRatio(
            LargeChangedTileRatio,
            nameof(LargeChangedTileRatio));

        if (LargeChangedPixelRatio <
            MeaningfulChangedPixelRatio)
        {
            throw new ArgumentException(
                "Large pixel threshold must be >= meaningful threshold.");
        }

        if (LargeChangedTileRatio <
            MeaningfulChangedTileRatio)
        {
            throw new ArgumentException(
                "Large tile threshold must be >= meaningful threshold.");
        }
    }

    private static void ValidateRatio(
        double value,
        string name)
    {
        if (value < 0.0 ||
            value > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                name,
                "Ratio must be between 0 and 1.");
        }
    }
}

public sealed record ChangeResult(
    ChangeClassification Classification,
    string Reason,
    int Width,
    int Height,
    double MeanAbsoluteDifference,
    double ChangedPixelRatio,
    double ChangedTileRatio,
    int ChangedPixelCount,
    int ChangedTileCount,
    int TotalTileCount,
    ChangeRegion? ChangedRegion,
    double DiffMilliseconds);

public sealed record ChangeDetectionCaptureFrame(
    int ContentWidth,
    int ContentHeight,
    int SurfaceWidth,
    int SurfaceHeight,
    int SourceWidth,
    int SourceHeight,
    int OutputWidth,
    int OutputHeight,
    double ScaleFactor,
    long BgraBytes,
    long LuminanceBytes,
    double FrameMilliseconds,
    double ResizeMilliseconds,
    double ReadbackMilliseconds,
    double LuminanceMilliseconds,
    double TotalMilliseconds,
    byte[] LuminancePixels);

public sealed record ChangeProbeResult(
    long EpochId,
    int ProfileWidth,
    ChangeDetectionCaptureFrame Capture,
    ChangeResult Change,
    double TotalMilliseconds);