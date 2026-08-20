using System;
using System.Diagnostics;

namespace LocalCopilot_App.Services;

public sealed class ChangeDetector
{
    private readonly ChangeDetectorOptions
        _options;

    private byte[]?
        _previous;

    private int
        _previousWidth;

    private int
        _previousHeight;

    public ChangeDetector(
        ChangeDetectorOptions? options = null)
    {
        _options =
            options ??
            ChangeDetectorOptions.CreateDefault();

        _options.Validate();
    }

    public bool HasBaseline =>
        _previous is not null;

    public void Reset()
    {
        if (_previous is not null)
        {
            Array.Clear(
                _previous,
                0,
                _previous.Length);
        }

        _previous =
            null;

        _previousWidth =
            0;

        _previousHeight =
            0;
    }

    public ChangeResult Process(
        byte[] current,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(
            current);

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height));
        }

        int expectedLength =
            checked(width * height);

        if (current.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Expected {expectedLength} luminance bytes, " +
                $"received {current.Length}.",
                nameof(current));
        }

        Stopwatch stopwatch =
            Stopwatch.StartNew();

        int tilesX =
            (width + _options.TileSize - 1) /
            _options.TileSize;

        int tilesY =
            (height + _options.TileSize - 1) /
            _options.TileSize;

        int totalTiles =
            checked(tilesX * tilesY);

        if (_previous is null)
        {
            _previous =
                current;

            _previousWidth =
                width;

            _previousHeight =
                height;

            stopwatch.Stop();

            return CreateBaseline(
                "first_frame",
                width,
                height,
                totalTiles,
                stopwatch.Elapsed.TotalMilliseconds);
        }

        if (_previousWidth != width ||
            _previousHeight != height)
        {
            Reset();

            _previous =
                current;

            _previousWidth =
                width;

            _previousHeight =
                height;

            stopwatch.Stop();

            return CreateBaseline(
                "dimensions_changed",
                width,
                height,
                totalTiles,
                stopwatch.Elapsed.TotalMilliseconds);
        }

        byte[] previous =
            _previous;

        long totalAbsoluteDifference =
            0;

        int changedPixelCount =
            0;

        int changedTileCount =
            0;

        int minChangedX =
            width;

        int minChangedY =
            height;

        int maxChangedX =
            -1;

        int maxChangedY =
            -1;

        int tileSize =
            _options.TileSize;

        int pixelThreshold =
            _options.PixelDifferenceThreshold;

        for (int tileY = 0;
            tileY < height;
            tileY += tileSize)
        {
            int tileBottom =
                Math.Min(
                    height,
                    tileY + tileSize);

            for (int tileX = 0;
                tileX < width;
                tileX += tileSize)
            {
                int tileRight =
                    Math.Min(
                        width,
                        tileX + tileSize);

                int tilePixelCount =
                    (tileRight - tileX) *
                    (tileBottom - tileY);

                int tileChangedPixels =
                    0;

                for (int y = tileY;
                    y < tileBottom;
                    y++)
                {
                    int row =
                        y * width;

                    for (int x = tileX;
                        x < tileRight;
                        x++)
                    {
                        int index =
                            row + x;

                        int difference =
                            current[index] -
                            previous[index];

                        if (difference < 0)
                        {
                            difference =
                                -difference;
                        }

                        totalAbsoluteDifference +=
                            difference;

                        if (difference <
                            pixelThreshold)
                        {
                            continue;
                        }

                        changedPixelCount++;
                        tileChangedPixels++;

                        if (x < minChangedX)
                            minChangedX = x;

                        if (y < minChangedY)
                            minChangedY = y;

                        if (x > maxChangedX)
                            maxChangedX = x;

                        if (y > maxChangedY)
                            maxChangedY = y;
                    }
                }

                double tileChangedRatio =
                    tileChangedPixels /
                    (double)tilePixelCount;

                if (tileChangedRatio >=
                    _options.TileChangedPixelRatioThreshold)
                {
                    changedTileCount++;
                }
            }
        }

        int totalPixels =
            checked(width * height);

        double changedPixelRatio =
            changedPixelCount /
            (double)totalPixels;

        double changedTileRatio =
            changedTileCount /
            (double)totalTiles;

        double meanAbsoluteDifference =
            totalAbsoluteDifference /
            (255.0 * totalPixels);

        ChangeClassification classification;

        if (changedPixelRatio >=
                _options.LargeChangedPixelRatio ||
            changedTileRatio >=
                _options.LargeChangedTileRatio)
        {
            classification =
                ChangeClassification.Large;
        }
        else if (
            changedPixelRatio >=
                _options.MeaningfulChangedPixelRatio ||
            changedTileRatio >=
                _options.MeaningfulChangedTileRatio)
        {
            classification =
                ChangeClassification.Meaningful;
        }
        else
        {
            classification =
                ChangeClassification.Insignificant;
        }

        ChangeRegion? changedRegion =
            changedPixelCount == 0
                ? null
                : new ChangeRegion(
                    minChangedX,
                    minChangedY,
                    maxChangedX - minChangedX + 1,
                    maxChangedY - minChangedY + 1);

        _previous =
            current;

        _previousWidth =
            width;

        _previousHeight =
            height;

        stopwatch.Stop();

        return new ChangeResult(
            classification,
            "compared",
            width,
            height,
            meanAbsoluteDifference,
            changedPixelRatio,
            changedTileRatio,
            changedPixelCount,
            changedTileCount,
            totalTiles,
            changedRegion,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    private static ChangeResult CreateBaseline(
        string reason,
        int width,
        int height,
        int totalTiles,
        double elapsedMilliseconds)
    {
        return new ChangeResult(
            ChangeClassification.Baseline,
            reason,
            width,
            height,
            0.0,
            0.0,
            0.0,
            0,
            0,
            totalTiles,
            null,
            elapsedMilliseconds);
    }
}