[CmdletBinding()]
param(
    [switch]$ValidateOnly,
    [switch]$Provision,
    [switch]$AcceptanceMode,
    [string]$BenchmarkRoot = "D:\LocalAI-Prerequisites",
    [string]$ExpectedBranch = "dev/m4-2-3b-ocr-transport",
    [string]$BindAddress = "0.0.0.0",
    [int]$Port = 49321,
    [ValidateRange(0,5000)]
    [int]$DiagnosticDelayMs = 0,
    [string]$ServerName = $env:COMPUTERNAME
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$schemaVersion = 1
$expectedPort = 49321
$expectedDetectionModel = "PP-OCRv5_server_det"
$expectedRecognitionModel = "arabic_PP-OCRv5_mobile_rec"

if ($ValidateOnly) {
    if ($schemaVersion -ne 1) { throw "Unexpected OCR server wrapper schema." }
    if ($expectedPort -ne 49321) { throw "Unexpected OCR server port." }
    if ($expectedDetectionModel -ne "PP-OCRv5_server_det") { throw "Unexpected detector pin." }
    if ($expectedRecognitionModel -ne "arabic_PP-OCRv5_mobile_rec") { throw "Unexpected recognizer pin." }
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot "scripts\m4_2_ocr_server.py"))) { throw "OCR server script is missing." }
    Write-Host "M4.2.3 OCR SERVER WRAPPER VALIDATION: PASS"
    return
}

if ($env:OS -ne "Windows_NT") { throw "M4.2.3 OCR server requires Windows." }

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Run the OCR server from a normal, non-Administrator PowerShell." }

$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$branch = (git -C $repoRoot branch --show-current | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw "Unable to read Git branch." }
if ($branch -ne $ExpectedBranch) { throw ("Expected branch '" + $ExpectedBranch + "'; current branch='" + $branch + "'.") }

$head = (git -C $repoRoot rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw "Unable to read Git HEAD." }

$status = git -C $repoRoot status --short | Out-String -Width 4096
if ($LASTEXITCODE -ne 0) { throw "Unable to read Git status." }
if (-not [string]::IsNullOrWhiteSpace($status)) { throw "OCR server launch requires a clean working tree." }

if ($Port -ne $expectedPort) { throw "Physical acceptance requires the pinned OCR server port 49321." }
if ([string]::IsNullOrWhiteSpace($ServerName)) { throw "ServerName is required." }

$root = [IO.Path]::GetFullPath($BenchmarkRoot)
$python = Join-Path $root "python312-nuget\python\tools\python.exe"
$modelRoot = Join-Path $root "models"
$detector = Join-Path $modelRoot ("official_models\" + $expectedDetectionModel)
$recognizer = Join-Path $modelRoot ("official_models\" + $expectedRecognitionModel)
$transportRoot = Join-Path $root "ocr-transport"
$certPath = Join-Path $transportRoot "server-cert.pem"
$keyPath = Join-Path $transportRoot "server-key.pem"
$authPath = Join-Path $transportRoot "server-auth-key.hex"
$clientBundle = Join-Path $transportRoot "client-bundle"
$clientConfig = Join-Path $clientBundle "ocr-client-config.json"
$serverScript = Join-Path $repoRoot "scripts\m4_2_ocr_server.py"
$transportProbe = Join-Path $repoRoot "scripts\m4_2_ocr_transport_probe.py"
function Find-OpenSsl {
    $command = Get-Command openssl.exe -ErrorAction SilentlyContinue
    if ($null -ne $command -and (Test-Path -LiteralPath $command.Source)) {
        return [IO.Path]::GetFullPath($command.Source)
    }

    $gitCommand = Get-Command git.exe -ErrorAction SilentlyContinue
    if ($null -ne $gitCommand -and (Test-Path -LiteralPath $gitCommand.Source)) {
        $gitFile = [IO.FileInfo]$gitCommand.Source
        $gitRoot = $gitFile.Directory
        if ($null -ne $gitRoot -and $gitRoot.Name -ieq "cmd") {
            $gitRoot = $gitRoot.Parent
        }

        if ($null -ne $gitRoot) {
            foreach ($relative in @("usr\bin\openssl.exe","mingw64\bin\openssl.exe","mingw32\bin\openssl.exe")) {
                $candidate = Join-Path $gitRoot.FullName $relative
                if (Test-Path -LiteralPath $candidate) {
                    return [IO.Path]::GetFullPath($candidate)
                }
            }
        }
    }

    throw "OpenSSL was not found. Git for Windows normally includes it; no download is performed by this runner."
}

$openssl = Find-OpenSsl

foreach ($required in @($python,$detector,$recognizer,$serverScript,$transportProbe,$openssl)) {
    if (-not (Test-Path -LiteralPath $required)) { throw ("Required OCR server dependency is missing: " + $required) }
}

if ($Provision) {
    if (Test-Path -LiteralPath $transportRoot) {
        $existing = Get-ChildItem -LiteralPath $transportRoot -Force -ErrorAction SilentlyContinue
        if (@($existing).Count -ne 0) { throw "OCR transport provisioning directory is not empty. Refusing to overwrite credentials." }
    }

    New-Item -ItemType Directory -Path $transportRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $clientBundle -Force | Out-Null

    $sanKind = "DNS"
    $parsedIp = $null
    if ([Net.IPAddress]::TryParse($ServerName,[ref]$parsedIp)) {
        $sanKind = "IP"
    }

    & $openssl req `
        -x509 `
        -newkey rsa:3072 `
        -sha256 `
        -days 730 `
        -nodes `
        -keyout $keyPath `
        -out $certPath `
        -subj "/CN=LocalCopilot OCR Server" `
        -addext ("subjectAltName={0}:{1}" -f $sanKind,$ServerName) `
        -addext "keyUsage=digitalSignature,keyEncipherment" `
        -addext "extendedKeyUsage=serverAuth"
    if ($LASTEXITCODE -ne 0) {
        throw "OpenSSL certificate provisioning failed."
    }

    & $openssl rand -hex -out $authPath 32
    if ($LASTEXITCODE -ne 0) {
        throw "OpenSSL authentication-key provisioning failed."
    }

    $clientAuthPath = Join-Path $clientBundle "ocr-auth-key.hex"
    Copy-Item -LiteralPath $authPath -Destination $clientAuthPath

    $fingerprintLine = (& $openssl x509 -in $certPath -noout -fingerprint -sha256 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $fingerprintLine -notmatch "=(?<fp>[0-9A-Fa-f:]{95})$") {
        throw "Unable to calculate server certificate SHA-256 fingerprint."
    }

    $certificateSha256Provisioned = ($Matches.fp -replace ":","").ToLowerInvariant()

    $clientConfiguration = [ordered]@{
        schema = 1
        server_name = $ServerName
        port = $expectedPort
        server_certificate_sha256 = $certificateSha256Provisioned
        authentication_key_file = "ocr-auth-key.hex"
    }

    $clientConfiguration | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $clientConfig -Encoding UTF8

    $currentPrincipal = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    foreach ($secretPath in @($keyPath,$authPath,$clientAuthPath)) {
        & icacls.exe $secretPath /inheritance:r /grant:r ("{0}:F" -f $currentPrincipal) | Out-Null
        if ($LASTEXITCODE -ne 0) { throw ("Unable to restrict credential ACL: " + $secretPath) }
    }

    Write-Host "M4.2.3 OCR PROVISIONING: PASS"
    Write-Host ("openssl=" + $openssl)
    Write-Host ("server_name=" + $ServerName)
    Write-Host ("certificate_sha256=" + $certificateSha256Provisioned)
    Write-Host ("client_bundle=" + $clientBundle)
    Write-Host "download_performed=False"
    Write-Host "authentication_key_printed=False"
    Write-Host "private_key_printed=False"
}

foreach ($required in @($certPath,$keyPath,$authPath,$clientConfig)) {
    if (-not (Test-Path -LiteralPath $required)) { throw ("OCR transport material is missing: " + $required + ". Run once with -Provision.") }
}

$config = Get-Content -LiteralPath $clientConfig -Raw | ConvertFrom-Json
if ([int]$config.schema -ne 1) { throw "Unexpected OCR client config schema." }
if ([int]$config.port -ne $expectedPort) { throw "Unexpected OCR client config port." }
$certificateSha256 = [string]$config.server_certificate_sha256
if ($certificateSha256 -notmatch "^[0-9a-f]{64}$") { throw "Invalid certificate SHA256 in client config." }

Write-Host ""
Write-Host "=============================================="
Write-Host "M4.2.3 OCR SERVER"
Write-Host "=============================================="
Write-Host ("head=" + $head)
Write-Host ("server_name=" + $ServerName)
Write-Host ("port=" + $Port)
Write-Host ("certificate_sha256=" + $certificateSha256)
Write-Host ("diagnostic_delay_ms=" + $DiagnosticDelayMs)
Write-Host ("client_bundle=" + $clientBundle)
Write-Host "authentication_key_printed=False"
Write-Host "private_key_printed=False"
Write-Host "raw_pixels_logged=False"
Write-Host "raw_ocr_logged=False"
Write-Host ""
Write-Host "Keep this PowerShell window open while client acceptance runs."
Write-Host ""

if (-not $AcceptanceMode) {
    & $python $serverScript --bind $BindAddress --port $Port --cert-file $certPath --key-file $keyPath --auth-key-file $authPath --model-root $modelRoot --device "gpu:0" --diagnostic-delay-ms $DiagnosticDelayMs
    if ($LASTEXITCODE -ne 0) { throw ("OCR server exited with code " + $LASTEXITCODE) }
    return
}

if ($DiagnosticDelayMs -lt 1000) {
    throw "AcceptanceMode requires DiagnosticDelayMs of at least 1000 ms so Busy/latest-wins behavior is measurable."
}

$stdoutPath = Join-Path $transportRoot "acceptance-server.stdout.log"
$stderrPath = Join-Path $transportRoot "acceptance-server.stderr.log"
Remove-Item -LiteralPath $stdoutPath,$stderrPath -Force -ErrorAction SilentlyContinue

$serverArguments = @(
    $serverScript,
    "--bind", $BindAddress,
    "--port", [string]$Port,
    "--cert-file", $certPath,
    "--key-file", $keyPath,
    "--auth-key-file", $authPath,
    "--model-root", $modelRoot,
    "--device", "gpu:0",
    "--diagnostic-delay-ms", [string]$DiagnosticDelayMs
)

$serverProcess = Start-Process -FilePath $python -ArgumentList $serverArguments -PassThru -NoNewWindow -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath

try {
    $readyDeadline = [DateTimeOffset]::UtcNow.AddSeconds(120)
    $ready = $false

    while ([DateTimeOffset]::UtcNow -lt $readyDeadline) {
        $serverProcess.Refresh()
        if ($serverProcess.HasExited) {
            $stderrText = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { "" }
            throw ("OCR server exited before READY. stderr_length=" + $stderrText.Length)
        }

        if (Test-Path -LiteralPath $stdoutPath) {
            $stdoutText = Get-Content -LiteralPath $stdoutPath -Raw
            if ($stdoutText -match "M4\.2\.3 OCR SERVER: READY") {
                $ready = $true
                break
            }
        }

        Start-Sleep -Milliseconds 250
    }

    if (-not $ready) {
        throw "OCR server did not report READY within 120 seconds."
    }

    & $python $transportProbe --client-bundle $clientBundle --server-host "127.0.0.1" --expect-busy
    if ($LASTEXITCODE -ne 0) {
        throw "Authenticated OCR transport probe failed."
    }

    Write-Host ""
    Write-Host "M4.2.3 OCR SERVER ACCEPTANCE PRECHECK: PASS"
    Write-Host "tls_pin_negative=PASS"
    Write-Host "wrong_key_negative=PASS"
    Write-Host "replay_negative=PASS"
    Write-Host "deadline_negative=PASS"
    Write-Host "single_active_busy=PASS"
    Write-Host ""
    Write-Host "Server remains running for the fixed-client product acceptance."
    Write-Host "Press Ctrl+C only after the client acceptance has finished."
    Write-Host ""

    [void]$serverProcess.WaitForExit()
}
finally {
    try {
        $serverProcess.Refresh()
        if (-not $serverProcess.HasExited) {
            Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }
    catch {
    }

    if (Test-Path -LiteralPath $stderrPath) {
        $stderrText = Get-Content -LiteralPath $stderrPath -Raw
        if ($stderrText -match "(?i)(rec_texts|ocr text|pixel payload)") {
            throw "Server acceptance stderr contains prohibited content markers."
        }
    }
}
