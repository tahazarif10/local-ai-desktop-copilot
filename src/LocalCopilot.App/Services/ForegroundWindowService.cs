using LocalCopilot_App.Diagnostics;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LocalCopilot_App.Services;

public sealed record ForegroundWindowSnapshot(
    nint Handle,
    uint ProcessId,
    string ProcessName,
    string WindowTitle);

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

    public ForegroundWindowSnapshot? GetCurrent(
        uint? excludedProcessId = null)
    {
        nint hwnd =
            GetForegroundWindow();

        DiagnosticLog.Write(
            "SERVICE.GET_CURRENT",
            $"foreground=0x{hwnd.ToInt64():X}");

        return GetFromHandle(
            hwnd,
            excludedProcessId);
    }

    public ForegroundWindowSnapshot? GetFromHandle(
        nint hwnd,
        uint? excludedProcessId = null)
    {
        DiagnosticLog.Write(
            "SERVICE.BEGIN",
            $"hwnd=0x{hwnd.ToInt64():X} " +
            $"excludedPid={excludedProcessId}");

        if (hwnd == nint.Zero)
        {
            DiagnosticLog.Write(
                "SERVICE.REJECT",
                "HWND is zero.");

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

        if (processId == 0)
        {
            DiagnosticLog.Write(
                "SERVICE.REJECT",
                $"PID=0 error={Marshal.GetLastWin32Error()}");

            return null;
        }

        if (excludedProcessId.HasValue &&
            processId == excludedProcessId.Value)
        {
            DiagnosticLog.Write(
                "SERVICE.REJECT",
                $"Own process pid={processId}");

            return null;
        }

        string processName =
            "Unknown";

        try
        {
            using Process process =
                Process.GetProcessById(
                    (int)processId);

            processName =
                process.ProcessName + ".exe";
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                "SERVICE.PROCESS_ERROR",
                $"type={ex.GetType().Name}");
        }

        int titleLength =
            GetWindowTextLengthW(
                hwnd);

        string title =
            string.Empty;

        int copied =
            0;

        if (titleLength > 0)
        {
            StringBuilder buffer =
                new(titleLength + 1);

            copied =
                GetWindowTextW(
                    hwnd,
                    buffer,
                    buffer.Capacity);

            title =
                buffer.ToString();
        }

        DiagnosticLog.Write(
            "SERVICE.TITLE",
            $"hwnd=0x{hwnd.ToInt64():X} " +
            $"titleLength={titleLength} " +
            $"copied={copied} " +
            $"hasTitle={!string.IsNullOrWhiteSpace(title)}");

        DiagnosticLog.Write(
            "SERVICE.RESULT",
            $"hwnd=0x{hwnd.ToInt64():X} " +
            $"pid={processId} " +
            $"process={processName} " +
            $"titleLength={title.Length}");

        if (processName.Equals(
                "explorer.exe",
                StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(title))
        {
            DiagnosticLog.Write(
                "SERVICE.REJECT",
                "Transient Explorer shell window.");

            return null;
        }

        return new ForegroundWindowSnapshot(
            hwnd,
            processId,
            processName,
            title);
    }
}