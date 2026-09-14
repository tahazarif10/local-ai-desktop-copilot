[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$targetProcess = $null
$clientProcess = $null
$gateEvent = $null
$enteredEvent = $null
$releaseEvent = $null
$runRoot = $null

function Quote-PsLiteral {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value
    )

    return "'" + $Value.Replace("'", "''") + "'"
}

function Wait-FileValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,
        [int]$TimeoutSeconds = 15
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $Path) {
            $value = [System.IO.File]::ReadAllText($Path).Trim()

            if (-not [string]::IsNullOrWhiteSpace($value)) {
                return $value
            }
        }

        $Process.Refresh()

        if ($Process.HasExited) {
            throw "Fixture target exited before publishing its control handle."
        }

        Start-Sleep -Milliseconds 100
    }

    throw "Fixture target did not publish its control handle before timeout."
}

try {
    if ($env:OS -ne "Windows_NT") {
        throw "Raw UI Automation provider smoke test requires Windows."
    }

    $sourcePath =
        Join-Path $PSScriptRoot "BlockingUiAutomationProvider.cs"

    $windowsPowerShell =
        Join-Path `
            $env:SystemRoot `
            "System32\WindowsPowerShell\v1.0\powershell.exe"

    $runRoot =
        Join-Path `
            ([System.IO.Path]::GetTempPath()) `
            ("localcopilot-m34-raw-provider-" + [Guid]::NewGuid().ToString("N"))

    New-Item -ItemType Directory -Path $runRoot -Force |
        Out-Null

    $fixtureAssemblyPath = Join-Path $runRoot "BlockingUiAutomationProvider.dll"
    $handlePath = Join-Path $runRoot "control-handle.txt"
    $stopPath = Join-Path $runRoot "target.stop"
    $resultPath = Join-Path $runRoot "name-result.txt"
    $clientFailurePath = Join-Path $runRoot "client-failure.txt"

    Add-Type -AssemblyName UIAutomationProvider
    Add-Type -AssemblyName UIAutomationTypes

    $providerAssembly =
        [System.Windows.Automation.Provider.AutomationInteropProvider].Assembly.Location
    $typesAssembly =
        [System.Windows.Automation.AutomationElementIdentifiers].Assembly.Location

    Add-Type `
        -Path $sourcePath `
        -OutputAssembly $fixtureAssemblyPath `
        -OutputType Library `
        -ReferencedAssemblies @(
            "System.Windows.Forms.dll",
            "System.Drawing.dll",
            "Accessibility.dll",
            $providerAssembly,
            $typesAssembly)

    if (-not (Test-Path -LiteralPath $fixtureAssemblyPath)) {
        throw "Controlled raw provider assembly was not produced."
    }

    $suffix = [Guid]::NewGuid().ToString("N")
    $gateName = "Local\LocalCopilotM34RawGate_" + $suffix
    $enteredName = "Local\LocalCopilotM34RawEntered_" + $suffix
    $releaseName = "Local\LocalCopilotM34RawRelease_" + $suffix
    $sentinel = "M34_RAW_" + [Guid]::NewGuid().ToString("N") + "_CONTENT"

    $gateEvent =
        New-Object Threading.EventWaitHandle(
            $false,
            [Threading.EventResetMode]::ManualReset,
            $gateName)

    $enteredEvent =
        New-Object Threading.EventWaitHandle(
            $false,
            [Threading.EventResetMode]::ManualReset,
            $enteredName)

    $releaseEvent =
        New-Object Threading.EventWaitHandle(
            $false,
            [Threading.EventResetMode]::ManualReset,
            $releaseName)

    $targetTemplate = @'
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationProvider
Add-Type -AssemblyName UIAutomationTypes
Add-Type -Path __FIXTURE_ASSEMBLY__

$form = New-Object System.Windows.Forms.Form
$form.Text = 'LocalCopilot M3.4 raw provider target'
$form.Width = 560
$form.Height = 240
$form.StartPosition = 'CenterScreen'

$button =
    New-Object LocalCopilotM34Fixture.BlockingNameButton(
        __GATE_EVENT__,
        __ENTERED_EVENT__,
        __RELEASE_EVENT__,
        __SENTINEL__)

$button.Text = 'Controlled blocking provider'
$button.Location = New-Object System.Drawing.Point(20, 20)
$button.AutoSize = $true
[void]$form.Controls.Add($button)

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 200
$timer.Add_Tick({
    if ([System.IO.File]::Exists(__STOP_PATH__)) {
        $timer.Stop()
        $form.Close()
    }
})

$form.Add_Shown({
    $form.Activate()
    [void]$button.Focus()
    [System.IO.File]::WriteAllText(
        __HANDLE_PATH__,
        $button.Handle.ToInt64().ToString())
    $timer.Start()
})

[void]$form.ShowDialog()
$timer.Dispose()
$form.Dispose()
'@

    $targetScript = $targetTemplate
    $targetScript = $targetScript.Replace(
        "__FIXTURE_ASSEMBLY__",
        (Quote-PsLiteral $fixtureAssemblyPath))
    $targetScript = $targetScript.Replace(
        "__GATE_EVENT__",
        (Quote-PsLiteral $gateName))
    $targetScript = $targetScript.Replace(
        "__ENTERED_EVENT__",
        (Quote-PsLiteral $enteredName))
    $targetScript = $targetScript.Replace(
        "__RELEASE_EVENT__",
        (Quote-PsLiteral $releaseName))
    $targetScript = $targetScript.Replace(
        "__SENTINEL__",
        (Quote-PsLiteral $sentinel))
    $targetScript = $targetScript.Replace(
        "__STOP_PATH__",
        (Quote-PsLiteral $stopPath))
    $targetScript = $targetScript.Replace(
        "__HANDLE_PATH__",
        (Quote-PsLiteral $handlePath))

    $targetEncoded =
        [Convert]::ToBase64String(
            [Text.Encoding]::Unicode.GetBytes($targetScript))

    $targetProcess =
        Start-Process `
            -FilePath $windowsPowerShell `
            -ArgumentList @(
                "-NoProfile",
                "-Sta",
                "-WindowStyle",
                "Hidden",
                "-EncodedCommand",
                $targetEncoded) `
            -PassThru

    $handleText =
        Wait-FileValue `
            -Path $handlePath `
            -Process $targetProcess

    [long]$controlHandle = 0

    if (-not [long]::TryParse($handleText, [ref]$controlHandle) -or
        $controlHandle -eq 0) {
        throw "Fixture target published an invalid child HWND."
    }

    $clientTemplate = @'
$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes

    $element =
        [System.Windows.Automation.AutomationElement]::FromHandle(
            [IntPtr]__CONTROL_HANDLE__)

    if ($null -eq $element) {
        throw 'AutomationElement.FromHandle returned null.'
    }

    $value =
        $element.GetCurrentPropertyValue(
            [System.Windows.Automation.AutomationElement]::NameProperty)

    [System.IO.File]::WriteAllText(
        __RESULT_PATH__,
        [string]$value)
}
catch {
    [System.IO.File]::WriteAllText(
        __FAILURE_PATH__,
        $_.Exception.ToString())
    exit 1
}
'@

    $clientScript = $clientTemplate
    $clientScript = $clientScript.Replace(
        "__CONTROL_HANDLE__",
        $controlHandle.ToString())
    $clientScript = $clientScript.Replace(
        "__RESULT_PATH__",
        (Quote-PsLiteral $resultPath))
    $clientScript = $clientScript.Replace(
        "__FAILURE_PATH__",
        (Quote-PsLiteral $clientFailurePath))

    $clientEncoded =
        [Convert]::ToBase64String(
            [Text.Encoding]::Unicode.GetBytes($clientScript))

    [void]$gateEvent.Set()

    $clientProcess =
        Start-Process `
            -FilePath $windowsPowerShell `
            -ArgumentList @(
                "-NoProfile",
                "-Sta",
                "-WindowStyle",
                "Hidden",
                "-EncodedCommand",
                $clientEncoded) `
            -PassThru

    if (-not $enteredEvent.WaitOne(10000)) {
        $clientProcess.Refresh()

        if ($clientProcess.HasExited) {
            [void]$clientProcess.WaitForExit()
            $failure =
                if (Test-Path -LiteralPath $clientFailurePath) {
                    [System.IO.File]::ReadAllText($clientFailurePath)
                }
                else {
                    "<no client failure file>"
                }

            throw (
                "UIA client exited before entering provider. " +
                "ExitCode=" + $clientProcess.ExitCode +
                " Failure=" + $failure)
        }

        throw "UIA client did not enter the raw provider Name call before timeout."
    }

    Write-Host "[PASS] UIA entered the raw provider Name call cross-process"

    $clientProcess.Refresh()
    if ($clientProcess.HasExited) {
        throw "UIA client returned while provider release was still closed."
    }

    [void]$releaseEvent.Set()

    if (-not $clientProcess.WaitForExit(10000)) {
        throw "UIA client did not recover after raw provider release."
    }

    if ($clientProcess.ExitCode -ne 0) {
        $failure =
            if (Test-Path -LiteralPath $clientFailurePath) {
                [System.IO.File]::ReadAllText($clientFailurePath)
            }
            else {
                "<no client failure file>"
            }

        throw (
            "UIA client failed after provider release. " +
            "ExitCode=" + $clientProcess.ExitCode +
            " Failure=" + $failure)
    }

    if (-not (Test-Path -LiteralPath $resultPath)) {
        throw "UIA client did not write the provider Name result."
    }

    $result = [System.IO.File]::ReadAllText($resultPath)
    if ($result -ne $sentinel) {
        throw "Raw provider Name result did not match the sentinel."
    }

    Write-Host "[PASS] Raw provider remained blocked until release"
    Write-Host "[PASS] Raw provider returned the controlled sentinel"
    Write-Host "M3.4 raw provider smoke test: PASS"
}
finally {
    if ($null -ne $releaseEvent) {
        try { [void]$releaseEvent.Set() } catch { }
    }

    if ($null -ne $clientProcess) {
        try {
            $clientProcess.Refresh()
            if (-not $clientProcess.HasExited) {
                Stop-Process -Id $clientProcess.Id -Force -ErrorAction SilentlyContinue
            }
        }
        catch { }
    }

    if ($null -ne $targetProcess) {
        try {
            $targetProcess.Refresh()
            if (-not $targetProcess.HasExited -and $null -ne $runRoot) {
                New-Item -ItemType File -Force -Path (Join-Path $runRoot "target.stop") |
                    Out-Null
                [void]$targetProcess.WaitForExit(5000)
            }
            $targetProcess.Refresh()
            if (-not $targetProcess.HasExited) {
                Stop-Process -Id $targetProcess.Id -Force -ErrorAction SilentlyContinue
            }
        }
        catch { }
    }

    foreach ($eventHandle in @($gateEvent, $enteredEvent, $releaseEvent)) {
        if ($null -ne $eventHandle) {
            try { $eventHandle.Dispose() } catch { }
        }
    }

    if ($null -ne $runRoot) {
        Remove-Item -LiteralPath $runRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
