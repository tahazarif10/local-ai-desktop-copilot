using System;

namespace LocalCopilot_App.Services;

public enum InputActivityKind
{
    MouseClick,
    MouseWheel,
    KeyboardActivity
}

public sealed record InputActivityEvent(
    long EpochId,
    InputActivityKind Kind,
    long TimestampTicks);
