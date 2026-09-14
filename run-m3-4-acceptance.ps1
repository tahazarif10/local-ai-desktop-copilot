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
# persist across ticks: the panel changed once and then stayed in the same
# state. That produced only Insignificant detector samples on the physical
# client. Patch the fixture state onto WinForms Panel.Tag, whose value persists
# across timer callbacks, without changing any product/runtime code.
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
    throw "M3.4 acceptance fixture patch validation failed. state=$stateMatches tick=$tickMatches"
}

$source = $source.Replace($oldState, $newState)
$source = $source.Replace($oldTick, $newTick)

Push-Location $PSScriptRoot
try {
    $runner = [ScriptBlock]::Create($source)
    & $runner @PSBoundParameters
}
finally {
    Pop-Location
}
