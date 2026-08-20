using LocalCopilot_App.Diagnostics;
using System;
using System.Collections.Generic;

namespace LocalCopilot_App.Services;

public enum PrivacyDisposition
{
    Allowed,
    Blocked
}

public sealed record PrivacyEvaluation(
    PrivacyDisposition Disposition,
    string Reason)
{
    public bool AllowsSensing =>
        Disposition == PrivacyDisposition.Allowed;
}

public sealed class PrivacyPolicy
{
    private readonly HashSet<string>
        _blockedProcesses;

    private PrivacyPolicy(
        HashSet<string> blockedProcesses)
    {
        _blockedProcesses =
            blockedProcesses;
    }

    public static PrivacyPolicy CreateDefault()
    {
        HashSet<string> blockedProcesses =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "1password.exe",
                "bitwarden.exe",
                "keepass.exe",
                "keepassxc.exe"
            };

        // Deterministic privacy test:
        // Notepad is blocked only during diagnostic runs.
        if (DiagnosticLog.IsEnabled)
        {
            blockedProcesses.Add(
                "notepad.exe");
        }

        DiagnosticLog.Write(
            "PRIVACY.POLICY_READY",
            $"blockedProcessCount={blockedProcesses.Count} " +
            $"diagnosticNotepadRule={DiagnosticLog.IsEnabled}");

        return new PrivacyPolicy(
            blockedProcesses);
    }

    public PrivacyEvaluation Evaluate(
        ForegroundWindowSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(
            snapshot);

        if (_blockedProcesses.Contains(
                snapshot.ProcessName))
        {
            return new PrivacyEvaluation(
                PrivacyDisposition.Blocked,
                "process_rule");
        }

        return new PrivacyEvaluation(
            PrivacyDisposition.Allowed,
            "allowed");
    }
}