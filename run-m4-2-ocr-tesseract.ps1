[CmdletBinding()]
param(
    [switch]$ValidateOnly,
    [string]$BenchmarkRoot = "D:\LocalAI-Prerequisites",
    [string]$ExpectedBranch = "dev/m4-2-2-benchmark-runner"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$schemaVersion = 1
$tesseractPackageId = "tesseract-ocr.tesseract"
$tesseractPackageVersion = "5.5.3"
$tessdataCommit = "87416418657359cb625c412a48b6e1d6d41c29bd"
$language = "fas+eng"
$oem = "1"
$psm = "6"

if ($ValidateOnly) {
    if ($schemaVersion -ne 1) { throw "Unexpected Tesseract benchmark schema." }
    if ($tesseractPackageId -ne "tesseract-ocr.tesseract") { throw "Unexpected Tesseract package id." }
    if ($tesseractPackageVersion -ne "5.5.3") { throw "Unexpected Tesseract package version." }
    if ($tessdataCommit.Length -ne 40) { throw "Tessdata commit must be pinned." }
    if ($language -ne "fas+eng" -or $oem -ne "1" -or $psm -ne "6") {
        throw "Unexpected Tesseract baseline configuration."
    }
    Write-Host "M4.2.2 Tesseract baseline wrapper validation: PASS"
    return
}

if ($env:OS -ne "Windows_NT") { throw "Tesseract baseline requires Windows." }

function Test-Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (Test-Elevated) {
    throw "Run the benchmark from a normal, non-Administrator PowerShell. The installer may request UAC separately."
}

$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$status = git -C $repoRoot status --short | Out-String -Width 4096
if ($LASTEXITCODE -ne 0) { throw "Unable to read Git status." }
if (-not [string]::IsNullOrWhiteSpace($status)) { throw "Tesseract baseline requires a clean working tree." }

$branch = (git -C $repoRoot branch --show-current | Out-String).Trim()
$head = (git -C $repoRoot rev-parse HEAD | Out-String).Trim()
if ($branch -ne $ExpectedBranch) {
    throw ("Expected branch '" + $ExpectedBranch + "'; current branch='" + $branch + "'.")
}

$root = [IO.Path]::GetFullPath($BenchmarkRoot)
$manifest = Join-Path $root "corpus\manifest.json"
$provenance = Join-Path $root "corpus\controlled-ui-provenance.json"
$python = Join-Path $root "python312-nuget\python\tools\python.exe"
$tessdataDir = Join-Path $root "tesseract\tessdata-fast"

if (-not (Test-Path -LiteralPath $manifest)) {
    throw "Controlled corpus manifest is missing. Run the controlled UI Paddle benchmark first."
}
if (-not (Test-Path -LiteralPath $provenance)) {
    throw "Controlled UI provenance is missing. Refusing to benchmark a different corpus."
}
if (-not (Test-Path -LiteralPath $python)) {
    throw "Isolated benchmark Python 3.12 is missing."
}

$prov = Get-Content -LiteralPath $provenance -Raw | ConvertFrom-Json
if ([string]$prov.mode -ne "controlled-os-rendered-ui") {
    throw "Corpus provenance is not controlled-os-rendered-ui."
}
if ([int]$prov.sample_count -ne 7) {
    throw "Controlled corpus must contain seven samples."
}
if ([bool]$prov.full_screen_capture) {
    throw "Full-screen corpus provenance is forbidden."
}

function Resolve-Tesseract {
    $command = Get-Command tesseract.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) { return $command.Source }

    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $candidates = @(
        (Join-Path $env:ProgramFiles "Tesseract-OCR\tesseract.exe"),
        (Join-Path $programFilesX86 "Tesseract-OCR\tesseract.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Tesseract-OCR\tesseract.exe")
    )
    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }
    return $null
}

$tesseract = Resolve-Tesseract
if ($null -eq $tesseract) {
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $winget) {
        throw "winget.exe is required to install the pinned Tesseract baseline."
    }

    Write-Host "Installing pinned Tesseract 5.5.3 baseline through Windows Package Manager..."
    Write-Host "A Windows UAC confirmation may appear for the machine-scope installer."
    & $winget.Source @(
        "install",
        "--id", $tesseractPackageId,
        "--version", $tesseractPackageVersion,
        "--exact",
        "--source", "winget",
        "--accept-source-agreements",
        "--accept-package-agreements",
        "--silent",
        "--disable-interactivity"
    )
    if ($LASTEXITCODE -ne 0) {
        throw "Pinned Tesseract installation failed with exit code $LASTEXITCODE."
    }

    $tesseract = Resolve-Tesseract
    if ($null -eq $tesseract) {
        throw "Tesseract installation completed but tesseract.exe was not found."
    }
}

New-Item -ItemType Directory -Path $tessdataDir -Force | Out-Null
$curl = Get-Command curl.exe -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $curl) { throw "curl.exe is required for pinned tessdata download." }

foreach ($name in @("fas","eng")) {
    $destination = Join-Path $tessdataDir ($name + ".traineddata")
    if (-not (Test-Path -LiteralPath $destination)) {
        $url = "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/$tessdataCommit/$name.traineddata"
        $partial = $destination + ".partial"
        Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
        Write-Host ("Downloading pinned tessdata_fast language: " + $name)
        & $curl.Source @(
            "--fail",
            "--location",
            "--show-error",
            "--silent",
            "--retry", "3",
            "--connect-timeout", "30",
            "--max-time", "600",
            "--output", $partial,
            $url
        )
        if ($LASTEXITCODE -ne 0) {
            Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
            throw ("Download failed for " + $name + ".traineddata.")
        }
        Move-Item -LiteralPath $partial -Destination $destination -Force
    }
}

Write-Host "Running local-only Tesseract fas+eng controlled baseline..."
Write-Host "Raw OCR text will not be printed or persisted."
Write-Host "The exact existing controlled OS-rendered corpus will be reused."

$script = Join-Path $repoRoot "scripts\m4_2_tesseract_benchmark.py"
& $python @(
    $script,
    "--benchmark-root", $root,
    "--manifest", $manifest,
    "--tesseract-exe", $tesseract,
    "--tessdata-dir", $tessdataDir,
    "--language", $language,
    "--oem", $oem,
    "--psm", $psm
)
if ($LASTEXITCODE -ne 0) {
    throw "Tesseract controlled baseline failed with exit code $LASTEXITCODE."
}
