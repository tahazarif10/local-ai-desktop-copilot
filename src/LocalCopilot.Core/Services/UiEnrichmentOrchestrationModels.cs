using System;

namespace LocalCopilot_App.Services;

public enum UiEnrichmentTriggerKind
{
    BackgroundChange,
    UserQuestion
}

public sealed record UiEnrichmentTrigger
{
    public UiEnrichmentTrigger(
        long epochId,
        long sourceId,
        UiEnrichmentTriggerKind kind,
        ChangeClassification? classification = null)
    {
        if (epochId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(epochId));
        }

        if (sourceId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceId));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind));
        }

        if (classification.HasValue &&
            !Enum.IsDefined(classification.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(classification));
        }

        if (kind == UiEnrichmentTriggerKind.BackgroundChange &&
            classification is null)
        {
            throw new ArgumentException(
                "A background trigger requires a change classification.",
                nameof(classification));
        }

        if (kind == UiEnrichmentTriggerKind.UserQuestion &&
            classification is not null)
        {
            throw new ArgumentException(
                "A user-question trigger cannot carry a change classification.",
                nameof(classification));
        }

        EpochId = epochId;
        SourceId = sourceId;
        Kind = kind;
        Classification = classification;
    }

    public long EpochId { get; }

    public long SourceId { get; }

    public UiEnrichmentTriggerKind Kind { get; }

    public ChangeClassification? Classification { get; }
}

public sealed record UiEnrichmentRequest(
    long RequestId,
    long EpochId,
    long SourceId,
    UiEnrichmentTriggerKind Kind,
    ChangeClassification? Classification);

public enum UiEnrichmentAdmissionOutcome
{
    DispatchNow,
    Queued,
    ReplacedPending,
    Rejected
}

public enum UiEnrichmentRejectionReason
{
    None,
    NotArmed,
    NoCurrentEpoch,
    StaleEpoch,
    EpochCancelled,
    CapabilityDenied,
    NonMeaningfulChange,
    DuplicateSource,
    BackgroundDebounced,
    PendingUserQuestionHasPriority,
    UserQuestionBackpressure,
    StaleCompletion,
    PolicyStopped
}

public sealed record UiEnrichmentInvalidation(
    UiEnrichmentRequest? Active,
    UiEnrichmentRequest? Pending)
{
    public static UiEnrichmentInvalidation Empty { get; } =
        new(
            Active: null,
            Pending: null);

    public int Count =>
        (Active is null ? 0 : 1) +
        (Pending is null ? 0 : 1);
}

public sealed record UiEnrichmentAdmissionDecision(
    UiEnrichmentAdmissionOutcome Outcome,
    UiEnrichmentRejectionReason RejectionReason,
    UiEnrichmentRequest? Request,
    UiEnrichmentRequest? ReplacedPending,
    UiEnrichmentInvalidation Invalidated);

public enum UiEnrichmentCompletionOutcome
{
    Idle,
    DispatchNext,
    Ignored
}

public sealed record UiEnrichmentCompletionDecision(
    UiEnrichmentCompletionOutcome Outcome,
    UiEnrichmentRejectionReason RejectionReason,
    UiEnrichmentRequest? Dispatch,
    UiEnrichmentInvalidation Invalidated);

public sealed record UiEnrichmentOrchestrationState(
    long EpochId,
    UiEnrichmentRequest? Active,
    UiEnrichmentRequest? Pending,
    bool IsStopped);
