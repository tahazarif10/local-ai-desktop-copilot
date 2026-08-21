using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class ChangeDetectorTests
{
    [TestMethod]
    public void Process_FirstFrame_CreatesBaseline()
    {
        ChangeDetector detector =
            new(CreateOptions());

        ChangeResult result = detector.Process(
            new byte[16],
            width: 4,
            height: 4);

        Assert.AreEqual(
            ChangeClassification.Baseline,
            result.Classification);
        Assert.AreEqual(
            "first_frame",
            result.Reason);
        Assert.AreEqual(
            1,
            result.TotalTileCount);
        Assert.IsNull(
            result.ChangedRegion);
        Assert.IsTrue(
            detector.HasBaseline);
    }

    [TestMethod]
    public void Process_IdenticalFrame_IsInsignificantWithNoRegion()
    {
        ChangeDetector detector =
            new(CreateOptions());

        detector.Process(
            new byte[16],
            4,
            4);

        ChangeResult result = detector.Process(
            new byte[16],
            4,
            4);

        Assert.AreEqual(
            ChangeClassification.Insignificant,
            result.Classification);
        Assert.AreEqual(
            0,
            result.ChangedPixelCount);
        Assert.AreEqual(
            0,
            result.ChangedTileCount);
        Assert.AreEqual(
            0.0,
            result.ChangedPixelRatio,
            0.0000001);
        Assert.IsNull(
            result.ChangedRegion);
    }

    [TestMethod]
    public void Process_DifferenceAtThreshold_CountsPixelAndRegion()
    {
        ChangeDetector detector =
            new(CreateOptions());

        detector.Process(
            new byte[16],
            4,
            4);

        byte[] current =
            new byte[16];
        current[6] =
            10;

        ChangeResult result = detector.Process(
            current,
            4,
            4);

        Assert.AreEqual(
            ChangeClassification.Insignificant,
            result.Classification);
        Assert.AreEqual(
            1,
            result.ChangedPixelCount);
        Assert.AreEqual(
            1.0 / 16.0,
            result.ChangedPixelRatio,
            0.0000001);
        Assert.AreEqual(
            new ChangeRegion(2, 1, 1, 1),
            result.ChangedRegion);
    }

    [TestMethod]
    public void Process_DifferenceBelowThreshold_OnlyAffectsMeanDifference()
    {
        ChangeDetector detector =
            new(CreateOptions());

        detector.Process(
            new byte[16],
            4,
            4);

        byte[] current =
            new byte[16];
        current[0] =
            9;

        ChangeResult result = detector.Process(
            current,
            4,
            4);

        Assert.AreEqual(
            0,
            result.ChangedPixelCount);
        Assert.IsNull(
            result.ChangedRegion);
        Assert.AreEqual(
            9.0 / (255.0 * 16.0),
            result.MeanAbsoluteDifference,
            0.0000001);
    }

    [TestMethod]
    public void Process_MeaningfulPixelBoundary_IsInclusive()
    {
        ChangeDetector detector =
            new(CreateOptions());

        detector.Process(
            new byte[16],
            4,
            4);

        byte[] current =
            new byte[16];
        Array.Fill(
            current,
            (byte)20,
            startIndex: 0,
            count: 4);

        ChangeResult result = detector.Process(
            current,
            4,
            4);

        Assert.AreEqual(
            ChangeClassification.Meaningful,
            result.Classification);
        Assert.AreEqual(
            4,
            result.ChangedPixelCount);
        Assert.AreEqual(
            new ChangeRegion(0, 0, 4, 1),
            result.ChangedRegion);
    }

    [TestMethod]
    public void Process_LargePixelBoundary_IsInclusive()
    {
        ChangeDetector detector =
            new(CreateOptions());

        detector.Process(
            new byte[16],
            4,
            4);

        byte[] current =
            new byte[16];
        Array.Fill(
            current,
            (byte)20,
            startIndex: 0,
            count: 12);

        ChangeResult result = detector.Process(
            current,
            4,
            4);

        Assert.AreEqual(
            ChangeClassification.Large,
            result.Classification);
        Assert.AreEqual(
            12,
            result.ChangedPixelCount);
    }

    [TestMethod]
    public void Process_LargeTileBoundary_IsInclusive()
    {
        ChangeDetectorOptions options =
            new(
                PixelDifferenceThreshold: 10,
                TileSize: 4,
                TileChangedPixelRatioThreshold: 0.25,
                MeaningfulChangedPixelRatio: 0.90,
                MeaningfulChangedTileRatio: 0.50,
                LargeChangedPixelRatio: 1.00,
                LargeChangedTileRatio: 1.00);

        ChangeDetector detector =
            new(options);

        detector.Process(
            new byte[32],
            8,
            4);

        byte[] current =
            new byte[32];
        current[0] = 20;
        current[1] = 20;
        current[8] = 20;
        current[9] = 20;
        current[4] = 20;
        current[5] = 20;
        current[12] = 20;
        current[13] = 20;

        ChangeResult result = detector.Process(
            current,
            8,
            4);

        Assert.AreEqual(
            ChangeClassification.Large,
            result.Classification);
        Assert.AreEqual(
            2,
            result.ChangedTileCount);
        Assert.AreEqual(
            1.0,
            result.ChangedTileRatio,
            0.0000001);
    }

    [TestMethod]
    public void Process_DimensionChange_ClearsOldFrameAndCreatesNewBaseline()
    {
        ChangeDetector detector =
            new(CreateOptions());

        byte[] first =
            Enumerable.Repeat(
                    (byte)44,
                    16)
                .ToArray();

        detector.Process(
            first,
            4,
            4);

        ChangeResult result = detector.Process(
            new byte[32],
            8,
            4);

        Assert.AreEqual(
            ChangeClassification.Baseline,
            result.Classification);
        Assert.AreEqual(
            "dimensions_changed",
            result.Reason);
        Assert.AreSequenceEqual(
            new byte[16],
            first);
    }

    [TestMethod]
    public void Reset_ClearsOwnedFrameAndRequiresNewBaseline()
    {
        ChangeDetector detector =
            new(CreateOptions());

        byte[] frame =
            Enumerable.Repeat(
                    (byte)55,
                    16)
                .ToArray();

        detector.Process(
            frame,
            4,
            4);

        detector.Reset();

        Assert.IsFalse(
            detector.HasBaseline);
        Assert.AreSequenceEqual(
            new byte[16],
            frame);

        ChangeResult result = detector.Process(
            new byte[16],
            4,
            4);

        Assert.AreEqual(
            ChangeClassification.Baseline,
            result.Classification);
    }

    [TestMethod]
    public void Constructor_InvalidOptions_Throw()
    {
        ChangeDetectorOptions valid =
            CreateOptions();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new ChangeDetector(
                valid with
                {
                    PixelDifferenceThreshold = 0
                }));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new ChangeDetector(
                valid with
                {
                    TileSize = 3
                }));

        Assert.ThrowsExactly<ArgumentException>(
            () => new ChangeDetector(
                valid with
                {
                    LargeChangedPixelRatio = 0.10
                }));
    }

    [TestMethod]
    public void Process_InvalidArguments_Throw()
    {
        ChangeDetector detector =
            new(CreateOptions());

        Assert.ThrowsExactly<ArgumentNullException>(
            () => detector.Process(
                null!,
                4,
                4));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => detector.Process(
                new byte[16],
                0,
                4));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => detector.Process(
                new byte[16],
                4,
                0));

        Assert.ThrowsExactly<ArgumentException>(
            () => detector.Process(
                new byte[15],
                4,
                4));
    }

    private static ChangeDetectorOptions CreateOptions() =>
        new(
            PixelDifferenceThreshold: 10,
            TileSize: 4,
            TileChangedPixelRatioThreshold: 0.50,
            MeaningfulChangedPixelRatio: 0.25,
            MeaningfulChangedTileRatio: 0.50,
            LargeChangedPixelRatio: 0.75,
            LargeChangedTileRatio: 1.00);
}
