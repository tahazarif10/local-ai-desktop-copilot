using LocalCopilot_App.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;

namespace LocalCopilot_App.Services;

[Flags]
public enum PrivacyCapability
{
    None = 0,
    ObserveIdentity = 1 << 0,
    ReadWindowTitle = 1 << 1,
    CapturePixels = 1 << 2,
    ReadUiStructure = 1 << 3,
    ReadUiText = 1 << 4,
    RunOcr = 1 << 5,
    RetainDerivedEvent = 1 << 6,
    SendTextToLocalServer = 1 << 7,
    SendPixelsToLocalServer = 1 << 8,
    CaptureMicrophone = 1 << 9,
    SendAudioToLocalServer = 1 << 10,
    All = ObserveIdentity | ReadWindowTitle | CapturePixels |
        ReadUiStructure | ReadUiText | RunOcr | RetainDerivedEvent |
        SendTextToLocalServer | SendPixelsToLocalServer |
        CaptureMicrophone | SendAudioToLocalServer
}

public enum PrivacyDisposition
{
    Allowed,
    Blocked
}

public sealed record PrivacyEvaluation(
    PrivacyCapability GrantedCapabilities,
    string RuleId,
    string Reason,
    long PolicyRevision)
{
    public PrivacyDisposition Disposition =>
        Allows(PrivacyCapability.ObserveIdentity)
            ? PrivacyDisposition.Allowed
            : PrivacyDisposition.Blocked;

    public bool Allows(PrivacyCapability capability)
    {
        if (capability == PrivacyCapability.None ||
            (capability & ~PrivacyCapability.All) != 0)
        {
            return false;
        }

        return (GrantedCapabilities & capability) == capability;
    }
}

public sealed record ApplicationPrivacyRule(
    string ProcessName,
    PrivacyCapability GrantedCapabilities,
    string RuleId);

public sealed record PrivacyPolicyConfiguration(
    bool EmergencyDeny,
    PrivacyCapability GlobalGrants,
    IReadOnlyCollection<ApplicationPrivacyRule> ApplicationRules)
{
    public static PrivacyPolicyConfiguration CreateProductDefault(
        bool diagnosticNotepadRuleEnabled = false)
    {
        List<ApplicationPrivacyRule> rules = new();

        if (diagnosticNotepadRuleEnabled)
        {
            rules.Add(
                new ApplicationPrivacyRule(
                    "notepad.exe",
                    PrivacyCapability.None,
                    "diagnostic_notepad"));
        }

        PrivacyCapability globalGrants =
            PrivacyCapability.ObserveIdentity |
            PrivacyCapability.ReadWindowTitle |
            PrivacyCapability.CapturePixels;

        // Existing metadata-only correlation is a diagnostic feature.
        // Product launches do not silently grant derived retention.
        if (diagnosticNotepadRuleEnabled)
        {
            globalGrants |=
                PrivacyCapability.RetainDerivedEvent;
        }

        return new PrivacyPolicyConfiguration(
            EmergencyDeny: false,
            GlobalGrants: globalGrants,
            ApplicationRules: rules);
    }
}

public sealed record PrivacyPolicyChanged(
    long PreviousRevision,
    long CurrentRevision);

public sealed class PrivacyPolicy
{
    private readonly object _gate = new();
    private PolicySnapshot _snapshot;

    internal PrivacyPolicy(
        PrivacyPolicyConfiguration configuration)
    {
        _snapshot = CreateSnapshot(configuration, revision: 1);
    }

    public event Action<PrivacyPolicyChanged>? Changed;

    public long Revision
    {
        get
        {
            lock (_gate)
            {
                return _snapshot.Revision;
            }
        }
    }

    public static PrivacyPolicy CreateDefault()
    {
        bool diagnosticNotepadRuleEnabled = DiagnosticLog.IsEnabled;
        PrivacyPolicy policy = new(
            PrivacyPolicyConfiguration.CreateProductDefault(
                diagnosticNotepadRuleEnabled));

        DiagnosticLog.Write(
            "PRIVACY.POLICY_READY",
            $"revision={policy.Revision} " +
            $"diagnosticNotepadRule={diagnosticNotepadRuleEnabled}");

        return policy;
    }

    public void ReplaceConfiguration(
        PrivacyPolicyConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        long previousRevision;
        long currentRevision;

        lock (_gate)
        {
            previousRevision = _snapshot.Revision;
            currentRevision = checked(previousRevision + 1);
            _snapshot = CreateSnapshot(configuration, currentRevision);
        }

        DiagnosticLog.Write(
            "PRIVACY.POLICY_CHANGED",
            $"previousRevision={previousRevision} " +
            $"currentRevision={currentRevision}");

        Changed?.Invoke(
            new PrivacyPolicyChanged(previousRevision, currentRevision));
    }

    public PrivacyEvaluation Evaluate(
        ForegroundWindowIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        PolicySnapshot snapshot;
        lock (_gate)
        {
            snapshot = _snapshot;
        }

        string normalizedProcessName =
            NormalizeProcessName(identity.ProcessName);

        DiagnosticLog.Write(
            "PRIVACY.CHECK",
            $"hwnd=0x{identity.Handle.ToInt64():X} " +
            $"pid={identity.ProcessId} process={identity.ProcessName} " +
            $"revision={snapshot.Revision}");

        PrivacyEvaluation evaluation;

        if (snapshot.EmergencyDeny)
        {
            evaluation = new PrivacyEvaluation(
                PrivacyCapability.None,
                "emergency_deny",
                "emergency_deny",
                snapshot.Revision);
        }
        else if (snapshot.ApplicationRules.TryGetValue(
                     normalizedProcessName,
                     out ApplicationPrivacyRule? rule))
        {
            evaluation = new PrivacyEvaluation(
                rule.GrantedCapabilities,
                rule.RuleId,
                rule.GrantedCapabilities == PrivacyCapability.None
                    ? "application_deny"
                    : "application_override",
                snapshot.Revision);
        }
        else
        {
            evaluation = new PrivacyEvaluation(
                snapshot.GlobalGrants,
                "global_grants",
                "global_grants",
                snapshot.Revision);
        }

        DiagnosticLog.Write(
            evaluation.Disposition == PrivacyDisposition.Blocked
                ? "PRIVACY.DENY"
                : "PRIVACY.ALLOW",
            $"hwnd=0x{identity.Handle.ToInt64():X} " +
            $"pid={identity.ProcessId} process={identity.ProcessName} " +
            $"rule={evaluation.RuleId} reason={evaluation.Reason} " +
            $"capabilities={(int)evaluation.GrantedCapabilities} " +
            $"revision={evaluation.PolicyRevision}");

        return evaluation;
    }

    private static PolicySnapshot CreateSnapshot(
        PrivacyPolicyConfiguration configuration,
        long revision)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if ((configuration.GlobalGrants & ~PrivacyCapability.All) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configuration),
                "Global grants contain an unknown capability.");
        }

        ArgumentNullException.ThrowIfNull(configuration.ApplicationRules);

        Dictionary<string, ApplicationPrivacyRule> rules =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (ApplicationPrivacyRule rule in configuration.ApplicationRules)
        {
            ArgumentNullException.ThrowIfNull(rule);

            string processName = NormalizeProcessName(rule.ProcessName);
            if (string.IsNullOrWhiteSpace(processName))
            {
                throw new ArgumentException(
                    "An application rule requires a process name.",
                    nameof(configuration));
            }

            if (string.IsNullOrWhiteSpace(rule.RuleId))
            {
                throw new ArgumentException(
                    "An application rule requires a rule ID.",
                    nameof(configuration));
            }

            if ((rule.GrantedCapabilities & ~PrivacyCapability.All) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(configuration),
                    "An application rule contains an unknown capability.");
            }

            ApplicationPrivacyRule normalizedRule = rule with
            {
                ProcessName = processName,
                RuleId = rule.RuleId.Trim()
            };

            if (!rules.TryAdd(processName, normalizedRule))
            {
                throw new ArgumentException(
                    $"Duplicate application rule for '{processName}'.",
                    nameof(configuration));
            }
        }

        return new PolicySnapshot(
            configuration.EmergencyDeny,
            configuration.GlobalGrants,
            rules,
            revision);
    }

    private static string NormalizeProcessName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        string normalized = Path.GetFileName(processName.Trim());
        if (!normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized += ".exe";
        }

        return normalized;
    }

    private sealed record PolicySnapshot(
        bool EmergencyDeny,
        PrivacyCapability GlobalGrants,
        IReadOnlyDictionary<string, ApplicationPrivacyRule> ApplicationRules,
        long Revision);
}
