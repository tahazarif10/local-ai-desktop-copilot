using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class PrivacyPolicyTests
{
    [TestMethod]
    public void ProductDefault_GrantsOnlyEphemeralScreenCapabilities()
    {
        PrivacyPolicy policy = new(
            PrivacyPolicyConfiguration.CreateProductDefault());

        PrivacyEvaluation result = Evaluate(policy, "editor");

        Assert.IsTrue(result.Allows(PrivacyCapability.ObserveIdentity));
        Assert.IsTrue(result.Allows(PrivacyCapability.ReadWindowTitle));
        Assert.IsTrue(result.Allows(PrivacyCapability.CapturePixels));
        Assert.IsFalse(result.Allows(PrivacyCapability.RetainDerivedEvent));
        Assert.IsFalse(result.Allows(PrivacyCapability.ReadUiStructure));
        Assert.IsFalse(result.Allows(PrivacyCapability.ReadUiText));
        Assert.IsFalse(result.Allows(PrivacyCapability.RunOcr));
        Assert.IsFalse(result.Allows(PrivacyCapability.CaptureMicrophone));
        Assert.IsFalse(result.Allows(PrivacyCapability.SendTextToLocalServer));
        Assert.IsFalse(result.Allows(PrivacyCapability.SendPixelsToLocalServer));
        Assert.IsFalse(result.Allows(PrivacyCapability.SendAudioToLocalServer));
    }

    [TestMethod]
    public void DiagnosticDefault_SeparatelyGrantsDerivedRetention()
    {
        PrivacyPolicy policy = new(
            PrivacyPolicyConfiguration.CreateProductDefault(
                diagnosticNotepadRuleEnabled: true));

        PrivacyEvaluation result = Evaluate(policy, "editor");

        Assert.IsTrue(result.Allows(PrivacyCapability.RetainDerivedEvent));
    }

    [TestMethod]
    public void Evaluate_DiagnosticNotepadRule_DeniesAllCapabilities()
    {
        PrivacyPolicy policy = new(
            PrivacyPolicyConfiguration.CreateProductDefault(
                diagnosticNotepadRuleEnabled: true));

        PrivacyEvaluation result = Evaluate(policy, "NoTePaD");

        Assert.AreEqual(PrivacyDisposition.Blocked, result.Disposition);
        Assert.AreEqual(PrivacyCapability.None, result.GrantedCapabilities);
        Assert.AreEqual("diagnostic_notepad", result.RuleId);
        Assert.AreEqual("application_deny", result.Reason);
        Assert.IsFalse(result.Allows(PrivacyCapability.ObserveIdentity));
        Assert.IsFalse(result.Allows(PrivacyCapability.ReadWindowTitle));
        Assert.IsFalse(result.Allows(PrivacyCapability.CapturePixels));
    }

    [TestMethod]
    public void Evaluate_GlobalGrant_DoesNotImplicitlyGrantOtherCapabilities()
    {
        PrivacyPolicy policy = CreatePolicy(
            globalGrants:
                PrivacyCapability.ObserveIdentity |
                PrivacyCapability.CapturePixels);

        PrivacyEvaluation result = Evaluate(policy, "editor");

        Assert.IsTrue(result.Allows(PrivacyCapability.ObserveIdentity));
        Assert.IsTrue(result.Allows(PrivacyCapability.CapturePixels));
        Assert.IsFalse(result.Allows(PrivacyCapability.ReadWindowTitle));
        Assert.IsFalse(result.Allows(PrivacyCapability.RunOcr));
        Assert.IsFalse(result.Allows(PrivacyCapability.RetainDerivedEvent));
        Assert.IsFalse(result.Allows(PrivacyCapability.SendPixelsToLocalServer));
    }

    [TestMethod]
    public void Evaluate_ApplicationOverride_ReplacesGlobalGrants()
    {
        PrivacyPolicy policy = CreatePolicy(
            globalGrants: PrivacyCapability.All,
            new ApplicationPrivacyRule(
                "browser.exe",
                PrivacyCapability.ObserveIdentity |
                PrivacyCapability.ReadWindowTitle,
                "browser_metadata_only"));

        PrivacyEvaluation result = Evaluate(policy, " BROWSER ");

        Assert.AreEqual("browser_metadata_only", result.RuleId);
        Assert.AreEqual("application_override", result.Reason);
        Assert.IsTrue(result.Allows(PrivacyCapability.ReadWindowTitle));
        Assert.IsFalse(result.Allows(PrivacyCapability.CapturePixels));
        Assert.IsFalse(result.Allows(PrivacyCapability.ReadUiText));
    }

    [TestMethod]
    public void Evaluate_ExactApplicationDeny_PrecedesGlobalGrants()
    {
        PrivacyPolicy policy = CreatePolicy(
            globalGrants: PrivacyCapability.All,
            new ApplicationPrivacyRule(
                "secret-editor",
                PrivacyCapability.None,
                "secret_editor_deny"));

        PrivacyEvaluation result = Evaluate(policy, "secret-editor.exe");

        Assert.AreEqual(PrivacyDisposition.Blocked, result.Disposition);
        Assert.AreEqual("secret_editor_deny", result.RuleId);
    }

    [TestMethod]
    public void Evaluate_EmergencyDeny_PrecedesApplicationOverride()
    {
        PrivacyPolicy policy = new(
            new PrivacyPolicyConfiguration(
                EmergencyDeny: true,
                GlobalGrants: PrivacyCapability.All,
                ApplicationRules:
                [
                    new ApplicationPrivacyRule(
                        "editor.exe",
                        PrivacyCapability.All,
                        "editor_allow")
                ]));

        PrivacyEvaluation result = Evaluate(policy, "editor");

        Assert.AreEqual(PrivacyDisposition.Blocked, result.Disposition);
        Assert.AreEqual("emergency_deny", result.RuleId);
        Assert.AreEqual(PrivacyCapability.None, result.GrantedCapabilities);
    }

    [TestMethod]
    public void ReplaceConfiguration_IncrementsRevisionAndRaisesChangedOnce()
    {
        PrivacyPolicy policy = CreatePolicy(
            PrivacyCapability.ObserveIdentity);
        PrivacyPolicyChanged? observed = null;
        int changeCount = 0;

        policy.Changed += change =>
        {
            observed = change;
            changeCount++;
        };

        policy.ReplaceConfiguration(
            new PrivacyPolicyConfiguration(
                EmergencyDeny: false,
                GlobalGrants:
                    PrivacyCapability.ObserveIdentity |
                    PrivacyCapability.CapturePixels,
                ApplicationRules: []));

        PrivacyEvaluation result = Evaluate(policy, "editor");

        Assert.AreEqual(1, changeCount);
        Assert.IsNotNull(observed);
        Assert.AreEqual(1L, observed.PreviousRevision);
        Assert.AreEqual(2L, observed.CurrentRevision);
        Assert.AreEqual(2L, policy.Revision);
        Assert.AreEqual(2L, result.PolicyRevision);
        Assert.IsTrue(result.Allows(PrivacyCapability.CapturePixels));
    }

    [TestMethod]
    public void ReplaceConfiguration_InvalidConfiguration_DoesNotChangeRevision()
    {
        PrivacyPolicy policy = CreatePolicy(
            PrivacyCapability.ObserveIdentity);

        Assert.ThrowsExactly<ArgumentException>(
            () => policy.ReplaceConfiguration(
                new PrivacyPolicyConfiguration(
                    EmergencyDeny: false,
                    GlobalGrants: PrivacyCapability.ObserveIdentity,
                    ApplicationRules:
                    [
                        new ApplicationPrivacyRule(
                            "editor",
                            PrivacyCapability.None,
                            "one"),
                        new ApplicationPrivacyRule(
                            "EDITOR.exe",
                            PrivacyCapability.None,
                            "two")
                    ])));

        Assert.AreEqual(1L, policy.Revision);
    }

    [TestMethod]
    public void Allows_CombinedCapabilities_RequiresEveryCapability()
    {
        PrivacyEvaluation evaluation = new(
            PrivacyCapability.ObserveIdentity |
            PrivacyCapability.CapturePixels,
            "test",
            "test",
            1);

        Assert.IsTrue(evaluation.Allows(
            PrivacyCapability.ObserveIdentity |
            PrivacyCapability.CapturePixels));
        Assert.IsFalse(evaluation.Allows(
            PrivacyCapability.CapturePixels |
            PrivacyCapability.RunOcr));
        Assert.IsFalse(evaluation.Allows(PrivacyCapability.None));
    }

    [TestMethod]
    public void Evaluate_NullIdentity_Throws()
    {
        PrivacyPolicy policy = CreatePolicy(PrivacyCapability.None);

        Assert.ThrowsExactly<ArgumentNullException>(
            () => policy.Evaluate(null!));
    }

    [TestMethod]
    public void Constructor_NullConfiguration_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new PrivacyPolicy(null!));
    }

    private static PrivacyEvaluation Evaluate(
        PrivacyPolicy policy,
        string processName) =>
        policy.Evaluate(
            new ForegroundWindowIdentity(
                (nint)0x1234,
                42,
                processName));

    private static PrivacyPolicy CreatePolicy(
        PrivacyCapability globalGrants,
        params ApplicationPrivacyRule[] rules) =>
        new(
            new PrivacyPolicyConfiguration(
                EmergencyDeny: false,
                GlobalGrants: globalGrants,
                ApplicationRules: rules));
}
