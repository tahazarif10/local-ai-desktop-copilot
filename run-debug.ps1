$ErrorActionPreference = "Stop"

try {
    $repoRoot = $PSScriptRoot

    if ([string]::IsNullOrWhiteSpace($repoRoot)) {
        $repoRoot = (Get-Location).Path
    }

    $projectPath = Join-Path $repoRoot "src\LocalCopilot.App\LocalCopilot.App.csproj"

    $cache = "H:\DevCache\LocalCopilot"

    $bundlePath =
        Join-Path $cache "m1-3-debug-bundle.txt"

    $probeLog =
        Join-Path $cache "m1-3-os-foreground.log"

    $metaPath =
        Join-Path $cache "m1-3-session-meta.txt"

    $probeStop =
        Join-Path $cache "m1-3-probe.stop"

    $bundleStop =
        Join-Path $cache "m1-3-bundle.stop",
                    "diagnostics.enabled"

    $diagnosticsFlag =
        Join-Path $cache "diagnostics.enabled"

    New-Item `
        -ItemType Directory `
        -Force `
        -Path $cache |
        Out-Null

    # -------------------------------------------------
    # Safety
    # -------------------------------------------------

    $existingApp =
        Get-Process "LocalCopilot.App" `
            -ErrorAction SilentlyContinue

    if ($existingApp) {
        throw "LocalCopilot is already running. Close it first."
    }

    # -------------------------------------------------
    # A previous abnormal debug shutdown must never leave
    # diagnostics enabled for normal application launches.
    Remove-Item `
        $diagnosticsFlag `
        -Force `
        -ErrorAction SilentlyContinue
    # New diagnostic session
    # -------------------------------------------------

    Remove-Item `
        $bundlePath,
        $probeLog,
        $metaPath,
        $probeStop,
        $bundleStop `
        -Force `
        -ErrorAction SilentlyContinue

    $sessionStart = Get-Date
    $sessionId = [Guid]::NewGuid().ToString()

    $branch =
        git -C $repoRoot branch --show-current

    $head =
        git -C $repoRoot rev-parse HEAD

    $dotnetVersion =
        dotnet --version

    $gitStatus =
        git -C $repoRoot status --short |
        Out-String

    $meta = @"
=================================================
LOCALCOPILOT LIVE DIAGNOSTIC SESSION
=================================================
Session ID: $sessionId
Session Start: $($sessionStart.ToString("o"))
Branch: $branch
HEAD: $head
Dotnet: $dotnetVersion
PowerShell: $($PSVersionTable.PSVersion)
OS: $([Environment]::OSVersion.VersionString)

Git status:
$gitStatus
"@

    [System.IO.File]::WriteAllText(
        $metaPath,
        $meta,
        (New-Object System.Text.UTF8Encoding($true))
    )

    # Create bundle immediately so it is NEVER stale.
    Copy-Item `
        $metaPath `
        $bundlePath `
        -Force

    Write-Host ""
    Write-Host "=============================================="
    Write-Host "NEW DIAGNOSTIC SESSION"
    Write-Host "=============================================="
    Write-Host "Session: $sessionId"
    Write-Host "Bundle:"
    Write-Host $bundlePath
    Write-Host ""

    # -------------------------------------------------
    # Build
    # -------------------------------------------------

    Write-Host "Building current source..."

    & dotnet build `
        $projectPath `
        -c Debug `
        -r win-x64

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed."
    }

    Write-Host ""
    Write-Host "BUILD OK"

    # -------------------------------------------------
    # Independent OS foreground probe
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
using System.Text;

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

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    public static extern int GetWindowTextW(
        IntPtr hWnd,
        StringBuilder lpString,
        int nMaxCount);
}
"@

            $utf8 =
                New-Object System.Text.UTF8Encoding($true)

            [System.IO.File]::WriteAllText(
                $probeLog,
                "=== Independent OS Foreground Probe ===" +
                [Environment]::NewLine,
                $utf8
            )

            $lastHwnd =
                [IntPtr]::Zero

            while (-not (Test-Path $probeStop)) {

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
                            $process =
                                Get-Process `
                                    -Id $targetProcessId `
                                    -ErrorAction Stop

                            $processName =
                                $process.ProcessName + ".exe"
                        }
                        catch {
                        }

                        $length =
                            [LocalCopilotForegroundProbe]::
                            GetWindowTextLengthW($hwnd)

                        $title = ""

                        if ($length -gt 0) {
                            $buffer =
                                New-Object `
                                    System.Text.StringBuilder `
                                    ($length + 1)

                            [void]
                            [LocalCopilotForegroundProbe]::
                            GetWindowTextW(
                                $hwnd,
                                $buffer,
                                $buffer.Capacity
                            )

                            $title =
                                $buffer.ToString()
                        }

                        $line =
                            "{0:o} | HWND=0x{1:X} | PID={2} | WThread={3} | Process={4} | Title=[{5}]" -f `
                            (Get-Date),
                            $hwnd.ToInt64(),
                            $targetProcessId,
                            $windowThread,
                            $processName,
                            $title

                        [System.IO.File]::AppendAllText(
                            $probeLog,
                            $line +
                            [Environment]::NewLine,
                            $utf8
                        )

                        $lastHwnd =
                            $hwnd
                    }
                }
                catch {
                    $errorLine =
                        "$(Get-Date -Format o) | PROBE ERROR | $($_.Exception.Message)"

                    [System.IO.File]::AppendAllText(
                        $probeLog,
                        $errorLine +
                        [Environment]::NewLine,
                        $utf8
                    )
                }

                Start-Sleep `
                    -Milliseconds 100
            }
        }

    # -------------------------------------------------
    # LIVE bundle updater
    # -------------------------------------------------

    $bundleJob =
        Start-Job `
        -ArgumentList `
            $cache,
            $bundlePath,
            $probeLog,
            $metaPath,
            $bundleStop,
            $sessionStart `
        -ScriptBlock {

            param(
                $cache,
                $bundlePath,
                $probeLog,
                $metaPath,
                $bundleStop,
                $sessionStart
            )

            $utf8 =
                New-Object System.Text.UTF8Encoding($true)

            function Update-Bundle {

                $builder =
                    New-Object System.Text.StringBuilder

                if (Test-Path $metaPath) {
                    [void]$builder.AppendLine(
                        [System.IO.File]::ReadAllText(
                            $metaPath
                        )
                    )
                }

                [void]$builder.AppendLine("")
                [void]$builder.AppendLine(
                    "================================================="
                )
                [void]$builder.AppendLine(
                    "LIVE BUNDLE STATUS"
                )
                [void]$builder.AppendLine(
                    "================================================="
                )

                [void]$builder.AppendLine(
                    "Bundle refreshed: " +
                    (Get-Date).ToString("o")
                )

                [void]$builder.AppendLine("")

                # -------------------------------------
                # Application diagnostic logs
                # -------------------------------------

                [void]$builder.AppendLine(
                    "================================================="
                )
                [void]$builder.AppendLine(
                    "CURRENT SESSION LOG FILES"
                )
                [void]$builder.AppendLine(
                    "================================================="
                )

                $excludedNames = @(
                    "m1-3-debug-bundle.txt",
                    "m1-3-debug-bundle.tmp",
                    "m1-3-os-foreground.log",
                    "m1-3-session-meta.txt",
                    "m1-3-probe.stop",
                    "m1-3-bundle.stop",
                    "diagnostics.enabled"
                )

                $candidateFiles =
                    Get-ChildItem `
                        $cache `
                        -File `
                        -ErrorAction SilentlyContinue |
                    Where-Object {
                        $_.Name -notin $excludedNames -and
                        $_.LastWriteTime -ge
                            $sessionStart.AddSeconds(-2)
                    } |
                    Sort-Object LastWriteTime

                if (-not $candidateFiles) {
                    [void]$builder.AppendLine(
                        "No application diagnostic file has changed yet."
                    )
                }
                else {
                    foreach ($file in $candidateFiles) {

                        [void]$builder.AppendLine("")
                        [void]$builder.AppendLine(
                            "-------------------------------------------------"
                        )

                        [void]$builder.AppendLine(
                            "FILE: " + $file.FullName
                        )

                        [void]$builder.AppendLine(
                            "LastWriteTime: " +
                            $file.LastWriteTime.ToString("o")
                        )

                        [void]$builder.AppendLine(
                            "-------------------------------------------------"
                        )

                        try {
                            [void]$builder.AppendLine(
                                [System.IO.File]::ReadAllText(
                                    $file.FullName
                                )
                            )
                        }
                        catch {
                            [void]$builder.AppendLine(
                                "Could not read file: " +
                                $_.Exception.Message
                            )
                        }
                    }
                }

                # -------------------------------------
                # Independent OS probe
                # -------------------------------------

                [void]$builder.AppendLine("")
                [void]$builder.AppendLine(
                    "================================================="
                )
                [void]$builder.AppendLine(
                    "INDEPENDENT OS FOREGROUND PROBE"
                )
                [void]$builder.AppendLine(
                    "================================================="
                )

                if (Test-Path $probeLog) {
                    try {
                        [void]$builder.AppendLine(
                            [System.IO.File]::ReadAllText(
                                $probeLog
                            )
                        )
                    }
                    catch {
                        [void]$builder.AppendLine(
                            "Probe log temporarily busy."
                        )
                    }
                }
                else {
                    [void]$builder.AppendLine(
                        "Probe has not produced data yet."
                    )
                }

                $tempPath =
                    Join-Path `
                        $cache `
                        "m1-3-debug-bundle.tmp"

                [System.IO.File]::WriteAllText(
                    $tempPath,
                    $builder.ToString(),
                    $utf8
                )

                Move-Item `
                    $tempPath `
                    $bundlePath `
                    -Force
            }

            while (-not (Test-Path $bundleStop)) {

                try {
                    Update-Bundle
                }
                catch {
                }

                Start-Sleep `
                    -Milliseconds 500
            }

            # Final refresh after application closes.
            try {
                Update-Bundle
            }
            catch {
            }
        }

    # Give both diagnostic workers time to initialize.
    Start-Sleep -Milliseconds 600

    Write-Host ""
    Write-Host "=============================================="
    Write-Host "LIVE DIAGNOSTIC MODE READY"
    Write-Host "=============================================="
    Write-Host ""
    Write-Host "Bundle is refreshed every 500 ms while app runs."
    Write-Host ""
    Write-Host "Launching LocalCopilot..."
    Write-Host ""

    # -------------------------------------------------
    # Launch application
    # -------------------------------------------------

    # Diagnostic logging is opt-in.
    # Normal dotnet run does not create this flag.
    New-Item `
        -ItemType File `
        -Force `
        -Path $diagnosticsFlag |
        Out-Null

    try {
        & dotnet run `
            --project $projectPath `
            -c Debug `
            -r win-x64 `
            --no-build
    }
    finally {

        # Logging must be OFF again before we do anything else.
        Remove-Item `
            $diagnosticsFlag `
            -Force `
            -ErrorAction SilentlyContinue

        Write-Host ""
        Write-Host "LocalCopilot closed."
        Write-Host "Finalizing diagnostics..."

        # First stop OS probe.
        New-Item `
            -ItemType File `
            -Force `
            -Path $probeStop |
            Out-Null

        Wait-Job `
            $probeJob `
            -Timeout 5 |
            Out-Null

        # Then let bundle writer perform one final read.
        New-Item `
            -ItemType File `
            -Force `
            -Path $bundleStop |
            Out-Null

        Wait-Job `
            $bundleJob `
            -Timeout 5 |
            Out-Null

        Stop-Job `
            $probeJob,
            $bundleJob `
            -ErrorAction SilentlyContinue

        Remove-Job `
            $probeJob,
            $bundleJob `
            -Force `
            -ErrorAction SilentlyContinue

        Remove-Item `
            $probeStop,
            $bundleStop `
            -Force `
            -ErrorAction SilentlyContinue

        Start-Sleep `
            -Milliseconds 300

        if (Test-Path $bundlePath) {

            try {
                Get-Content `
                    $bundlePath `
                    -Raw |
                    Set-Clipboard

                Write-Host ""
                Write-Host "=============================================="
                Write-Host "LATEST BUNDLE COPIED TO CLIPBOARD"
                Write-Host "=============================================="
            }
            catch {
                Write-Host ""
                Write-Host "Bundle created, clipboard copy failed."
            }

            Write-Host ""
            Write-Host $bundlePath
            Write-Host ""
            Write-Host "Paste it directly into ChatGPT."
        }
        else {
            Write-Host ""
            Write-Host "ERROR: bundle was not created."
        }
    }
}
catch {
    Write-Host ""
    Write-Host "=============================================="
    Write-Host "DEBUG RUNNER ERROR"
    Write-Host "=============================================="
    Write-Host $_.Exception.Message
    Write-Host ""
    Write-Host "PowerShell remains open."
}
