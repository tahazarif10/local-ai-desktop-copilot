using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class PrivacyPolicyTests
{
    [TestMethod]
    public void Evaluate_DiagnosticNotepadRule_BlocksCaseInsensitively()
    {
        PrivacyPolicy policy = CreatePolicy(
            diagnosticNotepadRuleEnabled: true,
            "notepad.exe");

        PrivacyEvaluation result = policy.Evaluate(
            new ForegroundWindowIdentity(
                (nint)0x1234,
                42,
                "NoTePaD"));

        Assert.AreEqual(
            PrivacyDisposition.Blocked,
            result.Disposition);
        Assert.AreEqual(
            "diagnostic_notepad",
            result.RuleId);
        Assert.AreEqual(
            "process_rule",
            result.Reason);
        Assert.IsFalse(
            result.AllowsSensing);
    }

    [TestMethod]
    public void Evaluate_NonDiagnosticBlockedProcess_UsesBlocklistRule()
    {
        PrivacyPolicy policy = CreatePolicy(
            diagnosticNotepadRuleEnabled: false,
            "secret-editor.exe");

        PrivacyEvaluation result = policy.Evaluate(
            new ForegroundWindowIdentity(
                (nint)0x1234,
                42,
                " secret-editor "));

        Assert.AreEqual(
            PrivacyDisposition.Blocked,
            result.Disposition);
        Assert.AreEqual(
            "process_blocklist",
            result.RuleId);
        Assert.AreEqual(
            "process_rule",
            result.Reason);
    }

    [TestMethod]
    public void Evaluate_UnknownProcess_IsAllowedByCurrentDefault()
    {
        PrivacyPolicy policy = CreatePolicy(
            diagnosticNotepadRuleEnabled: false,
            "blocked.exe");

        PrivacyEvaluation result = policy.Evaluate(
            new ForegroundWindowIdentity(
                (nint)0x1234,
                42,
                "calculator"));

        Assert.AreEqual(
            PrivacyDisposition.Allowed,
            result.Disposition);
        Assert.AreEqual(
            "default_allow",
            result.RuleId);
        Assert.AreEqual(
            "allowed",
            result.Reason);
        Assert.IsTrue(
            result.AllowsSensing);
    }

    [TestMethod]
    public void Evaluate_NullIdentity_Throws()
    {
        PrivacyPolicy policy = CreatePolicy(
            diagnosticNotepadRuleEnabled: false);

        Assert.ThrowsExactly<ArgumentNullException>(
            () => policy.Evaluate(null!));
    }

    [TestMethod]
    public void Constructor_NullBlocklist_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new PrivacyPolicy(
                null!,
                diagnosticNotepadRuleEnabled: false));
    }

    private static PrivacyPolicy CreatePolicy(
        bool diagnosticNotepadRuleEnabled,
        params string[] blockedProcesses) =>
        new(
            new HashSet<string>(
                blockedProcesses,
                StringComparer.OrdinalIgnoreCase),
            diagnosticNotepadRuleEnabled);
}
