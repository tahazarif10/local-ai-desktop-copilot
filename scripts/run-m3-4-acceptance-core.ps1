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

$repoRoot = $null
$runRoot = $null
$target = $null
$deniedSession = $null
$allowedSession = $null
$acceptanceError = $null
$acceptancePassed = $false
$providerRegression = "NOT_RUN"
$evidencePath = $null

function Test-M34Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function ConvertTo-M34SingleQuotedLiteral {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    return "'" + $Value.Replace("'", "''") + "'"
}

function Get-M34FileContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    try {
        if (-not (Test-Path -LiteralPath $Path)) {
            return ""
        }

        return [System.IO.File]::ReadAllText($Path)
    }
    catch [System.IO.IOException] {
        return ""
    }
}

function Wait-M34LogMatch {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Pattern,
        [int]$AfterOffset = 0,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds,
        [AllowNull()]
        [System.Diagnostics.Process]$RunnerProcess
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $regex = New-Object System.Text.RegularExpressions.Regex(
        $Pattern,
        [System.Text.RegularExpressions.RegexOptions]::Multiline)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $content = Get-M34FileContent -Path $Path

        if ($content.Length -ge $AfterOffset) {
            $match = $regex.Match($content.Substring($AfterOffset))
            if ($match.Success) {
                return $match
            }
        }

        if ($null -ne $RunnerProcess) {
            $RunnerProcess.Refresh()
            if ($RunnerProcess.HasExited) {
                throw "The diagnostic runner exited before the expected event."
            }
        }

        Start-Sleep -Milliseconds 75
    }

    throw "Timed out waiting for a required M3.4 metadata event."
}

function Assert-M34NoPattern {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,
        [Parameter(Mandatory = $true)]
        [string]$Pattern,
        [Parameter(Mandatory = $true)]
        [string]$Failure
    )

    if ([Regex]::IsMatch(
            $Content,
            $Pattern,
            [Text.RegularExpressions.RegexOptions]::Multiline)) {
        throw $Failure
    }
}

function Wait-M34ProcessWindow {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)]
        [string]$Description,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $Process.Refresh()

        if ($Process.HasExited) {
            throw "$Description exited before its window became ready."
        }

        if ($Process.MainWindowHandle -ne [IntPtr]::Zero) {
            return [IntPtr]$Process.MainWindowHandle
        }

        Start-Sleep -Milliseconds 100
    }

    throw "$Description did not expose a main window before timeout."
}

function Set-M34Foreground {
    param(
        [Parameter(Mandatory = $true)]
        [IntPtr]$Handle,
        [Parameter(Mandatory = $true)]
        [string]$Description,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    [void][LocalCopilotM34Acceptance.WindowMethods]::ShowWindow($Handle, 9)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        [void][LocalCopilotM34Acceptance.WindowMethods]::SetForegroundWindow($Handle)

        if ([LocalCopilotM34Acceptance.WindowMethods]::GetForegroundWindow() -eq
            $Handle) {
            return
        }

        Start-Sleep -Milliseconds 100
    }

    throw "$Description could not become the foreground window."
}

function Start-M34Target {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PowerShellPath,
        [Parameter(Mandatory = $true)]
        [string]$StopPath,
        [Parameter(Mandatory = $true)]
        [string]$Sentinel
    )

    $template = @'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$form = New-Object System.Windows.Forms.Form
$form.Text = 'LocalCopilot M3.4 runtime target'
$form.Width = 760
$form.Height = 460
$form.StartPosition = 'CenterScreen'

$panel = New-Object System.Windows.Forms.Panel
$panel.Location = New-Object System.Drawing.Point(20, 20)
$panel.Size = New-Object System.Drawing.Size(700, 280)
$panel.BackColor = [System.Drawing.Color]::Black

$label = New-Object System.Windows.Forms.Label
$label.Location = New-Object System.Drawing.Point(20, 330)
$label.AutoSize = $true
$label.Font = New-Object System.Drawing.Font('Segoe UI', 14)
$label.Text = __SENTINEL__ + '_A'
$label.AccessibleName = __SENTINEL__ + '_A'

[void]$form.Controls.Add($panel)
[void]$form.Controls.Add($label)

$stopPath = __STOP_PATH__
$flip = $false
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 800
$timer.Add_Tick({
    if ([System.IO.File]::Exists($stopPath)) {
        $timer.Stop()
        $form.Close()
        return
    }

    $flip = -not $flip
    if ($flip) {
        $panel.BackColor = [System.Drawing.Color]::White
        $label.Text = __SENTINEL__ + '_B'
        $label.AccessibleName = __SENTINEL__ + '_B'
    }
    else {
        $panel.BackColor = [System.Drawing.Color]::Black
        $label.Text = __SENTINEL__ + '_A'
        $label.AccessibleName = __SENTINEL__ + '_A'
    }
})
$form.Add_Shown({
    $form.Activate()
    $timer.Start()
})

[void]$form.ShowDialog()
$timer.Dispose()
$form.Dispose()
'@

    $script = $template
    $script = $script.Replace(
        "__STOP_PATH__",
        (ConvertTo-M34SingleQuotedLiteral $StopPath))
    $script = $script.Replace(
        "__SENTINEL__",
        (ConvertTo-M34SingleQuotedLiteral $Sentinel))

    $encoded = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes($script))

    $process = Start-Process `
        -FilePath $PowerShellPath `
        -ArgumentList @(
            "-NoProfile",
            "-Sta",
            "-WindowStyle",
            "Hidden",
            "-EncodedCommand",
            $encoded) `
        -PassThru

    $handle = Wait-M34ProcessWindow `
        -Process $process `
        -Description "M3.4 runtime target" `
        -TimeoutSeconds $StepTimeoutSeconds

    return [pscustomobject]@{
        Process = $process
        Handle = $handle
        StopPath = $StopPath
    }
}

function Stop-M34Target {
    param(
        [AllowNull()]
        [object]$Target
    )

    if ($null -eq $Target) {
        return
    }

    try {
        if (-not $Target.Process.HasExited) {
            New-Item -ItemType File -Force -Path $Target.StopPath | Out-Null
            [void]$Target.Process.WaitForExit(5000)
        }
    }
    catch {
    }

    try {
        $Target.Process.Refresh()
        if (-not $Target.Process.HasExited) {
            Stop-Process -Id $Target.Process.Id -Force -ErrorAction SilentlyContinue
        }
    }
    catch {
    }

    Remove-Item -LiteralPath $Target.StopPath -Force -ErrorAction SilentlyContinue
}

function Wait-M34RunnerSession {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$RunnerProcess,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $directories = @(
            Get-ChildItem -LiteralPath $Root -Directory -ErrorAction SilentlyContinue)

        if ($directories.Count -eq 1) {
            return $directories[0].FullName
        }

        if ($directories.Count -gt 1) {
            throw "An M3.4 diagnostic root contains more than one session."
        }

        $RunnerProcess.Refresh()
        if ($RunnerProcess.HasExited) {
            throw "The diagnostic runner exited before creating a session."
        }

        Start-Sleep -Milliseconds 100
    }

    throw "The diagnostic runner did not create a session before timeout."
}

function Wait-M34AppProcess {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$RunnerProcess,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $processes = @(Get-Process "LocalCopilot.App" -ErrorAction SilentlyContinue)

        if ($processes.Count -eq 1) {
            return $processes[0]
        }

        if ($processes.Count -gt 1) {
            throw "More than one LocalCopilot process is running."
        }

        $RunnerProcess.Refresh()
        if ($RunnerProcess.HasExited) {
            throw "The diagnostic runner exited before LocalCopilot launched."
        }

        Start-Sleep -Milliseconds 250
    }

    throw "LocalCopilot did not launch before timeout."
}

function Invoke-M34Button {
    param(
        [Parameter(Mandatory = $true)]
        [object]$AppRoot,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)

    $button = $AppRoot.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)

    if ($null -eq $button) {
        throw "A required LocalCopilot M3.4 command was not found: $Name"
    }

    $pattern = $button.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)

    ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
}

function Start-M34DiagnosticSession {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PowerShellPath,
        [Parameter(Mandatory = $true)]
        [string]$RunnerPath,
        [Parameter(Mandatory = $true)]
        [string]$Root,
        [switch]$EnableUiText
    )

    New-Item -ItemType Directory -Path $Root -Force | Out-Null

    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        ('"{0}"' -f $RunnerPath),
        "-DiagnosticRoot",
        ('"{0}"' -f $Root),
        "-Milestone",
        "M3.4")

    if ($EnableUiText) {
        $arguments += "-EnableUiText"
    }

    $runner = Start-Process `
        -FilePath $PowerShellPath `
        -ArgumentList $arguments `
        -NoNewWindow `
        -PassThru

    $sessionDirectory = Wait-M34RunnerSession `
        -Root $Root `
        -RunnerProcess $runner `
        -TimeoutSeconds $StartupTimeoutSeconds

    $appLogPath = Join-Path $sessionDirectory "app.log"
    $metaPath = Join-Path $sessionDirectory "session-meta.txt"
    $bundlePath = Join-Path $sessionDirectory "diagnostic-bundle.txt"

    $app = Wait-M34AppProcess `
        -RunnerProcess $runner `
        -TimeoutSeconds $StartupTimeoutSeconds

    $appHandle = Wait-M34ProcessWindow `
        -Process $app `
        -Description "LocalCopilot" `
        -TimeoutSeconds $StepTimeoutSeconds

    [void](Wait-M34LogMatch `
        -Path $appLogPath `
        -Pattern ('^schema=1 .*uiTextEnabled={0}\r?$' -f ([bool]$EnableUiText)) `
        -TimeoutSeconds $StepTimeoutSeconds `
        -RunnerProcess $runner)

    $appRoot = [System.Windows.Automation.AutomationElement]::FromHandle($appHandle)
    if ($null -eq $appRoot) {
        throw "LocalCopilot UI Automation root was unavailable."
    }

    return [pscustomobject]@{
        Runner = $runner
        App = $app
        AppHandle = $appHandle
        AppRoot = $appRoot
        Directory = $sessionDirectory
        AppLog = $appLogPath
        Meta = $metaPath
        Bundle = $bundlePath
    }
}

function Stop-M34DiagnosticSession {
    param(
        [AllowNull()]
        [object]$Session
    )

    if ($null -eq $Session) {
        return
    }

    try {
        $Session.App.Refresh()
        if (-not $Session.App.HasExited) {
            [void][LocalCopilotM34Acceptance.WindowMethods]::PostMessage(
                [IntPtr]$Session.AppHandle,
                0x0010,
                [IntPtr]::Zero,
                [IntPtr]::Zero)
            [void]$Session.App.WaitForExit(15000)
        }
    }
    catch {
    }

    try {
        $Session.App.Refresh()
        if (-not $Session.App.HasExited) {
            Stop-Process -Id $Session.App.Id -Force -ErrorAction SilentlyContinue
        }
    }
    catch {
    }

    try {
        [void]$Session.Runner.WaitForExit(30000)
    }
    catch {
    }
}

function Set-M34TargetContext {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Session,
        [Parameter(Mandatory = $true)]
        [object]$Target
    )

    Set-M34Foreground `
        -Handle $Target.Handle `
        -Description "M3.4 runtime target" `
        -TimeoutSeconds $StepTimeoutSeconds

    $pattern = '(?m)^.*\| CONTEXT\.APPLY \| .*pid={0} .*privacy=Allowed .*$' -f
        $Target.Process.Id

    [void](Wait-M34LogMatch `
        -Path $Session.AppLog `
        -Pattern $pattern `
        -TimeoutSeconds $StepTimeoutSeconds `
        -RunnerProcess $Session.Runner)
}

try {
    if ($env:OS -ne "Windows_NT") {
        throw "M3.4 physical acceptance requires Windows."
    }

    if (Test-M34Elevated) {
        throw "Run M3.4 acceptance from a normal, non-Administrator PowerShell."
    }

    $repoRoot = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($repoRoot)) {
        $repoRoot = (Get-Location).Path
    }
    $repoRoot = [System.IO.Path]::GetFullPath($repoRoot)

    $gitStatus = git -C $repoRoot status --short | Out-String -Width 4096
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve Git working-tree status."
    }
    if (-not [string]::IsNullOrWhiteSpace($gitStatus)) {
        throw "M3.4 physical acceptance requires a clean working tree."
    }

    $branch = (git -C $repoRoot branch --show-current | Out-String).Trim()
    $head = (git -C $repoRoot rev-parse HEAD | Out-String).Trim()

    $windowsPowerShell = Join-Path `
        $env:SystemRoot `
        "System32\WindowsPowerShell\v1.0\powershell.exe"
    $runnerPath = Join-Path $repoRoot "run-debug.ps1"
    $providerRunnerPath = Join-Path $repoRoot "run-m3-4-provider-isolation.ps1"

    if (-not (Test-Path -LiteralPath $runnerPath) -or
        -not (Test-Path -LiteralPath $providerRunnerPath)) {
        throw "Required M3.4 runner files are missing."
    }

    if ([string]::IsNullOrWhiteSpace($DiagnosticRoot)) {
        $DiagnosticRoot = Join-Path $repoRoot ".localcopilot\diagnostics"
    }
    elseif (-not [System.IO.Path]::IsPathRooted($DiagnosticRoot)) {
        $DiagnosticRoot = Join-Path $repoRoot $DiagnosticRoot
    }

    $diagnosticBase = [System.IO.Path]::GetFullPath($DiagnosticRoot)
    $runRoot = Join-Path `
        $diagnosticBase `
        ("m3-4-acceptance-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    $evidencePath = Join-Path $runRoot "m3-4-acceptance-evidence.txt"

    if ($null -eq ("LocalCopilotM34Acceptance.WindowMethods" -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace LocalCopilotM34Acceptance
{
    public static class WindowMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(
            IntPtr hWnd,
            uint msg,
            IntPtr wParam,
            IntPtr lParam);
    }
}
'@
    }

    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes

    $sentinel = "M34_AUTO_" + [Guid]::NewGuid().ToString("N")

    Write-Host ""
    Write-Host "=============================================="
    Write-Host "M3.4 ONE-COMMAND ACCEPTANCE"
    Write-Host "=============================================="
    Write-Host "Branch: $branch"
    Write-Host "HEAD:   $head"
    Write-Host ""
    Write-Host "Starting controlled changing same-integrity target..."

    $target = Start-M34Target `
        -PowerShellPath $windowsPowerShell `
        -StopPath (Join-Path $runRoot "runtime-target.stop") `
        -Sentinel $sentinel

    Write-Host "[RUN] Capability-denied automatic enrichment"

    $deniedSession = Start-M34DiagnosticSession `
        -PowerShellPath $windowsPowerShell `
        -RunnerPath $runnerPath `
        -Root (Join-Path $runRoot "denied")

    Invoke-M34Button -AppRoot $deniedSession.AppRoot -Name "Arm auto sensing"
    Set-M34TargetContext -Session $deniedSession -Target $target

    $deniedOffset = (Get-M34FileContent -Path $deniedSession.AppLog).Length

    [void](Wait-M34LogMatch `
        -Path $deniedSession.AppLog `
        -Pattern '(?m)^.*\| UIENRICH\.ADMIT \| .*kind=BackgroundChange classification=(Meaningful|Large) outcome=Rejected reason=CapabilityDenied .*$' `
        -AfterOffset $deniedOffset `
        -TimeoutSeconds $StepTimeoutSeconds `
        -RunnerProcess $deniedSession.Runner)

    Start-Sleep -Milliseconds 1200
    $deniedTail = (Get-M34FileContent -Path $deniedSession.AppLog).Substring($deniedOffset)
    Assert-M34NoPattern `
        -Content $deniedTail `
        -Pattern '(?m)^.*\| UIENRICH\.DISPATCH \|' `
        -Failure "Automatic semantic UIA dispatched without ReadUiText capability."

    Write-Host "[PASS] ReadUiText denial blocks automatic UIA before dispatch"

    Stop-M34DiagnosticSession -Session $deniedSession
    $deniedSession.Runner.Refresh()
    if (-not $deniedSession.Runner.HasExited) {
        throw "The denied diagnostic runner did not exit cleanly."
    }

    $deniedBundle = Get-M34FileContent -Path $deniedSession.Bundle
    if ($deniedBundle.IndexOf($sentinel, [StringComparison]::Ordinal) -ge 0) {
        throw "Controlled semantic sentinel leaked into the denied diagnostic bundle."
    }

    Write-Host "[RUN] Enabled automatic enrichment and bounded debounce"

    $allowedSession = Start-M34DiagnosticSession `
        -PowerShellPath $windowsPowerShell `
        -RunnerPath $runnerPath `
        -Root (Join-Path $runRoot "allowed") `
        -EnableUiText

    Invoke-M34Button -AppRoot $allowedSession.AppRoot -Name "Arm auto sensing"
    Set-M34TargetContext -Session $allowedSession -Target $target

    $allowedOffset = (Get-M34FileContent -Path $allowedSession.AppLog).Length

    $backgroundAdmission = Wait-M34LogMatch `
        -Path $allowedSession.AppLog `
        -Pattern '(?m)^.*\| UIENRICH\.ADMIT \| .*kind=BackgroundChange classification=(Meaningful|Large) outcome=DispatchNow reason=None request=(\d+) .*$' `
        -AfterOffset $allowedOffset `
        -TimeoutSeconds $StepTimeoutSeconds `
        -RunnerProcess $allowedSession.Runner

    $backgroundRequest = $backgroundAdmission.Groups[2].Value

    [void](Wait-M34LogMatch `
        -Path $allowedSession.AppLog `
        -Pattern ('(?m)^.*\| UIENRICH\.DISPATCH \| request={0} .*kind=BackgroundChange .*$' -f [Regex]::Escape($backgroundRequest)) `
        -AfterOffset $allowedOffset `
        -TimeoutSeconds $StepTimeoutSeconds `
        -RunnerProcess $allowedSession.Runner)

    [void](Wait-M34LogMatch `
        -Path $allowedSession.AppLog `
        -Pattern ('(?m)^.*\| UIENRICH\.RESULT \| request={0} .*kind=BackgroundChange outcome=Available reason=SnapshotCaptured .*content=redacted.*$' -f [Regex]::Escape($backgroundRequest)) `
        -AfterOffset $allowedOffset `
        -TimeoutSeconds $StepTimeoutSeconds `
        -RunnerProcess $allowedSession.Runner)

    [void](Wait-M34LogMatch `
        -Path $allowedSession.AppLog `
        -Pattern '(?m)^.*\| UIENRICH\.ADMIT \| .*kind=BackgroundChange classification=(Meaningful|Large) outcome=Rejected reason=BackgroundDebounced .*$' `
        -AfterOffset $allowedOffset `
        -TimeoutSeconds $StepTimeoutSeconds `
        -RunnerProcess $allowedSession.Runner)

    Write-Host "[PASS] Meaningful/Large change dispatches bounded M3.3 snapshot and debounce rejects repeats"

    Write-Host "[RUN] Metadata-only user-question priority path"

    $questionOffset = (Get-M34FileContent -Path $allowedSession.AppLog).Length
    Invoke-M34Button `
        -AppRoot $allowedSession.AppRoot `
        -Name "Request M3.4 user-question enrichment (diagnostic)"

    $questionAdmission = Wait-M34LogMatch `
        -Path $allowedSession.AppLog `
        -Pattern '(?m)^.*\| UIENRICH\.ADMIT \| .*kind=UserQuestion classification=none outcome=(DispatchNow|Queued|ReplacedPending) reason=None request=(\d+) .*$' `
        -AfterOffset $questionOffset `
        -TimeoutSeconds $StepTimeoutSeconds `
        -RunnerProcess $allowedSession.Runner

    $questionRequest = $questionAdmission.Groups[2].Value

    [void](Wait-M34LogMatch `
        -Path $allowedSession.AppLog `
        -Pattern ('(?m)^.*\| UIENRICH\.RESULT \| request={0} .*kind=UserQuestion outcome=Available reason=SnapshotCaptured .*content=redacted.*$' -f [Regex]::Escape($questionRequest)) `
        -AfterOffset $questionOffset `
        -TimeoutSeconds $StepTimeoutSeconds `
        -RunnerProcess $allowedSession.Runner)

    Write-Host "[PASS] User-question trigger reaches the same bounded semantic path"

    Write-Host "[RUN] Disarm invalidation and no post-disarm dispatch"

    $disarmOffset = (Get-M34FileContent -Path $allowedSession.AppLog).Length
    Invoke-M34Button -AppRoot $allowedSession.AppRoot -Name "Disarm auto sensing"

    [void](Wait-M34LogMatch `
        -Path $allowedSession.AppLog `
        -Pattern '(?m)^.*\| ORCH\.DISARM \| reason=user_disarm.*$' `
        -AfterOffset $disarmOffset `
        -TimeoutSeconds $StepTimeoutSeconds `
        -RunnerProcess $allowedSession.Runner)

    Start-Sleep -Milliseconds 1800
    $disarmTail = (Get-M34FileContent -Path $allowedSession.AppLog).Substring($disarmOffset)
    Assert-M34NoPattern `
        -Content $disarmTail `
        -Pattern '(?m)^.*\| UIENRICH\.DISPATCH \|' `
        -Failure "Automatic UI enrichment dispatched after Disarm."

    Write-Host "[PASS] Disarm stops automatic enrichment"

    Stop-M34DiagnosticSession -Session $allowedSession
    $allowedSession.Runner.Refresh()
    if (-not $allowedSession.Runner.HasExited) {
        throw "The enabled diagnostic runner did not exit cleanly."
    }

    $allowedLog = Get-M34FileContent -Path $allowedSession.AppLog
    if (-not [Regex]::IsMatch(
            $allowedLog,
            '(?m)^.*\| UIENRICH\.STOP \| reason=dispose.*$')) {
        throw "The application-owned enrichment runtime did not stop during shutdown."
    }

    if (-not [Regex]::IsMatch(
            $allowedLog,
            '(?m)^.*\| UIA\.WORKER_DISPOSE \| started=True joined=True.*$')) {
        throw "The UIA worker did not report joined=True at final shutdown."
    }

    $enrichmentStopIndex = $allowedLog.IndexOf("| UIENRICH.STOP |", [StringComparison]::Ordinal)
    $coordinatorStopIndex = $allowedLog.IndexOf("| COORD.STOP |", [StringComparison]::Ordinal)
    if ($enrichmentStopIndex -lt 0 -or
        $coordinatorStopIndex -lt 0 -or
        $enrichmentStopIndex -gt $coordinatorStopIndex) {
        throw "Enrichment runtime teardown did not precede coordinator/UIA teardown."
    }

    $allowedBundle = Get-M34FileContent -Path $allowedSession.Bundle
    if ($allowedBundle.IndexOf($sentinel, [StringComparison]::Ordinal) -ge 0) {
        throw "Controlled semantic sentinel leaked into the enabled diagnostic bundle."
    }

    Write-Host "[PASS] Runtime teardown ordering, joined worker, and redaction"

    Stop-M34Target -Target $target
    $target = $null

    Write-Host "[RUN] Accepted in-process provider-isolation regression"

    # Keep the nested provider-regression diagnostic path safely below the
    # legacy MAX_PATH boundary used by Windows PowerShell/.NET Framework.
    # The provider runner creates two additional GUID/timestamp directories,
    # so nesting it below the already-long acceptance run root can reach 260
    # characters before session-meta.txt is written.
    $providerRoot =
        Join-Path `
            $diagnosticBase `
            ("m34pr-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))

    & $providerRunnerPath `
        -DiagnosticRoot $providerRoot `
        -ExpectedBranch $branch
    $providerRegression = "PASS"

    $providerEvidenceFiles = @(
        Get-ChildItem `
            -LiteralPath $providerRoot `
            -Recurse `
            -File `
            -Filter "*.txt" `
            -ErrorAction SilentlyContinue)

    $providerEvidenceFound = $false
    foreach ($providerFile in $providerEvidenceFiles) {
        $providerText = Get-M34FileContent -Path $providerFile.FullName
        if ($providerText.IndexOf(
                "Architecture classification: InProcessCandidate",
                [StringComparison]::Ordinal) -ge 0 -and
            $providerText.IndexOf(
                "Worker joined: True",
                [StringComparison]::Ordinal) -ge 0 -and
            $providerText.IndexOf(
                "Controlled sentinel scan: PASS",
                [StringComparison]::Ordinal) -ge 0) {
            $providerEvidenceFound = $true
            break
        }
    }

    if (-not $providerEvidenceFound) {
        throw "Provider-isolation regression did not produce accepted in-process evidence."
    }

    Write-Host "[PASS] In-process provider recovery invariant remains valid"

    $acceptancePassed = $true
}
catch {
    $acceptanceError = $_.Exception
}
finally {
    if ($null -ne $allowedSession) {
        Stop-M34DiagnosticSession -Session $allowedSession
    }

    if ($null -ne $deniedSession) {
        Stop-M34DiagnosticSession -Session $deniedSession
    }

    if ($null -ne $target) {
        Stop-M34Target -Target $target
    }

    if ($null -ne $evidencePath) {
        try {
            $branchValue = "unknown"
            $headValue = "unknown"
            $statusValue = "unknown"

            if ($null -ne $repoRoot) {
                $branchValue = (git -C $repoRoot branch --show-current | Out-String).Trim()
                $headValue = (git -C $repoRoot rev-parse HEAD | Out-String).Trim()
                $statusValue = (git -C $repoRoot status --short | Out-String -Width 4096).Trim()
                if ([string]::IsNullOrWhiteSpace($statusValue)) {
                    $statusValue = "clean"
                }
            }

            $errorText = "none"
            if ($null -ne $acceptanceError) {
                $errorText = $acceptanceError.GetType().Name + ": " + $acceptanceError.Message
            }

            $evidence = @"
=================================================
M3.4 RUNTIME-INTEGRATION ACCEPTANCE EVIDENCE
=================================================
Branch: $branchValue
HEAD: $headValue
Working tree: $statusValue
Runner elevated: $(Test-M34Elevated)
ReadUiText denial gate: $($(if ($acceptancePassed) { "PASS" } else { "SEE_ERROR" }))
Automatic background dispatch/debounce: $($(if ($acceptancePassed) { "PASS" } else { "SEE_ERROR" }))
User-question runtime path: $($(if ($acceptancePassed) { "PASS" } else { "SEE_ERROR" }))
Disarm no-post-dispatch gate: $($(if ($acceptancePassed) { "PASS" } else { "SEE_ERROR" }))
Redaction/sentinel scan: $($(if ($acceptancePassed) { "PASS" } else { "SEE_ERROR" }))
Joined UIA teardown: $($(if ($acceptancePassed) { "PASS" } else { "SEE_ERROR" }))
Provider-isolation regression: $providerRegression
Overall: $($(if ($acceptancePassed) { "PASS" } else { "FAIL" }))
Error: $errorText
"@

            $utf8 = New-Object System.Text.UTF8Encoding($true)
            [System.IO.File]::WriteAllText($evidencePath, $evidence, $utf8)

            try {
                Set-Clipboard -Value $evidence
            }
            catch {
            }
        }
        catch {
        }
    }
}

if (-not $acceptancePassed) {
    Write-Host ""
    Write-Host "=============================================="
    Write-Host "M3.4 RUNTIME-INTEGRATION ACCEPTANCE: FAIL"
    Write-Host "=============================================="
    if ($null -ne $acceptanceError) {
        Write-Host "Type=$($acceptanceError.GetType().Name) HRESULT=0x$($acceptanceError.HResult.ToString('X8'))"
        Write-Host "Reason: $($acceptanceError.Message)"
    }
    if ($null -ne $evidencePath) {
        Write-Host "Evidence: $evidencePath"
    }
    throw "M3.4 runtime-integration acceptance failed."
}

Write-Host ""
Write-Host "=============================================="
Write-Host "M3.4 RUNTIME-INTEGRATION ACCEPTANCE: PASS"
Write-Host "=============================================="
Write-Host "Evidence: $evidencePath"
Write-Host "Evidence summary copied to clipboard."
