[CmdletBinding()]
param(
    [string]$DiagnosticRoot,
    [ValidateRange(30, 900)]
    [int]$StartupTimeoutSeconds = 300,
    [ValidateRange(5, 120)]
    [int]$StepTimeoutSeconds = 30,
    [ValidateRange(11, 60)]
    [int]$ProviderObservationSeconds = 12
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

# Physical measurement harness only. UI Automation is invoked against
# deterministic same-integrity fixtures through LocalCopilot's diagnostic-only
# command. The provider call is released during cleanup even after a failure.

$repoRoot = $null
$runRoot = $null
$sessionDirectory = $null
$appLogPath = $null
$metaPath = $null
$diagnosticBundlePath = $null
$acceptanceBundlePath = $null
$runnerProcess = $null
$appProcess = $null
$blockingTarget = $null
$recoveryTarget = $null
$gateEvent = $null
$enteredEvent = $null
$releaseEvent = $null
$acceptanceError = $null
$acceptancePassed = $false
$classification = "Inconclusive"
$recoveryState = "Inconclusive"
$recoveryOutcome = "NotObserved"
$recoveryReason = "NotObserved"
$failureSummaryPath = $null

function Test-M34Elevated {
    $identity =
        [Security.Principal.WindowsIdentity]::GetCurrent()

    $principal =
        New-Object System.Security.Principal.WindowsPrincipal($identity)

    return $principal.IsInRole(
        [System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function ConvertTo-M34SingleQuotedLiteral {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
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

function Wait-M34ProcessWindow {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)]
        [string]$Description,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

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

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    $regex =
        New-Object System.Text.RegularExpressions.Regex(
            $Pattern,
            [System.Text.RegularExpressions.RegexOptions]::Multiline)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $content = Get-M34FileContent -Path $Path

        if ($content.Length -ge $AfterOffset) {
            $match =
                $regex.Match(
                    $content.Substring($AfterOffset))

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

    throw "Timed out waiting for a required metadata event."
}

function Wait-M34OptionalLogMatch {
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

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    $regex =
        New-Object System.Text.RegularExpressions.Regex(
            $Pattern,
            [System.Text.RegularExpressions.RegexOptions]::Multiline)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $content = Get-M34FileContent -Path $Path

        if ($content.Length -ge $AfterOffset) {
            $match =
                $regex.Match(
                    $content.Substring($AfterOffset))

            if ($match.Success) {
                return $match
            }
        }

        if ($null -ne $RunnerProcess) {
            $RunnerProcess.Refresh()

            if ($RunnerProcess.HasExited) {
                return $null
            }
        }

        Start-Sleep -Milliseconds 75
    }

    return $null
}

function Assert-M34Pattern {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,
        [Parameter(Mandatory = $true)]
        [string]$Pattern,
        [Parameter(Mandatory = $true)]
        [string]$Failure
    )

    if (-not [Regex]::IsMatch(
            $Content,
            $Pattern,
            [Text.RegularExpressions.RegexOptions]::Multiline)) {
        throw $Failure
    }
}

function Start-M34Target {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PowerShellPath,
        [Parameter(Mandatory = $true)]
        [string]$StopPath,
        [Parameter(Mandatory = $true)]
        [string]$WindowTitle,
        [Parameter(Mandatory = $true)]
        [string]$ButtonText,
        [Parameter(Mandatory = $true)]
        [string]$FixtureSourcePath,
        [switch]$Blocking,
        [string]$GateEventName = "",
        [string]$EnteredEventName = "",
        [string]$ReleaseEventName = "",
        [string]$Sentinel = ""
    )

    $targetTemplate = @'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (__BLOCKING__) {
    Add-Type -Path __FIXTURE_SOURCE__ -ReferencedAssemblies @(
        "System.Windows.Forms.dll",
        "System.Drawing.dll",
        "Accessibility.dll"
    )
}

$form = New-Object System.Windows.Forms.Form
$form.Text = __WINDOW_TITLE__
$form.Width = 560
$form.Height = 240
$form.StartPosition = 'CenterScreen'

if (__BLOCKING__) {
    $button =
        New-Object LocalCopilotM34Fixture.BlockingNameButton(
            __GATE_EVENT__,
            __ENTERED_EVENT__,
            __RELEASE_EVENT__,
            __SENTINEL__)
}
else {
    $button = New-Object System.Windows.Forms.Button
    $button.AccessibleName = 'Controlled recovery provider'
}

$button.Text = __BUTTON_TEXT__
$button.Location = New-Object System.Drawing.Point(20, 20)
$button.AutoSize = $true

$label = New-Object System.Windows.Forms.Label
$label.Text = 'Controlled same-integrity UI Automation fixture'
$label.AccessibleName = 'Controlled provider fixture'
$label.Location = New-Object System.Drawing.Point(20, 80)
$label.AutoSize = $true

[void]$form.Controls.Add($button)
[void]$form.Controls.Add($label)

$stopPath = __STOP_PATH__
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 200
$timer.Add_Tick({
    if ([System.IO.File]::Exists($stopPath)) {
        $timer.Stop()
        $form.Close()
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

    $targetScript =
        $targetTemplate.Replace(
            "__BLOCKING__",
            $(if ($Blocking) { '$true' } else { '$false' }))

    $targetScript =
        $targetScript.Replace(
            "__WINDOW_TITLE__",
            (ConvertTo-M34SingleQuotedLiteral $WindowTitle))

    $targetScript =
        $targetScript.Replace(
            "__BUTTON_TEXT__",
            (ConvertTo-M34SingleQuotedLiteral $ButtonText))

    $targetScript =
        $targetScript.Replace(
            "__FIXTURE_SOURCE__",
            (ConvertTo-M34SingleQuotedLiteral $FixtureSourcePath))

    $targetScript =
        $targetScript.Replace(
            "__STOP_PATH__",
            (ConvertTo-M34SingleQuotedLiteral $StopPath))

    $targetScript =
        $targetScript.Replace(
            "__GATE_EVENT__",
            (ConvertTo-M34SingleQuotedLiteral $GateEventName))

    $targetScript =
        $targetScript.Replace(
            "__ENTERED_EVENT__",
            (ConvertTo-M34SingleQuotedLiteral $EnteredEventName))

    $targetScript =
        $targetScript.Replace(
            "__RELEASE_EVENT__",
            (ConvertTo-M34SingleQuotedLiteral $ReleaseEventName))

    $targetScript =
        $targetScript.Replace(
            "__SENTINEL__",
            (ConvertTo-M34SingleQuotedLiteral $Sentinel))

    $encodedCommand =
        [Convert]::ToBase64String(
            [Text.Encoding]::Unicode.GetBytes($targetScript))

    $arguments =
        @(
            "-NoProfile",
            "-Sta",
            "-WindowStyle",
            "Hidden",
            "-EncodedCommand",
            $encodedCommand
        )

    $process =
        Start-Process `
            -FilePath $PowerShellPath `
            -ArgumentList $arguments `
            -PassThru

    $windowParameters = @{
        Process = $process
        Description = $WindowTitle
        TimeoutSeconds = $StepTimeoutSeconds
    }

    $handle = Wait-M34ProcessWindow @windowParameters

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
        $Target.Process.Refresh()

        if (-not $Target.Process.HasExited) {
            New-Item -ItemType File -Force -Path $Target.StopPath |
                Out-Null

            [void]$Target.Process.WaitForExit(5000)
        }
    }
    catch {
    }

    try {
        $Target.Process.Refresh()

        if (-not $Target.Process.HasExited) {
            Stop-Process `
                -Id $Target.Process.Id `
                -Force `
                -ErrorAction SilentlyContinue
        }
    }
    catch {
    }

    Remove-Item `
        -LiteralPath $Target.StopPath `
        -Force `
        -ErrorAction SilentlyContinue
}

function Invoke-M34Button {
    param(
        [Parameter(Mandatory = $true)]
        [object]$AppRoot,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $condition =
        New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $Name)

    $button =
        $AppRoot.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition)

    if ($null -eq $button) {
        throw "A required LocalCopilot diagnostic command was not found."
    }

    if ($button.Current.ControlType -ne
        [System.Windows.Automation.ControlType]::Button) {
        throw "A required LocalCopilot diagnostic command was not a button."
    }

    $pattern =
        $button.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)

    ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
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

    [void][LocalCopilotM34Acceptance.WindowMethods]::ShowWindow(
        $Handle,
        9)

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        [void][LocalCopilotM34Acceptance.WindowMethods]::SetForegroundWindow(
            $Handle)

        if ([LocalCopilotM34Acceptance.WindowMethods]::GetForegroundWindow() -eq
            $Handle) {
            return
        }

        Start-Sleep -Milliseconds 100
    }

    throw "$Description could not become the foreground window."
}

function Set-M34AllowedTargetContext {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Target,
        [Parameter(Mandatory = $true)]
        [string]$Description,
        [Parameter(Mandatory = $true)]
        [string]$LogPath,
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$RunnerProcess,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    $foregroundParameters = @{
        Handle = $Target.Handle
        Description = $Description
        TimeoutSeconds = $TimeoutSeconds
    }

    Set-M34Foreground @foregroundParameters

    $contextPattern =
        '(?m)^.*\| CONTEXT\.APPLY \| .*pid={0} .*privacy=Allowed .*$' -f
            $Target.Process.Id

    $waitParameters = @{
        Path = $LogPath
        Pattern = $contextPattern
        TimeoutSeconds = $TimeoutSeconds
        RunnerProcess = $RunnerProcess
    }

    [void](Wait-M34LogMatch @waitParameters)
}

function Invoke-M34QueuedRequest {
    param(
        [Parameter(Mandatory = $true)]
        [object]$AppRoot,
        [Parameter(Mandatory = $true)]
        [string]$ButtonName,
        [Parameter(Mandatory = $true)]
        [string]$Operation,
        [Parameter(Mandatory = $true)]
        [ValidateSet("RootProbe", "SemanticSnapshot")]
        [string]$Kind,
        [Parameter(Mandatory = $true)]
        [string]$LogPath,
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$RunnerProcess,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds,
        [switch]$WaitForDiagnosticHold
    )

    $offset =
        (Get-M34FileContent -Path $LogPath).Length

    Invoke-M34Button -AppRoot $AppRoot -Name $ButtonName

    $beginPattern =
        '(?m)^.*\| UIA\.PROBE_BEGIN \| operation={0} .*$' -f
            [Regex]::Escape($Operation)

    $beginParameters = @{
        Path = $LogPath
        Pattern = $beginPattern
        AfterOffset = $offset
        TimeoutSeconds = $TimeoutSeconds
        RunnerProcess = $RunnerProcess
    }

    [void](Wait-M34LogMatch @beginParameters)

    $queuePattern =
        '(?m)^.*\| UIA\.QUEUE \| request=(\d+) epoch=(\d+) kind={0} .*

    $queueParameters = @{
        Path = $LogPath
        Pattern = $queuePattern
        AfterOffset = $offset
        TimeoutSeconds = $TimeoutSeconds
        RunnerProcess = $RunnerProcess
    }

    $queueMatch = Wait-M34LogMatch @queueParameters
    $requestId = $queueMatch.Groups[1].Value

    if ($WaitForDiagnosticHold) {
        $holdPattern =
            '(?m)^.*\| UIA\.DIAGNOSTIC_HOLD \| request={0} durationMs=2000.*$' -f
                [Regex]::Escape($requestId)

        $holdParameters = @{
            Path = $LogPath
            Pattern = $holdPattern
            AfterOffset = $offset
            TimeoutSeconds = $TimeoutSeconds
            RunnerProcess = $RunnerProcess
        }

        [void](Wait-M34LogMatch @holdParameters)
    }

    return [pscustomobject]@{
        RequestId = $requestId
        Epoch = $queueMatch.Groups[2].Value
        Kind = $Kind
        Offset = $offset
    }
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

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $directories =
            @(
                Get-ChildItem `
                    -LiteralPath $Root `
                    -Directory `
                    -ErrorAction SilentlyContinue
            )

        if ($directories.Count -eq 1) {
            return $directories[0].FullName
        }

        if ($directories.Count -gt 1) {
            throw "The acceptance root contains more than one session."
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

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $processes =
            @(Get-Process "LocalCopilot.App" -ErrorAction SilentlyContinue)

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

function Stop-M34App {
    param(
        [AllowNull()]
        [System.Diagnostics.Process]$Process
    )

    if ($null -eq $Process) {
        return
    }

    try {
        $Process.Refresh()

        if (-not $Process.HasExited) {
            $handle = [IntPtr]$Process.MainWindowHandle

            if ($handle -ne [IntPtr]::Zero) {
                [void][LocalCopilotM34Acceptance.WindowMethods]::PostMessage(
                    $handle,
                    0x0010,
                    [IntPtr]::Zero,
                    [IntPtr]::Zero)

                [void]$Process.WaitForExit(20000)
            }
        }
    }
    catch {
    }

    try {
        $Process.Refresh()

        if (-not $Process.HasExited) {
            Stop-Process `
                -Id $Process.Id `
                -Force `
                -ErrorAction SilentlyContinue
        }
    }
    catch {
    }
}

try {
    if ($env:OS -ne "Windows_NT") {
        throw "M3.4 provider-isolation measurement requires Windows."
    }

    if (Test-M34Elevated) {
        throw "Run the measurement from a normal, non-Administrator PowerShell."
    }

    $repoRoot = $PSScriptRoot

    if ([string]::IsNullOrWhiteSpace($repoRoot)) {
        $repoRoot = (Get-Location).Path
    }

    $repoRoot = [System.IO.Path]::GetFullPath($repoRoot)
    $runnerPath = Join-Path $repoRoot "run-debug.ps1"
    $workerPath =
        Join-Path $repoRoot "src\LocalCopilot.App\Services\UiAutomationProbeWorker.cs"
    $fixtureSourcePath =
        Join-Path $repoRoot "tests\fixtures\BlockingUiAutomationProvider.cs"

    if (-not (Test-Path -LiteralPath $runnerPath)) {
        throw "run-debug.ps1 was not found beside the measurement wrapper."
    }

    if (-not (Test-Path -LiteralPath $fixtureSourcePath)) {
        throw "The controlled provider fixture source was not found."
    }

    $branch =
        (git -C $repoRoot branch --show-current | Out-String).Trim()

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve the Git branch."
    }

    if ($branch -ne "dev/m3-4-2-provider-isolation") {
        throw "Run the measurement only from dev/m3-4-2-provider-isolation."
    }

    $head =
        (git -C $repoRoot rev-parse HEAD | Out-String).Trim()

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve the Git HEAD."
    }

    $gitStatus =
        git -C $repoRoot status --short |
        Out-String -Width 4096

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve the Git working-tree status."
    }

    if (-not [string]::IsNullOrWhiteSpace($gitStatus)) {
        throw "M3.4 provider-isolation measurement requires a clean working tree."
    }

    $workerSource = Get-M34FileContent -Path $workerPath

    Assert-M34Pattern `
        -Content $workerSource `
        -Pattern 'NativeTransactionTimeoutMilliseconds\s*=\s*1500;' `
        -Failure "The measurement no longer matches the configured native transaction timeout."

    Assert-M34Pattern `
        -Content $workerSource `
        -Pattern 'WorkerJoinTimeoutMilliseconds\s*=\s*5000;' `
        -Failure "The measurement no longer matches the configured worker join timeout."

    $existingApps =
        @(Get-Process "LocalCopilot.App" -ErrorAction SilentlyContinue)

    if ($existingApps.Count -ne 0) {
        throw "Close the existing LocalCopilot process before measurement."
    }

    $windowsPowerShell =
        Join-Path `
            $env:SystemRoot `
            "System32\WindowsPowerShell\v1.0\powershell.exe"

    if (-not (Test-Path -LiteralPath $windowsPowerShell)) {
        throw "Windows PowerShell 5.1 was not found."
    }

    if ([string]::IsNullOrWhiteSpace($DiagnosticRoot)) {
        $DiagnosticRoot =
            Join-Path `
                $repoRoot `
                ".localcopilot\diagnostics"
    }
    elseif (-not [System.IO.Path]::IsPathRooted($DiagnosticRoot)) {
        $DiagnosticRoot = Join-Path $repoRoot $DiagnosticRoot
    }

    $diagnosticBase =
        [System.IO.Path]::GetFullPath($DiagnosticRoot)

    $runRoot =
        Join-Path `
            $diagnosticBase `
            (
                "m3-4-provider-isolation-" +
                [Guid]::NewGuid().ToString("N")
            )

    New-Item -ItemType Directory -Path $runRoot -Force |
        Out-Null

    if ($runnerPath.Contains('"') -or $runRoot.Contains('"')) {
        throw "Measurement paths cannot contain a quote character."
    }

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

    $eventSuffix = [Guid]::NewGuid().ToString("N")
    $gateEventName = "Local\LocalCopilotM34Gate_" + $eventSuffix
    $enteredEventName = "Local\LocalCopilotM34Entered_" + $eventSuffix
    $releaseEventName = "Local\LocalCopilotM34Release_" + $eventSuffix

    $gateEvent =
        New-Object Threading.EventWaitHandle(
            $false,
            [Threading.EventResetMode]::ManualReset,
            $gateEventName)

    $enteredEvent =
        New-Object Threading.EventWaitHandle(
            $false,
            [Threading.EventResetMode]::ManualReset,
            $enteredEventName)

    $releaseEvent =
        New-Object Threading.EventWaitHandle(
            $false,
            [Threading.EventResetMode]::ManualReset,
            $releaseEventName)

    $sentinel =
        "M34_PROVIDER_" +
        [Guid]::NewGuid().ToString("N") +
        "_CONTENT"

    Write-Host ""
    Write-Host "=============================================="
    Write-Host "M3.4 PROVIDER-ISOLATION MEASUREMENT"
    Write-Host "=============================================="
    Write-Host "Starting controlled same-integrity providers..."

    $blockingParameters = @{
        PowerShellPath = $windowsPowerShell
        StopPath = (Join-Path $runRoot "blocking-target.stop")
        WindowTitle = "LocalCopilot M3.4 blocking provider"
        ButtonText = "Controlled blocking provider"
        FixtureSourcePath = $fixtureSourcePath
        Blocking = $true
        GateEventName = $gateEventName
        EnteredEventName = $enteredEventName
        ReleaseEventName = $releaseEventName
        Sentinel = $sentinel
    }

    $blockingTarget = Start-M34Target @blockingParameters

    $recoveryParameters = @{
        PowerShellPath = $windowsPowerShell
        StopPath = (Join-Path $runRoot "recovery-target.stop")
        WindowTitle = "LocalCopilot M3.4 recovery provider"
        ButtonText = "Controlled recovery provider"
        FixtureSourcePath = $fixtureSourcePath
    }

    $recoveryTarget = Start-M34Target @recoveryParameters

    $runnerArguments =
        @(
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            ('"{0}"' -f $runnerPath),
            "-DiagnosticRoot",
            ('"{0}"' -f $runRoot),
            "-EnableUiText",
            "-Milestone",
            "M3.4"
        )

    $runnerProcess =
        Start-Process `
            -FilePath $windowsPowerShell `
            -ArgumentList $runnerArguments `
            -NoNewWindow `
            -PassThru

    $sessionParameters = @{
        Root = $runRoot
        RunnerProcess = $runnerProcess
        TimeoutSeconds = $StartupTimeoutSeconds
    }

    $sessionDirectory = Wait-M34RunnerSession @sessionParameters
    $appLogPath = Join-Path $sessionDirectory "app.log"
    $metaPath = Join-Path $sessionDirectory "session-meta.txt"
    $diagnosticBundlePath =
        Join-Path $sessionDirectory "diagnostic-bundle.txt"
    $acceptanceBundlePath =
        Join-Path $sessionDirectory "m3-4-provider-isolation-bundle.txt"

    $appParameters = @{
        RunnerProcess = $runnerProcess
        TimeoutSeconds = $StartupTimeoutSeconds
    }

    $appProcess = Wait-M34AppProcess @appParameters

    $appWindowParameters = @{
        Process = $appProcess
        Description = "LocalCopilot"
        TimeoutSeconds = $StepTimeoutSeconds
    }

    $appHandle = Wait-M34ProcessWindow @appWindowParameters

    $schemaParameters = @{
        Path = $appLogPath
        Pattern = '^schema=1 .*uiTextEnabled=True\r?$'
        TimeoutSeconds = $StepTimeoutSeconds
        RunnerProcess = $runnerProcess
    }

    [void](Wait-M34LogMatch @schemaParameters)

    $appRoot =
        [System.Windows.Automation.AutomationElement]::FromHandle(
            $appHandle)

    if ($null -eq $appRoot) {
        throw "LocalCopilot UI Automation root was unavailable."
    }

    $armOffset =
        (Get-M34FileContent -Path $appLogPath).Length

    Invoke-M34Button -AppRoot $appRoot -Name "Arm auto sensing"

    $armParameters = @{
        Path = $appLogPath
        Pattern = '(?m)^.*\| COORD\.OBSERVER_START \| observerRunning=True.*$'
        AfterOffset = $armOffset
        TimeoutSeconds = $StepTimeoutSeconds
        RunnerProcess = $runnerProcess
    }

    [void](Wait-M34LogMatch @armParameters)

    $blockingContextParameters = @{
        Target = $blockingTarget
        Description = "blocking provider"
        LogPath = $appLogPath
        RunnerProcess = $runnerProcess
        TimeoutSeconds = $StepTimeoutSeconds
    }

    Set-M34AllowedTargetContext @blockingContextParameters

    Write-Host "[RUN] Entering a real cross-process blocking Name provider call"

    $blockingRequestParameters = @{
        AppRoot = $appRoot
        ButtonName = "Measure M3.4 controlled provider isolation"
        Operation = "uia_provider_isolation"
        Kind = "SemanticSnapshot"
        LogPath = $appLogPath
        RunnerProcess = $runnerProcess
        TimeoutSeconds = $StepTimeoutSeconds
        WaitForDiagnosticHold = $true
    }

    $blockingRequest =
        Invoke-M34QueuedRequest @blockingRequestParameters

    $providerClock =
        [System.Diagnostics.Stopwatch]::StartNew()

    [void]$gateEvent.Set()

    if (-not $enteredEvent.WaitOne(
            $StepTimeoutSeconds * 1000)) {
        throw "The controlled provider did not enter its blocking Name getter."
    }

    $providerEnteredMilliseconds =
        $providerClock.ElapsedMilliseconds

    $integrityPattern =
        '(?m)^.*\| UIA\.INTEGRITY_CHECK \| request={0} currentRid=(\d+) targetRid=(\d+) inspected=True mayRead=True .*$' -f
            [Regex]::Escape($blockingRequest.RequestId)

    $integrityParameters = @{
        Path = $appLogPath
        Pattern = $integrityPattern
        AfterOffset = $blockingRequest.Offset
        TimeoutSeconds = $StepTimeoutSeconds
        RunnerProcess = $runnerProcess
    }

    $integrityMatch = Wait-M34LogMatch @integrityParameters
    $currentRid = $integrityMatch.Groups[1].Value
    $targetRid = $integrityMatch.Groups[2].Value

    if ($currentRid -ne $targetRid) {
        throw "The controlled provider was not at the client's integrity level."
    }

    Write-Host "[PASS] Blocking call entered at the same integrity level"
    Write-Host "[RUN] Cancelling its epoch and queueing a healthy provider"

    $recoveryContextParameters = @{
        Target = $recoveryTarget
        Description = "recovery provider"
        LogPath = $appLogPath
        RunnerProcess = $runnerProcess
        TimeoutSeconds = $StepTimeoutSeconds
    }

    Set-M34AllowedTargetContext @recoveryContextParameters

    $recoveryRequestParameters = @{
        AppRoot = $appRoot
        ButtonName = "Probe UI Automation root (M3.1 regression)"
        Operation = "uia_root_probe"
        Kind = "RootProbe"
        LogPath = $appLogPath
        RunnerProcess = $runnerProcess
        TimeoutSeconds = $StepTimeoutSeconds
    }

    $recoveryRequest =
        Invoke-M34QueuedRequest @recoveryRequestParameters

    # Slice 3 automatic enrichment can legitimately queue a same-epoch
    # SemanticSnapshot after the recovery RootProbe. That replacement is part
    # of the product path under test, not a provider failure. Follow only the
    # explicit supersession chain for this recovery request and accept recovery
    # only when a same-epoch replacement completes Available on a worker thread
    # with identity revalidated. This preserves the Slice 2 architecture
    # invariant without racing the Slice 3 enrichment scheduler.
    $recoveryDeadline =
        [DateTimeOffset]::UtcNow.AddSeconds($ProviderObservationSeconds)

    $recoveryEvidenceRequestId = $recoveryRequest.RequestId
    $recoveryEvidenceKind = $recoveryRequest.Kind
    $recoveryObserved = $false
    $recoveryOutcome = "NotObserved"
    $recoveryReason = "NotObserved"
    $recoveryWorkerThread = "0"
    $recoveryIdentityRevalidated = "False"
    $supersessionDepth = 0

    while ([DateTimeOffset]::UtcNow -lt $recoveryDeadline) {
        $currentLog = Get-M34FileContent -Path $appLogPath
        $tail =
            if ($currentLog.Length -ge $recoveryRequest.Offset) {
                $currentLog.Substring($recoveryRequest.Offset)
            }
            else {
                ""
            }

        if ($recoveryEvidenceKind -eq "RootProbe") {
            $resultPattern =
                '(?m)^.*\| UIA\.PROBE_RESULT \| request={0} epoch={1} outcome=(\w+) reason=(\w+) .*workerThread=(\d+) identityRevalidated=(True|False) .*$' -f
                    [Regex]::Escape($recoveryEvidenceRequestId),
                    [Regex]::Escape($recoveryRequest.Epoch)

            $resultMatch = [Regex]::Match($tail, $resultPattern)

            if ($resultMatch.Success) {
                $recoveryOutcome = $resultMatch.Groups[1].Value
                $recoveryReason = $resultMatch.Groups[2].Value
                $recoveryWorkerThread = $resultMatch.Groups[3].Value
                $recoveryIdentityRevalidated = $resultMatch.Groups[4].Value

                if ($recoveryOutcome -eq "Available" -and
                    $recoveryReason -eq "RootResolved" -and
                    $recoveryWorkerThread -ne "0" -and
                    $recoveryIdentityRevalidated -eq "True") {
                    $recoveryObserved = $true
                    break
                }

                if ($recoveryOutcome -ne "Cancelled" -or
                    $recoveryReason -ne "Superseded") {
                    throw (
                        "The healthy-provider recovery returned outcome=" +
                        $recoveryOutcome +
                        " reason=" +
                        $recoveryReason +
                        " instead of Available/RootResolved.")
                }
            }
        }
        else {
            $completionPattern =
                '(?m)^.*\| UIA\.REQUEST_COMPLETE \| request={0} epoch={1} kind={2} outcome=(\w+) reason=(\w+) .*workerThread=(\d+) identityRevalidated=(True|False) .*$' -f
                    [Regex]::Escape($recoveryEvidenceRequestId),
                    [Regex]::Escape($recoveryRequest.Epoch),
                    [Regex]::Escape($recoveryEvidenceKind)

            $completionMatch = [Regex]::Match($tail, $completionPattern)

            if ($completionMatch.Success) {
                $recoveryOutcome = $completionMatch.Groups[1].Value
                $recoveryReason = $completionMatch.Groups[2].Value
                $recoveryWorkerThread = $completionMatch.Groups[3].Value
                $recoveryIdentityRevalidated = $completionMatch.Groups[4].Value

                if ($recoveryOutcome -eq "Available" -and
                    ($recoveryReason -eq "RootResolved" -or
                     $recoveryReason -eq "SnapshotCaptured") -and
                    $recoveryWorkerThread -ne "0" -and
                    $recoveryIdentityRevalidated -eq "True") {
                    $recoveryObserved = $true
                    break
                }

                if ($recoveryOutcome -ne "Cancelled" -or
                    $recoveryReason -ne "Superseded") {
                    throw (
                        "The same-epoch recovery replacement returned outcome=" +
                        $recoveryOutcome +
                        " reason=" +
                        $recoveryReason +
                        ".")
                }
            }
        }

        $replacementPattern =
            '(?m)^.*\| UIA\.QUEUE \| request=(\d+) epoch={0} kind=(RootProbe|SemanticSnapshot) replacedRequest={1} replacedEpoch={0}.*$' -f
                [Regex]::Escape($recoveryRequest.Epoch),
                [Regex]::Escape($recoveryEvidenceRequestId)

        $replacementMatch = [Regex]::Match($tail, $replacementPattern)

        if ($replacementMatch.Success) {
            $recoveryEvidenceRequestId = $replacementMatch.Groups[1].Value
            $recoveryEvidenceKind = $replacementMatch.Groups[2].Value
            $supersessionDepth++

            if ($supersessionDepth -gt 8) {
                throw "The healthy-provider recovery exceeded the bounded supersession chain."
            }

            continue
        }

        $runnerProcess.Refresh()
        if ($runnerProcess.HasExited) {
            throw "The diagnostic runner exited while waiting for healthy-provider recovery."
        }

        Start-Sleep -Milliseconds 75
    }

    $recoveryElapsedMilliseconds =
        $providerClock.ElapsedMilliseconds

    if ($recoveryObserved) {
        $blockingCompletionPattern =
            '(?m)^.*\| UIA\.REQUEST_COMPLETE \| request={0} .*workerThread=\d+ .*$' -f
                [Regex]::Escape($blockingRequest.RequestId)

        $blockingCompletionParameters = @{
            Path = $appLogPath
            Pattern = $blockingCompletionPattern
            AfterOffset = $blockingRequest.Offset
            TimeoutSeconds = $StepTimeoutSeconds
            RunnerProcess = $runnerProcess
        }

        [void](Wait-M34LogMatch @blockingCompletionParameters)

        if ($recoveryElapsedMilliseconds -le 10000) {
            $recoveryState = "RecoveredWithinDeadline"
            $classification = "InProcessCandidate"
            Write-Host (
                "[PASS] Same-worker recovery completed within the request deadline " +
                "(request=" +
                $recoveryEvidenceRequestId +
                " kind=" +
                $recoveryEvidenceKind +
                " supersessionDepth=" +
                $supersessionDepth +
                ")")
        }
        else {
            $recoveryState = "RecoveredAfterDeadline"
            $classification = "RestartableHelperRequired"
            Write-Host "[PASS] Same-worker recovery occurred only after the request deadline"
        }
    }
    else {
        $currentLog = Get-M34FileContent -Path $appLogPath

        $blockingCompleted =
            [Regex]::IsMatch(
                $currentLog.Substring($blockingRequest.Offset),
                ('(?m)^.*\| UIA\.REQUEST_COMPLETE \| request={0} .*$' -f
                    [Regex]::Escape($blockingRequest.RequestId)))

        if ($blockingCompleted) {
            throw (
                "The blocking request returned, but no same-epoch healthy-provider " +
                "request completed Available within the observation bound.")
        }

        $recoveryState = "WorkerWedgedBeyondObservation"
        $classification = "RestartableHelperRequired"
        Write-Host "[PASS] Epoch cancellation and the request deadline could not free the worker"
    }

    Write-Host "[RUN] Closing LocalCopilot while the provider remains blocked"

    $shutdownClock =
        [System.Diagnostics.Stopwatch]::StartNew()

    [void][LocalCopilotM34Acceptance.WindowMethods]::PostMessage(
        $appHandle,
        0x0010,
        [IntPtr]::Zero,
        [IntPtr]::Zero)

    if (-not $appProcess.WaitForExit(25000)) {
        throw "LocalCopilot did not close within the bounded shutdown watchdog."
    }

    $shutdownElapsedMilliseconds =
        $shutdownClock.ElapsedMilliseconds

    if (-not $runnerProcess.WaitForExit(45000)) {
        throw "The diagnostic runner did not finalize after application exit."
    }

    $finalLog = Get-M34FileContent -Path $appLogPath
    $meta = Get-M34FileContent -Path $metaPath
    $diagnosticBundle =
        Get-M34FileContent -Path $diagnosticBundlePath

    Assert-M34Pattern `
        -Content $meta `
        -Pattern '(?m)^Milestone: M3\.4\r?$' `
        -Failure "The diagnostic session was not labeled M3.4."

    Assert-M34Pattern `
        -Content $meta `
        -Pattern '(?m)^Runner elevated: False\r?$' `
        -Failure "The diagnostic runner was not non-elevated."

    Assert-M34Pattern `
        -Content $meta `
        -Pattern '(?m)^Build Result: PASS\r?$' `
        -Failure "The strict Windows build did not pass."

    Assert-M34Pattern `
        -Content $meta `
        -Pattern '(?m)^Application Result: PASS\r?$' `
        -Failure "The diagnostic application run did not pass."

    Assert-M34Pattern `
        -Content $meta `
        -Pattern '(?m)^Runner Result: PASS\r?$' `
        -Failure "The diagnostic runner did not pass."

    Assert-M34Pattern `
        -Content $finalLog `
        -Pattern '(?m)^.*\| COORD\.STOP \| reason=application_shutdown HasThreadAccess=True.*$' `
        -Failure "Coordinator shutdown evidence was missing."

    Assert-M34Pattern `
        -Content $finalLog `
        -Pattern '(?m)^.*\| COORD\.DISPOSE \| Coordinator disposed\..*$' `
        -Failure "Coordinator disposal evidence was missing."

    $joined =
        [Regex]::IsMatch(
            $finalLog,
            '(?m)^.*\| UIA\.WORKER_DISPOSE \| started=True joined=True.*$')

    $notJoined =
        [Regex]::IsMatch(
            $finalLog,
            '(?m)^.*\| UIA\.WORKER_DISPOSE \| started=True joined=False.*$')

    if ($recoveryObserved) {
        if (-not $joined -or $notJoined) {
            throw "The recovered worker did not join cleanly."
        }
    }
    else {
        if ($joined -or -not $notJoined) {
            throw "The blocked in-process worker did not produce joined=False evidence."
        }

        $pendingStoppedPattern =
            '(?m)^.*\| UIA\.REQUEST_COMPLETE \| request={0} .*outcome=Cancelled reason=WorkerStopped .*workerThread=0 .*$' -f
                [Regex]::Escape($recoveryRequest.RequestId)

        Assert-M34Pattern `
            -Content $finalLog `
            -Pattern $pendingStoppedPattern `
            -Failure "The pending recovery request was not cancelled during shutdown."
    }

    if ([string]::IsNullOrWhiteSpace($diagnosticBundle)) {
        throw "The whitelisted diagnostic bundle was not produced."
    }

    foreach ($source in @($finalLog, $meta, $diagnosticBundle)) {
        if ($source.IndexOf(
                $sentinel,
                [StringComparison]::Ordinal) -ge 0) {
            throw "Controlled provider content escaped into diagnostic evidence."
        }
    }

    $sessionMatch =
        [Regex]::Match(
            $meta,
            '(?m)^Session ID: ([0-9a-fA-F-]+)\r?$')

    if (-not $sessionMatch.Success) {
        throw "The diagnostic session ID was unavailable."
    }

    $sessionId = $sessionMatch.Groups[1].Value
    $workerJoinedText =
        if ($joined) { "True" } else { "False" }

    $builder =
        New-Object System.Text.StringBuilder

    [void]$builder.AppendLine(
        "=================================================")
    [void]$builder.AppendLine(
        "M3.4 PROVIDER-ISOLATION EVIDENCE")
    [void]$builder.AppendLine(
        "=================================================")
    [void]$builder.AppendLine("Bundle schema: 1")
    [void]$builder.AppendLine("Session ID: " + $sessionId)
    [void]$builder.AppendLine("Branch: " + $branch)
    [void]$builder.AppendLine("HEAD: " + $head)
    [void]$builder.AppendLine("Working tree: clean")
    [void]$builder.AppendLine("Runner elevated: False")
    [void]$builder.AppendLine("Provider integrity RID: " + $targetRid)
    [void]$builder.AppendLine("Client integrity RID: " + $currentRid)
    [void]$builder.AppendLine("Same integrity: True")
    [void]$builder.AppendLine("Provider call entered: True")
    [void]$builder.AppendLine(
        "Provider entered elapsed ms: " +
        $providerEnteredMilliseconds)
    [void]$builder.AppendLine(
        "Configured native transaction timeout ms: 1500")
    [void]$builder.AppendLine(
        "Request deadline ms: 10000")
    [void]$builder.AppendLine(
        "Recovery observation bound ms: " +
        ($ProviderObservationSeconds * 1000))
    [void]$builder.AppendLine(
        "Healthy provider recovered before release: " +
        $recoveryObserved)
    [void]$builder.AppendLine(
        "Recovery observation elapsed ms: " +
        $recoveryElapsedMilliseconds)
    [void]$builder.AppendLine(
        "Shutdown elapsed ms: " +
        $shutdownElapsedMilliseconds)
    [void]$builder.AppendLine(
        "Worker joined: " +
        $workerJoinedText)
    [void]$builder.AppendLine(
        "Recovery state: " +
        $recoveryState)
    [void]$builder.AppendLine(
        "Architecture classification: " +
        $classification)
    [void]$builder.AppendLine(
        "Provider content: redacted")
    [void]$builder.AppendLine(
        "Controlled sentinel scan: PASS")
    [void]$builder.AppendLine("")
    [void]$builder.AppendLine(
        "The following source is the existing explicit-whitelist diagnostic bundle.")
    [void]$builder.AppendLine("")
    [void]$builder.AppendLine($diagnosticBundle)

    $evidence = $builder.ToString()

    if ($evidence.IndexOf(
            $sentinel,
            [StringComparison]::Ordinal) -ge 0) {
        throw "Controlled provider content escaped into the final evidence bundle."
    }

    [System.IO.File]::WriteAllText(
        $acceptanceBundlePath,
        $evidence,
        (New-Object System.Text.UTF8Encoding($true)))

    try {
        Get-Content -LiteralPath $acceptanceBundlePath -Raw |
            Set-Clipboard
    }
    catch {
        Write-Host "Evidence created; clipboard copy failed."
    }

    $acceptancePassed = $true
}
catch {
    $acceptanceError = $_.Exception

    if (-not [string]::IsNullOrWhiteSpace($sessionDirectory)) {
        try {
            $failureSummaryPath =
                Join-Path $sessionDirectory "provider-failure-summary.txt"

            $failureBuilder =
                New-Object System.Text.StringBuilder

            [void]$failureBuilder.AppendLine(
                "=================================================")
            [void]$failureBuilder.AppendLine(
                "M3.4 PROVIDER-ISOLATION FAILURE SUMMARY")
            [void]$failureBuilder.AppendLine(
                "=================================================")
            [void]$failureBuilder.AppendLine("Schema: 1")
            [void]$failureBuilder.AppendLine(
                "Failure UTC: " + [DateTimeOffset]::UtcNow.ToString("o"))
            [void]$failureBuilder.AppendLine(
                "Exception type: " + $acceptanceError.GetType().Name)
            [void]$failureBuilder.AppendLine(
                ("Exception HRESULT: 0x{0:X8}" -f $acceptanceError.HResult))
            [void]$failureBuilder.AppendLine(
                "Exception message: " + $acceptanceError.Message)
            [void]$failureBuilder.AppendLine(
                "Recovery state: " + $recoveryState)
            [void]$failureBuilder.AppendLine(
                "Recovery outcome: " + $recoveryOutcome)
            [void]$failureBuilder.AppendLine(
                "Recovery reason: " + $recoveryReason)

            if ($null -ne $blockingRequest) {
                [void]$failureBuilder.AppendLine(
                    "Blocking request ID: " + $blockingRequest.RequestId)
            }

            if ($null -ne $recoveryRequest) {
                [void]$failureBuilder.AppendLine(
                    "Recovery request ID: " + $recoveryRequest.RequestId)
                [void]$failureBuilder.AppendLine(
                    "Recovery epoch: " + $recoveryRequest.Epoch)
            }

            if (-not [string]::IsNullOrWhiteSpace($recoveryEvidenceRequestId)) {
                [void]$failureBuilder.AppendLine(
                    "Recovery evidence request ID: " + $recoveryEvidenceRequestId)
                [void]$failureBuilder.AppendLine(
                    "Recovery evidence kind: " + $recoveryEvidenceKind)
                [void]$failureBuilder.AppendLine(
                    "Recovery supersession depth: " + $supersessionDepth)
            }

            [void]$failureBuilder.AppendLine("")
            [void]$failureBuilder.AppendLine(
                "Filtered metadata tail (content-free diagnostic events only):")

            $failureLog = Get-M34FileContent -Path $appLogPath
            if (-not [string]::IsNullOrWhiteSpace($failureLog)) {
                $metadataPattern =
                    '\| (CONTEXT\.APPLY|COORD\.|UIA\.(PROBE_BEGIN|QUEUE|DIAGNOSTIC_HOLD|INTEGRITY_CHECK|REQUEST_START|REQUEST_COMPLETE|PROBE_RESULT|WORKER_START|WORKER_STOP|WORKER_DISPOSE|REQUEST_CANCELLED|QUEUE_REJECT))'

                $metadataLines =
                    @(
                        $failureLog -split "\r?\n" |
                        Where-Object {
                            $_ -match $metadataPattern
                        } |
                        Select-Object -Last 120
                    )

                foreach ($metadataLine in $metadataLines) {
                    [void]$failureBuilder.AppendLine($metadataLine)
                }
            }
            else {
                [void]$failureBuilder.AppendLine(
                    "app.log was unavailable or empty.")
            }

            [System.IO.File]::WriteAllText(
                $failureSummaryPath,
                $failureBuilder.ToString(),
                (New-Object System.Text.UTF8Encoding($true)))
        }
        catch {
            $failureSummaryPath = $null
        }
    }
}
finally {
    if ($null -ne $releaseEvent) {
        try {
            [void]$releaseEvent.Set()
        }
        catch {
        }
    }

    Stop-M34Target -Target $blockingTarget
    Stop-M34Target -Target $recoveryTarget
    Stop-M34App -Process $appProcess

    if ($null -ne $runnerProcess) {
        try {
            $runnerProcess.Refresh()

            if (-not $runnerProcess.HasExited) {
                [void]$runnerProcess.WaitForExit(15000)
            }

            if (-not $runnerProcess.HasExited) {
                Stop-Process `
                    -Id $runnerProcess.Id `
                    -Force `
                    -ErrorAction SilentlyContinue
            }
        }
        catch {
        }
    }

    foreach ($eventHandle in @(
            $gateEvent,
            $enteredEvent,
            $releaseEvent)) {
        if ($null -ne $eventHandle) {
            try {
                $eventHandle.Dispose()
            }
            catch {
            }
        }
    }
}

Write-Host ""
Write-Host "=============================================="

if ($acceptancePassed) {
    Write-Host "M3.4 PROVIDER-ISOLATION MEASUREMENT: PASS"
    Write-Host "=============================================="
    Write-Host ("Architecture classification: " + $classification)
    Write-Host "Same-integrity blocking call entered: PASS"
    Write-Host "Healthy-provider recovery measured: PASS"
    Write-Host "Bounded application shutdown measured: PASS"
    Write-Host "Whitelisted sentinel scan: PASS"
    Write-Host ""
    Write-Host "Evidence bundle:"
    Write-Host $acceptanceBundlePath
    Write-Host ""
    Write-Host "The complete evidence bundle is on the clipboard."
}
else {
    Write-Host "M3.4 PROVIDER-ISOLATION MEASUREMENT: FAIL"
    Write-Host "=============================================="

    if ($null -ne $acceptanceError) {
        Write-Host (
            "Type={0} HRESULT=0x{1:X8}" -f
                $acceptanceError.GetType().Name,
                $acceptanceError.HResult)
        Write-Host ("Reason: " + $acceptanceError.Message)
        Write-Host ("Recovery outcome: " + $recoveryOutcome)
        Write-Host ("Recovery reason: " + $recoveryReason)
    }

    if (-not [string]::IsNullOrWhiteSpace($failureSummaryPath)) {
        Write-Host ""
        Write-Host "Failure summary:"
        Write-Host $failureSummaryPath
    }

    if (-not [string]::IsNullOrWhiteSpace($sessionDirectory)) {
        Write-Host ""
        Write-Host "Evidence directory:"
        Write-Host $sessionDirectory
    }

    throw "M3.4 provider-isolation measurement failed."
}
 -f
            [Regex]::Escape($Kind)

    $queueParameters = @{
        Path = $LogPath
        Pattern = $queuePattern
        AfterOffset = $offset
        TimeoutSeconds = $TimeoutSeconds
        RunnerProcess = $RunnerProcess
    }

    $queueMatch = Wait-M34LogMatch @queueParameters
    $requestId = $queueMatch.Groups[1].Value

    if ($WaitForDiagnosticHold) {
        $holdPattern =
            '(?m)^.*\| UIA\.DIAGNOSTIC_HOLD \| request={0} durationMs=2000.*$' -f
                [Regex]::Escape($requestId)

        $holdParameters = @{
            Path = $LogPath
            Pattern = $holdPattern
            AfterOffset = $offset
            TimeoutSeconds = $TimeoutSeconds
            RunnerProcess = $RunnerProcess
        }

        [void](Wait-M34LogMatch @holdParameters)
    }

    return [pscustomobject]@{
        RequestId = $requestId
        Offset = $offset
    }
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

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $directories =
            @(
                Get-ChildItem `
                    -LiteralPath $Root `
                    -Directory `
                    -ErrorAction SilentlyContinue
            )

        if ($directories.Count -eq 1) {
            return $directories[0].FullName
        }

        if ($directories.Count -gt 1) {
            throw "The acceptance root contains more than one session."
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

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $processes =
            @(Get-Process "LocalCopilot.App" -ErrorAction SilentlyContinue)

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

function Stop-M34App {
    param(
        [AllowNull()]
        [System.Diagnostics.Process]$Process
    )

    if ($null -eq $Process) {
        return
    }

    try {
        $Process.Refresh()

        if (-not $Process.HasExited) {
            $handle = [IntPtr]$Process.MainWindowHandle

            if ($handle -ne [IntPtr]::Zero) {
                [void][LocalCopilotM34Acceptance.WindowMethods]::PostMessage(
                    $handle,
                    0x0010,
                    [IntPtr]::Zero,
                    [IntPtr]::Zero)

                [void]$Process.WaitForExit(20000)
            }
        }
    }
    catch {
    }

    try {
        $Process.Refresh()

        if (-not $Process.HasExited) {
            Stop-Process `
                -Id $Process.Id `
                -Force `
                -ErrorAction SilentlyContinue
        }
    }
    catch {
    }
}

try {
    if ($env:OS -ne "Windows_NT") {
        throw "M3.4 provider-isolation measurement requires Windows."
    }

    if (Test-M34Elevated) {
        throw "Run the measurement from a normal, non-Administrator PowerShell."
    }

    $repoRoot = $PSScriptRoot

    if ([string]::IsNullOrWhiteSpace($repoRoot)) {
        $repoRoot = (Get-Location).Path
    }

    $repoRoot = [System.IO.Path]::GetFullPath($repoRoot)
    $runnerPath = Join-Path $repoRoot "run-debug.ps1"
    $workerPath =
        Join-Path $repoRoot "src\LocalCopilot.App\Services\UiAutomationProbeWorker.cs"
    $fixtureSourcePath =
        Join-Path $repoRoot "tests\fixtures\BlockingUiAutomationProvider.cs"

    if (-not (Test-Path -LiteralPath $runnerPath)) {
        throw "run-debug.ps1 was not found beside the measurement wrapper."
    }

    if (-not (Test-Path -LiteralPath $fixtureSourcePath)) {
        throw "The controlled provider fixture source was not found."
    }

    $branch =
        (git -C $repoRoot branch --show-current | Out-String).Trim()

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve the Git branch."
    }

    if ($branch -ne "dev/m3-4-2-provider-isolation") {
        throw "Run the measurement only from dev/m3-4-2-provider-isolation."
    }

    $head =
        (git -C $repoRoot rev-parse HEAD | Out-String).Trim()

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve the Git HEAD."
    }

    $gitStatus =
        git -C $repoRoot status --short |
        Out-String -Width 4096

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve the Git working-tree status."
    }

    if (-not [string]::IsNullOrWhiteSpace($gitStatus)) {
        throw "M3.4 provider-isolation measurement requires a clean working tree."
    }

    $workerSource = Get-M34FileContent -Path $workerPath

    Assert-M34Pattern `
        -Content $workerSource `
        -Pattern 'NativeTransactionTimeoutMilliseconds\s*=\s*1500;' `
        -Failure "The measurement no longer matches the configured native transaction timeout."

    Assert-M34Pattern `
        -Content $workerSource `
        -Pattern 'WorkerJoinTimeoutMilliseconds\s*=\s*5000;' `
        -Failure "The measurement no longer matches the configured worker join timeout."

    $existingApps =
        @(Get-Process "LocalCopilot.App" -ErrorAction SilentlyContinue)

    if ($existingApps.Count -ne 0) {
        throw "Close the existing LocalCopilot process before measurement."
    }

    $windowsPowerShell =
        Join-Path `
            $env:SystemRoot `
            "System32\WindowsPowerShell\v1.0\powershell.exe"

    if (-not (Test-Path -LiteralPath $windowsPowerShell)) {
        throw "Windows PowerShell 5.1 was not found."
    }

    if ([string]::IsNullOrWhiteSpace($DiagnosticRoot)) {
        $DiagnosticRoot =
            Join-Path `
                $repoRoot `
                ".localcopilot\diagnostics"
    }
    elseif (-not [System.IO.Path]::IsPathRooted($DiagnosticRoot)) {
        $DiagnosticRoot = Join-Path $repoRoot $DiagnosticRoot
    }

    $diagnosticBase =
        [System.IO.Path]::GetFullPath($DiagnosticRoot)

    $runRoot =
        Join-Path `
            $diagnosticBase `
            (
                "m3-4-provider-isolation-" +
                [Guid]::NewGuid().ToString("N")
            )

    New-Item -ItemType Directory -Path $runRoot -Force |
        Out-Null

    if ($runnerPath.Contains('"') -or $runRoot.Contains('"')) {
        throw "Measurement paths cannot contain a quote character."
    }

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

    $eventSuffix = [Guid]::NewGuid().ToString("N")
    $gateEventName = "Local\LocalCopilotM34Gate_" + $eventSuffix
    $enteredEventName = "Local\LocalCopilotM34Entered_" + $eventSuffix
    $releaseEventName = "Local\LocalCopilotM34Release_" + $eventSuffix

    $gateEvent =
        New-Object Threading.EventWaitHandle(
            $false,
            [Threading.EventResetMode]::ManualReset,
            $gateEventName)

    $enteredEvent =
        New-Object Threading.EventWaitHandle(
            $false,
            [Threading.EventResetMode]::ManualReset,
            $enteredEventName)

    $releaseEvent =
        New-Object Threading.EventWaitHandle(
            $false,
            [Threading.EventResetMode]::ManualReset,
            $releaseEventName)

    $sentinel =
        "M34_PROVIDER_" +
        [Guid]::NewGuid().ToString("N") +
        "_CONTENT"

    Write-Host ""
    Write-Host "=============================================="
    Write-Host "M3.4 PROVIDER-ISOLATION MEASUREMENT"
    Write-Host "=============================================="
    Write-Host "Starting controlled same-integrity providers..."

    $blockingParameters = @{
        PowerShellPath = $windowsPowerShell
        StopPath = (Join-Path $runRoot "blocking-target.stop")
        WindowTitle = "LocalCopilot M3.4 blocking provider"
        ButtonText = "Controlled blocking provider"
        FixtureSourcePath = $fixtureSourcePath
        Blocking = $true
        GateEventName = $gateEventName
        EnteredEventName = $enteredEventName
        ReleaseEventName = $releaseEventName
        Sentinel = $sentinel
    }

    $blockingTarget = Start-M34Target @blockingParameters

    $recoveryParameters = @{
        PowerShellPath = $windowsPowerShell
        StopPath = (Join-Path $runRoot "recovery-target.stop")
        WindowTitle = "LocalCopilot M3.4 recovery provider"
        ButtonText = "Controlled recovery provider"
        FixtureSourcePath = $fixtureSourcePath
    }

    $recoveryTarget = Start-M34Target @recoveryParameters

    $runnerArguments =
        @(
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            ('"{0}"' -f $runnerPath),
            "-DiagnosticRoot",
            ('"{0}"' -f $runRoot),
            "-EnableUiText",
            "-Milestone",
            "M3.4"
        )

    $runnerProcess =
        Start-Process `
            -FilePath $windowsPowerShell `
            -ArgumentList $runnerArguments `
            -NoNewWindow `
            -PassThru

    $sessionParameters = @{
        Root = $runRoot
        RunnerProcess = $runnerProcess
        TimeoutSeconds = $StartupTimeoutSeconds
    }

    $sessionDirectory = Wait-M34RunnerSession @sessionParameters
    $appLogPath = Join-Path $sessionDirectory "app.log"
    $metaPath = Join-Path $sessionDirectory "session-meta.txt"
    $diagnosticBundlePath =
        Join-Path $sessionDirectory "diagnostic-bundle.txt"
    $acceptanceBundlePath =
        Join-Path $sessionDirectory "m3-4-provider-isolation-bundle.txt"

    $appParameters = @{
        RunnerProcess = $runnerProcess
        TimeoutSeconds = $StartupTimeoutSeconds
    }

    $appProcess = Wait-M34AppProcess @appParameters

    $appWindowParameters = @{
        Process = $appProcess
        Description = "LocalCopilot"
        TimeoutSeconds = $StepTimeoutSeconds
    }

    $appHandle = Wait-M34ProcessWindow @appWindowParameters

    $schemaParameters = @{
        Path = $appLogPath
        Pattern = '^schema=1 .*uiTextEnabled=True\r?$'
        TimeoutSeconds = $StepTimeoutSeconds
        RunnerProcess = $runnerProcess
    }

    [void](Wait-M34LogMatch @schemaParameters)

    $appRoot =
        [System.Windows.Automation.AutomationElement]::FromHandle(
            $appHandle)

    if ($null -eq $appRoot) {
        throw "LocalCopilot UI Automation root was unavailable."
    }

    $armOffset =
        (Get-M34FileContent -Path $appLogPath).Length

    Invoke-M34Button -AppRoot $appRoot -Name "Arm auto sensing"

    $armParameters = @{
        Path = $appLogPath
        Pattern = '(?m)^.*\| COORD\.OBSERVER_START \| observerRunning=True.*$'
        AfterOffset = $armOffset
        TimeoutSeconds = $StepTimeoutSeconds
        RunnerProcess = $runnerProcess
    }

    [void](Wait-M34LogMatch @armParameters)

    $blockingContextParameters = @{
        Target = $blockingTarget
        Description = "blocking provider"
        LogPath = $appLogPath
        RunnerProcess = $runnerProcess
        TimeoutSeconds = $StepTimeoutSeconds
    }

    Set-M34AllowedTargetContext @blockingContextParameters

    Write-Host "[RUN] Entering a real cross-process blocking Name provider call"

    $blockingRequestParameters = @{
        AppRoot = $appRoot
        ButtonName = "Measure M3.4 controlled provider isolation"
        Operation = "uia_provider_isolation"
        Kind = "SemanticSnapshot"
        LogPath = $appLogPath
        RunnerProcess = $runnerProcess
        TimeoutSeconds = $StepTimeoutSeconds
        WaitForDiagnosticHold = $true
    }

    $blockingRequest =
        Invoke-M34QueuedRequest @blockingRequestParameters

    $providerClock =
        [System.Diagnostics.Stopwatch]::StartNew()

    [void]$gateEvent.Set()

    if (-not $enteredEvent.WaitOne(
            $StepTimeoutSeconds * 1000)) {
        throw "The controlled provider did not enter its blocking Name getter."
    }

    $providerEnteredMilliseconds =
        $providerClock.ElapsedMilliseconds

    $integrityPattern =
        '(?m)^.*\| UIA\.INTEGRITY_CHECK \| request={0} currentRid=(\d+) targetRid=(\d+) inspected=True mayRead=True .*$' -f
            [Regex]::Escape($blockingRequest.RequestId)

    $integrityParameters = @{
        Path = $appLogPath
        Pattern = $integrityPattern
        AfterOffset = $blockingRequest.Offset
        TimeoutSeconds = $StepTimeoutSeconds
        RunnerProcess = $runnerProcess
    }

    $integrityMatch = Wait-M34LogMatch @integrityParameters
    $currentRid = $integrityMatch.Groups[1].Value
    $targetRid = $integrityMatch.Groups[2].Value

    if ($currentRid -ne $targetRid) {
        throw "The controlled provider was not at the client's integrity level."
    }

    Write-Host "[PASS] Blocking call entered at the same integrity level"
    Write-Host "[RUN] Cancelling its epoch and queueing a healthy provider"

    $recoveryContextParameters = @{
        Target = $recoveryTarget
        Description = "recovery provider"
        LogPath = $appLogPath
        RunnerProcess = $runnerProcess
        TimeoutSeconds = $StepTimeoutSeconds
    }

    Set-M34AllowedTargetContext @recoveryContextParameters

    $recoveryRequestParameters = @{
        AppRoot = $appRoot
        ButtonName = "Probe UI Automation root (M3.1 regression)"
        Operation = "uia_root_probe"
        Kind = "RootProbe"
        LogPath = $appLogPath
        RunnerProcess = $runnerProcess
        TimeoutSeconds = $StepTimeoutSeconds
    }

    $recoveryRequest =
        Invoke-M34QueuedRequest @recoveryRequestParameters

    $recoveryPattern =
        '(?m)^.*\| UIA\.PROBE_RESULT \| request={0} epoch=\d+ outcome=(\w+) reason=(\w+) .*$' -f
            [Regex]::Escape($recoveryRequest.RequestId)

    $recoveryWaitParameters = @{
        Path = $appLogPath
        Pattern = $recoveryPattern
        AfterOffset = $recoveryRequest.Offset
        TimeoutSeconds = $ProviderObservationSeconds
        RunnerProcess = $runnerProcess
    }

    $recoveryMatch =
        Wait-M34OptionalLogMatch @recoveryWaitParameters

    $recoveryObserved = $null -ne $recoveryMatch
    $recoveryElapsedMilliseconds =
        $providerClock.ElapsedMilliseconds

    if ($recoveryObserved) {
        $recoveryOutcome = $recoveryMatch.Groups[1].Value
        $recoveryReason = $recoveryMatch.Groups[2].Value

        if ($recoveryOutcome -ne "Available" -or
            $recoveryReason -ne "RootResolved") {
            throw (
                "The healthy-provider recovery returned outcome=" +
                $recoveryOutcome +
                " reason=" +
                $recoveryReason +
                " instead of Available/RootResolved.")
        }

        $blockingCompletionPattern =
            '(?m)^.*\| UIA\.REQUEST_COMPLETE \| request={0} .*workerThread=\d+ .*$' -f
                [Regex]::Escape($blockingRequest.RequestId)

        $blockingCompletionParameters = @{
            Path = $appLogPath
            Pattern = $blockingCompletionPattern
            AfterOffset = $blockingRequest.Offset
            TimeoutSeconds = $StepTimeoutSeconds
            RunnerProcess = $runnerProcess
        }

        [void](Wait-M34LogMatch @blockingCompletionParameters)

        if ($recoveryElapsedMilliseconds -le 10000) {
            $recoveryState = "RecoveredWithinDeadline"
            $classification = "InProcessCandidate"
            Write-Host "[PASS] Same-worker recovery completed within the request deadline"
        }
        else {
            $recoveryState = "RecoveredAfterDeadline"
            $classification = "RestartableHelperRequired"
            Write-Host "[PASS] Same-worker recovery occurred only after the request deadline"
        }
    }
    else {
        $currentLog = Get-M34FileContent -Path $appLogPath

        $blockingCompleted =
            [Regex]::IsMatch(
                $currentLog.Substring($blockingRequest.Offset),
                ('(?m)^.*\| UIA\.REQUEST_COMPLETE \| request={0} .*$' -f
                    [Regex]::Escape($blockingRequest.RequestId)))

        if ($blockingCompleted) {
            throw "The blocking request returned, but the healthy provider did not recover."
        }

        $recoveryState = "WorkerWedgedBeyondObservation"
        $classification = "RestartableHelperRequired"
        Write-Host "[PASS] Epoch cancellation and the request deadline could not free the worker"
    }

    Write-Host "[RUN] Closing LocalCopilot while the provider remains blocked"

    $shutdownClock =
        [System.Diagnostics.Stopwatch]::StartNew()

    [void][LocalCopilotM34Acceptance.WindowMethods]::PostMessage(
        $appHandle,
        0x0010,
        [IntPtr]::Zero,
        [IntPtr]::Zero)

    if (-not $appProcess.WaitForExit(25000)) {
        throw "LocalCopilot did not close within the bounded shutdown watchdog."
    }

    $shutdownElapsedMilliseconds =
        $shutdownClock.ElapsedMilliseconds

    if (-not $runnerProcess.WaitForExit(45000)) {
        throw "The diagnostic runner did not finalize after application exit."
    }

    $finalLog = Get-M34FileContent -Path $appLogPath
    $meta = Get-M34FileContent -Path $metaPath
    $diagnosticBundle =
        Get-M34FileContent -Path $diagnosticBundlePath

    Assert-M34Pattern `
        -Content $meta `
        -Pattern '(?m)^Milestone: M3\.4\r?$' `
        -Failure "The diagnostic session was not labeled M3.4."

    Assert-M34Pattern `
        -Content $meta `
        -Pattern '(?m)^Runner elevated: False\r?$' `
        -Failure "The diagnostic runner was not non-elevated."

    Assert-M34Pattern `
        -Content $meta `
        -Pattern '(?m)^Build Result: PASS\r?$' `
        -Failure "The strict Windows build did not pass."

    Assert-M34Pattern `
        -Content $meta `
        -Pattern '(?m)^Application Result: PASS\r?$' `
        -Failure "The diagnostic application run did not pass."

    Assert-M34Pattern `
        -Content $meta `
        -Pattern '(?m)^Runner Result: PASS\r?$' `
        -Failure "The diagnostic runner did not pass."

    Assert-M34Pattern `
        -Content $finalLog `
        -Pattern '(?m)^.*\| COORD\.STOP \| reason=application_shutdown HasThreadAccess=True.*$' `
        -Failure "Coordinator shutdown evidence was missing."

    Assert-M34Pattern `
        -Content $finalLog `
        -Pattern '(?m)^.*\| COORD\.DISPOSE \| Coordinator disposed\..*$' `
        -Failure "Coordinator disposal evidence was missing."

    $joined =
        [Regex]::IsMatch(
            $finalLog,
            '(?m)^.*\| UIA\.WORKER_DISPOSE \| started=True joined=True.*$')

    $notJoined =
        [Regex]::IsMatch(
            $finalLog,
            '(?m)^.*\| UIA\.WORKER_DISPOSE \| started=True joined=False.*$')

    if ($recoveryObserved) {
        if (-not $joined -or $notJoined) {
            throw "The recovered worker did not join cleanly."
        }
    }
    else {
        if ($joined -or -not $notJoined) {
            throw "The blocked in-process worker did not produce joined=False evidence."
        }

        $pendingStoppedPattern =
            '(?m)^.*\| UIA\.REQUEST_COMPLETE \| request={0} .*outcome=Cancelled reason=WorkerStopped .*workerThread=0 .*$' -f
                [Regex]::Escape($recoveryRequest.RequestId)

        Assert-M34Pattern `
            -Content $finalLog `
            -Pattern $pendingStoppedPattern `
            -Failure "The pending recovery request was not cancelled during shutdown."
    }

    if ([string]::IsNullOrWhiteSpace($diagnosticBundle)) {
        throw "The whitelisted diagnostic bundle was not produced."
    }

    foreach ($source in @($finalLog, $meta, $diagnosticBundle)) {
        if ($source.IndexOf(
                $sentinel,
                [StringComparison]::Ordinal) -ge 0) {
            throw "Controlled provider content escaped into diagnostic evidence."
        }
    }

    $sessionMatch =
        [Regex]::Match(
            $meta,
            '(?m)^Session ID: ([0-9a-fA-F-]+)\r?$')

    if (-not $sessionMatch.Success) {
        throw "The diagnostic session ID was unavailable."
    }

    $sessionId = $sessionMatch.Groups[1].Value
    $workerJoinedText =
        if ($joined) { "True" } else { "False" }

    $builder =
        New-Object System.Text.StringBuilder

    [void]$builder.AppendLine(
        "=================================================")
    [void]$builder.AppendLine(
        "M3.4 PROVIDER-ISOLATION EVIDENCE")
    [void]$builder.AppendLine(
        "=================================================")
    [void]$builder.AppendLine("Bundle schema: 1")
    [void]$builder.AppendLine("Session ID: " + $sessionId)
    [void]$builder.AppendLine("Branch: " + $branch)
    [void]$builder.AppendLine("HEAD: " + $head)
    [void]$builder.AppendLine("Working tree: clean")
    [void]$builder.AppendLine("Runner elevated: False")
    [void]$builder.AppendLine("Provider integrity RID: " + $targetRid)
    [void]$builder.AppendLine("Client integrity RID: " + $currentRid)
    [void]$builder.AppendLine("Same integrity: True")
    [void]$builder.AppendLine("Provider call entered: True")
    [void]$builder.AppendLine(
        "Provider entered elapsed ms: " +
        $providerEnteredMilliseconds)
    [void]$builder.AppendLine(
        "Configured native transaction timeout ms: 1500")
    [void]$builder.AppendLine(
        "Request deadline ms: 10000")
    [void]$builder.AppendLine(
        "Recovery observation bound ms: " +
        ($ProviderObservationSeconds * 1000))
    [void]$builder.AppendLine(
        "Healthy provider recovered before release: " +
        $recoveryObserved)
    [void]$builder.AppendLine(
        "Recovery observation elapsed ms: " +
        $recoveryElapsedMilliseconds)
    [void]$builder.AppendLine(
        "Shutdown elapsed ms: " +
        $shutdownElapsedMilliseconds)
    [void]$builder.AppendLine(
        "Worker joined: " +
        $workerJoinedText)
    [void]$builder.AppendLine(
        "Recovery state: " +
        $recoveryState)
    [void]$builder.AppendLine(
        "Architecture classification: " +
        $classification)
    [void]$builder.AppendLine(
        "Provider content: redacted")
    [void]$builder.AppendLine(
        "Controlled sentinel scan: PASS")
    [void]$builder.AppendLine("")
    [void]$builder.AppendLine(
        "The following source is the existing explicit-whitelist diagnostic bundle.")
    [void]$builder.AppendLine("")
    [void]$builder.AppendLine($diagnosticBundle)

    $evidence = $builder.ToString()

    if ($evidence.IndexOf(
            $sentinel,
            [StringComparison]::Ordinal) -ge 0) {
        throw "Controlled provider content escaped into the final evidence bundle."
    }

    [System.IO.File]::WriteAllText(
        $acceptanceBundlePath,
        $evidence,
        (New-Object System.Text.UTF8Encoding($true)))

    try {
        Get-Content -LiteralPath $acceptanceBundlePath -Raw |
            Set-Clipboard
    }
    catch {
        Write-Host "Evidence created; clipboard copy failed."
    }

    $acceptancePassed = $true
}
catch {
    $acceptanceError = $_.Exception

    if (-not [string]::IsNullOrWhiteSpace($sessionDirectory)) {
        try {
            $failureSummaryPath =
                Join-Path $sessionDirectory "provider-failure-summary.txt"

            $failureBuilder =
                New-Object System.Text.StringBuilder

            [void]$failureBuilder.AppendLine(
                "=================================================")
            [void]$failureBuilder.AppendLine(
                "M3.4 PROVIDER-ISOLATION FAILURE SUMMARY")
            [void]$failureBuilder.AppendLine(
                "=================================================")
            [void]$failureBuilder.AppendLine("Schema: 1")
            [void]$failureBuilder.AppendLine(
                "Failure UTC: " + [DateTimeOffset]::UtcNow.ToString("o"))
            [void]$failureBuilder.AppendLine(
                "Exception type: " + $acceptanceError.GetType().Name)
            [void]$failureBuilder.AppendLine(
                ("Exception HRESULT: 0x{0:X8}" -f $acceptanceError.HResult))
            [void]$failureBuilder.AppendLine(
                "Exception message: " + $acceptanceError.Message)
            [void]$failureBuilder.AppendLine(
                "Recovery state: " + $recoveryState)
            [void]$failureBuilder.AppendLine(
                "Recovery outcome: " + $recoveryOutcome)
            [void]$failureBuilder.AppendLine(
                "Recovery reason: " + $recoveryReason)

            if ($null -ne $blockingRequest) {
                [void]$failureBuilder.AppendLine(
                    "Blocking request ID: " + $blockingRequest.RequestId)
            }

            if ($null -ne $recoveryRequest) {
                [void]$failureBuilder.AppendLine(
                    "Recovery request ID: " + $recoveryRequest.RequestId)
            }

            [void]$failureBuilder.AppendLine("")
            [void]$failureBuilder.AppendLine(
                "Filtered metadata tail (content-free diagnostic events only):")

            $failureLog = Get-M34FileContent -Path $appLogPath
            if (-not [string]::IsNullOrWhiteSpace($failureLog)) {
                $metadataPattern =
                    '\| (CONTEXT\.APPLY|COORD\.|UIA\.(PROBE_BEGIN|QUEUE|DIAGNOSTIC_HOLD|INTEGRITY_CHECK|REQUEST_START|REQUEST_COMPLETE|PROBE_RESULT|WORKER_START|WORKER_STOP|WORKER_DISPOSE|REQUEST_CANCELLED|QUEUE_REJECT))'

                $metadataLines =
                    @(
                        $failureLog -split "\r?\n" |
                        Where-Object {
                            $_ -match $metadataPattern
                        } |
                        Select-Object -Last 120
                    )

                foreach ($metadataLine in $metadataLines) {
                    [void]$failureBuilder.AppendLine($metadataLine)
                }
            }
            else {
                [void]$failureBuilder.AppendLine(
                    "app.log was unavailable or empty.")
            }

            [System.IO.File]::WriteAllText(
                $failureSummaryPath,
                $failureBuilder.ToString(),
                (New-Object System.Text.UTF8Encoding($true)))
        }
        catch {
            $failureSummaryPath = $null
        }
    }
}
finally {
    if ($null -ne $releaseEvent) {
        try {
            [void]$releaseEvent.Set()
        }
        catch {
        }
    }

    Stop-M34Target -Target $blockingTarget
    Stop-M34Target -Target $recoveryTarget
    Stop-M34App -Process $appProcess

    if ($null -ne $runnerProcess) {
        try {
            $runnerProcess.Refresh()

            if (-not $runnerProcess.HasExited) {
                [void]$runnerProcess.WaitForExit(15000)
            }

            if (-not $runnerProcess.HasExited) {
                Stop-Process `
                    -Id $runnerProcess.Id `
                    -Force `
                    -ErrorAction SilentlyContinue
            }
        }
        catch {
        }
    }

    foreach ($eventHandle in @(
            $gateEvent,
            $enteredEvent,
            $releaseEvent)) {
        if ($null -ne $eventHandle) {
            try {
                $eventHandle.Dispose()
            }
            catch {
            }
        }
    }
}

Write-Host ""
Write-Host "=============================================="

if ($acceptancePassed) {
    Write-Host "M3.4 PROVIDER-ISOLATION MEASUREMENT: PASS"
    Write-Host "=============================================="
    Write-Host ("Architecture classification: " + $classification)
    Write-Host "Same-integrity blocking call entered: PASS"
    Write-Host "Healthy-provider recovery measured: PASS"
    Write-Host "Bounded application shutdown measured: PASS"
    Write-Host "Whitelisted sentinel scan: PASS"
    Write-Host ""
    Write-Host "Evidence bundle:"
    Write-Host $acceptanceBundlePath
    Write-Host ""
    Write-Host "The complete evidence bundle is on the clipboard."
}
else {
    Write-Host "M3.4 PROVIDER-ISOLATION MEASUREMENT: FAIL"
    Write-Host "=============================================="

    if ($null -ne $acceptanceError) {
        Write-Host (
            "Type={0} HRESULT=0x{1:X8}" -f
                $acceptanceError.GetType().Name,
                $acceptanceError.HResult)
        Write-Host ("Reason: " + $acceptanceError.Message)
        Write-Host ("Recovery outcome: " + $recoveryOutcome)
        Write-Host ("Recovery reason: " + $recoveryReason)
    }

    if (-not [string]::IsNullOrWhiteSpace($failureSummaryPath)) {
        Write-Host ""
        Write-Host "Failure summary:"
        Write-Host $failureSummaryPath
    }

    if (-not [string]::IsNullOrWhiteSpace($sessionDirectory)) {
        Write-Host ""
        Write-Host "Evidence directory:"
        Write-Host $sessionDirectory
    }

    throw "M3.4 provider-isolation measurement failed."
}
