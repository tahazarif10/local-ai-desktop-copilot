[CmdletBinding()]
param(
    [string]$DiagnosticRoot
)

$ErrorActionPreference = "Stop"

$repoRoot = $null
$projectPath = $null
$sessionDirectory = $null
$bundlePath = $null
$probeLog = $null
$metaPath = $null
$probeStop = $null
$probeJob = $null
$runnerError = $null
$sessionStartUtc = $null
$sessionId = $null
$buildResult = "NOT_RUN"
$applicationResult = "NOT_RUN"
$applicationExitCode = $null

try {
    $repoRoot = $PSScriptRoot

    if ([string]::IsNullOrWhiteSpace($repoRoot)) {
        $repoRoot = (Get-Location).Path
    }

    $repoRoot =
        [System.IO.Path]::GetFullPath($repoRoot)

    $projectPath =
        Join-Path `
            $repoRoot `
            "src\LocalCopilot.App\LocalCopilot.App.csproj"

    if ([string]::IsNullOrWhiteSpace($DiagnosticRoot)) {
        $DiagnosticRoot =
            Join-Path `
                $repoRoot `
                ".localcopilot\diagnostics"
    }
    elseif (-not [System.IO.Path]::IsPathRooted($DiagnosticRoot)) {
        $DiagnosticRoot =
            Join-Path `
                $repoRoot `
                $DiagnosticRoot
    }

    $DiagnosticRoot =
        [System.IO.Path]::GetFullPath($DiagnosticRoot)

    # -------------------------------------------------
    # Safety
    # -------------------------------------------------

    $existingApp =
        Get-Process `
            "LocalCopilot.App" `
            -ErrorAction SilentlyContinue

    if ($existingApp) {
        throw "LocalCopilot is already running. Close it first."
    }

    $branch =
        (git -C $repoRoot branch --show-current |
            Out-String).Trim()

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve the Git branch."
    }

    $head =
        (git -C $repoRoot rev-parse HEAD |
            Out-String).Trim()

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve the Git HEAD."
    }

    $gitStatus =
        git -C $repoRoot status --short |
        Out-String -Width 4096

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve the Git status."
    }

    $dotnetVersion =
        (dotnet --version |
            Out-String).Trim()

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve the .NET SDK version."
    }

    # -------------------------------------------------
    # Session-scoped paths
    # -------------------------------------------------

    New-Item `
        -ItemType Directory `
        -Force `
        -Path $DiagnosticRoot |
        Out-Null

    $sessionStartUtc =
        [DateTimeOffset]::UtcNow

    $sessionId =
        [Guid]::NewGuid()

    $sessionFolder =
        "{0}-{1}" -f `
            $sessionStartUtc.ToString("yyyyMMddTHHmmssfffZ"),
            $sessionId.ToString("N")

    $sessionDirectory =
        Join-Path `
            $DiagnosticRoot `
            $sessionFolder

    New-Item `
        -ItemType Directory `
        -Path $sessionDirectory |
        Out-Null

    $bundlePath =
        Join-Path `
            $sessionDirectory `
            "diagnostic-bundle.txt"

    $probeLog =
        Join-Path `
            $sessionDirectory `
            "os-foreground.log"

    $metaPath =
        Join-Path `
            $sessionDirectory `
            "session-meta.txt"

    $probeStop =
        Join-Path `
            $sessionDirectory `
            "probe.stop"

    $utf8 =
        New-Object System.Text.UTF8Encoding($true)

    $meta = @"
=================================================
LOCALCOPILOT LIVE DIAGNOSTIC SESSION
=================================================
Schema: 1
Milestone: M2.4.4
Session ID: $($sessionId.ToString("D"))
Session Start UTC: $($sessionStartUtc.ToString("o"))
Branch: $branch
HEAD: $head
Dotnet: $dotnetVersion
PowerShell: $($PSVersionTable.PSVersion)
OS: $([Environment]::OSVersion.VersionString)
Diagnostic activation: launch-scoped, expiring token
Application log: app.log

Git status:
$gitStatus
"@

    [System.IO.File]::WriteAllText(
        $metaPath,
        $meta,
        $utf8
    )

    Write-Host ""
    Write-Host "=============================================="
    Write-Host "NEW M2.4.4 DIAGNOSTIC SESSION"
    Write-Host "=============================================="
    Write-Host "Session: $($sessionId.ToString("D"))"
    Write-Host "Directory:"
    Write-Host $sessionDirectory
    Write-Host ""

    # -------------------------------------------------
    # Canonical strict build
    # -------------------------------------------------

    Write-Host "Building current source..."

    & dotnet build `
        $projectPath `
        -c Debug `
        -r win-x64 `
        --warnaserror

    if ($LASTEXITCODE -ne 0) {
        $buildResult = "FAIL"
        throw "Build failed."
    }

    $buildResult = "PASS"

    Write-Host ""
    Write-Host "BUILD OK"

    # -------------------------------------------------
    # Independent metadata-only OS foreground probe
    # -------------------------------------------------

    $probeJob =
        Start-Job `
            -ArgumentList $probeLog, $probeStop `
            -ScriptBlock {

                param(
                    $probeLog,
                    $probeStop
                )

                Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class LocalCopilotForegroundProbe
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(
        IntPtr hWnd,
        out uint processId);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    public static extern int GetWindowTextLengthW(
        IntPtr hWnd);
}
"@

                $jobUtf8 =
                    New-Object System.Text.UTF8Encoding($true)

                [System.IO.File]::WriteAllText(
                    $probeLog,
                    "=== Independent OS Foreground Probe ===" +
                    [Environment]::NewLine,
                    $jobUtf8
                )

                $lastHwnd =
                    [IntPtr]::Zero

                while (-not (Test-Path -LiteralPath $probeStop)) {
                    try {
                        $hwnd =
                            [LocalCopilotForegroundProbe]::
                            GetForegroundWindow()

                        if (
                            $hwnd -ne [IntPtr]::Zero -and
                            $hwnd -ne $lastHwnd
                        ) {
                            [uint32]$targetProcessId = 0

                            $windowThread =
                                [LocalCopilotForegroundProbe]::
                                GetWindowThreadProcessId(
                                    $hwnd,
                                    [ref]$targetProcessId
                                )

                            $processName = "Unknown"

                            try {
                                $targetProcess =
                                    Get-Process `
                                        -Id $targetProcessId `
                                        -ErrorAction Stop

                                $processName =
                                    $targetProcess.ProcessName +
                                    ".exe"
                            }
                            catch {
                            }

                            $titleLength =
                                [LocalCopilotForegroundProbe]::
                                GetWindowTextLengthW($hwnd)

                            $line =
                                "{0:o} | HWND=0x{1:X} | PID={2} | WThread={3} | Process={4} | TitleLength={5} | HasTitle={6}" -f `
                                (Get-Date),
                                $hwnd.ToInt64(),
                                $targetProcessId,
                                $windowThread,
                                $processName,
                                $titleLength,
                                ($titleLength -gt 0)

                            [System.IO.File]::AppendAllText(
                                $probeLog,
                                $line +
                                [Environment]::NewLine,
                                $jobUtf8
                            )

                            $lastHwnd =
                                $hwnd
                        }
                    }
                    catch {
                        $exception =
                            $_.Exception

                        $errorLine =
                            "{0:o} | PROBE.ERROR | type={1} hresult=0x{2:X8}" -f `
                            (Get-Date),
                            $exception.GetType().Name,
                            $exception.HResult

                        [System.IO.File]::AppendAllText(
                            $probeLog,
                            $errorLine +
                            [Environment]::NewLine,
                            $jobUtf8
                        )
                    }

                    Start-Sleep `
                        -Milliseconds 100
                }
            }

    Start-Sleep `
        -Milliseconds 600

    # -------------------------------------------------
    # Time-bounded launch token for this MSIX run only
    # -------------------------------------------------

    $descriptor =
        [ordered]@{
            SchemaVersion = 1
            SessionId = $sessionId.ToString("D")
            SessionDirectory = $sessionDirectory
            CreatedUtc = $sessionStartUtc.ToString("o")
            ExpiresUtc = $sessionStartUtc.AddHours(4).ToString("o")
        }

    $descriptorJson =
        $descriptor |
        ConvertTo-Json -Compress

    $descriptorBytes =
        [System.Text.Encoding]::UTF8.GetBytes(
            $descriptorJson
        )

    $activationToken =
        [Convert]::ToBase64String(
            $descriptorBytes
        )

    $activationToken =
        $activationToken.TrimEnd(
            [char[]]"="
        )

    $activationToken =
        $activationToken.Replace(
            "+",
            "-"
        )

    $activationToken =
        $activationToken.Replace(
            "/",
            "_"
        )

    $launchArguments =
        "--localcopilot-diagnostics=$activationToken"

    Write-Host ""
    Write-Host "=============================================="
    Write-Host "LIVE DIAGNOSTIC MODE READY"
    Write-Host "=============================================="
    Write-Host "Launching LocalCopilot with one-session diagnostics..."
    Write-Host ""

    & dotnet run `
        --project $projectPath `
        -c Debug `
        -r win-x64 `
        --no-build `
        "/p:WinAppLaunchArgs=$launchArguments"

    $applicationExitCode =
        $LASTEXITCODE

    if ($applicationExitCode -ne 0) {
        $applicationResult = "FAIL"
        throw "Application run failed."
    }

    $applicationResult = "PASS"
}
catch {
    $runnerError =
        $_.Exception
}
finally {
    # Stop the independent probe before reading any bundle source.
    if ($probeJob) {
        try {
            New-Item `
                -ItemType File `
                -Force `
                -Path $probeStop |
                Out-Null

            Wait-Job `
                $probeJob `
                -Timeout 5 |
                Out-Null

            if ($probeJob.State -ne "Completed") {
                Stop-Job `
                    $probeJob `
                    -ErrorAction SilentlyContinue
            }

            Remove-Job `
                $probeJob `
                -Force `
                -ErrorAction SilentlyContinue
        }
        catch {
        }
    }

    if (
        $probeStop -and
        (Test-Path -LiteralPath $probeStop)
    ) {
        Remove-Item `
            -LiteralPath $probeStop `
            -Force `
            -ErrorAction SilentlyContinue
    }

    if (
        $metaPath -and
        (Test-Path -LiteralPath $metaPath)
    ) {
        try {
            $sessionEndUtc =
                [DateTimeOffset]::UtcNow

            $runnerResult =
                if ($runnerError) {
                    "FAIL"
                }
                else {
                    "PASS"
                }

            $errorType =
                if ($runnerError) {
                    $runnerError.GetType().Name
                }
                else {
                    "none"
                }

            $errorHResult =
                if ($runnerError) {
                    "0x{0:X8}" -f $runnerError.HResult
                }
                else {
                    "none"
                }

            $exitCodeText =
                if ($null -eq $applicationExitCode) {
                    "n/a"
                }
                else {
                    $applicationExitCode.ToString()
                }

            $finalMeta = @"

=================================================
SESSION COMPLETION
=================================================
Session End UTC: $($sessionEndUtc.ToString("o"))
Build Result: $buildResult
Application Result: $applicationResult
Application Exit Code: $exitCodeText
Runner Result: $runnerResult
Runner Error Type: $errorType
Runner Error HRESULT: $errorHResult
"@

            [System.IO.File]::AppendAllText(
                $metaPath,
                $finalMeta,
                (New-Object System.Text.UTF8Encoding($true))
            )
        }
        catch {
        }
    }

    # Build the final bundle from this exact session and explicit whitelist.
    if ($bundlePath -and $sessionDirectory) {
        try {
            $bundleUtf8 =
                New-Object System.Text.UTF8Encoding($true)

            $builder =
                New-Object System.Text.StringBuilder

            [void]$builder.AppendLine(
                "================================================="
            )
            [void]$builder.AppendLine(
                "LOCALCOPILOT DIAGNOSTIC BUNDLE"
            )
            [void]$builder.AppendLine(
                "================================================="
            )
            [void]$builder.AppendLine(
                "Bundle schema: 1"
            )
            [void]$builder.AppendLine(
                "Session ID: " +
                $sessionId.ToString("D")
            )
            [void]$builder.AppendLine(
                "Generated UTC: " +
                [DateTimeOffset]::UtcNow.ToString("o")
            )
            [void]$builder.AppendLine(
                "Included files are restricted to the explicit whitelist below."
            )

            $bundleWhitelist =
                @(
                    [pscustomobject]@{
                        Name = "session-meta.txt"
                        Path = $metaPath
                    },
                    [pscustomobject]@{
                        Name = "app.log"
                        Path = Join-Path $sessionDirectory "app.log"
                    },
                    [pscustomobject]@{
                        Name = "os-foreground.log"
                        Path = $probeLog
                    }
                )

            [void]$builder.AppendLine("")
            [void]$builder.AppendLine("Whitelist:")

            foreach ($source in $bundleWhitelist) {
                [void]$builder.AppendLine(
                    "- " +
                    $source.Name
                )
            }

            foreach ($source in $bundleWhitelist) {
                [void]$builder.AppendLine("")
                [void]$builder.AppendLine(
                    "================================================="
                )
                [void]$builder.AppendLine(
                    "FILE: " +
                    $source.Name
                )
                [void]$builder.AppendLine(
                    "================================================="
                )

                if (Test-Path -LiteralPath $source.Path) {
                    try {
                        [void]$builder.AppendLine(
                            [System.IO.File]::ReadAllText(
                                $source.Path
                            )
                        )
                    }
                    catch {
                        [void]$builder.AppendLine(
                            "Whitelisted file was temporarily unreadable."
                        )
                    }
                }
                else {
                    [void]$builder.AppendLine(
                        "Whitelisted file was not produced."
                    )
                }
            }

            $temporaryBundlePath =
                Join-Path `
                    $sessionDirectory `
                    "diagnostic-bundle.tmp"

            [System.IO.File]::WriteAllText(
                $temporaryBundlePath,
                $builder.ToString(),
                $bundleUtf8
            )

            Move-Item `
                -LiteralPath $temporaryBundlePath `
                -Destination $bundlePath `
                -Force

            try {
                Get-Content `
                    -LiteralPath $bundlePath `
                    -Raw |
                    Set-Clipboard

                Write-Host ""
                Write-Host "=============================================="
                Write-Host "LATEST BUNDLE COPIED TO CLIPBOARD"
                Write-Host "=============================================="
            }
            catch {
                Write-Host ""
                Write-Host "Bundle created; clipboard copy failed."
            }

            Write-Host ""
            Write-Host $bundlePath
            Write-Host ""
            Write-Host "Paste the complete clipboard contents into ChatGPT."
        }
        catch {
            Write-Host ""
            Write-Host "ERROR: final diagnostic bundle could not be created."
        }
    }
}

if ($runnerError) {
    Write-Host ""
    Write-Host "=============================================="
    Write-Host "DEBUG RUNNER ERROR"
    Write-Host "=============================================="
    Write-Host (
        "Type={0} HRESULT=0x{1:X8}" -f `
            $runnerError.GetType().Name,
            $runnerError.HResult
    )
    Write-Host ""
    Write-Host "PowerShell remains open."
}
