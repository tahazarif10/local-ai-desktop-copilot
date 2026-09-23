[CmdletBinding()]
param(
    [switch]$ValidateOnly,
    [string]$ClientBundle,
    [string]$ServerHost,
    [string]$DiagnosticRoot,
    [string]$ExpectedBranch = "dev/m4-2-3b-ocr-transport",
    [ValidateRange(30,900)]
    [int]$StartupTimeoutSeconds = 300,
    [ValidateRange(10,180)]
    [int]$StepTimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

function Test-Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-FileText([string]$Path) {
    try {
        if (-not (Test-Path -LiteralPath $Path)) { return "" }
        return [IO.File]::ReadAllText($Path)
    }
    catch [IO.IOException] { return "" }
}

function Wait-Log {
    param(
        [string]$Path,
        [string]$Pattern,
        [int]$AfterOffset = 0,
        [int]$TimeoutSeconds,
        [Diagnostics.Process]$Runner
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $regex = New-Object Text.RegularExpressions.Regex(
        $Pattern,
        [Text.RegularExpressions.RegexOptions]::Multiline)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $text = Get-FileText $Path
        if ($text.Length -ge $AfterOffset) {
            $match = $regex.Match($text.Substring($AfterOffset))
            if ($match.Success) { return $match }
        }

        if ($null -ne $Runner) {
            $Runner.Refresh()
            if ($Runner.HasExited) {
                throw "Diagnostic runner exited before the required OCR event."
            }
        }

        Start-Sleep -Milliseconds 100
    }

    throw ("Timed out waiting for OCR acceptance event: " + $Pattern)
}

if ($ValidateOnly) {
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot "run-debug.ps1"))) {
        throw "run-debug.ps1 is missing."
    }
    Write-Host "M4.2.3 OCR CLIENT ACCEPTANCE VALIDATION: PASS"
    return
}

if ($env:OS -ne "Windows_NT") { throw "M4.2.3 OCR client acceptance requires Windows." }
if (Test-Elevated) { throw "Run OCR client acceptance from a normal, non-Administrator PowerShell." }
if ([string]::IsNullOrWhiteSpace($ClientBundle)) { throw "ClientBundle is required." }

$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$bundleRoot = [IO.Path]::GetFullPath($ClientBundle)
$configPath = Join-Path $bundleRoot "ocr-client-config.json"
if (-not (Test-Path -LiteralPath $configPath)) { throw "OCR client bundle configuration is missing." }

$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
if ([int]$config.schema -ne 1) { throw "Unexpected OCR client bundle schema." }
if ([int]$config.port -ne 49321) { throw "Unexpected OCR client port." }

$configuredHost = [string]$config.server_name
$hostName = if ([string]::IsNullOrWhiteSpace($ServerHost)) { $configuredHost } else { $ServerHost.Trim() }
if ([string]::IsNullOrWhiteSpace($hostName)) { throw "OCR server host is empty." }

$certificateSha256 = [string]$config.server_certificate_sha256
if ($certificateSha256 -notmatch "^[0-9a-f]{64}$") { throw "OCR client certificate pin is invalid." }

$authKeyPath = Join-Path $bundleRoot ([string]$config.authentication_key_file)
if (-not (Test-Path -LiteralPath $authKeyPath)) { throw "OCR client authentication key file is missing." }

$branch = (git -C $repoRoot branch --show-current | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw "Unable to read Git branch." }
if ($branch -ne $ExpectedBranch) { throw ("Expected branch '" + $ExpectedBranch + "'; current branch='" + $branch + "'.") }
$head = (git -C $repoRoot rev-parse HEAD | Out-String).Trim()
$status = git -C $repoRoot status --short | Out-String -Width 4096
if ($LASTEXITCODE -ne 0) { throw "Unable to read Git status." }
if (-not [string]::IsNullOrWhiteSpace($status)) { throw "OCR client acceptance requires a clean working tree." }

$windowsPowerShell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
$runnerPath = Join-Path $repoRoot "run-debug.ps1"

if ([string]::IsNullOrWhiteSpace($DiagnosticRoot)) {
    $DiagnosticRoot = Join-Path $repoRoot ".localcopilot\diagnostics"
}
elseif (-not [IO.Path]::IsPathRooted($DiagnosticRoot)) {
    $DiagnosticRoot = Join-Path $repoRoot $DiagnosticRoot
}

$acceptanceRoot = Join-Path ([IO.Path]::GetFullPath($DiagnosticRoot)) ("m4-2-3-ocr-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $acceptanceRoot -Force | Out-Null

if ($null -eq ("LocalCopilotM423Acceptance.Native" -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LocalCopilotM423Acceptance
{
    public static class Native
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLengthW(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        public static IntPtr FindWindowForProcess(int processId, string exactTitle)
        {
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

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Wait-ProcessWindow {
    param([Diagnostics.Process]$Process,[string]$ExactTitle,[int]$TimeoutSeconds)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) { throw "Target process exited before its window was ready." }

        $handle = if ([string]::IsNullOrWhiteSpace($ExactTitle)) {
            [IntPtr]$Process.MainWindowHandle
        } else {
            [LocalCopilotM423Acceptance.Native]::FindWindowForProcess([int]$Process.Id,$ExactTitle)
        }

        if ($handle -ne [IntPtr]::Zero) { return $handle }
        Start-Sleep -Milliseconds 100
    }

    throw "Timed out waiting for a required window."
}

function Set-Foreground([IntPtr]$Handle) {
    [void][LocalCopilotM423Acceptance.Native]::ShowWindow($Handle,9)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StepTimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        [void][LocalCopilotM423Acceptance.Native]::SetForegroundWindow($Handle)
        if ([LocalCopilotM423Acceptance.Native]::GetForegroundWindow() -eq $Handle) { return }
        Start-Sleep -Milliseconds 100
    }
    throw "Controlled OCR target could not become foreground."
}

function Invoke-AppButton($Root,[string]$Name) {
    $condition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    $button = $Root.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
    if ($null -eq $button) { throw ("Required LocalCopilot button not found: " + $Name) }
    $pattern = $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
    ([Windows.Automation.InvokePattern]$pattern).Invoke()
}

$target = $null
$runner = $null
$app = $null
$sessionDirectory = $null
$goPath = Join-Path $acceptanceRoot "target.go"
$stopPath = Join-Path $acceptanceRoot "target.stop"
$sentinelA = "OCR_ACCEPT_SERVO_ERROR_42_AXIS_A1"
$sentinelB = "OCR_ACCEPT_MOTOR_WARNING_17_AXIS_A2"

$previousUri = $env:LOCALCOPILOT_OCR_SERVER_URI
$previousPin = $env:LOCALCOPILOT_OCR_SERVER_CERT_SHA256
$previousKey = $env:LOCALCOPILOT_OCR_AUTH_KEY_FILE

try {
    $env:LOCALCOPILOT_OCR_SERVER_URI = "https://{0}:49321" -f $hostName
    $env:LOCALCOPILOT_OCR_SERVER_CERT_SHA256 = $certificateSha256
    $env:LOCALCOPILOT_OCR_AUTH_KEY_FILE = $authKeyPath

    $template = @'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$form = New-Object System.Windows.Forms.Form
$form.Text = 'LocalCopilot M4.2.3 OCR target'
$form.Width = 760
$form.Height = 440
$form.StartPosition = 'CenterScreen'

$panel = New-Object System.Windows.Forms.Panel
$panel.Location = New-Object System.Drawing.Point(20,20)
$panel.Size = New-Object System.Drawing.Size(700,330)
$panel.BackColor = [System.Drawing.Color]::Black
$panel.Tag = 0

$label = New-Object System.Windows.Forms.Label
$label.Location = New-Object System.Drawing.Point(45,120)
$label.Size = New-Object System.Drawing.Size(610,80)
$label.Font = New-Object System.Drawing.Font('Consolas',24,[System.Drawing.FontStyle]::Bold)
$label.ForeColor = [System.Drawing.Color]::White
$label.BackColor = [System.Drawing.Color]::Black
$label.Text = '__SENTINEL_A__'
[void]$panel.Controls.Add($label)
[void]$form.Controls.Add($panel)

$go = '__GO_PATH__'
$stop = '__STOP_PATH__'
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 700
$timer.Add_Tick({
    if ([IO.File]::Exists($stop)) {
        $timer.Stop()
        $form.Close()
        return
    }
    if (-not [IO.File]::Exists($go)) { return }

    $panel.Tag = [int]$panel.Tag + 1
    if ([int]$panel.Tag -gt 6) {
        $timer.Stop()
        return
    }

    if (([int]$panel.Tag % 2) -eq 0) {
        $label.BackColor = [Drawing.Color]::Black
        $label.ForeColor = [Drawing.Color]::White
        $label.Text = '__SENTINEL_A__'
    }
    else {
        $label.BackColor = [Drawing.Color]::White
        $label.ForeColor = [Drawing.Color]::Black
        $label.Text = '__SENTINEL_B__'
    }
})
$form.Add_Shown({ $form.Activate(); $timer.Start() })
[void]$form.ShowDialog()
$timer.Dispose()
$form.Dispose()
'@

    $targetScript = $template.Replace("__GO_PATH__",$goPath.Replace("'","''"))
    $targetScript = $targetScript.Replace("__STOP_PATH__",$stopPath.Replace("'","''"))
    $targetScript = $targetScript.Replace("__SENTINEL_A__",$sentinelA)
    $targetScript = $targetScript.Replace("__SENTINEL_B__",$sentinelB)
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($targetScript))

    $target = Start-Process -FilePath $windowsPowerShell -ArgumentList @(
        "-NoProfile","-Sta","-WindowStyle","Hidden","-EncodedCommand",$encoded) -PassThru
    $targetHandle = Wait-ProcessWindow -Process $target -ExactTitle "LocalCopilot M4.2.3 OCR target" -TimeoutSeconds $StepTimeoutSeconds

    $existingApps = @(Get-Process "LocalCopilot.App" -ErrorAction SilentlyContinue)
    if ($existingApps.Count -ne 0) {
        throw "Close all existing LocalCopilot.App processes before OCR acceptance."
    }

    $sessionRoot = Join-Path $acceptanceRoot "diagnostic"
    New-Item -ItemType Directory -Path $sessionRoot -Force | Out-Null

    $runner = Start-Process -FilePath $windowsPowerShell -ArgumentList @(
        "-NoProfile","-ExecutionPolicy","Bypass",
        "-File",('"{0}"' -f $runnerPath),
        "-DiagnosticRoot",('"{0}"' -f $sessionRoot),
        "-Milestone","M4.2.3","-EnableOcr") -NoNewWindow -PassThru

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $dirs = @(Get-ChildItem -LiteralPath $sessionRoot -Directory -ErrorAction SilentlyContinue)
        if ($dirs.Count -eq 1) { $sessionDirectory = $dirs[0].FullName; break }
        $runner.Refresh()
        if ($runner.HasExited) { throw "Diagnostic runner exited before session creation." }
        Start-Sleep -Milliseconds 100
    }
    if ([string]::IsNullOrWhiteSpace($sessionDirectory)) { throw "Diagnostic session was not created." }

    $appLog = Join-Path $sessionDirectory "app.log"
    [void](Wait-Log -Path $appLog -Pattern '^schema=1 .*ocrEnabled=True\r?$' -TimeoutSeconds $StartupTimeoutSeconds -Runner $runner)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $apps = @(Get-Process "LocalCopilot.App" -ErrorAction SilentlyContinue)
        if ($apps.Count -eq 1) { $app = $apps[0]; break }
        if ($apps.Count -gt 1) { throw "More than one LocalCopilot process is running." }
        $runner.Refresh()
        if ($runner.HasExited) { throw "Diagnostic runner exited before app launch." }
        Start-Sleep -Milliseconds 200
    }
    if ($null -eq $app) { throw "LocalCopilot did not launch." }

    $appHandle = Wait-ProcessWindow -Process $app -ExactTitle "" -TimeoutSeconds $StepTimeoutSeconds
    $appRoot = [Windows.Automation.AutomationElement]::FromHandle($appHandle)
    if ($null -eq $appRoot) { throw "LocalCopilot UI Automation root unavailable." }

    Invoke-AppButton -Root $appRoot -Name "Arm auto sensing"
    Set-Foreground $targetHandle

    [void](Wait-Log `
        -Path $appLog `
        -Pattern ('(?m)^.*\| CONTEXT\.APPLY \| .*pid={0} .*privacy=Allowed .*$' -f $target.Id) `
        -TimeoutSeconds $StepTimeoutSeconds `
        -Runner $runner)

    $ocrOffset = (Get-FileText $appLog).Length
    New-Item -ItemType File -Path $goPath -Force | Out-Null

    [void](Wait-Log `
        -Path $appLog `
        -Pattern '(?m)^.*\| OCR\.REPLACE_PENDING \| .*$' `
        -AfterOffset $ocrOffset `
        -TimeoutSeconds $StepTimeoutSeconds `
        -Runner $runner)

    [void](Wait-Log `
        -Path $appLog `
        -Pattern '(?m)^.*\| OCR\.DROP \| .*stage=publication reason=NotLatestRequest.*$' `
        -AfterOffset $ocrOffset `
        -TimeoutSeconds $StepTimeoutSeconds `
        -Runner $runner)

    [void](Wait-Log `
        -Path $appLog `
        -Pattern '(?m)^.*\| OCR\.RESULT \| .*content=redacted.*$' `
        -AfterOffset $ocrOffset `
        -TimeoutSeconds $StepTimeoutSeconds `
        -Runner $runner)

    $logText = Get-FileText $appLog
    if ($logText.Contains($sentinelA) -or $logText.Contains($sentinelB)) {
        throw "Raw controlled OCR text leaked into the diagnostic log."
    }

    $stopOffset = $logText.Length
    Invoke-AppButton -Root $appRoot -Name "Disarm auto sensing"
    [void](Wait-Log `
        -Path $appLog `
        -Pattern '(?m)^.*\| ORCH\.DISARM \| reason=user_disarm.*$' `
        -AfterOffset $stopOffset `
        -TimeoutSeconds $StepTimeoutSeconds `
        -Runner $runner)

    [void][LocalCopilotM423Acceptance.Native]::PostMessage(
        [IntPtr]$appHandle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
    if (-not $app.WaitForExit(15000)) {
        throw "LocalCopilot did not exit within the teardown deadline."
    }

    if (-not $runner.WaitForExit(30000)) {
        throw "Diagnostic runner did not finish bundle collection within the deadline."
    }

    $finalLog = Get-FileText $appLog
    if ($finalLog.Contains($sentinelA) -or $finalLog.Contains($sentinelB)) {
        throw "Raw OCR sentinel appeared after shutdown."
    }

    $ocrStopIndex = $finalLog.IndexOf("| OCR.RUNTIME_STOP |",[StringComparison]::Ordinal)
    $coordStopIndex = $finalLog.IndexOf("| COORD.STOP |",[StringComparison]::Ordinal)
    if ($ocrStopIndex -lt 0 -or $coordStopIndex -lt 0 -or $ocrStopIndex -gt $coordStopIndex) {
        throw "OCR runtime teardown did not precede coordinator teardown."
    }

    if (-not [Regex]::IsMatch(
            $finalLog,
            '(?m)^.*\| OCR\.RUNTIME_STOP \| .*joined=True.*
    $bundlePath = Join-Path $sessionDirectory "diagnostic-bundle.txt"
    $bundleText = Get-FileText $bundlePath
    if ($bundleText.Contains($sentinelA) -or $bundleText.Contains($sentinelB)) {
        throw "Raw OCR sentinel leaked into the final diagnostic bundle."
    }

    Write-Host ""
    Write-Host "M4.2.3 OCR CLIENT ACCEPTANCE: PASS"
    Write-Host ("head=" + $head)
    Write-Host "authenticated_tls_product_path=PASS"
    Write-Host "bounded_roi_capture=PASS"
    Write-Host "latest_wins_stale_rejection=PASS"
    Write-Host "disarm_cancellation=PASS"
    Write-Host "runtime_before_coordinator_teardown=PASS"
    Write-Host "raw_pixels_logged=False"
    Write-Host "raw_ocr_logged=False"
    Write-Host ("evidence_directory=" + $sessionDirectory)
}
finally {
    if ($null -ne $app) {
        try {
            $app.Refresh()
            if (-not $app.HasExited) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
        } catch {}
    }
    if ($null -ne $target) {
        try {
            if (-not $target.HasExited) {
                New-Item -ItemType File -Path $stopPath -Force | Out-Null
                [void]$target.WaitForExit(3000)
            }
            $target.Refresh()
            if (-not $target.HasExited) { Stop-Process -Id $target.Id -Force -ErrorAction SilentlyContinue }
        } catch {}
    }

    $env:LOCALCOPILOT_OCR_SERVER_URI = $previousUri
    $env:LOCALCOPILOT_OCR_SERVER_CERT_SHA256 = $previousPin
    $env:LOCALCOPILOT_OCR_AUTH_KEY_FILE = $previousKey
}
)) {
        throw "OCR runtime did not join active work before teardown."
    }

    $bundlePath = Join-Path $sessionDirectory "diagnostic-bundle.txt"
    $bundleText = Get-FileText $bundlePath
    if ($bundleText.Contains($sentinelA) -or $bundleText.Contains($sentinelB)) {
        throw "Raw OCR sentinel leaked into the final diagnostic bundle."
    }

    Write-Host ""
    Write-Host "M4.2.3 OCR CLIENT ACCEPTANCE: PASS"
    Write-Host ("head=" + $head)
    Write-Host "authenticated_tls_product_path=PASS"
    Write-Host "bounded_roi_capture=PASS"
    Write-Host "latest_wins_stale_rejection=PASS"
    Write-Host "disarm_cancellation=PASS"
    Write-Host "runtime_before_coordinator_teardown=PASS"
    Write-Host "raw_pixels_logged=False"
    Write-Host "raw_ocr_logged=False"
    Write-Host ("evidence_directory=" + $sessionDirectory)
}
finally {
    if ($null -ne $app) {
        try {
            $app.Refresh()
            if (-not $app.HasExited) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
        } catch {}
    }
    if ($null -ne $target) {
        try {
            if (-not $target.HasExited) {
                New-Item -ItemType File -Path $stopPath -Force | Out-Null
                [void]$target.WaitForExit(3000)
            }
            $target.Refresh()
            if (-not $target.HasExited) { Stop-Process -Id $target.Id -Force -ErrorAction SilentlyContinue }
        } catch {}
    }

    $env:LOCALCOPILOT_OCR_SERVER_URI = $previousUri
    $env:LOCALCOPILOT_OCR_SERVER_CERT_SHA256 = $previousPin
    $env:LOCALCOPILOT_OCR_AUTH_KEY_FILE = $previousKey
}
