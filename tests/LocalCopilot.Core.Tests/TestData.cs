using LocalCopilot_App.Services;
using System.Diagnostics;

namespace LocalCopilot.Core.Tests;

internal static class TestData
{
    public static ForegroundWindowSnapshot Snapshot(
        nint? handle = null,
        uint processId = 42,
        string processName = "editor",
        string windowTitle = "Document") =>
        new(
            handle ?? (nint)0x1234,
            processId,
            processName,
            windowTitle);

    public static PrivacyEvaluation Allowed(
        string ruleId = "default_allow",
        string reason = "allowed") =>
        new(
            PrivacyDisposition.Allowed,
            ruleId,
            reason);

    public static PrivacyEvaluation Blocked(
        string ruleId = "process_blocklist",
        string reason = "process_rule") =>
        new(
            PrivacyDisposition.Blocked,
            ruleId,
            reason);

    public static ChangeResult Change(
        ChangeClassification classification) =>
        new(
            classification,
            "compared",
            Width: 8,
            Height: 8,
            MeanAbsoluteDifference: 0.25,
            ChangedPixelRatio: 0.25,
            ChangedTileRatio: 0.25,
            ChangedPixelCount: 16,
            ChangedTileCount: 1,
            TotalTileCount: 4,
            ChangedRegion: new ChangeRegion(0, 0, 4, 4),
            DiffMilliseconds: 1.0);

    public static PersistentChangeSample Sample(
        ChangeClassification classification,
        long epochId = 7) =>
        new(
            epochId,
            ProfileWidth: 640,
            Change: Change(classification),
            SourceWidth: 1920,
            SourceHeight: 1080,
            OutputWidth: 640,
            OutputHeight: 360,
            ResizeMilliseconds: 1.0,
            ReadbackMilliseconds: 1.0,
            LuminanceMilliseconds: 1.0,
            ProcessingMilliseconds: 4.0,
            FramesArrived: 10,
            FramesReplaced: 2,
            SamplesProcessed: 8,
            FramePoolRecreates: 0,
            StaleDropped: 0);

    public static long StopwatchTicks(
        TimeSpan duration) =>
        checked(
            (long)Math.Ceiling(
                duration.TotalSeconds *
                Stopwatch.Frequency));
}
