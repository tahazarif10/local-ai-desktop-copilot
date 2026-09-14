[CmdletBinding()]
param(
    [string]$DiagnosticRoot,
    [ValidateRange(30, 900)]
    [int]$StartupTimeoutSeconds = 300,
    [ValidateRange(5, 120)]
    [int]$StepTimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$corePath = Join-Path $PSScriptRoot "scripts\run-m3-4-acceptance-core.ps1"
if (-not (Test-Path -LiteralPath $corePath)) {
    throw "M3.4 acceptance core runner is missing: $corePath"
}

$source = [System.IO.File]::ReadAllText($corePath)

# The original fixture used a local PowerShell event-handler variable for its
# visual toggle. Event callbacks get a fresh local scope, so the value did not
# persist across ticks. Patch the fixture state onto WinForms Panel.Tag, whose
# value persists across timer callbacks, without changing product/runtime code.
$oldState = '$flip = $false'
$newState = '$panel.Tag = $false'
$oldTick = @'
    $flip = -not $flip
    if ($flip) {
'@
$newTick = @'
    $panel.Tag = -not [bool]$panel.Tag
    if ([bool]$panel.Tag) {
'@

$stateMatches = [Regex]::Matches($source, [Regex]::Escape($oldState)).Count
$tickMatches = [Regex]::Matches($source, [Regex]::Escape($oldTick)).Count
if ($stateMatches -ne 1 -or $tickMatches -ne 1) {
    throw "M3.4 acceptance fixture state patch validation failed. state=$stateMatches tick=$tickMatches"
}

$source = $source.Replace($oldState, $newState)
$source = $source.Replace($oldTick, $newTick)

# Start-Process launches powershell.exe, whose MainWindowHandle is not a safe
# locator for the WinForms fixture hosted inside that process. Resolve the exact
# controlled form by title AND process ID, matching the provider-isolation
# runner's already-accepted PID-scoped approach.
if ($null -eq ("LocalCopilotM34AcceptanceFixtureWindowV2.WindowLookup" -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LocalCopilotM34AcceptanceFixtureWindowV2
{
    public static class WindowLookup
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowTextLengthW(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);

        public static IntPtr FindWindowForProcess(int processId, string exactTitle)
        {
            if (processId <= 0)
                throw new ArgumentOutOfRangeException(nameof(processId));

            if (string.IsNullOrEmpty(exactTitle))
                throw new ArgumentException("A non-empty exact window title is required.", nameof(exactTitle));

            IntPtr found = IntPtr.Zero;

            EnumWindows(
                delegate(IntPtr hWnd, IntPtr lParam)
                {
                    uint ownerPid;
                    GetWindowThreadProcessId(hWnd, out ownerPid);
                    if (ownerPid != (uint)processId)
                        return true;

                    int length = GetWindowTextLengthW(hWnd);
                    if (length <= 0)
                        return true;

                    StringBuilder title = new StringBuilder(length + 1);
                    GetWindowTextW(hWnd, title, title.Capacity);

                    if (string.Equals(title.ToString(), exactTitle, StringComparison.Ordinal))
                    {
                        found = hWnd;
                        return false;
                    }

                    return true;
                },
                IntPtr.Zero);

            return found;
        }
    }
}
'@
}

function Wait-M34FixtureWindow {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)]
        [string]$WindowTitle,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $Process.Refresh()

        if ($Process.HasExited) {
            throw "M3.4 runtime target exited before its real WinForms window became ready."
        }

        $handle = [LocalCopilotM34AcceptanceFixtureWindowV2.WindowLookup]::FindWindowForProcess(
            [int]$Process.Id,
            $WindowTitle)

        if ($handle -ne [IntPtr]::Zero) {
            return $handle
        }

        Start-Sleep -Milliseconds 100
    }

    throw "M3.4 runtime target did not expose its exact PID-scoped WinForms HWND before timeout."
}

$oldWindowLookup = @'
    $handle = Wait-M34ProcessWindow `
        -Process $process `
        -Description "M3.4 runtime target" `
        -TimeoutSeconds $StepTimeoutSeconds
'@

$newWindowLookup = @'
    try {
        $handle = Wait-M34FixtureWindow `
            -Process $process `
            -WindowTitle "LocalCopilot M3.4 runtime target" `
            -TimeoutSeconds $StepTimeoutSeconds
    }
    catch {
        try {
            $process.Refresh()
            if (-not $process.HasExited) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }
        }
        catch {
        }

        throw
    }
'@

$windowLookupMatches = [Regex]::Matches(
    $source,
    [Regex]::Escape($oldWindowLookup)).Count

if ($windowLookupMatches -ne 1) {
    throw "M3.4 acceptance PID-scoped fixture resolver patch validation failed. matches=$windowLookupMatches"
}

$source = $source.Replace($oldWindowLookup, $newWindowLookup)

# Validate the native helper itself without depending on any live fixture.
$impossibleTitle = "LocalCopilot-M3.4-validation-" + [Guid]::NewGuid().ToString("N")
$validationHandle = [LocalCopilotM34AcceptanceFixtureWindowV2.WindowLookup]::FindWindowForProcess(
    [Environment]::ProcessId,
    $impossibleTitle)
if ($validationHandle -ne [IntPtr]::Zero) {
    throw "M3.4 acceptance PID-scoped fixture resolver validation returned an unexpected HWND."
}

Push-Location $PSScriptRoot
try {
    $runner = [ScriptBlock]::Create($source)
    & $runner @PSBoundParameters
}
finally {
    Pop-Location
}
