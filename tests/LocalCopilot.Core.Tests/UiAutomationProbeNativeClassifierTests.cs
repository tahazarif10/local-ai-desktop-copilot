using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class UiAutomationProbeNativeClassifierTests
{
    [TestMethod]
    public void Classify_SuccessfulResolvedElement_IsAvailable()
    {
        UiAutomationProbeClassification result =
            UiAutomationProbeNativeClassifier.Classify(
                hresult: 0,
                elementResolved: true,
                deadlineExpired: false);

        Assert.AreEqual(UiAutomationProbeOutcome.Available, result.Outcome);
        Assert.AreEqual(UiAutomationProbeReason.RootResolved, result.Reason);
        Assert.IsNull(result.HResult);
    }

    [TestMethod]
    public void Classify_SuccessWithoutElement_IsUnavailable()
    {
        UiAutomationProbeClassification result =
            UiAutomationProbeNativeClassifier.Classify(
                hresult: 0,
                elementResolved: false,
                deadlineExpired: false);

        Assert.AreEqual(UiAutomationProbeOutcome.Unavailable, result.Outcome);
        Assert.AreEqual(UiAutomationProbeReason.ElementUnavailable, result.Reason);
        Assert.AreEqual(0, result.HResult);
    }

    [TestMethod]
    [DataRow(unchecked((int)0x80040201))]
    [DataRow(unchecked((int)0x80040204))]
    [DataRow(unchecked((int)0x80070005))]
    [DataRow(unchecked((int)0x80070057))]
    [DataRow(unchecked((int)0x80070578))]
    public void Classify_KnownUnavailableHResult_IsUnavailable(
        int hresult)
    {
        UiAutomationProbeClassification result =
            UiAutomationProbeNativeClassifier.Classify(
                hresult,
                elementResolved: false,
                deadlineExpired: false);

        Assert.AreEqual(UiAutomationProbeOutcome.Unavailable, result.Outcome);
        Assert.AreEqual(UiAutomationProbeReason.ElementUnavailable, result.Reason);
        Assert.AreEqual(hresult, result.HResult);
    }

    [TestMethod]
    [DataRow(unchecked((int)0x80131505))]
    [DataRow(unchecked((int)0x800705B4))]
    [DataRow(unchecked((int)0x8001011F))]
    public void Classify_KnownTimeoutHResult_IsTimeout(
        int hresult)
    {
        UiAutomationProbeClassification result =
            UiAutomationProbeNativeClassifier.Classify(
                hresult,
                elementResolved: false,
                deadlineExpired: false);

        Assert.AreEqual(UiAutomationProbeOutcome.Timeout, result.Outcome);
        Assert.AreEqual(UiAutomationProbeReason.DeadlineExpired, result.Reason);
        Assert.AreEqual(hresult, result.HResult);
    }

    [TestMethod]
    public void Classify_ElapsedDeadline_WinsOverNativeSuccess()
    {
        UiAutomationProbeClassification result =
            UiAutomationProbeNativeClassifier.Classify(
                hresult: 0,
                elementResolved: true,
                deadlineExpired: true);

        Assert.AreEqual(UiAutomationProbeOutcome.Timeout, result.Outcome);
        Assert.AreEqual(UiAutomationProbeReason.DeadlineExpired, result.Reason);
    }

    [TestMethod]
    public void Classify_UnexpectedFailure_IsFaulted()
    {
        const int failure = unchecked((int)0x80004005);

        UiAutomationProbeClassification result =
            UiAutomationProbeNativeClassifier.Classify(
                failure,
                elementResolved: false,
                deadlineExpired: false);

        Assert.AreEqual(UiAutomationProbeOutcome.Faulted, result.Outcome);
        Assert.AreEqual(UiAutomationProbeReason.NativeFailure, result.Reason);
        Assert.AreEqual(failure, result.HResult);
    }
}
