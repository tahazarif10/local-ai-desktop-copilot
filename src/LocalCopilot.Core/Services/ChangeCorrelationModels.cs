namespace LocalCopilot_App.Services;

public sealed record ChangeCorrelationResult(
    long EpochId,
    ChangeClassification Classification,
    InputActivityKind? PossibleTrigger,
    double? TriggerAgeMilliseconds);
