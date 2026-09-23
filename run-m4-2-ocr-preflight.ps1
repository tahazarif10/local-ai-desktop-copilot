[CmdletBinding()]
param(
    [ValidateSet("Client", "Server", "Unknown")]
    [string]$MachineRole = "Unknown",
    [string]$OutputRoot,
    [string]$ExpectedBranch = "dev/m4-2-ocr-benchmark",
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$schemaVersion = 1
$candidateIds = @(
    "windows-media-ocr",
    "tesseract-5-fas-eng",
    "paddleocr-ppocrv5-fa-en"
)

function Test-M42Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-M42Command {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    return Get-Command $Name -ErrorAction SilentlyContinue |
        Select-Object -First 1
}

function Get-M42FirstLine {
    param(
        [AllowNull()]
        [object[]]$Lines
    )

    if ($null -eq $Lines) {
        return "unavailable"
    }

    foreach ($line in @($Lines)) {
        $text = [string]$line
        if (-not [string]::IsNullOrWhiteSpace($text)) {
            return $text.Trim()
        }
    }

    return "unavailable"
}

function Get-M42WindowsMediaOcrProbe {
    $result = [ordered]@{
        Available = $false
        LanguageTags = @()
        EnglishAvailable = $false
        PersianAvailable = $false
        MixedEligible = $false
        ErrorType = "none"
    }

    try {
        Add-Type -AssemblyName System.Runtime.WindowsRuntime

        [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime] |
            Out-Null

        $languages =
            [Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages

        $tags = @()
        foreach ($language in $languages) {
            if ($null -ne $language -and
                -not [string]::IsNullOrWhiteSpace($language.LanguageTag)) {
                $tags += [string]$language.LanguageTag
            }
        }

        $tags = @($tags | Sort-Object -Unique)

        $english =
            @($tags | Where-Object {
                $_ -eq "en" -or $_ -like "en-*"
            }).Count -gt 0

        $persian =
            @($tags | Where-Object {
                $_ -eq "fa" -or $_ -like "fa-*"
            }).Count -gt 0

        $result.Available = $true
        $result.LanguageTags = $tags
        $result.EnglishAvailable = $english
        $result.PersianAvailable = $persian
        $result.MixedEligible = $english -and $persian
    }
    catch {
        $result.ErrorType = $_.Exception.GetType().Name
    }

    return [pscustomobject]$result
}

function Get-M42TesseractProbe {
    $result = [ordered]@{
        Available = $false
        Version = "unavailable"
        EnglishAvailable = $false
        PersianAvailable = $false
        MixedEligible = $false
        ErrorType = "none"
    }

    $command = Get-M42Command -Name "tesseract.exe"
    if ($null -eq $command) {
        return [pscustomobject]$result
    }

    try {
        $versionLines = @(& $command.Source --version 2>&1)
        $languageLines = @(& $command.Source --list-langs 2>&1)

        if ($LASTEXITCODE -ne 0) {
            throw "Tesseract language enumeration returned a nonzero exit code."
        }

        $languageSet =
            @($languageLines |
                ForEach-Object { ([string]$_).Trim() } |
                Where-Object {
                    $_ -and $_ -notmatch "^List of available languages"
                })

        $english =
            @($languageSet | Where-Object { $_ -eq "eng" }).Count -gt 0

        $persian =
            @($languageSet | Where-Object { $_ -eq "fas" }).Count -gt 0

        $result.Available = $true
        $result.Version = Get-M42FirstLine -Lines $versionLines
        $result.EnglishAvailable = $english
        $result.PersianAvailable = $persian
        $result.MixedEligible = $english -and $persian
    }
    catch {
        $result.ErrorType = $_.Exception.GetType().Name
    }

    return [pscustomobject]$result
}

function Get-M42PythonInvocation {
    $py = Get-M42Command -Name "py.exe"

    if ($null -ne $py) {
        return [pscustomobject]@{
            FilePath = $py.Source
            PrefixArguments = @("-3")
        }
    }

    $python = Get-M42Command -Name "python.exe"

    if ($null -ne $python) {
        return [pscustomobject]@{
            FilePath = $python.Source
            PrefixArguments = @()
        }
    }

    return $null
}

function Get-M42PaddleProbe {
    $result = [ordered]@{
        PythonAvailable = $false
        PythonVersion = "unavailable"
        PythonSupported = $false
        PaddleAvailable = $false
        PaddleVersion = "unavailable"
        PaddleOcrVersion = "unavailable"
        Device = "unavailable"
        CompiledWithCuda = $false
        ErrorType = "none"
    }

    $python = Get-M42PythonInvocation

    if ($null -eq $python) {
        return [pscustomobject]$result
    }

    $result.PythonAvailable = $true

    try {
        $versionArgs = @($python.PrefixArguments) + @(
            "-c",
            "import platform; print(platform.python_version())"
        )

        $versionLines = @(& $python.FilePath $versionArgs 2>&1)

        if ($LASTEXITCODE -eq 0) {
            $result.PythonVersion =
                Get-M42FirstLine -Lines $versionLines
        }

        try {
            $pythonVersion =
                [Version]$result.PythonVersion

            $result.PythonSupported =
                $pythonVersion.Major -eq 3 -and
                $pythonVersion.Minor -ge 9 -and
                $pythonVersion.Minor -le 13
        }
        catch {
            $result.PythonSupported = $false
        }

        if (-not $result.PythonSupported) {
            $result.ErrorType = "UnsupportedPythonVersion"
            return [pscustomobject]$result
        }

        $probeCode = @'
import importlib.metadata as metadata
import json

result = {
    "paddle_available": False,
    "paddle_version": "unavailable",
    "paddleocr_version": "unavailable",
    "device": "unavailable",
    "compiled_with_cuda": False,
    "error_type": "none",
}

try:
    import paddle
    result["paddle_available"] = True
    result["paddle_version"] = str(getattr(paddle, "__version__", "unknown"))
    result["device"] = str(paddle.device.get_device())
    result["compiled_with_cuda"] = bool(paddle.device.is_compiled_with_cuda())
except Exception as exc:
    result["error_type"] = type(exc).__name__

try:
    result["paddleocr_version"] = metadata.version("paddleocr")
except Exception:
    pass

print(json.dumps(result, ensure_ascii=True, separators=(",", ":")))
'@

        $probeArgs = @($python.PrefixArguments) + @(
            "-c",
            $probeCode
        )

        $probeLines = @(& $python.FilePath $probeArgs 2>&1)

        if ($LASTEXITCODE -ne 0) {
            throw "Python Paddle probe returned a nonzero exit code."
        }

        $jsonLine = Get-M42FirstLine -Lines $probeLines
        $parsed = $jsonLine | ConvertFrom-Json

        $result.PaddleAvailable =
            [bool]$parsed.paddle_available

        $result.PaddleVersion =
            [string]$parsed.paddle_version

        $result.PaddleOcrVersion =
            [string]$parsed.paddleocr_version

        $result.Device =
            [string]$parsed.device

        $result.CompiledWithCuda =
            [bool]$parsed.compiled_with_cuda

        $result.ErrorType =
            [string]$parsed.error_type
    }
    catch {
        $result.ErrorType = $_.Exception.GetType().Name
    }

    return [pscustomobject]$result
}

function Get-M42HardwareMetadata {
    $cpuName = "unavailable"
    $logicalProcessors = "unavailable"
    $memoryGiB = "unavailable"
    $gpuSummary = "unavailable"

    try {
        $cpu =
            Get-CimInstance Win32_Processor |
            Select-Object -First 1

        if ($null -ne $cpu) {
            $cpuName = ([string]$cpu.Name).Trim()
            $logicalProcessors =
                [string]$cpu.NumberOfLogicalProcessors
        }
    }
    catch {
    }

    try {
        $system =
            Get-CimInstance Win32_ComputerSystem |
            Select-Object -First 1

        if ($null -ne $system -and
            $null -ne $system.TotalPhysicalMemory) {
            $memoryGiB =
                "{0:N1}" -f (
                    [double]$system.TotalPhysicalMemory / 1GB)
        }
    }
    catch {
    }

    try {
        $gpuNames =
            @(Get-CimInstance Win32_VideoController |
                ForEach-Object {
                    ([string]$_.Name).Trim()
                } |
                Where-Object {
                    -not [string]::IsNullOrWhiteSpace($_)
                } |
                Sort-Object -Unique)

        if ($gpuNames.Count -gt 0) {
            $gpuSummary = $gpuNames -join "; "
        }
    }
    catch {
    }

    return [pscustomobject]@{
        Cpu = $cpuName
        LogicalProcessors = $logicalProcessors
        MemoryGiB = $memoryGiB
        Gpu = $gpuSummary
    }
}

function Get-M42NvidiaMetadata {
    $command = Get-M42Command -Name "nvidia-smi.exe"

    if ($null -eq $command) {
        return "unavailable"
    }

    try {
        $arguments = @(
            "--query-gpu=name,driver_version,memory.total",
            "--format=csv,noheader,nounits"
        )

        $lines = @(& $command.Source $arguments 2>&1)

        if ($LASTEXITCODE -ne 0) {
            return "unavailable"
        }

        $clean =
            @($lines |
                ForEach-Object { ([string]$_).Trim() } |
                Where-Object { $_ })

        if ($clean.Count -eq 0) {
            return "unavailable"
        }

        return $clean -join "; "
    }
    catch {
        return "unavailable"
    }
}

if ($ValidateOnly) {
    if ($schemaVersion -ne 1) {
        throw "Unexpected M4.2 OCR preflight schema."
    }

    if ($candidateIds.Count -ne 3) {
        throw "Unexpected M4.2 OCR candidate count."
    }

    if ($candidateIds -notcontains "windows-media-ocr" -or
        $candidateIds -notcontains "tesseract-5-fas-eng" -or
        $candidateIds -notcontains "paddleocr-ppocrv5-fa-en") {
        throw "M4.2 OCR preflight candidate IDs are incomplete."
    }

    Write-Host "M4.2 OCR preflight validation: PASS"
    return
}

if ($env:OS -ne "Windows_NT") {
    throw "M4.2 OCR preflight requires Windows."
}

if (Test-M42Elevated) {
    throw "Run M4.2 OCR preflight from a normal, non-Administrator PowerShell."
}

$repoRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)

$gitStatus =
    git -C $repoRoot status --short |
    Out-String -Width 4096

if ($LASTEXITCODE -ne 0) {
    throw "Unable to resolve Git working-tree status."
}

if (-not [string]::IsNullOrWhiteSpace($gitStatus)) {
    throw "M4.2 OCR preflight requires a clean working tree."
}

$branch =
    (git -C $repoRoot branch --show-current | Out-String).Trim()

$head =
    (git -C $repoRoot rev-parse HEAD | Out-String).Trim()

if (-not [string]::IsNullOrWhiteSpace($ExpectedBranch) -and
    $branch -ne $ExpectedBranch) {
    throw (
        "Run M4.2 OCR preflight only from branch '" +
        $ExpectedBranch +
        "'. Current branch='" +
        $branch +
        "'.")
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot =
        Join-Path $repoRoot ".localcopilot\benchmarks"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot =
        Join-Path $repoRoot $OutputRoot
}

$outputBase = [System.IO.Path]::GetFullPath($OutputRoot)

$runDirectory =
    Join-Path $outputBase (
        "m4-2-ocr-preflight-" +
        [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ") +
        "-" +
        [Guid]::NewGuid().ToString("N").Substring(0, 8))

New-Item -ItemType Directory -Path $runDirectory -Force |
    Out-Null

$hardware = Get-M42HardwareMetadata
$windowsOcr = Get-M42WindowsMediaOcrProbe
$tesseract = Get-M42TesseractProbe
$paddle = Get-M42PaddleProbe
$nvidia = Get-M42NvidiaMetadata

if ($windowsOcr.MixedEligible) {
    $windowsEligibility = "eligible-fa-en"
}
elseif ($windowsOcr.Available) {
    $windowsEligibility = "available-language-incomplete"
}
else {
    $windowsEligibility = "unavailable"
}

if ($tesseract.MixedEligible) {
    $tesseractEligibility = "eligible-fas-eng"
}
elseif ($tesseract.Available) {
    $tesseractEligibility = "available-language-incomplete"
}
else {
    $tesseractEligibility = "unavailable"
}

if ($paddle.PythonAvailable -and
    -not $paddle.PythonSupported) {
    $paddleEligibility = "python-version-unsupported"
}
elseif ($paddle.PaddleAvailable -and
    $paddle.PaddleOcrVersion -ne "unavailable") {
    $paddleEligibility = "runtime-present"
}
elseif ($paddle.PythonAvailable) {
    $paddleEligibility = "python-present-runtime-missing"
}
else {
    $paddleEligibility = "unavailable"
}

if (@($windowsOcr.LanguageTags).Count -gt 0) {
    $languageTags = @($windowsOcr.LanguageTags) -join ","
}
else {
    $languageTags = "none"
}

$summary = @"
=================================================
M4.2 OCR BENCHMARK PREFLIGHT
=================================================
schema=$schemaVersion
machine_role=$MachineRole
branch=$branch
head=$head
working_tree=clean
runner_elevated=False
os_version=$([Environment]::OSVersion.VersionString)
cpu=$($hardware.Cpu)
logical_processors=$($hardware.LogicalProcessors)
memory_gib=$($hardware.MemoryGiB)
gpu=$($hardware.Gpu)
nvidia=$nvidia

candidate_windows_ai_text_recognition=excluded-npu-required-on-fixed-hardware

candidate_windows_media_ocr=$windowsEligibility
windows_media_ocr_available=$($windowsOcr.Available)
windows_media_ocr_english=$($windowsOcr.EnglishAvailable)
windows_media_ocr_persian=$($windowsOcr.PersianAvailable)
windows_media_ocr_languages=$languageTags
windows_media_ocr_error_type=$($windowsOcr.ErrorType)

candidate_tesseract=$tesseractEligibility
tesseract_available=$($tesseract.Available)
tesseract_version=$($tesseract.Version)
tesseract_english=$($tesseract.EnglishAvailable)
tesseract_persian=$($tesseract.PersianAvailable)
tesseract_error_type=$($tesseract.ErrorType)

candidate_paddleocr=$paddleEligibility
python_available=$($paddle.PythonAvailable)
python_version=$($paddle.PythonVersion)
python_supported_for_paddle=$($paddle.PythonSupported)
paddle_available=$($paddle.PaddleAvailable)
paddle_version=$($paddle.PaddleVersion)
paddleocr_version=$($paddle.PaddleOcrVersion)
paddle_device=$($paddle.Device)
paddle_compiled_with_cuda=$($paddle.CompiledWithCuda)
paddle_error_type=$($paddle.ErrorType)

content_captured=False
ocr_executed=False
dependencies_installed=False
selection_made=False
"@

$evidencePath =
    Join-Path $runDirectory "m4-2-ocr-preflight.txt"

$utf8 = New-Object System.Text.UTF8Encoding($true)

[System.IO.File]::WriteAllText(
    $evidencePath,
    $summary,
    $utf8)

try {
    Set-Clipboard -Value $summary
}
catch {
}

Write-Host ""
Write-Host "=============================================="
Write-Host "M4.2 OCR BENCHMARK PREFLIGHT: PASS"
Write-Host "=============================================="
Write-Host $summary
Write-Host "Evidence: $evidencePath"
Write-Host "Evidence summary copied to clipboard."
