[CmdletBinding()]
param(
    [string]$DiagnosticRoot,
    [ValidateRange(30, 900)]
    [int]$StartupTimeoutSeconds = 300,
    [ValidateRange(5, 120)]
    [int]$StepTimeoutSeconds = 30,
    [ValidateRange(11, 60)]
    [int]$ProviderObservationSeconds = 12,
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

namespace LocalCopilotM34Wrapper
{
    public static class WindowLookup
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindow(
            string lpClassName,
            string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(
            IntPtr hWnd,
            out uint lpdwProcessId);
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
            [LocalCopilotM34Wrapper.WindowLookup]::FindWindow(
                $null,
                $WindowTitle)

        if ($handle -ne [IntPtr]::Zero) {
            [uint32]$windowPid = 0

            [void][LocalCopilotM34Wrapper.WindowLookup]::GetWindowThreadProcessId(
                $handle,
                [ref]$windowPid)

            if ($windowPid -eq [uint32]$Process.Id) {
                return $handle
            }
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
# independently in Windows CI and resolves the real WinForms HWND rather than
# PowerShell's console MainWindowHandle.
$coreSource = $coreSource.Replace("`r`n", "`n")

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
    $handle =
        Wait-M34FixtureWindow `
            -Process $process `
            -WindowTitle $WindowTitle `
            -TimeoutSeconds $StepTimeoutSeconds
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
    throw "Real WinForms fixture-window resolution was not injected."
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
    [void][LocalCopilotM34Wrapper.WindowLookup]::FindWindow(
        $null,
        "LocalCopilot M3.4 validation window that must not exist")

    Write-Host "M3.4 raw-provider runner patch validation: PASS"
    Write-Host "M3.4 real fixture HWND resolver validation: PASS"
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
