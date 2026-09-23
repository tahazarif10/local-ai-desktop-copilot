[CmdletBinding()]
param(
    [switch]$ValidateOnly,
    [string]$BenchmarkRoot = "D:\LocalAI-Prerequisites",
    [string]$ExpectedBranch = "dev/m4-2-2-benchmark-runner"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$schemaVersion = 1
$categories = @("english-ui","terminal-console","browser-ui")

if ($ValidateOnly) {
    if ($schemaVersion -ne 1) { throw "Unexpected matched English comparison schema." }
    if ($categories.Count -ne 3) { throw "Matched English comparison must use exactly three categories." }
    Write-Host "M4.2.2 matched English comparison validation: PASS"
    return
}

if ($env:OS -ne "Windows_NT") { throw "Matched English comparison requires Windows." }

function Test-Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (Test-Elevated) {
    throw "Run the matched English comparison from a normal, non-Administrator PowerShell."
}

$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$status = git -C $repoRoot status --short | Out-String -Width 4096
if ($LASTEXITCODE -ne 0) { throw "Unable to read Git status." }
if (-not [string]::IsNullOrWhiteSpace($status)) {
    throw "Matched English comparison requires a clean working tree."
}

$branch = (git -C $repoRoot branch --show-current | Out-String).Trim()
$head = (git -C $repoRoot rev-parse HEAD | Out-String).Trim()
if ($branch -ne $ExpectedBranch) {
    throw ("Expected branch '" + $ExpectedBranch + "'; current branch='" + $branch + "'.")
}

$paddleRunner = Join-Path $repoRoot "run-m4-2-ocr-benchmark.ps1"
$tesseractRunner = Join-Path $repoRoot "run-m4-2-ocr-tesseract.ps1"
$windowsRunner = Join-Path $repoRoot "run-m4-2-ocr-windows-media.ps1"

foreach ($required in @($paddleRunner,$tesseractRunner,$windowsRunner)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw ("Required comparison runner is missing: " + $required)
    }
}

Write-Host ""
Write-Host "=================================================="
Write-Host "M4.2.2 MATCHED ENGLISH SUBSET COMPARISON"
Write-Host "=================================================="
Write-Host ("head=" + $head)
Write-Host "categories=english-ui,terminal-console,browser-ui"
Write-Host "corpus_reused=True"
Write-Host "raw_ocr_logged=False"
Write-Host "raw_content_persisted=False"
Write-Host ""

Write-Host "----- PaddleOCR matched subset -----"
& $paddleRunner -Run -BenchmarkRoot $BenchmarkRoot -ExpectedBranch $ExpectedBranch -IncludeCategory $categories
if ($LASTEXITCODE -ne 0) { throw "PaddleOCR matched subset failed." }

Write-Host ""
Write-Host "----- Tesseract matched subset -----"
& $tesseractRunner -BenchmarkRoot $BenchmarkRoot -ExpectedBranch $ExpectedBranch -IncludeCategory $categories
if ($LASTEXITCODE -ne 0) { throw "Tesseract matched subset failed." }

Write-Host ""
Write-Host "----- Windows Media OCR matched subset -----"
& $windowsRunner -BenchmarkRoot $BenchmarkRoot -ExpectedBranch $ExpectedBranch
if ($LASTEXITCODE -ne 0) { throw "Windows Media OCR matched subset failed." }

Write-Host ""
Write-Host "M4.2.2 MATCHED ENGLISH SUBSET COMPARISON: PASS"
