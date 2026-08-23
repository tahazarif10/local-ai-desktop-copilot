using System;

namespace LocalCopilot_App.Services;

public enum UiAutomationProbeOutcome
{
    Available,
    Unavailable,
    Timeout,
    Cancelled,
    Stale,
    Faulted
}

public enum UiAutomationProbeReason
{
    RootResolved,
    NoCurrentEpoch,
    CapabilityDenied,
    IdentityChanged,
    HigherIntegrity,
    AccessInspectionFailed,
    ElementUnavailable,
    DeadlineExpired,
    RequestCancelled,
    Superseded,
    WorkerStopped,
    WorkerInitializationFailed,
    NativeFailure,
    PublicationRejected
}

public sealed record UiAutomationProbeResult(
    long RequestId,
    long EpochId,
    UiAutomationProbeOutcome Outcome,
    UiAutomationProbeReason Reason,
    TimeSpan Elapsed,
    int? HResult,
    int WorkerThreadId,
    bool IdentityRevalidated);

public sealed record UiAutomationProbeClassification(
    UiAutomationProbeOutcome Outcome,
    UiAutomationProbeReason Reason,
    int? HResult);

public static class UiAutomationProbeNativeClassifier
{
    private const int UiAutomationElementNotAvailable =
        unchecked((int)0x80040201);

    private const int UiAutomationNotSupported =
        unchecked((int)0x80040204);

    private const int UiAutomationTimeout =
        unchecked((int)0x80131505);

    private const int AccessDenied =
        unchecked((int)0x80070005);

    private const int InvalidArgument =
        unchecked((int)0x80070057);

    private const int InvalidWindowHandle =
        unchecked((int)0x80070578);

    private const int WindowsTimeout =
        unchecked((int)0x800705B4);

    private const int RpcTimeout =
        unchecked((int)0x8001011F);

    public static UiAutomationProbeClassification Classify(
        int hresult,
        bool elementResolved,
        bool deadlineExpired)
    {
        if (deadlineExpired ||
            IsTimeout(hresult))
        {
            return new UiAutomationProbeClassification(
                UiAutomationProbeOutcome.Timeout,
                UiAutomationProbeReason.DeadlineExpired,
                hresult);
        }

        if (hresult >= 0 &&
            elementResolved)
        {
            return new UiAutomationProbeClassification(
                UiAutomationProbeOutcome.Available,
                UiAutomationProbeReason.RootResolved,
                HResult: null);
        }

        if (hresult >= 0 ||
            IsUnavailable(hresult))
        {
            return new UiAutomationProbeClassification(
                UiAutomationProbeOutcome.Unavailable,
                UiAutomationProbeReason.ElementUnavailable,
                hresult);
        }

        return new UiAutomationProbeClassification(
            UiAutomationProbeOutcome.Faulted,
            UiAutomationProbeReason.NativeFailure,
            hresult);
    }

    private static bool IsTimeout(
        int hresult)
    {
        return hresult == UiAutomationTimeout ||
            hresult == WindowsTimeout ||
            hresult == RpcTimeout;
    }

    private static bool IsUnavailable(
        int hresult)
    {
        return hresult == UiAutomationElementNotAvailable ||
            hresult == UiAutomationNotSupported ||
            hresult == AccessDenied ||
            hresult == InvalidArgument ||
            hresult == InvalidWindowHandle;
    }
}

public static class UiAutomationProbePublicationGate
{
    public static UiAutomationProbeResult Apply(
        UiAutomationProbeResult result,
        ContextEpoch? currentEpoch,
        long latestRequestId)
    {
        ArgumentNullException.ThrowIfNull(result);

        bool mayPublish =
            currentEpoch is not null &&
            result.RequestId == latestRequestId &&
            currentEpoch.Id == result.EpochId &&
            !currentEpoch.CancellationToken.IsCancellationRequested &&
            currentEpoch.Privacy.Allows(
                PrivacyCapability.ReadUiStructure);

        return mayPublish
            ? result
            : result with
            {
                Outcome = UiAutomationProbeOutcome.Stale,
                Reason = UiAutomationProbeReason.PublicationRejected
            };
    }
}
