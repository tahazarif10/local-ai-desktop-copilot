using System;
using System.Diagnostics;

namespace LocalCopilot_App.Services;

/// <summary>
/// Owns only metadata admission state for future M3.4 UI enrichment. It does
/// not invoke UI Automation or own content-bearing results.
/// </summary>
public sealed class UiEnrichmentOrchestrationPolicy
{
    private const PrivacyCapability RequiredCapabilities =
        PrivacyCapability.ReadUiStructure |
        PrivacyCapability.ReadUiText;

    private readonly object _gate =
        new();

    private readonly long _backgroundDebounceTicks;

    private readonly Func<long> _getTimestamp;

    private long _epochId;

    private long _nextRequestId;

    private long _lastAcceptedBackgroundSourceId;

    private long _lastAcceptedQuestionSourceId;

    private long? _lastAcceptedBackgroundTimestamp;

    private UiEnrichmentRequest? _active;

    private UiEnrichmentRequest? _pending;

    private bool _stopped;

    public UiEnrichmentOrchestrationPolicy(
        TimeSpan backgroundDebounce)
        : this(
            backgroundDebounce,
            Stopwatch.GetTimestamp,
            Stopwatch.Frequency)
    {
    }

    internal UiEnrichmentOrchestrationPolicy(
        TimeSpan backgroundDebounce,
        Func<long> getTimestamp,
        long timestampFrequency)
    {
        if (backgroundDebounce <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(backgroundDebounce));
        }

        if (timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestampFrequency));
        }

        _getTimestamp =
            getTimestamp ??
            throw new ArgumentNullException(
                nameof(getTimestamp));

        _backgroundDebounceTicks =
            checked(
                (long)Math.Ceiling(
                    backgroundDebounce.TotalSeconds *
                    timestampFrequency));

        if (_backgroundDebounceTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(backgroundDebounce));
        }
    }

    public UiEnrichmentOrchestrationState GetState()
    {
        lock (_gate)
        {
            return new UiEnrichmentOrchestrationState(
                _epochId,
                _active,
                _pending,
                _stopped);
        }
    }

    public UiEnrichmentAdmissionDecision Admit(
        UiEnrichmentTrigger trigger,
        bool isArmed,
        ContextEpoch? currentEpoch)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        lock (_gate)
        {
            if (_stopped)
            {
                return Reject(
                    UiEnrichmentRejectionReason.PolicyStopped);
            }

            UiEnrichmentInvalidation invalidated =
                SynchronizeEpoch(
                    isArmed,
                    currentEpoch);

            UiEnrichmentRejectionReason eligibility =
                EvaluateEligibility(
                    trigger.EpochId,
                    isArmed,
                    currentEpoch);

            if (eligibility != UiEnrichmentRejectionReason.None)
            {
                if (eligibility is
                    UiEnrichmentRejectionReason.NotArmed or
                    UiEnrichmentRejectionReason.NoCurrentEpoch or
                    UiEnrichmentRejectionReason.EpochCancelled or
                    UiEnrichmentRejectionReason.CapabilityDenied)
                {
                    invalidated = CoalesceInvalidation(
                        invalidated,
                        InvalidateCore(
                            resetEpoch: eligibility is
                                UiEnrichmentRejectionReason.NotArmed or
                                UiEnrichmentRejectionReason.NoCurrentEpoch));
                }

                return Reject(
                    eligibility,
                    invalidated);
            }

            if (trigger.Kind ==
                UiEnrichmentTriggerKind.BackgroundChange)
            {
                if (trigger.Classification is not
                    ChangeClassification.Meaningful and not
                    ChangeClassification.Large)
                {
                    return Reject(
                        UiEnrichmentRejectionReason.NonMeaningfulChange,
                        invalidated);
                }

                if (trigger.SourceId <=
                    _lastAcceptedBackgroundSourceId)
                {
                    return Reject(
                        UiEnrichmentRejectionReason.DuplicateSource,
                        invalidated);
                }

                long now =
                    _getTimestamp();

                if (_lastAcceptedBackgroundTimestamp.HasValue)
                {
                    long elapsed =
                        now -
                        _lastAcceptedBackgroundTimestamp.Value;

                    if (elapsed < 0 ||
                        elapsed < _backgroundDebounceTicks)
                    {
                        return Reject(
                            UiEnrichmentRejectionReason.BackgroundDebounced,
                            invalidated);
                    }
                }

                return AdmitEligible(
                    trigger,
                    now,
                    invalidated);
            }

            if (trigger.SourceId <=
                _lastAcceptedQuestionSourceId)
            {
                return Reject(
                    UiEnrichmentRejectionReason.DuplicateSource,
                    invalidated);
            }

            return AdmitEligible(
                trigger,
                admittedTimestamp: null,
                invalidated);
        }
    }

    public UiEnrichmentCompletionDecision Complete(
        long requestId,
        bool isArmed,
        ContextEpoch? currentEpoch)
    {
        if (requestId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestId));
        }

        lock (_gate)
        {
            if (_stopped)
            {
                return new UiEnrichmentCompletionDecision(
                    UiEnrichmentCompletionOutcome.Ignored,
                    UiEnrichmentRejectionReason.PolicyStopped,
                    Dispatch: null,
                    UiEnrichmentInvalidation.Empty);
            }

            if (_active is null ||
                _active.RequestId != requestId)
            {
                return new UiEnrichmentCompletionDecision(
                    UiEnrichmentCompletionOutcome.Ignored,
                    UiEnrichmentRejectionReason.StaleCompletion,
                    Dispatch: null,
                    UiEnrichmentInvalidation.Empty);
            }

            UiEnrichmentRequest completed =
                _active;

            _active =
                null;

            UiEnrichmentInvalidation invalidated =
                SynchronizeEpoch(
                    isArmed,
                    currentEpoch);

            UiEnrichmentRejectionReason eligibility =
                EvaluateEligibility(
                    completed.EpochId,
                    isArmed,
                    currentEpoch);

            if (eligibility != UiEnrichmentRejectionReason.None)
            {
                UiEnrichmentInvalidation remaining =
                    InvalidateCore(
                        resetEpoch: eligibility is
                            UiEnrichmentRejectionReason.NotArmed or
                            UiEnrichmentRejectionReason.NoCurrentEpoch);

                return new UiEnrichmentCompletionDecision(
                    UiEnrichmentCompletionOutcome.Idle,
                    eligibility,
                    Dispatch: null,
                    CoalesceInvalidation(
                        invalidated,
                        remaining));
            }

            if (_pending is null)
            {
                return new UiEnrichmentCompletionDecision(
                    UiEnrichmentCompletionOutcome.Idle,
                    UiEnrichmentRejectionReason.None,
                    Dispatch: null,
                    invalidated);
            }

            UiEnrichmentRequest next =
                _pending;

            _pending =
                null;

            _active =
                next;

            return new UiEnrichmentCompletionDecision(
                UiEnrichmentCompletionOutcome.DispatchNext,
                UiEnrichmentRejectionReason.None,
                next,
                invalidated);
        }
    }

    public UiEnrichmentInvalidation Invalidate()
    {
        lock (_gate)
        {
            return InvalidateCore(
                resetEpoch: true);
        }
    }

    public UiEnrichmentInvalidation Stop()
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return UiEnrichmentInvalidation.Empty;
            }

            _stopped =
                true;

            return InvalidateCore(
                resetEpoch: true);
        }
    }

    private UiEnrichmentAdmissionDecision AdmitEligible(
        UiEnrichmentTrigger trigger,
        long? admittedTimestamp,
        UiEnrichmentInvalidation invalidated)
    {
        if (_active is not null &&
            _pending?.Kind ==
                UiEnrichmentTriggerKind.UserQuestion)
        {
            return Reject(
                trigger.Kind ==
                    UiEnrichmentTriggerKind.UserQuestion
                    ? UiEnrichmentRejectionReason.UserQuestionBackpressure
                    : UiEnrichmentRejectionReason.PendingUserQuestionHasPriority,
                invalidated);
        }

        UiEnrichmentRequest request =
            new(
                checked(++_nextRequestId),
                trigger.EpochId,
                trigger.SourceId,
                trigger.Kind,
                trigger.Classification);

        RecordAccepted(
            trigger,
            admittedTimestamp);

        if (_active is null)
        {
            _active =
                request;

            return new UiEnrichmentAdmissionDecision(
                UiEnrichmentAdmissionOutcome.DispatchNow,
                UiEnrichmentRejectionReason.None,
                request,
                ReplacedPending: null,
                invalidated);
        }

        if (_pending is null)
        {
            _pending =
                request;

            return new UiEnrichmentAdmissionDecision(
                UiEnrichmentAdmissionOutcome.Queued,
                UiEnrichmentRejectionReason.None,
                request,
                ReplacedPending: null,
                invalidated);
        }

        UiEnrichmentRequest replaced =
            _pending;

        _pending =
            request;

        return new UiEnrichmentAdmissionDecision(
            UiEnrichmentAdmissionOutcome.ReplacedPending,
            UiEnrichmentRejectionReason.None,
            request,
            replaced,
            invalidated);
    }

    private void RecordAccepted(
        UiEnrichmentTrigger trigger,
        long? admittedTimestamp)
    {
        if (trigger.Kind ==
            UiEnrichmentTriggerKind.BackgroundChange)
        {
            _lastAcceptedBackgroundSourceId =
                trigger.SourceId;

            _lastAcceptedBackgroundTimestamp =
                admittedTimestamp ??
                throw new InvalidOperationException(
                    "A background trigger requires an admission timestamp.");

            return;
        }

        _lastAcceptedQuestionSourceId =
            trigger.SourceId;
    }

    private UiEnrichmentInvalidation SynchronizeEpoch(
        bool isArmed,
        ContextEpoch? currentEpoch)
    {
        if (!isArmed ||
            currentEpoch is null)
        {
            return UiEnrichmentInvalidation.Empty;
        }

        if (_epochId == currentEpoch.Id)
        {
            return UiEnrichmentInvalidation.Empty;
        }

        UiEnrichmentInvalidation invalidated =
            InvalidateCore(
                resetEpoch: false);

        _epochId =
            currentEpoch.Id;

        return invalidated;
    }

    private static UiEnrichmentRejectionReason EvaluateEligibility(
        long expectedEpochId,
        bool isArmed,
        ContextEpoch? currentEpoch)
    {
        if (!isArmed)
        {
            return UiEnrichmentRejectionReason.NotArmed;
        }

        if (currentEpoch is null)
        {
            return UiEnrichmentRejectionReason.NoCurrentEpoch;
        }

        if (currentEpoch.Id != expectedEpochId)
        {
            return UiEnrichmentRejectionReason.StaleEpoch;
        }

        if (currentEpoch.CancellationToken.IsCancellationRequested)
        {
            return UiEnrichmentRejectionReason.EpochCancelled;
        }

        if (!currentEpoch.Privacy.Allows(
                RequiredCapabilities))
        {
            return UiEnrichmentRejectionReason.CapabilityDenied;
        }

        return UiEnrichmentRejectionReason.None;
    }

    private UiEnrichmentInvalidation InvalidateCore(
        bool resetEpoch)
    {
        UiEnrichmentInvalidation invalidated =
            new(
                _active,
                _pending);

        _active =
            null;

        _pending =
            null;

        _lastAcceptedBackgroundSourceId =
            0;

        _lastAcceptedQuestionSourceId =
            0;

        _lastAcceptedBackgroundTimestamp =
            null;

        if (resetEpoch)
        {
            _epochId =
                0;
        }

        return invalidated;
    }

    private static UiEnrichmentInvalidation CoalesceInvalidation(
        UiEnrichmentInvalidation first,
        UiEnrichmentInvalidation second)
    {
        if (first.Count > 0 &&
            second.Count > 0)
        {
            throw new InvalidOperationException(
                "Invalidation batches must not overlap.");
        }

        return first.Count > 0
            ? first
            : second;
    }

    private static UiEnrichmentAdmissionDecision Reject(
        UiEnrichmentRejectionReason reason,
        UiEnrichmentInvalidation? invalidated = null) =>
        new(
            UiEnrichmentAdmissionOutcome.Rejected,
            reason,
            Request: null,
            ReplacedPending: null,
            invalidated ??
            UiEnrichmentInvalidation.Empty);
}
