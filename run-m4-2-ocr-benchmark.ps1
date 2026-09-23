[CmdletBinding()]
param(
    [switch]$InitializeCorpus,
    [switch]$PrepareModels,
    [switch]$Run,
    [switch]$ValidateOnly,
    [string]$BenchmarkRoot = "D:\LocalAI-Prerequisites",
    [string]$ExpectedBranch = "dev/m4-2-2-benchmark-runner",
    [int]$WarmRuns = 3,
    [double]$InferenceTimeoutSeconds = 15.0,
    [string[]]$IncludeCategory = @(),
    [ValidateSet("PP-OCRv5_mobile_det","PP-OCRv5_server_det")]
    [string]$DetectionModel = "PP-OCRv5_mobile_det",
    [ValidateSet("arabic_PP-OCRv5_mobile_rec","en_PP-OCRv5_mobile_rec")]
    [string]$RecognitionModel = "arabic_PP-OCRv5_mobile_rec"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$schemaVersion = 1
$defaultRecognitionModel = "arabic_PP-OCRv5_mobile_rec"
$defaultDetectionModel = "PP-OCRv5_mobile_det"
$englishOnlyCategories = @("english-ui","terminal-console","browser-ui")

function Test-M422Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Write-M422CorpusScaffold {
    param([Parameter(Mandatory = $true)][string]$Root)

    $corpusRoot = Join-Path $Root "corpus"
    $imagesRoot = Join-Path $corpusRoot "images"
    $truthRoot = Join-Path $corpusRoot "ground-truth"
    $manifestPath = Join-Path $corpusRoot "manifest.json"
    $instructionsPath = Join-Path $corpusRoot "README-LOCAL.txt"

    New-Item -ItemType Directory -Path $imagesRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $truthRoot -Force | Out-Null

    $definitions = @(
        @{ id = "s001"; category = "persian-ui" },
        @{ id = "s002"; category = "english-ui" },
        @{ id = "s003"; category = "mixed-fa-en" },
        @{ id = "s004"; category = "terminal-console" },
        @{ id = "s005"; category = "dialog" },
        @{ id = "s006"; category = "browser-ui" },
        @{ id = "s007"; category = "desktop-app-ui" }
    )

    if (-not (Test-Path -LiteralPath $manifestPath)) {
        $samples = @()
        foreach ($definition in $definitions) {
            $sampleId = [string]$definition.id
            $samples += [ordered]@{
                id = $sampleId
                category = [string]$definition.category
                image = "images/$sampleId.png"
                ground_truth_file = "ground-truth/$sampleId.txt"
            }
        }

        $manifest = [ordered]@{ schema = $schemaVersion; samples = $samples }
        $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    }

    foreach ($definition in $definitions) {
        $truthPath = Join-Path $truthRoot ([string]$definition.id + ".txt")
        if (-not (Test-Path -LiteralPath $truthPath)) {
            New-Item -ItemType File -Path $truthPath | Out-Null
        }
    }

    $instructions = @(
        "M4.2.2 LOCAL OCR CORPUS",
        "",
        "This folder is intentionally outside Git.",
        "",
        "For each opaque sample ID:",
        "1. Save a bounded ROI screenshot to images\<id>.png.",
        "2. Type the exact visible text into ground-truth\<id>.txt.",
        "3. Preserve visible line breaks and internal spacing.",
        "4. Do not paste screenshot or ground-truth contents into GitHub, logs, or chat.",
        "",
        "s001  Persian UI text",
        "s002  English UI text",
        "s003  Mixed Persian-English technical UI text",
        "s004  Terminal/console text",
        "s005  Dialog text",
        "s006  Browser UI text",
        "s007  Desktop application UI text",
        "",
        "Use bounded application/UI regions. Do not use full-screen screenshots."
    )
    $instructions | Set-Content -LiteralPath $instructionsPath -Encoding UTF8

    Write-Host ""
    Write-Host "M4.2.2 corpus scaffold: READY"
    Write-Host "corpus_root=$corpusRoot"
    Write-Host "manifest=$manifestPath"
    Write-Host "No screenshot or ground-truth content was created."
}

if ($ValidateOnly) {
    if ($schemaVersion -ne 1) { throw "Unexpected M4.2.2 benchmark wrapper schema." }
    if ($defaultRecognitionModel -ne "arabic_PP-OCRv5_mobile_rec") { throw "Default Persian recognition model pin changed." }
    if ($defaultDetectionModel -ne "PP-OCRv5_mobile_det") { throw "Default detection model pin changed." }
    if ($WarmRuns -lt 1 -or $WarmRuns -gt 20) { throw "WarmRuns must be between 1 and 20." }
    if ($InferenceTimeoutSeconds -le 0) { throw "InferenceTimeoutSeconds must be positive." }
    $runnerPath = Join-Path $PSScriptRoot "scripts\m4_2_ocr_benchmark.py"
    if (-not (Test-Path -LiteralPath $runnerPath)) { throw "M4.2.2 Python benchmark runner is missing." }
    Write-Host "M4.2.2 OCR benchmark wrapper validation: PASS"
    return
}

if ($env:OS -ne "Windows_NT") { throw "M4.2.2 controlled benchmark requires Windows." }

if ($RecognitionModel -eq "en_PP-OCRv5_mobile_rec") {
    $requested = @($IncludeCategory | Sort-Object -Unique)
    if ($requested.Count -ne $englishOnlyCategories.Count) {
        throw "English-specific recognition requires exactly the three English-only benchmark categories."
    }
    foreach ($category in $englishOnlyCategories) {
        if ($requested -notcontains $category) {
            throw "English-specific recognition requires english-ui, terminal-console, and browser-ui."
        }
    }
}
if (Test-M422Elevated) { throw "Run the M4.2.2 benchmark from a normal, non-Administrator PowerShell." }

$modeCount = @([bool]$InitializeCorpus, [bool]$PrepareModels, [bool]$Run) | Where-Object { $_ } | Measure-Object | Select-Object -ExpandProperty Count
if ($modeCount -ne 1) { throw "Choose exactly one mode: -InitializeCorpus, -PrepareModels, or -Run." }

$repoRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$gitStatus = git -C $repoRoot status --short | Out-String -Width 4096
if ($LASTEXITCODE -ne 0) { throw "Unable to resolve Git working-tree status." }
if (-not [string]::IsNullOrWhiteSpace($gitStatus)) { throw "M4.2.2 benchmark requires a clean working tree." }
$branch = (git -C $repoRoot branch --show-current | Out-String).Trim()
$head = (git -C $repoRoot rev-parse HEAD | Out-String).Trim()
if (-not [string]::IsNullOrWhiteSpace($ExpectedBranch) -and $branch -ne $ExpectedBranch) {
    throw ("Run M4.2.2 benchmark only from branch '" + $ExpectedBranch + "'. Current branch='" + $branch + "'.")
}

$root = [System.IO.Path]::GetFullPath($BenchmarkRoot)
$python = Join-Path $root "python312-nuget\python\tools\python.exe"
$runner = Join-Path $repoRoot "scripts\m4_2_ocr_benchmark.py"
$tempRoot = Join-Path $root "temp"
$pipCacheRoot = Join-Path $root "pip-cache"
$modelCacheRoot = Join-Path $root "models"

New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
New-Item -ItemType Directory -Path $pipCacheRoot -Force | Out-Null
New-Item -ItemType Directory -Path $modelCacheRoot -Force | Out-Null

$env:TEMP = $tempRoot
$env:TMP = $tempRoot
$env:PIP_CACHE_DIR = $pipCacheRoot
$env:PADDLE_PDX_CACHE_HOME = $modelCacheRoot

if ($InitializeCorpus) {
    Write-M422CorpusScaffold -Root $root
    return
}

if (-not (Test-Path -LiteralPath $python)) {
    throw ("Project-local Python 3.12 runtime is missing: " + $python + ". Complete the accepted M4.2.2 environment setup first.")
}
if (-not (Test-Path -LiteralPath $runner)) { throw "M4.2.2 Python benchmark runner is missing." }

$commonArguments = @(
    $runner,
    "--benchmark-root", $root,
    "--device", "gpu:0",
    "--engine", "paddle_static",
    "--detection-model", $DetectionModel,
    "--recognition-model", $RecognitionModel,
    "--warm-runs", [string]$WarmRuns,
    "--inference-timeout-seconds", [string]$InferenceTimeoutSeconds
)

if ($PrepareModels) {
    Write-Host "Preparing pinned OCR models under local benchmark storage..."
    Write-Host "No benchmark screenshots or ground truth will be read."
    & $python @commonArguments --prepare-models
    if ($LASTEXITCODE -ne 0) { throw "M4.2.2 model preparation failed with exit code $LASTEXITCODE." }
    return
}

$manifest = Join-Path $root "corpus\manifest.json"
if (-not (Test-Path -LiteralPath $manifest)) {
    throw ("Local corpus manifest is missing. Run -InitializeCorpus first: " + $manifest)
}

Write-Host "Running local-only controlled OCR benchmark..."
Write-Host "Raw OCR text will not be printed or persisted."
Write-Host "Only aggregate metrics are written below the benchmark root."

$runArguments = @($commonArguments) + @("--manifest", $manifest, "--run")
foreach ($category in $IncludeCategory) {
    $runArguments += @("--include-category", $category)
}
& $python @runArguments
if ($LASTEXITCODE -ne 0) { throw "M4.2.2 controlled OCR benchmark failed with exit code $LASTEXITCODE." }
