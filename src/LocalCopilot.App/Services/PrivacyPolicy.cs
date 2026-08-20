using LocalCopilot_App.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;

namespace LocalCopilot_App.Services;

public enum PrivacyDisposition
{
    Allowed,
    Blocked
}

public sealed record PrivacyEvaluation(
    PrivacyDisposition Disposition,
    string RuleId,
    string Reason)
{
    public bool AllowsSensing =>
        Disposition == PrivacyDisposition.Allowed;
}

public sealed class PrivacyPolicy
{
    private readonly HashSet<string>
        _blockedProcesses;

    private readonly bool
        _diagnosticNotepadRuleEnabled;

    private PrivacyPolicy(
        HashSet<string> blockedProcesses,
        bool diagnosticNotepadRuleEnabled)
    {
        _blockedProcesses =
            blockedProcesses;

        _diagnosticNotepadRuleEnabled =
            diagnosticNotepadRuleEnabled;
    }

    public static PrivacyPolicy CreateDefault()
    {
        bool diagnosticNotepadRuleEnabled =
            DiagnosticLog.IsEnabled;

        HashSet<string> blockedProcesses =
            new(
                StringComparer.OrdinalIgnoreCase);

        // Deterministic acceptance-test fixture only.
        // Product defaults must not silently blacklist
        // unrelated applications.
        if (diagnosticNotepadRuleEnabled)
        {
            blockedProcesses.Add(
                "notepad.exe");
        }

        DiagnosticLog.Write(
            "PRIVACY.POLICY_READY",
            $"blockedProcessCount={blockedProcesses.Count} " +
            $"diagnosticNotepadRule={diagnosticNotepadRuleEnabled}");

        return new PrivacyPolicy(
            blockedProcesses,
            diagnosticNotepadRuleEnabled);
    }

    public PrivacyEvaluation Evaluate(
        ForegroundWindowIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(
            identity);

        string normalizedProcessName =
            NormalizeProcessName(
                identity.ProcessName);

        DiagnosticLog.Write(
            "PRIVACY.CHECK",
            $"hwnd=0x{identity.Handle.ToInt64():X} " +
            $"pid={identity.ProcessId} " +
            $"process={identity.ProcessName}");

        if (_blockedProcesses.Contains(
                normalizedProcessName))
        {
            string ruleId =
                _diagnosticNotepadRuleEnabled &&
                normalizedProcessName.Equals(
                    "notepad.exe",
                    StringComparison.OrdinalIgnoreCase)
                    ? "diagnostic_notepad"
                    : "process_blocklist";

            PrivacyEvaluation denied =
                new(
                    PrivacyDisposition.Blocked,
                    ruleId,
                    "process_rule");

            DiagnosticLog.Write(
                "PRIVACY.DENY",
                $"hwnd=0x{identity.Handle.ToInt64():X} " +
                $"pid={identity.ProcessId} " +
                $"process={identity.ProcessName} " +
                $"rule={denied.RuleId} " +
                $"reason={denied.Reason}");

            return denied;
        }

        PrivacyEvaluation allowed =
            new(
                PrivacyDisposition.Allowed,
                "default_allow",
                "allowed");

        DiagnosticLog.Write(
            "PRIVACY.ALLOW",
            $"hwnd=0x{identity.Handle.ToInt64():X} " +
            $"pid={identity.ProcessId} " +
            $"process={identity.ProcessName} " +
            $"rule={allowed.RuleId}");

        return allowed;
    }

    private static string NormalizeProcessName(
        string processName)
    {
        if (string.IsNullOrWhiteSpace(
                processName))
        {
            return string.Empty;
        }

        string normalized =
            Path.GetFileName(
                processName.Trim());

        if (!normalized.EndsWith(
                ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            normalized +=
                ".exe";
        }

        return normalized;
    }
}