[CmdletBinding()]
param(
    [switch]$ValidateOnly,
    [string]$BenchmarkRoot = "D:\LocalAI-Prerequisites",
    [string]$ExpectedBranch = "dev/m4-2-2-benchmark-runner"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$schemaVersion = 1
$languageTag = "en-US"
$warmRuns = 3
$timeoutSeconds = 15.0
$eligibleCategories = @("english-ui","terminal-console","browser-ui")

if ($ValidateOnly) {
    if ($schemaVersion -ne 1) { throw "Unexpected Windows Media OCR benchmark schema." }
    if ($languageTag -ne "en-US") { throw "Windows Media OCR baseline must remain en-US only." }
    if ($warmRuns -ne 3) { throw "Unexpected warm-run count." }
    if ($eligibleCategories.Count -ne 3) { throw "Unexpected English-only category set." }
    Write-Host "M4.2.2 Windows Media OCR baseline validation: PASS"
    return
}

if ($env:OS -ne "Windows_NT") { throw "Windows Media OCR baseline requires Windows." }

function Test-Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (Test-Elevated) {
    throw "Run the benchmark from a normal, non-Administrator PowerShell."
}

$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$status = git -C $repoRoot status --short | Out-String -Width 4096
if ($LASTEXITCODE -ne 0) { throw "Unable to read Git status." }
if (-not [string]::IsNullOrWhiteSpace($status)) {
    throw "Windows Media OCR baseline requires a clean working tree."
}

$branch = (git -C $repoRoot branch --show-current | Out-String).Trim()
$head = (git -C $repoRoot rev-parse HEAD | Out-String).Trim()
if ($branch -ne $ExpectedBranch) {
    throw ("Expected branch '" + $ExpectedBranch + "'; current branch='" + $branch + "'.")
}

$root = [IO.Path]::GetFullPath($BenchmarkRoot)
$manifestPath = Join-Path $root "corpus\manifest.json"
$provenancePath = Join-Path $root "corpus\controlled-ui-provenance.json"
$python = Join-Path $root "python312-nuget\python\tools\python.exe"
$scoreScript = Join-Path $repoRoot "scripts\m4_2_score_stdin.py"

foreach ($required in @($manifestPath,$provenancePath,$python,$scoreScript)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw ("Required benchmark input is missing: " + $required)
    }
}

$provenance = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
if ([string]$provenance.mode -ne "controlled-os-rendered-ui") {
    throw "Corpus provenance is not controlled-os-rendered-ui."
}
if ([int]$provenance.sample_count -ne 7) {
    throw "Controlled corpus must contain seven samples."
}
if ([bool]$provenance.full_screen_capture) {
    throw "Full-screen corpus provenance is forbidden."
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ([int]$manifest.schema -ne 1) { throw "Unexpected corpus manifest schema." }

$samples = @(
    $manifest.samples |
    Where-Object { $eligibleCategories -contains [string]$_.category }
)
if ($samples.Count -ne 3) {
    throw "Windows Media OCR English-only baseline requires exactly three eligible corpus samples."
}

Add-Type -AssemblyName System.Runtime.WindowsRuntime

[Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null
[Windows.Globalization.Language, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null
[Windows.Storage.StorageFile, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null
[Windows.Storage.FileAccessMode, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null
[Windows.Storage.Streams.IRandomAccessStreamWithContentType, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.BitmapDecoder, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.SoftwareBitmap, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null
[Windows.Media.Ocr.OcrResult, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null

$asTaskGeneric = [System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object {
        $_.Name -eq "AsTask" -and
        $_.IsGenericMethod -and
        $_.GetParameters().Count -eq 1
    } |
    Select-Object -First 1

if ($null -eq $asTaskGeneric) {
    throw "Unable to resolve Windows Runtime AsTask bridge."
}

function Await-WinRt {
    param(
        [Parameter(Mandatory = $true)][object]$Operation,
        [Parameter(Mandatory = $true)][Type]$ResultType
    )

    $task = $asTaskGeneric.MakeGenericMethod($ResultType).Invoke($null,@($Operation))
    if (-not $task.Wait([TimeSpan]::FromSeconds($timeoutSeconds))) {
        throw "Windows Media OCR async operation exceeded the benchmark deadline."
    }
    return $task.Result
}

$availableTags = @(
    [Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages |
    ForEach-Object { [string]$_.LanguageTag }
)
if ($availableTags -notcontains $languageTag) {
    throw "Windows Media OCR en-US recognizer is unavailable on this machine."
}

$language = New-Object Windows.Globalization.Language($languageTag)
$engineStopwatch = [Diagnostics.Stopwatch]::StartNew()
$engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($language)
$engineStopwatch.Stop()
if ($null -eq $engine) { throw "Unable to create Windows Media OCR en-US engine." }

function Invoke-WindowsOcr {
    param([Parameter(Mandatory = $true)][string]$ImagePath)

    $overall = [Diagnostics.Stopwatch]::StartNew()
    $file = Await-WinRt ([Windows.Storage.StorageFile]::GetFileFromPathAsync($ImagePath)) ([Windows.Storage.StorageFile])
    $stream = $null
    $bitmap = $null
    try {
        $stream = Await-WinRt ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStreamWithContentType])
        $decoder = Await-WinRt ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
        $bitmap = Await-WinRt ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])

        $recognizeWatch = [Diagnostics.Stopwatch]::StartNew()
        $result = Await-WinRt ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
        $recognizeWatch.Stop()
        $overall.Stop()

        return [pscustomobject]@{
            Text = [string]$result.Text
            RecognitionMs = [double]$recognizeWatch.Elapsed.TotalMilliseconds
            EndToEndMs = [double]$overall.Elapsed.TotalMilliseconds
        }
    }
    finally {
        if ($null -ne $bitmap) { $bitmap.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

$coldImage = Join-Path (Split-Path $manifestPath -Parent) ([string]$samples[0].image)
$cold = Invoke-WindowsOcr -ImagePath $coldImage
$coldFirstOcrMs = $cold.RecognitionMs
$coldEndToEndMs = $cold.EndToEndMs
$cold = $null

$latencies = New-Object System.Collections.Generic.List[double]
$endToEndLatencies = New-Object System.Collections.Generic.List[double]
$characterEdits = 0
$referenceCodePoints = 0
$wordEdits = 0
$referenceWords = 0
$exactMatches = 0
$failures = 0
$timeouts = 0
$categoryCounts = [ordered]@{}

foreach ($sample in $samples) {
    $category = [string]$sample.category
    if (-not $categoryCounts.Contains($category)) { $categoryCounts[$category] = 0 }
    $categoryCounts[$category] = [int]$categoryCounts[$category] + 1

    $corpusRoot = Split-Path $manifestPath -Parent
    $imagePath = Join-Path $corpusRoot ([string]$sample.image)
    $truthPath = Join-Path $corpusRoot ([string]$sample.ground_truth_file)
    $firstHypothesis = $null

    for ($i = 0; $i -lt $warmRuns; $i++) {
        try {
            $ocr = Invoke-WindowsOcr -ImagePath $imagePath
        }
        catch {
            $failures++
            if ($_.Exception.Message -like "*deadline*") { $timeouts++ }
            throw
        }

        $latencies.Add([double]$ocr.RecognitionMs)
        $endToEndLatencies.Add([double]$ocr.EndToEndMs)
        if ($null -eq $firstHypothesis) { $firstHypothesis = [string]$ocr.Text }
    }

    $scoreJson = $firstHypothesis | & $python $scoreScript --truth-file $truthPath
    if ($LASTEXITCODE -ne 0) { throw "Aggregate-safe OCR scoring helper failed." }
    $score = ([string]$scoreJson) | ConvertFrom-Json

    $characterEdits += [int]$score.character_edits
    $referenceCodePoints += [int]$score.reference_code_points
    $wordEdits += [int]$score.word_edits
    $referenceWords += [int]$score.reference_words
    if ([bool]$score.exact_normalized_match) { $exactMatches++ }

    $firstHypothesis = $null
}

function Percentile([double[]]$Values,[double]$Fraction) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return 0.0 }
    if ($sorted.Count -eq 1) { return [double]$sorted[0] }
    $rank = ($sorted.Count - 1) * $Fraction
    $lower = [Math]::Floor($rank)
    $upper = [Math]::Ceiling($rank)
    if ($lower -eq $upper) { return [double]$sorted[$lower] }
    $weight = $rank - $lower
    return ([double]$sorted[$lower] * (1.0 - $weight)) + ([double]$sorted[$upper] * $weight)
}

$cer = if ($referenceCodePoints -eq 0) { if ($characterEdits -eq 0) { 0.0 } else { 1.0 } } else { [double]$characterEdits / $referenceCodePoints }
$wer = if ($referenceWords -eq 0) { if ($wordEdits -eq 0) { 0.0 } else { 1.0 } } else { [double]$wordEdits / $referenceWords }

$result = [ordered]@{
    schema = $schemaVersion
    mode = "controlled-benchmark"
    candidate = "windows-media-ocr-en-us"
    scope = "english-only-eligible-subset"
    sample_count = $samples.Count
    category_counts = $categoryCounts
    raw_content_persisted = $false
    raw_ocr_logged = $false
    backend_selected = $false
    failure_count = $failures
    timeout_count = $timeouts
    strict_character_error_rate = [Math]::Round($cer,8)
    strict_word_error_rate = [Math]::Round($wer,8)
    exact_normalized_match_rate = [Math]::Round(([double]$exactMatches / $samples.Count),8)
    warm_run_count_per_sample = $warmRuns
    warm_recognition_p50_ms = [Math]::Round((Percentile $latencies.ToArray() 0.50),3)
    warm_recognition_p95_ms = [Math]::Round((Percentile $latencies.ToArray() 0.95),3)
    warm_recognition_max_ms = [Math]::Round((($latencies | Measure-Object -Maximum).Maximum),3)
    warm_end_to_end_p50_ms = [Math]::Round((Percentile $endToEndLatencies.ToArray() 0.50),3)
    warm_end_to_end_p95_ms = [Math]::Round((Percentile $endToEndLatencies.ToArray() 0.95),3)
    cold_engine_create_ms = [Math]::Round($engineStopwatch.Elapsed.TotalMilliseconds,3)
    cold_first_ocr_ms = [Math]::Round($coldFirstOcrMs,3)
    cold_first_end_to_end_ms = [Math]::Round($coldEndToEndMs,3)
    language = $languageTag
    device = "cpu"
    inference_timeout_seconds = $timeoutSeconds
    persian_supported = $false
    mixed_fa_en_eligible = $false
}

$resultsRoot = Join-Path $root "results"
New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null
$stamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssZ")
$outputPath = Join-Path $resultsRoot ("aggregate-windows-media-ocr-" + $stamp + ".json")
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $outputPath -Encoding UTF8

Write-Host "M4.2.2 WINDOWS MEDIA OCR BENCHMARK: PASS"
Write-Host ($result | ConvertTo-Json -Depth 6)
Write-Host ("aggregate_result=" + $outputPath)
