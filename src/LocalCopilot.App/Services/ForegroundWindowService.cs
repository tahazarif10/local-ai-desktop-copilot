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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(
        nint hWnd,
        out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLengthW(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(
        nint hWnd,
        StringBuilder lpString,
        int nMaxCount);

    public ForegroundWindowSnapshot? GetCurrent()
    {
        nint hwnd = GetForegroundWindow();

        if (hwnd == nint.Zero)
            return null;

        GetWindowThreadProcessId(hwnd, out uint processId);

        string processName = "Unknown";

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            processName = process.ProcessName + ".exe";
        }
        catch
        {
        }

        int titleLength = GetWindowTextLengthW(hwnd);
        string title = string.Empty;

        if (titleLength > 0)
        {
            StringBuilder buffer = new(titleLength + 1);
            GetWindowTextW(hwnd, buffer, buffer.Capacity);
            title = buffer.ToString();
        }

        return new ForegroundWindowSnapshot(
            hwnd,
            processId,
            processName,
            title);
    }
}
