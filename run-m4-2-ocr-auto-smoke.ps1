[CmdletBinding()]
param(
    [switch]$ValidateOnly,
    [string]$BenchmarkRoot = "D:\LocalAI-Prerequisites",
    [string]$ExpectedBranch = "dev/m4-2-2-benchmark-runner"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$schemaVersion = 1

if ($ValidateOnly) {
    if ($schemaVersion -ne 1) { throw "Unexpected auto-smoke schema." }
    Write-Host "M4.2.2 automatic synthetic OCR smoke validation: PASS"
    return
}

if ($env:OS -ne "Windows_NT") { throw "Automatic OCR smoke corpus generation requires Windows." }

$repoRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$gitStatus = git -C $repoRoot status --short | Out-String -Width 4096
if ($LASTEXITCODE -ne 0) { throw "Unable to resolve Git working-tree status." }
if (-not [string]::IsNullOrWhiteSpace($gitStatus)) { throw "Automatic OCR smoke requires a clean working tree." }

$branch = (git -C $repoRoot branch --show-current | Out-String).Trim()
if ($branch -ne $ExpectedBranch) {
    throw ("Run automatic OCR smoke only from branch '" + $ExpectedBranch + "'. Current branch='" + $branch + "'.")
}

$root = [System.IO.Path]::GetFullPath($BenchmarkRoot)
$corpusRoot = Join-Path $root "corpus"
$imagesRoot = Join-Path $corpusRoot "images"
$truthRoot = Join-Path $corpusRoot "ground-truth"
$manifestPath = Join-Path $corpusRoot "manifest.json"

New-Item -ItemType Directory -Path $imagesRoot -Force | Out-Null
New-Item -ItemType Directory -Path $truthRoot -Force | Out-Null

Add-Type -AssemblyName System.Drawing

function ConvertFrom-M422Utf8Base64 {
    param([Parameter(Mandatory = $true)][string]$Value)

    $bytes = [Convert]::FromBase64String($Value)
    return [Text.Encoding]::UTF8.GetString($bytes)
}

function New-M422SyntheticImage {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string[]]$Lines,
        [switch]$TerminalStyle
    )

    $width = 1200
    $height = 360
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

        if ($TerminalStyle) {
            $background = [System.Drawing.Color]::FromArgb(24, 24, 24)
            $foreground = [System.Drawing.Brushes]::White
            $fontName = "Consolas"
            $fontSize = 27
        }
        else {
            $background = [System.Drawing.Color]::FromArgb(248, 248, 248)
            $foreground = [System.Drawing.Brushes]::Black
            $fontName = "Segoe UI"
            $fontSize = 30
        }

        $graphics.Clear($background)
        $font = New-Object System.Drawing.Font($fontName, $fontSize, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
        try {
            $y = 45.0
            foreach ($line in $Lines) {
                $containsRtl = $line -match "[\u0600-\u06FF]"
                $format = New-Object System.Drawing.StringFormat
                try {
                    $format.Trimming = [System.Drawing.StringTrimming]::None
                    if ($containsRtl) {
                        $format.FormatFlags = [System.Drawing.StringFormatFlags]::DirectionRightToLeft
                        $format.Alignment = [System.Drawing.StringAlignment]::Near
                        $rect = New-Object System.Drawing.RectangleF(80, $y, 1040, 60)
                    }
                    else {
                        $format.Alignment = [System.Drawing.StringAlignment]::Near
                        $rect = New-Object System.Drawing.RectangleF(80, $y, 1040, 60)
                    }
                    $graphics.DrawString($line, $font, $foreground, $rect, $format)
                }
                finally {
                    $format.Dispose()
                }
                $y += 82.0
            }
        }
        finally {
            $font.Dispose()
        }

        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$faSettings = ConvertFrom-M422Utf8Base64 "2KrZhti424zZhdin2Kog2K/Ys9iq2q/Yp9mH"
$faPressure = ConvertFrom-M422Utf8Base64 "2YHYtNin2LEg2qnYp9ix24wgNiDYqNin2LE="
$faSpeed = ConvertFrom-M422Utf8Base64 "2LPYsdi52Kog2K3Ysdqp2KogMTIwINmF24zZhNuMINmF2KrYsSDYqNixINir2KfZhtuM2Yc="
$faAxisPressure = ConvertFrom-M422Utf8Base64 "2YHYtNin2LEg2YXYrdmI2LEg2KjYsdin2KjYsSA2INio2KfYsQ=="
$faWarning = ConvertFrom-M422Utf8Base64 "2YfYtNiv2KfYsSDYs9uM2LPYqtmF"
$faSavePrompt = ConvertFrom-M422Utf8Base64 "2KLbjNinINiq2YbYuNuM2YXYp9iqINis2K/bjNivINiw2K7bjNix2Ycg2LTZiNiv2J8="
$faConfirmCancel = ConvertFrom-M422Utf8Base64 "2KrYp9uM24zYryAgICAg2KfZhti12LHYp9mB"
$faLocalAddress = ConvertFrom-M422Utf8Base64 "2KLYr9ix2LMg2YXYrdmE24wgMTI3LjAuMC4x"
$faReady = ConvertFrom-M422Utf8Base64 "2YjYtti524zYqiDYotmF2KfYr9mHINio2Ycg2qnYp9ix"

$samples = @(
    [ordered]@{ id="s001"; category="persian-ui"; terminal=$false; lines=@($faSettings, $faPressure, $faSpeed) },
    [ordered]@{ id="s002"; category="english-ui"; terminal=$false; lines=@("Machine Settings", "Working Pressure 6 bar", "Axis Speed 120 mm per second") },
    [ordered]@{ id="s003"; category="mixed-fa-en"; terminal=$false; lines=@("Servo A1 Ready", $faAxisPressure, "Error Code ER01") },
    [ordered]@{ id="s004"; category="terminal-console"; terminal=$true; lines=@("PS D:\local-ai-desktop-copilot>", "Build succeeded.", "0 Warning(s)  0 Error(s)") },
    [ordered]@{ id="s005"; category="dialog"; terminal=$false; lines=@($faWarning, $faSavePrompt, $faConfirmCancel) },
    [ordered]@{ id="s006"; category="browser-ui"; terminal=$false; lines=@("Local AI Dashboard", "Server Status Connected", $faLocalAddress) },
    [ordered]@{ id="s007"; category="desktop-app-ui"; terminal=$false; lines=@("Production Control", "Job E-CT-2140", $faReady) }
)

$manifestSamples = @()
foreach ($sample in $samples) {
    $id = [string]$sample.id
    $imagePath = Join-Path $imagesRoot ($id + ".png")
    $truthPath = Join-Path $truthRoot ($id + ".txt")

    New-M422SyntheticImage -Path $imagePath -Lines ([string[]]$sample.lines) -TerminalStyle:([bool]$sample.terminal)

    $truth = ([string[]]$sample.lines) -join "`n"
    $truth | Set-Content -LiteralPath $truthPath -Encoding UTF8

    $manifestSamples += [ordered]@{
        id = $id
        category = [string]$sample.category
        image = "images/$id.png"
        ground_truth_file = "ground-truth/$id.txt"
    }
}

$manifest = [ordered]@{ schema = 1; samples = $manifestSamples }
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host "Synthetic local corpus generated automatically."
Write-Host "corpus_root=$corpusRoot"
Write-Host "This is a deterministic pipeline smoke corpus, not final real-world OCR acceptance evidence."

$runner = Join-Path $repoRoot "run-m4-2-ocr-benchmark.ps1"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner -Run -BenchmarkRoot $root -ExpectedBranch $ExpectedBranch
if ($LASTEXITCODE -ne 0) { throw "Automatic synthetic OCR smoke benchmark failed with exit code $LASTEXITCODE." }
