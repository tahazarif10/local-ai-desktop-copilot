using LocalCopilot_App.Diagnostics;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LocalCopilot_App.Services;

public sealed class ForegroundWindowService
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(
        nint hWnd,
        out uint processId);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern int GetWindowTextLengthW(
        nint hWnd);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern int GetWindowTextW(
        nint hWnd,
        StringBuilder lpString,
        int nMaxCount);

    public ForegroundWindowObservation? GetCurrent(
        PrivacyPolicy privacyPolicy,
        uint? excludedProcessId = null)
    {
        ArgumentNullException.ThrowIfNull(
            privacyPolicy);

        nint hwnd =
            GetForegroundWindow();

        DiagnosticLog.Write(
            "SERVICE.GET_CURRENT",
            $"foreground=0x{hwnd.ToInt64():X}");

        return GetFromHandle(
            hwnd,
            privacyPolicy,
            excludedProcessId);
    }

    public ForegroundWindowObservation? GetFromHandle(
        nint hwnd,
        PrivacyPolicy privacyPolicy,
        uint? excludedProcessId = null)
    {
        ArgumentNullException.ThrowIfNull(
            privacyPolicy);

        DiagnosticLog.Write(
            "SERVICE.BEGIN",
            $"hwnd=0x{hwnd.ToInt64():X} " +
            $"excludedPid={excludedProcessId}");

        if (hwnd == nint.Zero)
        {
            DiagnosticLog.Write(
                "SERVICE.REJECT",
                "reason=hwnd_zero");

            return null;
        }

        uint windowThreadId =
            GetWindowThreadProcessId(
                hwnd,
                out uint processId);

        DiagnosticLog.Write(
            "SERVICE.PID",
            $"hwnd=0x{hwnd.ToInt64():X} " +
            $"windowThread={windowThreadId} " +
            $"pid={processId}");

        if (windowThreadId == 0 ||
            processId == 0)
        {
            DiagnosticLog.Write(
                "SERVICE.REJECT",
                $"reason=invalid_window_identity " +
                $"error={Marshal.GetLastWin32Error()}");

            return null;
        }

        if (excludedProcessId.HasValue &&
            processId == excludedProcessId.Value)
        {
            DiagnosticLog.Write(
                "SERVICE.REJECT",
                $"reason=own_process pid={processId}");

            return null;
        }

        string processName;

        try
        {
            using Process process =
                Process.GetProcessById(
                    checked((int)processId));

            processName =
                process.ProcessName;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                "SERVICE.PROCESS_ERROR",
                $"pid={processId} " +
                $"type={ex.GetType().Name}");

            DiagnosticLog.Write(
                "SERVICE.REJECT",
                "reason=process_identity_unavailable");

            return null;
        }

        if (string.IsNullOrWhiteSpace(
                processName))
        {
            DiagnosticLog.Write(
                "SERVICE.REJECT",
                "reason=empty_process_name");

            return null;
        }

        ForegroundWindowIdentity identity =
            new(
                hwnd,
                processId,
                processName);

        DiagnosticLog.Write(
            "SERVICE.IDENTITY",
            $"hwnd=0x{identity.Handle.ToInt64():X} " +
            $"pid={identity.ProcessId} " +
            $"process={identity.ProcessName}");

        PrivacyEvaluation privacy =
            privacyPolicy.Evaluate(
                identity);

        if (!privacy.AllowsSensing)
        {
            DiagnosticLog.Write(
                "SERVICE.PRIVACY_DENY",
                $"hwnd=0x{identity.Handle.ToInt64():X} " +
                $"pid={identity.ProcessId} " +
                $"process={identity.ProcessName} " +
                $"rule={privacy.RuleId}");

            ForegroundWindowSnapshot blockedSnapshot =
                new(
                    identity.Handle,
                    identity.ProcessId,
                    identity.ProcessName,
                    string.Empty);

            return new ForegroundWindowObservation(
                blockedSnapshot,
                privacy);
        }

        // Revalidate the HWND/PID immediately before
        // any content-bearing title API is called.
        uint confirmedThreadId =
            GetWindowThreadProcessId(
                identity.Handle,
                out uint confirmedProcessId);

        if (confirmedThreadId == 0 ||
            confirmedProcessId !=
                identity.ProcessId)
        {
            DiagnosticLog.Write(
                "SERVICE.REJECT",
                $"reason=identity_changed_before_title " +
                $"expectedPid={identity.ProcessId} " +
                $"actualPid={confirmedProcessId}");

            return null;
        }

        int titleLength =
            GetWindowTextLengthW(
                identity.Handle);

        string title =
            string.Empty;

        int copied =
            0;

        if (titleLength > 0)
        {
            StringBuilder buffer =
                new(
                    titleLength + 1);

            copied =
                GetWindowTextW(
                    identity.Handle,
                    buffer,
                    buffer.Capacity);

            title =
                buffer.ToString();
        }

        DiagnosticLog.Write(
            "SERVICE.TITLE",
            $"hwnd=0x{identity.Handle.ToInt64():X} " +
            $"titleLength={titleLength} " +
            $"copied={copied} " +
            $"hasTitle={!string.IsNullOrWhiteSpace(title)}");

        DiagnosticLog.Write(
            "SERVICE.RESULT",
            $"hwnd=0x{identity.Handle.ToInt64():X} " +
            $"pid={identity.ProcessId} " +
            $"process={identity.ProcessName} " +
            $"titleLength={title.Length}");

        if (identity.ProcessName.Equals(
                "explorer",
                StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(
                title))
        {
            DiagnosticLog.Write(
                "SERVICE.REJECT",
                "reason=transient_explorer_shell");

            return null;
        }

        ForegroundWindowSnapshot snapshot =
            new(
                identity.Handle,
                identity.ProcessId,
                identity.ProcessName,
                title);

        return new ForegroundWindowObservation(
            snapshot,
            privacy);
    }
}
