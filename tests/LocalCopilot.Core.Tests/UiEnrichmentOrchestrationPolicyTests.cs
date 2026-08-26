using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class UiEnrichmentOrchestrationPolicyTests
{
    private const long TimestampFrequency =
        1000;

    [TestMethod]
    public void Trigger_InvalidIdentityOrShape_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Background(
                epochId: 0,
                sourceId: 1));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Background(
                epochId: 7,
                sourceId: 0));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new UiEnrichmentTrigger(
                7,
                1,
                (UiEnrichmentTriggerKind)99));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new UiEnrichmentTrigger(
                7,
                1,
                UiEnrichmentTriggerKind.BackgroundChange,
                (ChangeClassification)99));

        Assert.ThrowsExactly<ArgumentException>(
            () => new UiEnrichmentTrigger(
                7,
                1,
                UiEnrichmentTriggerKind.BackgroundChange));

        Assert.ThrowsExactly<ArgumentException>(
            () => new UiEnrichmentTrigger(
                7,
                1,
                UiEnrichmentTriggerKind.UserQuestion,
                ChangeClassification.Meaningful));
    }

    [TestMethod]
    public void Constructor_InvalidDebounceOrClock_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new UiEnrichmentOrchestrationPolicy(
                TimeSpan.Zero));

        Assert.ThrowsExactly<ArgumentNullException>(
            () => new UiEnrichmentOrchestrationPolicy(
                TimeSpan.FromMilliseconds(500),
                null!,
                TimestampFrequency));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new UiEnrichmentOrchestrationPolicy(
                TimeSpan.FromMilliseconds(500),
                () => 0,
                timestampFrequency: 0));
    }

    [TestMethod]
    public void Admit_RequiresArmedCurrentUncancelledCapableEpoch()
    {
        long now = 0;
        UiEnrichmentOrchestrationPolicy policy =
            CreatePolicy(() => now);

        UiEnrichmentTrigger trigger =
            Background();

        AssertRejected(
            policy.Admit(
                trigger,
                isArmed: false,
                Epoch()),
            UiEnrichmentRejectionReason.NotArmed);

        AssertRejected(
            policy.Admit(
                trigger,
                isArmed: true,
                currentEpoch: null),
            UiEnrichmentRejectionReason.NoCurrentEpoch);

        AssertRejected(
            policy.Admit(
                trigger,
                isArmed: true,
                Epoch(id: 8)),
            UiEnrichmentRejectionReason.StaleEpoch);

        using CancellationTokenSource cancellation =
            new();
        cancellation.Cancel();

        AssertRejected(
            policy.Admit(
                trigger,
                isArmed: true,
                Epoch(cancellationToken: cancellation.Token)),
            UiEnrichmentRejectionReason.EpochCancelled);

        AssertRejected(
            policy.Admit(
                trigger,
                isArmed: true,
                Epoch(
                    capabilities:
                        PrivacyCapability.ReadUiStructure)),
            UiEnrichmentRejectionReason.CapabilityDenied);
    }

    [TestMethod]
    public void Admit_BackgroundRequiresMeaningfulOrLargeChange()
    {
        UiEnrichmentOrchestrationPolicy policy =
            CreatePolicy(() => 0);

        AssertRejected(
            policy.Admit(
                Background(
                    sourceId: 1,
                    classification:
                        ChangeClassification.Baseline),
                isArmed: true,
                Epoch()),
            UiEnrichmentRejectionReason.NonMeaningfulChange);

        AssertRejected(
            policy.Admit(
                Background(
                    sourceId: 2,
                    classification:
                        ChangeClassification.Insignificant),
                isArmed: true,
                Epoch()),
            UiEnrichmentRejectionReason.NonMeaningfulChange);

        Assert.AreEqual(
            UiEnrichmentAdmissionOutcome.DispatchNow,
            policy.Admit(
                Background(
                    sourceId: 3,
                    classification:
                        ChangeClassification.Large),
                isArmed: true,
                Epoch()).Outcome);
    }

    [TestMethod]
    public void Admit_CancellationOrCapabilityLossInvalidatesBoundedState()
    {
        long now = 0;
        UiEnrichmentOrchestrationPolicy cancelledPolicy =
            CreatePolicy(() => now);

        using CancellationTokenSource cancellation =
            new();

        ContextEpoch cancellableEpoch =
            Epoch(cancellationToken: cancellation.Token);

        cancelledPolicy.Admit(
            Background(sourceId: 1),
            isArmed: true,
            cancellableEpoch);

        cancelledPolicy.Admit(
            Question(sourceId: 1),
            isArmed: true,
            cancellableEpoch);

        cancellation.Cancel();

        UiEnrichmentAdmissionDecision cancelled =
            cancelledPolicy.Admit(
                Question(sourceId: 2),
                isArmed: true,
                cancellableEpoch);

        AssertRejected(
            cancelled,
            UiEnrichmentRejectionReason.EpochCancelled);
        Assert.AreEqual(2, cancelled.Invalidated.Count);
        Assert.IsNull(cancelledPolicy.GetState().Active);
        Assert.IsNull(cancelledPolicy.GetState().Pending);

        UiEnrichmentOrchestrationPolicy deniedPolicy =
            CreatePolicy(() => now);

        deniedPolicy.Admit(
            Background(sourceId: 1),
            isArmed: true,
            Epoch());

        deniedPolicy.Admit(
            Question(sourceId: 1),
            isArmed: true,
            Epoch());

        UiEnrichmentAdmissionDecision denied =
            deniedPolicy.Admit(
                Question(sourceId: 2),
                isArmed: true,
                Epoch(
                    capabilities:
                        PrivacyCapability.ReadUiStructure));

        AssertRejected(
            denied,
            UiEnrichmentRejectionReason.CapabilityDenied);
        Assert.AreEqual(2, denied.Invalidated.Count);
        Assert.IsNull(deniedPolicy.GetState().Active);
        Assert.IsNull(deniedPolicy.GetState().Pending);
    }

    [TestMethod]
    public void Admit_FirstEligibleTriggerDispatchesAndDuplicateRejects()
    {
        UiEnrichmentOrchestrationPolicy policy =
            CreatePolicy(() => 0);

        UiEnrichmentAdmissionDecision first =
            policy.Admit(
                Background(),
                isArmed: true,
                Epoch());

        Assert.AreEqual(
            UiEnrichmentAdmissionOutcome.DispatchNow,
            first.Outcome);
        Assert.AreEqual(
            UiEnrichmentRejectionReason.None,
            first.RejectionReason);
        Assert.IsNotNull(first.Request);
        Assert.AreEqual(1L, first.Request.RequestId);

        AssertRejected(
            policy.Admit(
                Background(),
                isArmed: true,
                Epoch()),
            UiEnrichmentRejectionReason.DuplicateSource);
    }

    [TestMethod]
    public void Admit_BackgroundDebounceUsesExplicitMonotonicWindow()
    {
        long now = 0;
        UiEnrichmentOrchestrationPolicy policy =
            CreatePolicy(() => now);

        Assert.AreEqual(
            UiEnrichmentAdmissionOutcome.DispatchNow,
            policy.Admit(
                Background(sourceId: 1),
                isArmed: true,
                Epoch()).Outcome);

        now = 499;

        AssertRejected(
            policy.Admit(
                Background(sourceId: 2),
                isArmed: true,
                Epoch()),
            UiEnrichmentRejectionReason.BackgroundDebounced);

        now = 500;

        Assert.AreEqual(
            UiEnrichmentAdmissionOutcome.Queued,
            policy.Admit(
                Background(sourceId: 2),
                isArmed: true,
                Epoch()).Outcome);
    }

    [TestMethod]
    public void Admit_NewerBackgroundReplacesOnlyPendingBackground()
    {
        long now = 0;
        UiEnrichmentOrchestrationPolicy policy =
            CreatePolicy(() => now);

        UiEnrichmentAdmissionDecision first =
            policy.Admit(
                Background(sourceId: 1),
                isArmed: true,
                Epoch());

        now = 500;

        UiEnrichmentAdmissionDecision second =
            policy.Admit(
                Background(sourceId: 2),
                isArmed: true,
                Epoch());

        now = 1000;

        UiEnrichmentAdmissionDecision third =
            policy.Admit(
                Background(sourceId: 3),
                isArmed: true,
                Epoch());

        Assert.AreEqual(
            UiEnrichmentAdmissionOutcome.DispatchNow,
            first.Outcome);
        Assert.AreEqual(
            UiEnrichmentAdmissionOutcome.Queued,
            second.Outcome);
        Assert.AreEqual(
            UiEnrichmentAdmissionOutcome.ReplacedPending,
            third.Outcome);
        Assert.IsNotNull(third.ReplacedPending);
        Assert.AreEqual(
            2L,
            third.ReplacedPending.SourceId);

        UiEnrichmentOrchestrationState state =
            policy.GetState();

        Assert.AreEqual(1L, state.Active?.SourceId);
        Assert.AreEqual(3L, state.Pending?.SourceId);
    }

    [TestMethod]
    public void Admit_UserQuestionBypassesDebounceAndReplacesBackground()
    {
        long now = 0;
        UiEnrichmentOrchestrationPolicy policy =
            CreatePolicy(() => now);

        policy.Admit(
            Background(sourceId: 1),
            isArmed: true,
            Epoch());

        now = 500;

        policy.Admit(
            Background(sourceId: 2),
            isArmed: true,
            Epoch());

        UiEnrichmentAdmissionDecision question =
            policy.Admit(
                Question(sourceId: 1),
                isArmed: true,
                Epoch());

        Assert.AreEqual(
            UiEnrichmentAdmissionOutcome.ReplacedPending,
            question.Outcome);
        Assert.AreEqual(
            UiEnrichmentTriggerKind.BackgroundChange,
            question.ReplacedPending?.Kind);
        Assert.AreEqual(
            UiEnrichmentTriggerKind.UserQuestion,
            policy.GetState().Pending?.Kind);
    }

    [TestMethod]
    public void Admit_BackgroundCannotDisplacePendingUserQuestion()
    {
        long now = 0;
        UiEnrichmentOrchestrationPolicy policy =
            CreatePolicy(() => now);

        policy.Admit(
            Background(sourceId: 1),
            isArmed: true,
            Epoch());

        policy.Admit(
            Question(sourceId: 1),
            isArmed: true,
            Epoch());

        now = 500;

        AssertRejected(
            policy.Admit(
                Background(sourceId: 2),
                isArmed: true,
                Epoch()),
            UiEnrichmentRejectionReason.PendingUserQuestionHasPriority);

        Assert.AreEqual(
            1L,
            policy.GetState().Pending?.SourceId);
        Assert.AreEqual(
            UiEnrichmentTriggerKind.UserQuestion,
            policy.GetState().Pending?.Kind);
    }

    [TestMethod]
    public void Admit_UserQuestionBackpressureIsExplicitAndRetryable()
    {
        UiEnrichmentOrchestrationPolicy policy =
            CreatePolicy(() => 0);

        UiEnrichmentAdmissionDecision first =
            policy.Admit(
                Question(sourceId: 1),
                isArmed: true,
                Epoch());

        UiEnrichmentAdmissionDecision second =
            policy.Admit(
                Question(sourceId: 2),
                isArmed: true,
                Epoch());

        AssertRejected(
            policy.Admit(
                Question(sourceId: 3),
                isArmed: true,
                Epoch()),
            UiEnrichmentRejectionReason.UserQuestionBackpressure);

        UiEnrichmentCompletionDecision promote =
            policy.Complete(
                first.Request!.RequestId,
                isArmed: true,
                Epoch());

        Assert.AreEqual(
            UiEnrichmentCompletionOutcome.DispatchNext,
            promote.Outcome);
        Assert.AreEqual(
            second.Request,
            promote.Dispatch);

        policy.Complete(
            second.Request!.RequestId,
            isArmed: true,
            Epoch());

        Assert.AreEqual(
            UiEnrichmentAdmissionOutcome.DispatchNow,
            policy.Admit(
                Question(sourceId: 3),
                isArmed: true,
                Epoch()).Outcome);
    }

    [TestMethod]
    public void Complete_PromotesPendingAndIgnoresStaleCompletion()
    {
        long now = 0;
        UiEnrichmentOrchestrationPolicy policy =
            CreatePolicy(() => now);

        UiEnrichmentAdmissionDecision first =
            policy.Admit(
                Background(sourceId: 1),
                isArmed: true,
                Epoch());

        now = 500;

        UiEnrichmentAdmissionDecision second =
            policy.Admit(
                Background(sourceId: 2),
                isArmed: true,
                Epoch());

        UiEnrichmentCompletionDecision completion =
            policy.Complete(
                first.Request!.RequestId,
                isArmed: true,
                Epoch());

        Assert.AreEqual(
            UiEnrichmentCompletionOutcome.DispatchNext,
            completion.Outcome);
        Assert.AreEqual(
            second.Request,
            completion.Dispatch);

        UiEnrichmentCompletionDecision stale =
            policy.Complete(
                first.Request.RequestId,
                isArmed: true,
                Epoch());

        Assert.AreEqual(
            UiEnrichmentCompletionOutcome.Ignored,
            stale.Outcome);
        Assert.AreEqual(
            UiEnrichmentRejectionReason.StaleCompletion,
            stale.RejectionReason);
        Assert.AreEqual(
            second.Request,
            policy.GetState().Active);
    }

    [TestMethod]
    public void EpochChange_InvalidatesOldWorkAndAdmitsCurrentTrigger()
    {
        long now = 0;
        UiEnrichmentOrchestrationPolicy policy =
            CreatePolicy(() => now);

        policy.Admit(
            Background(
                epochId: 7,
                sourceId: 1),
            isArmed: true,
            Epoch(id: 7));

        now = 500;

        policy.Admit(
            Background(
                epochId: 7,
                sourceId: 2),
            isArmed: true,
            Epoch(id: 7));

        UiEnrichmentAdmissionDecision current =
            policy.Admit(
                Background(
                    epochId: 8,
                    sourceId: 1),
                isArmed: true,
                Epoch(id: 8));

        Assert.AreEqual(
            UiEnrichmentAdmissionOutcome.DispatchNow,
            current.Outcome);
        Assert.AreEqual(2, current.Invalidated.Count);
        Assert.AreEqual(8L, policy.GetState().EpochId);
        Assert.AreEqual(8L, policy.GetState().Active?.EpochId);
        Assert.IsNull(policy.GetState().Pending);
    }

    [TestMethod]
    public void Complete_RevalidatesContextAndInvalidatesPending()
    {
        long now = 0;
        UiEnrichmentOrchestrationPolicy policy =
            CreatePolicy(() => now);

        UiEnrichmentAdmissionDecision first =
            policy.Admit(
                Background(sourceId: 1),
                isArmed: true,
                Epoch());

        now = 500;

        policy.Admit(
            Background(sourceId: 2),
            isArmed: true,
            Epoch());

        UiEnrichmentCompletionDecision completion =
            policy.Complete(
                first.Request!.RequestId,
                isArmed: false,
                Epoch());

        Assert.AreEqual(
            UiEnrichmentCompletionOutcome.Idle,
            completion.Outcome);
        Assert.AreEqual(
            UiEnrichmentRejectionReason.NotArmed,
            completion.RejectionReason);
        Assert.AreEqual(1, completion.Invalidated.Count);
        Assert.AreEqual(0L, policy.GetState().EpochId);
        Assert.IsNull(policy.GetState().Active);
        Assert.IsNull(policy.GetState().Pending);
    }

    [TestMethod]
    public void Invalidate_ReturnsBoundedStateAndPolicyCanBeReused()
    {
        long now = 0;
        UiEnrichmentOrchestrationPolicy policy =
            CreatePolicy(() => now);

        policy.Admit(
            Background(sourceId: 1),
            isArmed: true,
            Epoch());

        now = 500;

        policy.Admit(
            Background(sourceId: 2),
            isArmed: true,
            Epoch());

        UiEnrichmentInvalidation invalidated =
            policy.Invalidate();

        Assert.AreEqual(2, invalidated.Count);
        Assert.AreEqual(0L, policy.GetState().EpochId);
        Assert.IsNull(policy.GetState().Active);
        Assert.IsNull(policy.GetState().Pending);

        Assert.AreEqual(
            UiEnrichmentAdmissionOutcome.DispatchNow,
            policy.Admit(
                Background(sourceId: 1),
                isArmed: true,
                Epoch()).Outcome);
    }

    [TestMethod]
    public void Stop_InvalidatesStateAndRejectsFutureTriggers()
    {
        long now = 0;
        UiEnrichmentOrchestrationPolicy policy =
            CreatePolicy(() => now);

        policy.Admit(
            Background(sourceId: 1),
            isArmed: true,
            Epoch());

        now = 500;

        policy.Admit(
            Background(sourceId: 2),
            isArmed: true,
            Epoch());

        Assert.AreEqual(2, policy.Stop().Count);
        Assert.AreEqual(0, policy.Stop().Count);
        Assert.IsTrue(policy.GetState().IsStopped);

        AssertRejected(
            policy.Admit(
                Question(sourceId: 1),
                isArmed: true,
                Epoch()),
            UiEnrichmentRejectionReason.PolicyStopped);
    }

    private static UiEnrichmentOrchestrationPolicy CreatePolicy(
        Func<long> getTimestamp) =>
        new(
            TimeSpan.FromMilliseconds(500),
            getTimestamp,
            TimestampFrequency);

    private static UiEnrichmentTrigger Background(
        long epochId = 7,
        long sourceId = 1,
        ChangeClassification classification =
            ChangeClassification.Meaningful) =>
        new(
            epochId,
            sourceId,
            UiEnrichmentTriggerKind.BackgroundChange,
            classification);

    private static UiEnrichmentTrigger Question(
        long epochId = 7,
        long sourceId = 1) =>
        new(
            epochId,
            sourceId,
            UiEnrichmentTriggerKind.UserQuestion);

    private static ContextEpoch Epoch(
        long id = 7,
        PrivacyCapability capabilities =
            PrivacyCapability.ReadUiStructure |
            PrivacyCapability.ReadUiText,
        CancellationToken cancellationToken = default) =>
        new(
            id,
            DateTimeOffset.UnixEpoch,
            TestData.Snapshot(),
            TestData.Allowed(
                capabilities: capabilities),
            cancellationToken);

    private static void AssertRejected(
        UiEnrichmentAdmissionDecision decision,
        UiEnrichmentRejectionReason reason)
    {
        Assert.AreEqual(
            UiEnrichmentAdmissionOutcome.Rejected,
            decision.Outcome);
        Assert.AreEqual(
            reason,
            decision.RejectionReason);
        Assert.IsNull(decision.Request);
    }
}
