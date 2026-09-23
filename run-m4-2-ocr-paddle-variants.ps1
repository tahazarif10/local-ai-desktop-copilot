[CmdletBinding()]
param(
    [switch]$ValidateOnly,
    [string]$BenchmarkRoot = "D:\LocalAI-Prerequisites",
    [string]$ExpectedBranch = "dev/m4-2-2-benchmark-runner"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$schemaVersion = 1
$englishCategories = @("english-ui","terminal-console","browser-ui")

if ($ValidateOnly) {
    if ($schemaVersion -ne 1) { throw "Unexpected Paddle variant benchmark schema." }
    if ($englishCategories.Count -ne 3) { throw "Unexpected English-only category set." }
    Write-Host "M4.2.2 Paddle variant benchmark validation: PASS"
    return
}

if ($env:OS -ne "Windows_NT") { throw "Paddle variant benchmark requires Windows." }

function Test-Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (Test-Elevated) {
    throw "Run the Paddle variant benchmark from a normal, non-Administrator PowerShell."
}

$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$status = git -C $repoRoot status --short | Out-String -Width 4096
if ($LASTEXITCODE -ne 0) { throw "Unable to read Git status." }
if (-not [string]::IsNullOrWhiteSpace($status)) {
    throw "Paddle variant benchmark requires a clean working tree."
}

$branch = (git -C $repoRoot branch --show-current | Out-String).Trim()
$head = (git -C $repoRoot rev-parse HEAD | Out-String).Trim()
if ($branch -ne $ExpectedBranch) {
    throw ("Expected branch '" + $ExpectedBranch + "'; current branch='" + $branch + "'.")
}

$runner = Join-Path $repoRoot "run-m4-2-ocr-benchmark.ps1"
if (-not (Test-Path -LiteralPath $runner)) { throw "Paddle benchmark runner is missing." }

Write-Host ""
Write-Host "=================================================="
Write-Host "M4.2.2 PADDLE BOUNDED VARIANT BENCHMARK"
Write-Host "=================================================="
Write-Host ("head=" + $head)
Write-Host "corpus_reused=True"
Write-Host "raw_ocr_logged=False"
Write-Host "raw_content_persisted=False"
Write-Host ""

Write-Host "----- Variant A: server detector + multilingual Arabic/Persian recognizer; full seven categories -----"
& $runner -PrepareModels -BenchmarkRoot $BenchmarkRoot -ExpectedBranch $ExpectedBranch -DetectionModel "PP-OCRv5_server_det" -RecognitionModel "arabic_PP-OCRv5_mobile_rec"
if ($LASTEXITCODE -ne 0) { throw "Variant A model preparation failed." }
& $runner -Run -BenchmarkRoot $BenchmarkRoot -ExpectedBranch $ExpectedBranch -DetectionModel "PP-OCRv5_server_det" -RecognitionModel "arabic_PP-OCRv5_mobile_rec"
if ($LASTEXITCODE -ne 0) { throw "Variant A benchmark failed." }

Write-Host ""
Write-Host "----- Variant B: mobile detector + English-specific recognizer; matched English subset -----"
& $runner -PrepareModels -BenchmarkRoot $BenchmarkRoot -ExpectedBranch $ExpectedBranch -DetectionModel "PP-OCRv5_mobile_det" -RecognitionModel "en_PP-OCRv5_mobile_rec" -IncludeCategory $englishCategories
if ($LASTEXITCODE -ne 0) { throw "Variant B model preparation failed." }
& $runner -Run -BenchmarkRoot $BenchmarkRoot -ExpectedBranch $ExpectedBranch -DetectionModel "PP-OCRv5_mobile_det" -RecognitionModel "en_PP-OCRv5_mobile_rec" -IncludeCategory $englishCategories
if ($LASTEXITCODE -ne 0) { throw "Variant B benchmark failed." }

Write-Host ""
Write-Host "----- Variant C: server detector + English-specific recognizer; matched English subset -----"
& $runner -PrepareModels -BenchmarkRoot $BenchmarkRoot -ExpectedBranch $ExpectedBranch -DetectionModel "PP-OCRv5_server_det" -RecognitionModel "en_PP-OCRv5_mobile_rec" -IncludeCategory $englishCategories
if ($LASTEXITCODE -ne 0) { throw "Variant C model preparation failed." }
& $runner -Run -BenchmarkRoot $BenchmarkRoot -ExpectedBranch $ExpectedBranch -DetectionModel "PP-OCRv5_server_det" -RecognitionModel "en_PP-OCRv5_mobile_rec" -IncludeCategory $englishCategories
if ($LASTEXITCODE -ne 0) { throw "Variant C benchmark failed." }

Write-Host ""
Write-Host "M4.2.2 PADDLE BOUNDED VARIANT BENCHMARK: PASS"
