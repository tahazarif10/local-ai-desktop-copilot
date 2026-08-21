namespace LocalCopilot_App.Services;

public sealed record ForegroundWindowIdentity(
    nint Handle,
    uint ProcessId,
    string ProcessName);

public sealed record ForegroundWindowSnapshot(
    nint Handle,
    uint ProcessId,
    string ProcessName,
    string WindowTitle);

public sealed record ForegroundWindowObservation(
    ForegroundWindowSnapshot Snapshot,
    PrivacyEvaluation Privacy);
