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

# Physical acceptance harness only. UI Automation invocation is limited to
# LocalCopilot's own static diagnostic buttons; target applications remain
# read-only inputs to the product path under test.

$repoRoot = $null
$runRoot = $null
$sessionDirectory = $null
$appLogPath = $null
$metaPath = $null
$bundlePath = $null
$runnerProcess = $null
$appProcess = $null
$normalTarget = $null
$elevatedTarget = $null
$acceptanceError = $null
$acceptancePassed = $false

function Test-M33Elevated {
    $identity =
        [Security.Principal.WindowsIdentity]::GetCurrent()

    $principal =
        New-Object `
            Security.Principal.WindowsPrincipal($identity)

    return $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function ConvertTo-M33SingleQuotedLiteral {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    return "'" + $Value.Replace("'", "''") + "'"
}

function Wait-M33ProcessWindow {
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

function Start-M33Target {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PowerShellPath,
        [Parameter(Mandatory = $true)]
        [string]$StopPath,
        [Parameter(Mandatory = $true)]
        [string]$WindowTitle,
        [Parameter(Mandatory = $true)]
        [string]$NameSentinel,
        [Parameter(Mandatory = $true)]
        [string]$ValueSentinel,
        [Parameter(Mandatory = $true)]
        [string]$VisibleTextSentinel,
        [Parameter(Mandatory = $true)]
        [string]$PasswordSentinel,
        [Parameter(Mandatory = $true)]
        [string]$OffscreenSentinel,
        [switch]$Elevated
    )

    $targetTemplate = @'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$form = New-Object System.Windows.Forms.Form
$form.Text = __WINDOW_TITLE__
$form.Width = 720
$form.Height = 420
$form.StartPosition = 'CenterScreen'
$form.AutoScroll = $true

$button = New-Object System.Windows.Forms.Button
$button.Text = __NAME_SENTINEL__
$button.AccessibleName = __NAME_SENTINEL__
$button.Location = New-Object System.Drawing.Point(20, 20)
$button.AutoSize = $true

$value = New-Object System.Windows.Forms.TextBox
$value.AccessibleName = 'M3.3 acceptance value field'
$value.Text = __VALUE_SENTINEL__
$value.Location = New-Object System.Drawing.Point(20, 70)
$value.Width = 420
$value.ReadOnly = $true

$visible = New-Object System.Windows.Forms.Label
$visible.Text = __VISIBLE_TEXT_SENTINEL__
$visible.AccessibleName = 'M3.3 acceptance visible text'
$visible.Location = New-Object System.Drawing.Point(20, 120)
$visible.AutoSize = $true

$password = New-Object System.Windows.Forms.TextBox
$password.AccessibleName = 'M3.3 acceptance password field'
$password.Text = __PASSWORD_SENTINEL__
$password.Location = New-Object System.Drawing.Point(20, 170)
$password.Width = 420
$password.UseSystemPasswordChar = $true

$offscreen = New-Object System.Windows.Forms.Label
$offscreen.Text = __OFFSCREEN_SENTINEL__
$offscreen.AccessibleName = 'M3.3 acceptance off-screen text'
$offscreen.Location = New-Object System.Drawing.Point(20, 800)
$offscreen.AutoSize = $true

[void]$form.Controls.Add($button)
[void]$form.Controls.Add($value)
[void]$form.Controls.Add($visible)
[void]$form.Controls.Add($password)
[void]$form.Controls.Add($offscreen)

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

    $targetScript = $targetTemplate
    $targetScript =
        $targetScript.Replace(
            "__WINDOW_TITLE__",
            (ConvertTo-M33SingleQuotedLiteral $WindowTitle))
    $targetScript =
        $targetScript.Replace(
            "__NAME_SENTINEL__",
            (ConvertTo-M33SingleQuotedLiteral $NameSentinel))
    $targetScript =
        $targetScript.Replace(
            "__VALUE_SENTINEL__",
            (ConvertTo-M33SingleQuotedLiteral $ValueSentinel))
    $targetScript =
        $targetScript.Replace(
            "__VISIBLE_TEXT_SENTINEL__",
            (ConvertTo-M33SingleQuotedLiteral $VisibleTextSentinel))
    $targetScript =
        $targetScript.Replace(
            "__PASSWORD_SENTINEL__",
            (ConvertTo-M33SingleQuotedLiteral $PasswordSentinel))
    $targetScript =
        $targetScript.Replace(
            "__OFFSCREEN_SENTINEL__",
            (ConvertTo-M33SingleQuotedLiteral $OffscreenSentinel))
    $targetScript =
        $targetScript.Replace(
            "__STOP_PATH__",
            (ConvertTo-M33SingleQuotedLiteral $StopPath))

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

    if ($Elevated) {
        try {
            $process =
                Start-Process `
                    -FilePath $PowerShellPath `
                    -ArgumentList $arguments `
                    -Verb RunAs `
                    -PassThru
        }
        catch {
            throw "The higher-integrity acceptance target was not authorized."
        }
    }
    else {
        $process =
            Start-Process `
                -FilePath $PowerShellPath `
                -ArgumentList $arguments `
                -PassThru
    }

    $handle =
        Wait-M33ProcessWindow `
            -Process $process `
            -Description $WindowTitle `
            -TimeoutSeconds $StepTimeoutSeconds

    return [pscustomobject]@{
        Process = $process
        Handle = $handle
        StopPath = $StopPath
    }
}

function Stop-M33Target {
    param(
        [AllowNull()]
        [object]$Target
    )

    if ($null -eq $Target) {
        return
    }

    $targetExited = $false

    try {
        if (-not $Target.Process.HasExited) {
            New-Item `
                -ItemType File `
                -Force `
                -Path $Target.StopPath |
                Out-Null

            $targetExited =
                $Target.Process.WaitForExit(5000)
        }
        else {
            $targetExited = $true
        }
    }
    catch {
    }

    try {
        if (-not $Target.Process.HasExited) {
            Stop-Process `
                -Id $Target.Process.Id `
                -Force `
                -ErrorAction SilentlyContinue
        }

        $Target.Process.Refresh()
        $targetExited = $Target.Process.HasExited
    }
    catch {
    }

    if ($targetExited) {
        Remove-Item `
            -LiteralPath $Target.StopPath `
            -Force `
            -ErrorAction SilentlyContinue
    }
}

function Get-M33FileContent {
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

function Get-M33LogTail {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [int]$AfterOffset
    )

    $content = Get-M33FileContent -Path $Path

    if ($content.Length -lt $AfterOffset) {
        throw "The diagnostic log changed unexpectedly."
    }

    return $content.Substring($AfterOffset)
}

function Wait-M33LogMatch {
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
        New-Object `
            System.Text.RegularExpressions.Regex(
                $Pattern,
                [System.Text.RegularExpressions.RegexOptions]::Multiline)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $content = Get-M33FileContent -Path $Path

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

function Assert-M33Pattern {
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

function Invoke-M33Button {
    param(
        [Parameter(Mandatory = $true)]
        [object]$AppRoot,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $condition =
        New-Object `
            System.Windows.Automation.PropertyCondition(
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

function Set-M33Foreground {
    param(
        [Parameter(Mandatory = $true)]
        [IntPtr]$Handle,
        [Parameter(Mandatory = $true)]
        [string]$Description,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    [void][LocalCopilotM33Acceptance.WindowMethods]::ShowWindow(
        $Handle,
        9)

    $deadline =
        [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        [void][LocalCopilotM33Acceptance.WindowMethods]::SetForegroundWindow(
            $Handle)

        if ([LocalCopilotM33Acceptance.WindowMethods]::GetForegroundWindow() -eq
            $Handle) {
            return
        }

        Start-Sleep -Milliseconds 100
    }

    throw "$Description could not become the foreground window."
}

function Set-M33AllowedTargetContext {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Target,
        [Parameter(Mandatory = $true)]
        [string]$LogPath,
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$RunnerProcess,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    Set-M33Foreground `
        -Handle $Target.Handle `
        -Description "acceptance target" `
        -TimeoutSeconds $TimeoutSeconds

    $contextPattern =
        '(?m)^.*\| CONTEXT\.APPLY \| .*pid={0} .*privacy=Allowed .*$' -f
            $Target.Process.Id

    [void](Wait-M33LogMatch `
        -Path $LogPath `
        -Pattern $contextPattern `
        -TimeoutSeconds $TimeoutSeconds `
        -RunnerProcess $RunnerProcess)
}

function Invoke-M33Request {
    param(
        [Parameter(Mandatory = $true)]
        [object]$AppRoot,
        [Parameter(Mandatory = $true)]
        [string]$ButtonName,
        [Parameter(Mandatory = $true)]
        [string]$Operation,
        [Parameter(Mandatory = $true)]
        [ValidateSet("RootProbe", "StructuralSnapshot", "SemanticSnapshot")]
        [string]$Kind,
        [Parameter(Mandatory = $true)]
        [string]$LogPath,
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$RunnerProcess,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    $offset =
        (Get-M33FileContent -Path $LogPath).Length

    Invoke-M33Button -AppRoot $AppRoot -Name $ButtonName

    $beginPattern =
        '(?m)^.*\| UIA\.PROBE_BEGIN \| operation={0} .*$' -f
            [Regex]::Escape($Operation)

    [void](Wait-M33LogMatch `
        -Path $LogPath `
        -Pattern $beginPattern `
        -AfterOffset $offset `
        -TimeoutSeconds $TimeoutSeconds `
        -RunnerProcess $RunnerProcess)

    $queuePattern =
        '(?m)^.*\| UIA\.QUEUE \| request=(\d+) epoch=\d+ kind={0} .*$' -f
            [Regex]::Escape($Kind)

    $queueMatch =
        Wait-M33LogMatch `
            -Path $LogPath `
            -Pattern $queuePattern `
            -AfterOffset $offset `
            -TimeoutSeconds $TimeoutSeconds `
            -RunnerProcess $RunnerProcess

    $requestId =
        $queueMatch.Groups[1].Value

    $resultPattern =
        '(?m)^.*\| UIA\.PROBE_RESULT \| request={0} .*$' -f
            [Regex]::Escape($requestId)

    [void](Wait-M33LogMatch `
        -Path $LogPath `
        -Pattern $resultPattern `
        -AfterOffset $offset `
        -TimeoutSeconds $TimeoutSeconds `
        -RunnerProcess $RunnerProcess)

    return [pscustomobject]@{
        RequestId = $requestId
        Offset = $offset
        Tail = Get-M33LogTail -Path $LogPath -AfterOffset $offset
    }
}

function Invoke-M33Burst {
    param(
        [Parameter(Mandatory = $true)]
        [object]$AppRoot,
        [Parameter(Mandatory = $true)]
        [string]$ButtonName,
        [Parameter(Mandatory = $true)]
        [string]$Operation,
        [Parameter(Mandatory = $true)]
        [string]$LogPath,
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$RunnerProcess,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds,
        [switch]$WaitForSemanticDispose
    )

    $offset =
        (Get-M33FileContent -Path $LogPath).Length

    Invoke-M33Button -AppRoot $AppRoot -Name $ButtonName

    $burstPattern =
        '(?m)^.*\| UIA\.BURST_QUEUED \| operation={0} epoch=\d+ first=(\d+) second=(\d+) third=(\d+).*$' -f
            [Regex]::Escape($Operation)

    $burstMatch =
        Wait-M33LogMatch `
            -Path $LogPath `
            -Pattern $burstPattern `
            -AfterOffset $offset `
            -TimeoutSeconds $TimeoutSeconds `
            -RunnerProcess $RunnerProcess

    $first = $burstMatch.Groups[1].Value
    $second = $burstMatch.Groups[2].Value
    $third = $burstMatch.Groups[3].Value

    $completionPattern =
        if ($WaitForSemanticDispose) {
            '(?m)^.*\| UIA\.SEMANTIC_DISPOSE \| request={0} .*reason=diagnostic_consumer_complete disposed=True.*$' -f
                [Regex]::Escape($third)
        }
        else {
            '(?m)^.*\| UIA\.PROBE_RESULT \| request={0} .*$' -f
                [Regex]::Escape($third)
        }

    [void](Wait-M33LogMatch `
        -Path $LogPath `
        -Pattern $completionPattern `
        -AfterOffset $offset `
        -TimeoutSeconds $TimeoutSeconds `
        -RunnerProcess $RunnerProcess)

    return [pscustomobject]@{
        First = $first
        Second = $second
        Third = $third
        Offset = $offset
        Tail = Get-M33LogTail -Path $LogPath -AfterOffset $offset
    }
}

function Assert-M33LatestWins {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Burst,
        [switch]$Semantic,
        [switch]$Structural
    )

    Assert-M33Pattern `
        -Content $Burst.Tail `
        -Pattern ('(?m)^.*\| UIA\.REQUEST_COMPLETE \| request={0} .*outcome=Cancelled reason=Superseded .*workerThread=0 .*$' -f [Regex]::Escape($Burst.Second)) `
        -Failure "Latest-wins did not supersede the middle request."

    Assert-M33Pattern `
        -Content $Burst.Tail `
        -Pattern ('(?m)^.*\| UIA\.PROBE_RESULT \| request={0} .*outcome=Stale reason=PublicationRejected .*snapshot=none.*$' -f [Regex]::Escape($Burst.First)) `
        -Failure "The non-latest result was not rejected as stale."

    Assert-M33Pattern `
        -Content $Burst.Tail `
        -Pattern ('(?m)^.*\| UIA\.PROBE_RESULT \| request={0} .*outcome=Available .*$' -f [Regex]::Escape($Burst.Third)) `
        -Failure "The newest request was not published as available."

    if ($Semantic) {
        Assert-M33Pattern `
            -Content $Burst.Tail `
            -Pattern ('(?m)^.*\| UIA\.REQUEST_COMPLETE \| request={0} .*outcome=Available reason=SnapshotCaptured .*semanticProduced=True.*$' -f [Regex]::Escape($Burst.First)) `
            -Failure "The held semantic request did not produce a disposable result."

        Assert-M33Pattern `
            -Content $Burst.Tail `
            -Pattern ('(?m)^.*\| UIA\.SEMANTIC_DISPOSE \| request={0} .*reason=publication_PublicationRejected disposed=True.*$' -f [Regex]::Escape($Burst.First)) `
            -Failure "The stale semantic result was not cleared."

        Assert-M33Pattern `
            -Content $Burst.Tail `
            -Pattern ('(?m)^.*\| UIA\.PROBE_RESULT \| request={0} .*semantic=selected-visible .*content=redacted.*$' -f [Regex]::Escape($Burst.Third)) `
            -Failure "The newest semantic aggregate was not published redacted."

        Assert-M33Pattern `
            -Content $Burst.Tail `
            -Pattern ('(?m)^.*\| UIA\.SEMANTIC_DISPOSE \| request={0} .*reason=diagnostic_consumer_complete disposed=True.*$' -f [Regex]::Escape($Burst.Third)) `
            -Failure "The consumed semantic result was not cleared."

        $publishedSemanticCount =
            [Regex]::Matches(
                $Burst.Tail,
                '(?m)^.*\| UIA\.PROBE_RESULT \| .*semantic=selected-visible .*content=redacted.*$').Count

        if ($publishedSemanticCount -ne 1) {
            throw "The semantic burst published more than the newest aggregate."
        }
    }

    if ($Structural) {
        Assert-M33Pattern `
            -Content $Burst.Tail `
            -Pattern ('(?m)^.*\| UIA\.PROBE_RESULT \| request={0} .*snapshot=structural .*$' -f [Regex]::Escape($Burst.Third)) `
            -Failure "The newest structural result was not published."
    }
}

function Wait-M33RunnerSession {
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

function Wait-M33AppProcess {
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

function Stop-M33App {
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
                [void][LocalCopilotM33Acceptance.WindowMethods]::PostMessage(
                    $handle,
                    0x0010,
                    [IntPtr]::Zero,
                    [IntPtr]::Zero)

                [void]$Process.WaitForExit(15000)
            }
        }
    }
    catch {
    }

    try {
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
        throw "M3.3 physical acceptance requires Windows."
    }

    if (Test-M33Elevated) {
        throw "Run M3.3 acceptance from a normal, non-Administrator PowerShell."
    }

    $repoRoot = $PSScriptRoot

    if ([string]::IsNullOrWhiteSpace($repoRoot)) {
        $repoRoot = (Get-Location).Path
    }

    $repoRoot = [System.IO.Path]::GetFullPath($repoRoot)

    $runnerPath = Join-Path $repoRoot "run-debug.ps1"

    if (-not (Test-Path -LiteralPath $runnerPath)) {
        throw "run-debug.ps1 was not found beside the acceptance wrapper."
    }

    $gitStatus =
        git -C $repoRoot status --short |
        Out-String -Width 4096

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve the Git working-tree status."
    }

    if (-not [string]::IsNullOrWhiteSpace($gitStatus)) {
        throw "M3.3 physical acceptance requires a clean working tree."
    }

    $windowsPowerShell =
        Join-Path `
            $env:SystemRoot `
            "System32\WindowsPowerShell\v1.0\powershell.exe"

    if (-not (Test-Path -LiteralPath $windowsPowerShell)) {
        throw "Windows PowerShell was not found."
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
            ("m3-3-acceptance-" + [Guid]::NewGuid().ToString("N"))

    New-Item -ItemType Directory -Path $runRoot -Force |
        Out-Null

    if ($runnerPath.Contains('"') -or $runRoot.Contains('"')) {
        throw "Acceptance paths cannot contain a quote character."
    }

    if ($null -eq ("LocalCopilotM33Acceptance.WindowMethods" -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace LocalCopilotM33Acceptance
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

    $sentinelRoot =
        "M33_AUTO_" + [Guid]::NewGuid().ToString("N")

    $sentinels =
        @(
            $sentinelRoot + "_NAME",
            $sentinelRoot + "_VALUE",
            $sentinelRoot + "_VISIBLE_TEXT",
            $sentinelRoot + "_PASSWORD",
            $sentinelRoot + "_OFFSCREEN"
        )

    Write-Host ""
    Write-Host "=============================================="
    Write-Host "M3.3 ONE-COMMAND ACCEPTANCE"
    Write-Host "=============================================="
    Write-Host "Starting a controlled same-integrity provider..."

    $normalTarget =
        Start-M33Target `
            -PowerShellPath $windowsPowerShell `
            -StopPath (Join-Path $runRoot "normal-target.stop") `
            -WindowTitle "LocalCopilot M3.3 acceptance target" `
            -NameSentinel $sentinels[0] `
            -ValueSentinel $sentinels[1] `
            -VisibleTextSentinel $sentinels[2] `
            -PasswordSentinel $sentinels[3] `
            -OffscreenSentinel $sentinels[4]

    $runnerArguments =
        @(
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            ('"{0}"' -f $runnerPath),
            "-DiagnosticRoot",
            ('"{0}"' -f $runRoot),
            "-EnableUiText"
        )

    $runnerProcess =
        Start-Process `
            -FilePath $windowsPowerShell `
            -ArgumentList $runnerArguments `
            -NoNewWindow `
            -PassThru

    $sessionDirectory =
        Wait-M33RunnerSession `
            -Root $runRoot `
            -RunnerProcess $runnerProcess `
            -TimeoutSeconds $StartupTimeoutSeconds

    $appLogPath = Join-Path $sessionDirectory "app.log"
    $metaPath = Join-Path $sessionDirectory "session-meta.txt"
    $bundlePath = Join-Path $sessionDirectory "diagnostic-bundle.txt"

    $appProcess =
        Wait-M33AppProcess `
            -RunnerProcess $runnerProcess `
            -TimeoutSeconds $StartupTimeoutSeconds

    $appHandle =
        Wait-M33ProcessWindow `
            -Process $appProcess `
            -Description "LocalCopilot" `
            -TimeoutSeconds $StepTimeoutSeconds

    [void](Wait-M33LogMatch `
        -Path $appLogPath `
        -Pattern '^schema=1 .*uiTextEnabled=True\r?$' `
        -TimeoutSeconds $StepTimeoutSeconds `
        -RunnerProcess $runnerProcess)

    $appRoot =
        [System.Windows.Automation.AutomationElement]::FromHandle(
            $appHandle)

    if ($null -eq $appRoot) {
        throw "LocalCopilot UI Automation root was unavailable."
    }

    $armOffset =
        (Get-M33FileContent -Path $appLogPath).Length

    Invoke-M33Button -AppRoot $appRoot -Name "Arm auto sensing"

    [void](Wait-M33LogMatch `
        -Path $appLogPath `
        -Pattern '(?m)^.*\| COORD\.OBSERVER_START \| observerRunning=True.*$' `
        -AfterOffset $armOffset `
        -TimeoutSeconds $StepTimeoutSeconds `
        -RunnerProcess $runnerProcess)

    Set-M33AllowedTargetContext `
        -Target $normalTarget `
        -LogPath $appLogPath `
        -RunnerProcess $runnerProcess `
        -TimeoutSeconds $StepTimeoutSeconds

    Write-Host "[RUN] Semantic latest-wins and clear-on-stale"

    $semanticBurst =
        Invoke-M33Burst `
            -AppRoot $appRoot `
            -ButtonName "Exercise semantic latest-wins/clear-on-stale burst" `
            -Operation "uia_semantic_latest_wins_burst" `
            -LogPath $appLogPath `
            -RunnerProcess $runnerProcess `
            -TimeoutSeconds $StepTimeoutSeconds `
            -WaitForSemanticDispose

    Assert-M33LatestWins -Burst $semanticBurst -Semantic
    Write-Host "[PASS] Semantic latest-wins, stale rejection, and clearing"

    Write-Host ""
    Write-Host "Windows will now request one UAC confirmation."
    Write-Host "Approve it to create the controlled higher-integrity target."

    $elevatedTarget =
        Start-M33Target `
            -PowerShellPath $windowsPowerShell `
            -StopPath (Join-Path $runRoot "elevated-target.stop") `
            -WindowTitle "LocalCopilot M3.3 higher-integrity target" `
            -NameSentinel "M3.3 higher-integrity target" `
            -ValueSentinel "blocked before UI Automation" `
            -VisibleTextSentinel "higher-integrity denial fixture" `
            -PasswordSentinel "not-read" `
            -OffscreenSentinel "not-read" `
            -Elevated

    Set-M33AllowedTargetContext `
        -Target $elevatedTarget `
        -LogPath $appLogPath `
        -RunnerProcess $runnerProcess `
        -TimeoutSeconds $StepTimeoutSeconds

    Write-Host "[RUN] Higher-integrity fail-closed gate"

    $integrityRequest =
        Invoke-M33Request `
            -AppRoot $appRoot `
            -ButtonName "Capture bounded semantic snapshot (explicit opt-in)" `
            -Operation "uia_semantic_snapshot" `
            -Kind "SemanticSnapshot" `
            -LogPath $appLogPath `
            -RunnerProcess $runnerProcess `
            -TimeoutSeconds $StepTimeoutSeconds

    $integrityPattern =
        '(?m)^.*\| UIA\.INTEGRITY_CHECK \| request={0} currentRid=(\d+) targetRid=(\d+) inspected=(True|False) mayRead=False .*$' -f
            [Regex]::Escape($integrityRequest.RequestId)

    $integrityMatch =
        [Regex]::Match(
            $integrityRequest.Tail,
            $integrityPattern,
            [Text.RegularExpressions.RegexOptions]::Multiline)

    if (-not $integrityMatch.Success) {
        throw "The elevated target did not fail at the integrity gate."
    }

    $integrityReasonMatch =
        [Regex]::Match(
            $integrityRequest.Tail,
            ('(?m)^.*\| UIA\.REQUEST_COMPLETE \| request={0} .*outcome=Unavailable reason=(HigherIntegrity|AccessInspectionFailed) .*snapshotProduced=False semanticProduced=False.*$' -f [Regex]::Escape($integrityRequest.RequestId)),
            [Text.RegularExpressions.RegexOptions]::Multiline)

    if (-not $integrityReasonMatch.Success) {
        throw "The higher-integrity request did not return a typed fail-closed denial."
    }

    $integrityInspected =
        $integrityMatch.Groups[3].Value -eq "True"

    if ($integrityInspected) {
        if ([uint32]$integrityMatch.Groups[2].Value -le
            [uint32]$integrityMatch.Groups[1].Value -or
            $integrityReasonMatch.Groups[1].Value -ne "HigherIntegrity") {
            throw "The inspected target was not rejected as higher integrity."
        }
    }
    elseif ($integrityReasonMatch.Groups[1].Value -ne
        "AccessInspectionFailed") {
        throw "An uninspectable target did not fail closed."
    }

    Assert-M33Pattern `
        -Content $integrityRequest.Tail `
        -Pattern ('(?m)^.*\| UIA\.PROBE_RESULT \| request={0} .*outcome=Unavailable reason=(HigherIntegrity|AccessInspectionFailed) .*snapshot=none.*$' -f [Regex]::Escape($integrityRequest.RequestId)) `
        -Failure "The higher-integrity denial was not published as metadata only."

    Write-Host "[PASS] Higher-integrity target denied before UI Automation"

    Stop-M33Target -Target $elevatedTarget

    $elevatedTarget.Process.Refresh()

    if (-not $elevatedTarget.Process.HasExited) {
        throw "The controlled higher-integrity target did not close."
    }

    $elevatedTarget = $null

    Set-M33AllowedTargetContext `
        -Target $normalTarget `
        -LogPath $appLogPath `
        -RunnerProcess $runnerProcess `
        -TimeoutSeconds $StepTimeoutSeconds

    Write-Host "[RUN] M3.1 timeout/recovery/latest-wins regressions"

    $forcedTimeout =
        Invoke-M33Request `
            -AppRoot $appRoot `
            -ButtonName "Force timeout, then retry normal probe" `
            -Operation "uia_forced_timeout" `
            -Kind "RootProbe" `
            -LogPath $appLogPath `
            -RunnerProcess $runnerProcess `
            -TimeoutSeconds $StepTimeoutSeconds

    Assert-M33Pattern `
        -Content $forcedTimeout.Tail `
        -Pattern ('(?m)^.*\| UIA\.REQUEST_COMPLETE \| request={0} .*outcome=Timeout reason=DeadlineExpired .*$' -f [Regex]::Escape($forcedTimeout.RequestId)) `
        -Failure "The forced root deadline did not time out deterministically."

    $rootRecovery =
        Invoke-M33Request `
            -AppRoot $appRoot `
            -ButtonName "Probe UI Automation root (M3.1 regression)" `
            -Operation "uia_root_probe" `
            -Kind "RootProbe" `
            -LogPath $appLogPath `
            -RunnerProcess $runnerProcess `
            -TimeoutSeconds $StepTimeoutSeconds

    Assert-M33Pattern `
        -Content $rootRecovery.Tail `
        -Pattern ('(?m)^.*\| UIA\.REQUEST_COMPLETE \| request={0} .*outcome=Available reason=RootResolved .*$' -f [Regex]::Escape($rootRecovery.RequestId)) `
        -Failure "The root probe did not recover immediately after timeout."

    $rootBurst =
        Invoke-M33Burst `
            -AppRoot $appRoot `
            -ButtonName "Exercise root latest-wins/stale burst" `
            -Operation "uia_latest_wins_burst" `
            -LogPath $appLogPath `
            -RunnerProcess $runnerProcess `
            -TimeoutSeconds $StepTimeoutSeconds

    Assert-M33LatestWins -Burst $rootBurst
    Write-Host "[PASS] M3.1 timeout recovery and latest-wins"

    Write-Host "[RUN] M3.2 structural normal/depth/latest-wins regressions"

    $structural =
        Invoke-M33Request `
            -AppRoot $appRoot `
            -ButtonName "Capture bounded structural snapshot" `
            -Operation "uia_structural_snapshot" `
            -Kind "StructuralSnapshot" `
            -LogPath $appLogPath `
            -RunnerProcess $runnerProcess `
            -TimeoutSeconds $StepTimeoutSeconds

    Assert-M33Pattern `
        -Content $structural.Tail `
        -Pattern ('(?m)^.*\| UIA\.PROBE_RESULT \| request={0} .*outcome=Available reason=SnapshotCaptured .*snapshot=structural .*$' -f [Regex]::Escape($structural.RequestId)) `
        -Failure "The normal structural snapshot regression failed."

    $depth =
        Invoke-M33Request `
            -AppRoot $appRoot `
            -ButtonName "Exercise depth-0 structural budget" `
            -Operation "uia_depth_budget" `
            -Kind "StructuralSnapshot" `
            -LogPath $appLogPath `
            -RunnerProcess $runnerProcess `
            -TimeoutSeconds $StepTimeoutSeconds

    Assert-M33Pattern `
        -Content $depth.Tail `
        -Pattern ('(?m)^.*\| UIA\.PROBE_RESULT \| request={0} .*outcome=Available reason=SnapshotCaptured .*snapshot=structural .*nodes=1 .*maxDepth=0 .*truncation=.*DepthLimit.*$' -f [Regex]::Escape($depth.RequestId)) `
        -Failure "The structural depth-0 budget regression failed."

    $structuralBurst =
        Invoke-M33Burst `
            -AppRoot $appRoot `
            -ButtonName "Exercise structural latest-wins/stale burst" `
            -Operation "uia_structural_latest_wins_burst" `
            -LogPath $appLogPath `
            -RunnerProcess $runnerProcess `
            -TimeoutSeconds $StepTimeoutSeconds

    Assert-M33LatestWins -Burst $structuralBurst -Structural
    Write-Host "[PASS] M3.2 structural regressions"

    $preTeardownLog =
        Get-M33FileContent -Path $appLogPath

    $workerStart =
        [Regex]::Match(
            $preTeardownLog,
            '(?m)^.*MThread=(\d+) \| UIA\.WORKER_START \| thread=(\d+) apartment=MTA .*$')

    $uiStart =
        [Regex]::Match(
            $preTeardownLog,
            '(?m)^.*MThread=(\d+) \| COORD\.START \| HasThreadAccess=True.*$')

    if (-not $workerStart.Success -or -not $uiStart.Success) {
        throw "The MTA/UI thread evidence was incomplete."
    }

    if ($workerStart.Groups[1].Value -ne $workerStart.Groups[2].Value -or
        $workerStart.Groups[2].Value -eq $uiStart.Groups[1].Value) {
        throw "UI Automation did not remain on one non-UI MTA thread."
    }

    $workerThread = $workerStart.Groups[2].Value
    $requestCompletions =
        [Regex]::Matches(
            $preTeardownLog,
            '(?m)^.*\| UIA\.REQUEST_COMPLETE \| .*workerThread=(\d+) .*$')

    foreach ($completion in $requestCompletions) {
        $thread = $completion.Groups[1].Value

        if ($thread -ne "0" -and $thread -ne $workerThread) {
            throw "A UI Automation request escaped the single MTA worker."
        }
    }

    Write-Host "[PASS] One reusable non-UI MTA worker"
    Write-Host "[RUN] Joined teardown during held semantic work"

    $heldOffset =
        (Get-M33FileContent -Path $appLogPath).Length

    Invoke-M33Button `
        -AppRoot $appRoot `
        -Name "Exercise semantic latest-wins/clear-on-stale burst"

    $heldPattern =
        '(?m)^.*\| UIA\.BURST_QUEUED \| operation=uia_semantic_latest_wins_burst epoch=\d+ first=(\d+) second=(\d+) third=(\d+).*$'

    $heldBurst =
        Wait-M33LogMatch `
            -Path $appLogPath `
            -Pattern $heldPattern `
            -AfterOffset $heldOffset `
            -TimeoutSeconds $StepTimeoutSeconds `
            -RunnerProcess $runnerProcess

    $heldFirst = $heldBurst.Groups[1].Value
    $heldSecond = $heldBurst.Groups[2].Value
    $heldThird = $heldBurst.Groups[3].Value

    [void](Wait-M33LogMatch `
        -Path $appLogPath `
        -Pattern ('(?m)^.*\| UIA\.DIAGNOSTIC_HOLD \| request={0} durationMs=2000.*$' -f [Regex]::Escape($heldFirst)) `
        -AfterOffset $heldOffset `
        -TimeoutSeconds $StepTimeoutSeconds `
        -RunnerProcess $runnerProcess)

    [void][LocalCopilotM33Acceptance.WindowMethods]::PostMessage(
        $appHandle,
        0x0010,
        [IntPtr]::Zero,
        [IntPtr]::Zero)

    if (-not $appProcess.WaitForExit(15000)) {
        throw "LocalCopilot did not close during the held semantic request."
    }

    if (-not $runnerProcess.WaitForExit(30000)) {
        throw "The diagnostic runner did not finalize after application exit."
    }

    $finalLog = Get-M33FileContent -Path $appLogPath
    $heldTail = $finalLog.Substring($heldOffset)

    Assert-M33Pattern `
        -Content $heldTail `
        -Pattern ('(?m)^.*\| UIA\.REQUEST_COMPLETE \| request={0} .*outcome=Cancelled reason=WorkerStopped .*$' -f [Regex]::Escape($heldFirst)) `
        -Failure "The active held semantic request was not cancelled by shutdown."

    Assert-M33Pattern `
        -Content $heldTail `
        -Pattern ('(?m)^.*\| UIA\.REQUEST_COMPLETE \| request={0} .*outcome=Cancelled reason=Superseded .*$' -f [Regex]::Escape($heldSecond)) `
        -Failure "The held burst middle request was not superseded."

    Assert-M33Pattern `
        -Content $heldTail `
        -Pattern ('(?m)^.*\| UIA\.REQUEST_COMPLETE \| request={0} .*outcome=Cancelled reason=WorkerStopped .*$' -f [Regex]::Escape($heldThird)) `
        -Failure "The pending held semantic request was not cancelled by shutdown."

    $workerStopCount =
        [Regex]::Matches(
            $finalLog,
            '(?m)^.*\| UIA\.WORKER_STOP \| thread=\d+.*$').Count

    $workerDisposeCount =
        [Regex]::Matches(
            $finalLog,
            '(?m)^.*\| UIA\.WORKER_DISPOSE \| started=True joined=True.*$').Count

    if ($workerStopCount -ne 1 -or $workerDisposeCount -ne 1) {
        throw "The UI Automation worker did not stop and join exactly once."
    }

    Assert-M33Pattern `
        -Content $finalLog `
        -Pattern '(?m)^.*\| COORD\.STOP \| reason=application_shutdown HasThreadAccess=True.*$' `
        -Failure "Coordinator shutdown evidence was missing."

    Assert-M33Pattern `
        -Content $finalLog `
        -Pattern '(?m)^.*\| COORD\.OBSERVER_STOP \| success=True.*$' `
        -Failure "Foreground observer shutdown evidence was missing."

    Assert-M33Pattern `
        -Content $finalLog `
        -Pattern '(?m)^.*\| COORD\.DISPOSE \| Coordinator disposed\..*$' `
        -Failure "Coordinator disposal evidence was missing."

    Write-Host "[PASS] Held-work cancellation and joined teardown"

    $meta = Get-M33FileContent -Path $metaPath

    Assert-M33Pattern `
        -Content $meta `
        -Pattern '(?m)^Build Result: PASS\r?$' `
        -Failure "The strict Windows build did not pass."

    Assert-M33Pattern `
        -Content $meta `
        -Pattern '(?m)^Application Result: PASS\r?$' `
        -Failure "The diagnostic application run did not pass."

    Assert-M33Pattern `
        -Content $meta `
        -Pattern '(?m)^Runner Result: PASS\r?$' `
        -Failure "The diagnostic runner did not pass."

    if (-not (Test-Path -LiteralPath $bundlePath)) {
        throw "The final whitelisted diagnostic bundle was not produced."
    }

    $bundle = Get-M33FileContent -Path $bundlePath

    foreach ($sentinel in $sentinels) {
        if ($finalLog.IndexOf(
                $sentinel,
                [StringComparison]::Ordinal) -ge 0 -or
            $meta.IndexOf(
                $sentinel,
                [StringComparison]::Ordinal) -ge 0 -or
            $bundle.IndexOf(
                $sentinel,
                [StringComparison]::Ordinal) -ge 0) {
            throw "A controlled semantic sentinel escaped into diagnostic evidence."
        }
    }

    Write-Host "[PASS] Controlled sentinels absent from whitelisted evidence"

    try {
        Get-Content -LiteralPath $bundlePath -Raw |
            Set-Clipboard
    }
    catch {
        Write-Host "Bundle created; clipboard copy failed."
    }

    $acceptancePassed = $true
}
catch {
    $acceptanceError = $_.Exception
}
finally {
    Stop-M33Target -Target $elevatedTarget
    Stop-M33Target -Target $normalTarget

    if ($null -ne $appProcess) {
        Stop-M33App -Process $appProcess
    }

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
}

Write-Host ""
Write-Host "=============================================="

if ($acceptancePassed) {
    Write-Host "M3.3 REMAINING PHYSICAL ACCEPTANCE: PASS"
    Write-Host "=============================================="
    Write-Host "Semantic latest-wins/clearing: PASS"
    Write-Host "Integrity plus M3.1/M3.2 regressions: PASS"
    Write-Host "Held-work joined teardown: PASS"
    Write-Host "Whitelisted sentinel scan: PASS"
    Write-Host ""
    Write-Host "Bundle:"
    Write-Host $bundlePath
    Write-Host ""
    Write-Host "The complete bundle is on the clipboard."
}
else {
    Write-Host "M3.3 REMAINING PHYSICAL ACCEPTANCE: FAIL"
    Write-Host "=============================================="

    if ($null -ne $acceptanceError) {
        Write-Host (
            "Type={0} HRESULT=0x{1:X8}" -f `
                $acceptanceError.GetType().Name,
                $acceptanceError.HResult)
        Write-Host ("Reason: " + $acceptanceError.Message)
    }

    if (-not [string]::IsNullOrWhiteSpace($sessionDirectory)) {
        Write-Host ""
        Write-Host "Evidence directory:"
        Write-Host $sessionDirectory
    }

    throw "M3.3 one-command acceptance failed."
}
