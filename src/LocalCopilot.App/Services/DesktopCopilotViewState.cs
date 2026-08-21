namespace LocalCopilot_App.Services;

public sealed record DesktopCopilotViewState(
    string ProcessName,
    string WindowTitle,
    string ProcessId,
    string WindowHandle,
    string LastObserved,
    string CaptureTargetStatus,
    string CapturedFrameStatus,
    string ChangeDetectionStatus,
    string PersistentChangeStatus,
    string OrchestratorStatus)
{
    public static DesktopCopilotViewState Initial { get; } =
        new(
            "Waiting...",
            "Waiting...",
            "-",
            "-",
            "-",
            "Not tested",
            "Not captured",
            "No change samples yet",
            "Stopped",
            "OFF");
}

public interface IDesktopCopilotView
{
    void Render(
        DesktopCopilotViewState state);
}
