[CmdletBinding()]
param(
    [switch]$ValidateOnly,
    [string]$BenchmarkRoot = "D:\LocalAI-Prerequisites",
    [string]$ExpectedBranch = "dev/m4-2-2-benchmark-runner",
    [string[]]$IncludeCategory = @(),
    [ValidateSet("fast","best")]
    [string]$TessdataVariant = "fast"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$schemaVersion = 1
$tesseractPackageId = "tesseract-ocr.tesseract"
$tesseractPackageVersion = "5.5.3"
$tesseractInstallerUrl = "https://github.com/tesseract-ocr/tesseract/releases/download/5.5.3/tesseract-ocr-w64-setup-5.5.3.20260724.exe"
$tesseractInstallerSha256 = "BEE9E3434BD94FD65387D9BE28CD467A41F61B1275383B55B0F59A1331270AE4"
$tessdataFastCommit = "87416418657359cb625c412a48b6e1d6d41c29bd"
$tessdataBestCommit = "e12c65a915945e4c28e237a9b52bc4a8f39a0cec"
$language = "fas+eng"
$oem = "1"
$psm = "6"

if ($ValidateOnly) {
    if ($schemaVersion -ne 1) { throw "Unexpected Tesseract benchmark schema." }
    if ($tesseractPackageId -ne "tesseract-ocr.tesseract") { throw "Unexpected Tesseract package id." }
    if ($tesseractPackageVersion -ne "5.5.3") { throw "Unexpected Tesseract package version." }
    if ($tesseractInstallerUrl -notmatch "^https://github\.com/tesseract-ocr/tesseract/releases/download/5\.5\.3/") { throw "Unexpected Tesseract installer source." }
    if ($tesseractInstallerSha256 -notmatch "^[A-F0-9]{64}$") { throw "Unexpected Tesseract installer SHA256." }
    if ($tessdataFastCommit.Length -ne 40 -or $tessdataBestCommit.Length -ne 40) { throw "Tessdata commits must be pinned." }
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
$tessdataCommit = if ($TessdataVariant -eq "best") { $tessdataBestCommit } else { $tessdataFastCommit }
$tessdataRepo = if ($TessdataVariant -eq "best") { "tessdata_best" } else { "tessdata_fast" }
$tessdataDir = Join-Path $root ("tesseract\tessdata-" + $TessdataVariant)

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
    if ($null -ne $winget) {
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
    }
    else {
        $curl = Get-Command curl.exe -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $curl) {
            throw "Neither winget.exe nor curl.exe is available for the pinned Tesseract installation."
        }

        $installerRoot = Join-Path $root "tesseract\installer"
        New-Item -ItemType Directory -Path $installerRoot -Force | Out-Null
        $installerPath = Join-Path $installerRoot "tesseract-ocr-w64-setup-5.5.3.20260724.exe"
        $partialPath = $installerPath + ".partial"

        if (-not (Test-Path -LiteralPath $installerPath)) {
            Remove-Item -LiteralPath $partialPath -Force -ErrorAction SilentlyContinue
            Write-Host "winget.exe is unavailable; downloading the pinned official Tesseract installer..."
            & $curl.Source @(
                "--fail",
                "--location",
                "--show-error",
                "--progress-bar",
                "--retry", "3",
                "--retry-delay", "3",
                "--connect-timeout", "30",
                "--max-time", "900",
                "--output", $partialPath,
                $tesseractInstallerUrl
            )
            if ($LASTEXITCODE -ne 0) {
                Remove-Item -LiteralPath $partialPath -Force -ErrorAction SilentlyContinue
                throw "Pinned Tesseract installer download failed with exit code $LASTEXITCODE."
            }
            Move-Item -LiteralPath $partialPath -Destination $installerPath -Force
        }
        else {
            Write-Host "Using cached pinned Tesseract installer."
        }

        Write-Host "Verifying Tesseract installer SHA256 against the pinned winget manifest..."
        $actualInstallerSha256 = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($actualInstallerSha256 -ne $tesseractInstallerSha256) {
            throw "Tesseract installer SHA256 mismatch; refusing installation."
        }

        Write-Host "Launching the verified Tesseract installer silently..."
        Write-Host "A Windows UAC confirmation may appear. Approve it to continue."
        $installerProcess = Start-Process -FilePath $installerPath -ArgumentList "/S" -Verb RunAs -Wait -PassThru
        if ($installerProcess.ExitCode -ne 0) {
            throw ("Pinned Tesseract installer failed with exit code " + $installerProcess.ExitCode + ".")
        }
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
        $url = "https://raw.githubusercontent.com/tesseract-ocr/$tessdataRepo/$tessdataCommit/$name.traineddata"
        $partial = $destination + ".partial"
        Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
        Write-Host ("Downloading pinned tessdata_" + $TessdataVariant + " language: " + $name)
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

Write-Host ("Running local-only Tesseract fas+eng controlled baseline (" + $TessdataVariant + ")...")
Write-Host "Raw OCR text will not be printed or persisted."
Write-Host "The exact existing controlled OS-rendered corpus will be reused."

$script = Join-Path $repoRoot "scripts\m4_2_tesseract_benchmark.py"
$arguments = @(
    $script,
    "--benchmark-root", $root,
    "--manifest", $manifest,
    "--tesseract-exe", $tesseract,
    "--tessdata-dir", $tessdataDir,
    "--tessdata-variant", $TessdataVariant,
    "--language", $language,
    "--oem", $oem,
    "--psm", $psm
)
foreach ($category in $IncludeCategory) {
    $arguments += @("--include-category", $category)
}
& $python @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Tesseract controlled baseline failed with exit code $LASTEXITCODE."
}
