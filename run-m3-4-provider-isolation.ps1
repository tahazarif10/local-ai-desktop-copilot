[CmdletBinding()]
param(
    [string]$DiagnosticRoot,
    [ValidateRange(30, 900)]
    [int]$StartupTimeoutSeconds = 300,
    [ValidateRange(5, 120)]
    [int]$StepTimeoutSeconds = 30,
    [ValidateRange(11, 60)]
    [int]$ProviderObservationSeconds = 12,
    [string]$ExpectedBranch = "dev/m3-4-2-provider-isolation",
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    $repoRoot = (Get-Location).Path
}

$repoRoot = [System.IO.Path]::GetFullPath($repoRoot)
$corePath =
    Join-Path `
        $repoRoot `
        "scripts\run-m3-4-provider-isolation-core.ps1"

if (-not (Test-Path -LiteralPath $corePath)) {
    throw "The M3.4 provider-isolation core runner was not found."
}

if ($null -eq ("LocalCopilotM34Wrapper.WindowLookup" -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LocalCopilotM34Wrapper
{
    public static class WindowLookup
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(
            EnumWindowsProc lpEnumFunc,
            IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(
            IntPtr hWnd,
            out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(
            IntPtr hWnd,
            StringBuilder lpString,
            int nMaxCount);

        public static IntPtr FindWindowForProcess(
            uint processId,
            string windowTitle)
        {
            IntPtr found = IntPtr.Zero;

            EnumWindows(
                delegate(IntPtr hWnd, IntPtr lParam)
                {
                    uint windowProcessId;
                    GetWindowThreadProcessId(hWnd, out windowProcessId);

                    if (windowProcessId != processId)
                    {
                        return true;
                    }

                    int length = GetWindowTextLength(hWnd);
                    StringBuilder title = new StringBuilder(length + 1);
                    GetWindowText(hWnd, title, title.Capacity);

                    if (String.Equals(
                            title.ToString(),
                            windowTitle,
                            StringComparison.Ordinal))
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

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $Process.Refresh()

        if ($Process.HasExited) {
            throw "$WindowTitle exited before publishing its fixture window."
        }

        $handle =
            [LocalCopilotM34Wrapper.WindowLookup]::FindWindowForProcess(
                [uint32]$Process.Id,
                $WindowTitle)

        if ($handle -ne [IntPtr]::Zero) {
            return $handle
        }

        Start-Sleep -Milliseconds 100
    }

    throw "$WindowTitle did not expose its real WinForms HWND before timeout."
}

$coreSource =
    [System.IO.File]::ReadAllText($corePath)

# Normalize line endings before applying the audited physical-fixture
# substitutions. The core runner remains the accepted measurement logic;
# this wrapper upgrades its controlled fixture to the raw UIA provider proven
# independently in Windows CI, resolves the real WinForms HWND by exact PID and
# title, and guarantees that a target process is cleaned up if startup fails.
$coreSource = $coreSource.Replace("`r`n", "`n")

# Keep the standalone measurement locked to its original Slice 2 branch by
# default. A higher-level acceptance harness may override only the expected
# branch provenance so the exact same measurement logic can be rerun on the
# clean Slice 3 candidate.
$legacyExpectedBranch = "dev/m3-4-2-provider-isolation"
$expectedBranchMatches =
    [Regex]::Matches(
        $coreSource,
        [Regex]::Escape($legacyExpectedBranch)).Count

if ($expectedBranchMatches -ne 2) {
    throw (
        "Expected exactly two Slice 2 branch provenance literals in the core runner; found " +
        $expectedBranchMatches +
        ".")
}

$coreSource =
    $coreSource.Replace(
        $legacyExpectedBranch,
        $ExpectedBranch)

$legacyFixtureBlock = @'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (__BLOCKING__) {
    Add-Type -Path __FIXTURE_SOURCE__ -ReferencedAssemblies @(
        "System.Windows.Forms.dll",
        "System.Drawing.dll",
        "Accessibility.dll"
    )
}
'@

$rawFixtureBlock = @'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationProvider
Add-Type -AssemblyName UIAutomationTypes

if (__BLOCKING__) {
    $providerAssembly =
        [System.Windows.Automation.Provider.AutomationInteropProvider].Assembly.Location
    $typesAssembly =
        [System.Windows.Automation.AutomationElementIdentifiers].Assembly.Location

    Add-Type -Path __FIXTURE_SOURCE__ -ReferencedAssemblies @(
        "System.Windows.Forms.dll",
        "System.Drawing.dll",
        "Accessibility.dll",
        $providerAssembly,
        $typesAssembly
    )
}
'@

$legacyShownBlock = @'
$form.Add_Shown({
    $form.Activate()
    $timer.Start()
})
'@

$focusedShownBlock = @'
$form.Add_Shown({
    $form.Activate()
    [void]$button.Focus()
    $timer.Start()
})
'@

$legacyHandleBlock = @'
    $windowParameters = @{
        Process = $process
        Description = $WindowTitle
        TimeoutSeconds = $StepTimeoutSeconds
    }

    $handle = Wait-M34ProcessWindow @windowParameters
'@

$fixtureHandleBlock = @'
    try {
        $handle =
            Wait-M34FixtureWindow `
                -Process $process `
                -WindowTitle $WindowTitle `
                -TimeoutSeconds $StepTimeoutSeconds
    }
    catch {
        try {
            $process.Refresh()

            if (-not $process.HasExited) {
                Stop-Process `
                    -Id $process.Id `
                    -Force `
                    -ErrorAction SilentlyContinue
            }
        }
        catch {
        }

        Remove-Item `
            -LiteralPath $StopPath `
            -Force `
            -ErrorAction SilentlyContinue

        throw
    }
'@

$legacyFixtureBlock =
    $legacyFixtureBlock.Replace("`r`n", "`n")
$rawFixtureBlock =
    $rawFixtureBlock.Replace("`r`n", "`n")
$legacyShownBlock =
    $legacyShownBlock.Replace("`r`n", "`n")
$focusedShownBlock =
    $focusedShownBlock.Replace("`r`n", "`n")
$legacyHandleBlock =
    $legacyHandleBlock.Replace("`r`n", "`n")
$fixtureHandleBlock =
    $fixtureHandleBlock.Replace("`r`n", "`n")

$fixtureMatches =
    [Regex]::Matches(
        $coreSource,
        [Regex]::Escape($legacyFixtureBlock)).Count

if ($fixtureMatches -ne 1) {
    throw (
        "Expected exactly one legacy provider compile block in the core runner; found " +
        $fixtureMatches +
        ".")
}

$shownMatches =
    [Regex]::Matches(
        $coreSource,
        [Regex]::Escape($legacyShownBlock)).Count

if ($shownMatches -ne 1) {
    throw (
        "Expected exactly one fixture Shown block in the core runner; found " +
        $shownMatches +
        ".")
}

$handleMatches =
    [Regex]::Matches(
        $coreSource,
        [Regex]::Escape($legacyHandleBlock)).Count

if ($handleMatches -ne 1) {
    throw (
        "Expected exactly one PowerShell MainWindowHandle block in the core runner; found " +
        $handleMatches +
        ".")
}

$patchedSource =
    $coreSource.Replace(
        $legacyFixtureBlock,
        $rawFixtureBlock)

$patchedSource =
    $patchedSource.Replace(
        $legacyShownBlock,
        $focusedShownBlock)

$patchedSource =
    $patchedSource.Replace(
        $legacyHandleBlock,
        $fixtureHandleBlock)

if (-not $patchedSource.Contains(
        "AutomationInteropProvider].Assembly.Location")) {
    throw "Raw UI Automation provider assembly resolution was not injected."
}

if (-not $patchedSource.Contains(
        '[void]$button.Focus()')) {
    throw "Controlled provider focus was not injected."
}

if (-not $patchedSource.Contains(
        'Wait-M34FixtureWindow')) {
    throw "PID-scoped real WinForms fixture-window resolution was not injected."
}

if (-not $fixtureHandleBlock.Contains(
        'Stop-Process')) {
    throw "Fixture startup cleanup was not injected."
}

$tokens = $null
$parseErrors = $null
$null =
    [System.Management.Automation.Language.Parser]::ParseInput(
        $patchedSource,
        [ref]$tokens,
        [ref]$parseErrors)

if (@($parseErrors).Count -ne 0) {
    $parseErrors |
        ForEach-Object {
            Write-Error $_.Message
        }

    throw "The patched M3.4 provider-isolation runner is not valid PowerShell."
}

if ($ValidateOnly) {
    $validationHandle =
        [LocalCopilotM34Wrapper.WindowLookup]::FindWindowForProcess(
            [uint32][Diagnostics.Process]::GetCurrentProcess().Id,
            "LocalCopilot M3.4 validation window that must not exist")

    if ($validationHandle -ne [IntPtr]::Zero) {
        throw "Fixture HWND resolver returned an unexpected validation window."
    }

    $branchGuard =
        'if ($branch -ne "' + $ExpectedBranch + '")'

    if (-not $patchedSource.Contains($branchGuard)) {
        throw "Expected-branch provenance override was not injected."
    }

    Write-Host "M3.4 raw-provider runner patch validation: PASS"
    Write-Host "M3.4 PID-scoped fixture HWND resolver validation: PASS"
    Write-Host "M3.4 fixture startup cleanup validation: PASS"
    Write-Host "M3.4 expected-branch provenance validation: PASS"
    return
}

$invokeParameters = @{
    StartupTimeoutSeconds = $StartupTimeoutSeconds
    StepTimeoutSeconds = $StepTimeoutSeconds
    ProviderObservationSeconds = $ProviderObservationSeconds
}

if (-not [string]::IsNullOrWhiteSpace($DiagnosticRoot)) {
    $invokeParameters.DiagnosticRoot = $DiagnosticRoot
}

$measurement =
    [ScriptBlock]::Create($patchedSource)

Push-Location $repoRoot

try {
    & $measurement @invokeParameters
}
finally {
    Pop-Location
}
