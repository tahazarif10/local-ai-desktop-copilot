using System;

namespace LocalCopilot_App.Services;

public sealed record PersistentChangeSample(
    long EpochId,
    int ProfileWidth,
    ChangeResult Change,
    int SourceWidth,
    int SourceHeight,
    int OutputWidth,
    int OutputHeight,
    double ResizeMilliseconds,
    double ReadbackMilliseconds,
    double LuminanceMilliseconds,
    double ProcessingMilliseconds,
    long FramesArrived,
    long FramesReplaced,
    long SamplesProcessed,
    long FramePoolRecreates,
    long StaleDropped);

public sealed record PersistentChangeSessionEnded(
    long EpochId,
    string Reason,
    bool HadError,
    string? ErrorType,
    int? ErrorHResult,
    long FramesArrived,
    long FramesReplaced,
    long SamplesProcessed,
    long FramePoolRecreates,
    long StaleDropped);
