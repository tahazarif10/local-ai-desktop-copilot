[CmdletBinding()]
param(
    [switch]$ValidateOnly,
    [switch]$Provision,
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
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot "tools\LocalCopilot.OcrProvisioner\LocalCopilot.OcrProvisioner.csproj"))) { throw "OCR provisioner project is missing." }
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
$provisioner = Join-Path $repoRoot "tools\LocalCopilot.OcrProvisioner\LocalCopilot.OcrProvisioner.csproj"

foreach ($required in @($python,$detector,$recognizer,$serverScript,$provisioner)) {
    if (-not (Test-Path -LiteralPath $required)) { throw ("Required OCR server dependency is missing: " + $required) }
}

if ($Provision) {
    if (Test-Path -LiteralPath $transportRoot) {
        $existing = Get-ChildItem -LiteralPath $transportRoot -Force -ErrorAction SilentlyContinue
        if (@($existing).Count -ne 0) { throw "OCR transport provisioning directory is not empty. Refusing to overwrite credentials." }
    }
    New-Item -ItemType Directory -Path $transportRoot -Force | Out-Null
    & dotnet run --project $provisioner -- --output-dir $transportRoot --server-name $ServerName
    if ($LASTEXITCODE -ne 0) { throw "OCR transport provisioning failed." }
    foreach ($secretPath in @($keyPath,$authPath,(Join-Path $clientBundle "ocr-auth-key.hex"))) {
        & icacls.exe $secretPath /inheritance:r /grant:r ("{0}:(R,W)" -f $env:USERNAME) | Out-Null
        if ($LASTEXITCODE -ne 0) { throw ("Unable to restrict credential ACL: " + $secretPath) }
    }
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

& $python $serverScript --bind $BindAddress --port $Port --cert-file $certPath --key-file $keyPath --auth-key-file $authPath --model-root $modelRoot --device "gpu:0" --diagnostic-delay-ms $DiagnosticDelayMs
if ($LASTEXITCODE -ne 0) { throw ("OCR server exited with code " + $LASTEXITCODE) }
