[CmdletBinding()]
param(
    [ValidateSet("Server", "Client")]
    [string]$MachineRole = "Server",
    [switch]$PreparePaddleGpu,
    [switch]$ValidateOnly,
    [string]$ExpectedBranch = "dev/m4-2-2-controlled-benchmark",
    [string]$BenchmarkRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$schemaVersion = 1
$paddlePythonTag = "3.12"
$paddleVersion = "3.2.0"
$paddleOcrVersion = "3.7.0"
$paddleIndex = "https://www.paddlepaddle.org.cn/packages/stable/cu126/"
$pythonNuGetVersion = "3.12.10"
$pythonNuGetPackageUrl = "https://www.nuget.org/api/v2/package/python/3.12.10"
$pythonNuGetRegistrationUrl = "https://api.nuget.org/v3/registration5-semver1/python/3.12.10.json"

function Test-M422Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-M422Command {
    param([Parameter(Mandatory = $true)][string]$Name)
    return Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1
}

function Get-M422NvidiaDriver {
    $command = Get-M422Command -Name "nvidia-smi.exe"
    if ($null -eq $command) { return $null }
    try {
        $args = @("--query-gpu=driver_version", "--format=csv,noheader")
        $line = @(& $command.Source $args 2>&1) | Select-Object -First 1
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace([string]$line)) { return $null }
        return ([string]$line).Trim()
    }
    catch { return $null }
}

function Test-M422PythonInstallManager {
    $py = Get-M422Command -Name "py.exe"
    if ($null -eq $py) { return $false }
    try {
        $output = @(& $py.Source help install 2>&1)
        if ($LASTEXITCODE -ne 0) { return $false }
        $text = $output -join "`n"
        return ($text -match "(?i)py install" -or $text -match "(?i)--target")
    }
    catch { return $false }
}

function Install-M422PythonFromNuGet {
    param(
        [Parameter(Mandatory = $true)][string]$PackageRoot,
        [Parameter(Mandatory = $true)][string]$CacheRoot
    )

    New-Item -ItemType Directory -Path $CacheRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $PackageRoot -Force | Out-Null

    $packagePath = Join-Path $CacheRoot ("python." + $pythonNuGetVersion + ".nupkg")
    $expectedPython = Join-Path $PackageRoot "python\tools\python.exe"

    if (Test-Path -LiteralPath $expectedPython) {
        Write-Host "Using cached project-local Python NuGet package."
        return
    }

    if (-not (Test-Path -LiteralPath $packagePath)) {
        $curl = Get-M422Command -Name "curl.exe"
        if ($null -eq $curl) {
            throw "curl.exe is required for the bounded Python package download."
        }

        $partialPath = $packagePath + ".partial"
        Remove-Item -LiteralPath $partialPath -Force -ErrorAction SilentlyContinue

        Write-Host "Downloading official CPython NuGet package $pythonNuGetVersion..."
        Write-Host "Source: nuget.org"
        Write-Host "Expected size: about 13.8 MB"

        & $curl.Source @(
            "--fail",
            "--location",
            "--show-error",
            "--progress-bar",
            "--ipv4",
            "--retry", "3",
            "--retry-delay", "3",
            "--retry-connrefused",
            "--connect-timeout", "30",
            "--max-time", "600",
            "--output", $partialPath,
            $pythonNuGetPackageUrl
        )

        if ($LASTEXITCODE -ne 0) {
            Remove-Item -LiteralPath $partialPath -Force -ErrorAction SilentlyContinue
            Write-Host ""
            Write-Host "Automatic NuGet package download failed."
            Write-Host "Manual fallback: open this official NuGet page in a browser:"
            Write-Host "https://www.nuget.org/packages/python/3.12.10"
            Write-Host "Choose Download package and save the file exactly as:"
            Write-Host $packagePath
            Write-Host "Then rerun this same setup command."
            Write-Host "The script will verify the NuGet package signature before extraction."
            throw "Official CPython NuGet package download failed with curl exit code $LASTEXITCODE."
        }

        Move-Item -LiteralPath $partialPath -Destination $packagePath -Force
    }
    else {
        Write-Host "Using cached CPython NuGet package."
    }

    $curl = Get-M422Command -Name "curl.exe"
    if ($null -eq $curl) {
        throw "curl.exe is required to verify the CPython NuGet package against nuget.org metadata."
    }

    $registrationPath = Join-Path $CacheRoot ("python." + $pythonNuGetVersion + ".registration.json")
    if (-not (Test-Path -LiteralPath $registrationPath)) {
        $registrationPartial = $registrationPath + ".partial"
        Remove-Item -LiteralPath $registrationPartial -Force -ErrorAction SilentlyContinue
        Write-Host "Downloading official nuget.org registration metadata..."
        & $curl.Source @(
            "--fail",
            "--location",
            "--show-error",
            "--silent",
            "--ipv4",
            "--retry", "3",
            "--retry-delay", "3",
            "--retry-connrefused",
            "--connect-timeout", "30",
            "--max-time", "300",
            "--output", $registrationPartial,
            $pythonNuGetRegistrationUrl
        )
        if ($LASTEXITCODE -ne 0) {
            Remove-Item -LiteralPath $registrationPartial -Force -ErrorAction SilentlyContinue
            throw "nuget.org registration metadata download failed with curl exit code $LASTEXITCODE."
        }
        Move-Item -LiteralPath $registrationPartial -Destination $registrationPath -Force
    }

    $registration = Get-Content -LiteralPath $registrationPath -Raw | ConvertFrom-Json
    $catalogUrl = [string]$registration.catalogEntry
    if ([string]::IsNullOrWhiteSpace($catalogUrl) -or $catalogUrl -notmatch "^https://api\.nuget\.org/") {
        throw "nuget.org registration metadata did not contain an expected catalogEntry URL."
    }

    $catalogPath = Join-Path $CacheRoot ("python." + $pythonNuGetVersion + ".catalog.json")
    $catalogPartial = $catalogPath + ".partial"
    Remove-Item -LiteralPath $catalogPartial -Force -ErrorAction SilentlyContinue
    Write-Host "Downloading official nuget.org catalog hash metadata..."
    & $curl.Source @(
        "--fail",
        "--location",
        "--show-error",
        "--silent",
        "--ipv4",
        "--retry", "3",
        "--retry-delay", "3",
        "--retry-connrefused",
        "--connect-timeout", "30",
        "--max-time", "300",
        "--output", $catalogPartial,
        $catalogUrl
    )
    if ($LASTEXITCODE -ne 0) {
        Remove-Item -LiteralPath $catalogPartial -Force -ErrorAction SilentlyContinue
        throw "nuget.org catalog metadata download failed with curl exit code $LASTEXITCODE."
    }
    Move-Item -LiteralPath $catalogPartial -Destination $catalogPath -Force

    $catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
    $hashAlgorithm = [string]$catalog.packageHashAlgorithm
    $expectedPackageHash = [string]$catalog.packageHash
    if ($hashAlgorithm -ne "SHA512" -or [string]::IsNullOrWhiteSpace($expectedPackageHash)) {
        throw "nuget.org catalog metadata did not contain the expected SHA512 package hash."
    }

    Write-Host "Verifying CPython NuGet package SHA512 against official nuget.org catalog metadata..."
    $actualHashHex = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA512).Hash
    $actualHashBytes = New-Object byte[] ($actualHashHex.Length / 2)
    for ($i = 0; $i -lt $actualHashBytes.Length; $i++) {
        $actualHashBytes[$i] = [Convert]::ToByte($actualHashHex.Substring($i * 2, 2), 16)
    }
    $actualPackageHash = [Convert]::ToBase64String($actualHashBytes)

    if (-not [string]::Equals($actualPackageHash, $expectedPackageHash, [StringComparison]::Ordinal)) {
        throw "CPython NuGet package SHA512 did not match official nuget.org catalog metadata."
    }

    $extractRoot = Join-Path $PackageRoot "python"
    if (Test-Path -LiteralPath $extractRoot) {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force
    }

    Write-Host "Extracting Python $pythonNuGetVersion entirely under: $PackageRoot"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $extractRoot)

    if (-not (Test-Path -LiteralPath $expectedPython)) {
        throw "Signed CPython NuGet package extracted, but tools\python.exe was not found."
    }
}

function Resolve-M422TargetPython {
    param([Parameter(Mandatory = $true)][string]$TargetRoot)
    $direct = Join-Path $TargetRoot "python.exe"
    if (Test-Path -LiteralPath $direct) { return $direct }
    $candidate = Get-ChildItem -LiteralPath $TargetRoot -Filter "python.exe" -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $candidate) { throw "Python 3.12 target install completed, but python.exe was not found." }
    return $candidate.FullName
}

function Invoke-M422Checked {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Description
    )
    & $FilePath $Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Description failed with exit code $LASTEXITCODE." }
}

if ($ValidateOnly) {
    if ($schemaVersion -ne 1) { throw "Unexpected M4.2.2 setup schema." }
    if ($paddlePythonTag -ne "3.12") { throw "M4.2.2 must use isolated Python 3.12." }
    if ($paddleVersion -ne "3.2.0") { throw "Unexpected pinned PaddlePaddle version." }
    if ($paddleOcrVersion -ne "3.7.0") { throw "Unexpected pinned PaddleOCR version." }
    if ($paddleIndex -notmatch "/cu126/$") { throw "M4.2.2 GPU benchmark must use the pinned CUDA 12.6 wheel index." }
    if ($pythonNuGetVersion -ne "3.12.10") { throw "Unexpected project-local Python NuGet version." }
    if ($pythonNuGetPackageUrl -ne "https://www.nuget.org/api/v2/package/python/3.12.10") { throw "Unexpected CPython NuGet package source." }
    if ($pythonNuGetRegistrationUrl -ne "https://api.nuget.org/v3/registration5-semver1/python/3.12.10.json") { throw "Unexpected CPython NuGet registration metadata source." }
    Write-Host "M4.2.2 benchmark setup validation: PASS"
    return
}

if ($env:OS -ne "Windows_NT") { throw "M4.2.2 benchmark setup requires Windows." }
if (Test-M422Elevated) { throw "Run M4.2.2 setup from a normal, non-Administrator PowerShell." }

$repoRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$gitStatus = git -C $repoRoot status --short | Out-String -Width 4096
if ($LASTEXITCODE -ne 0) { throw "Unable to resolve Git working-tree status." }
if (-not [string]::IsNullOrWhiteSpace($gitStatus)) { throw "M4.2.2 setup requires a clean working tree." }
$branch = (git -C $repoRoot branch --show-current | Out-String).Trim()
$head = (git -C $repoRoot rev-parse HEAD | Out-String).Trim()
if (-not [string]::IsNullOrWhiteSpace($ExpectedBranch) -and $branch -ne $ExpectedBranch) {
    throw ("Run M4.2.2 setup only from branch '" + $ExpectedBranch + "'. Current branch='" + $branch + "'.")
}

if ([string]::IsNullOrWhiteSpace($BenchmarkRoot)) {
    $root = Join-Path $repoRoot ".localcopilot\ocr-benchmark"
}
else {
    $root = [System.IO.Path]::GetFullPath($BenchmarkRoot)
}

$pythonPackageRoot = Join-Path $root "python312-nuget"
$runtimeRoot = Join-Path $pythonPackageRoot "python\tools"
$cacheRoot = Join-Path $root "cache"
$evidenceRoot = Join-Path $root "evidence"
$pipCacheRoot = Join-Path $root "pip-cache"
$tempRoot = Join-Path $root "temp"

New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
New-Item -ItemType Directory -Path $pipCacheRoot -Force | Out-Null
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

# Keep all benchmark download/cache/temp activity on the selected benchmark drive.
# These environment changes are process-local to this PowerShell run.
$env:TEMP = $tempRoot
$env:TMP = $tempRoot
$env:PIP_CACHE_DIR = $pipCacheRoot

$driver = Get-M422NvidiaDriver
$installManagerAvailable = Test-M422PythonInstallManager
$prepared = $false
$pythonVersion = "unavailable"
$paddleRuntimeVersion = "unavailable"
$paddleOcrRuntimeVersion = "unavailable"
$paddleDevice = "unavailable"
$compiledWithCuda = $false

if ($PreparePaddleGpu) {
    if ($MachineRole -ne "Server") { throw "GPU Paddle benchmark preparation is allowed only for MachineRole=Server." }
    if ([string]::IsNullOrWhiteSpace($driver)) { throw "NVIDIA driver metadata is unavailable; refusing GPU benchmark preparation." }
    try { $driverVersion = [Version]$driver } catch { throw "Unable to parse NVIDIA driver version '$driver'." }
    $minimumDriver = [Version]"550.54.14"
    if ($driverVersion -lt $minimumDriver) { throw ("NVIDIA driver " + $driver + " is below the pinned CUDA 12.6 wheel minimum " + $minimumDriver + ".") }
    if (-not (Test-Path -LiteralPath (Join-Path $runtimeRoot "python.exe"))) {
        if ($installManagerAvailable) {
            New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
            $py = Get-M422Command -Name "py.exe"
            if ($null -eq $py) { throw "py.exe disappeared after install-manager preflight." }
            Invoke-M422Checked -FilePath $py.Source -Arguments @("install", "--target=$runtimeRoot", $paddlePythonTag) -Description "Isolated Python 3.12 target install"
        }
        else {
            Install-M422PythonFromNuGet -PackageRoot $pythonPackageRoot -CacheRoot $cacheRoot
        }
    }

    $python = Resolve-M422TargetPython -TargetRoot $runtimeRoot
    Write-Host "Bootstrapping pip in isolated Python..."
    Invoke-M422Checked -FilePath $python -Arguments @("-m", "ensurepip", "--upgrade") -Description "ensurepip"
    Invoke-M422Checked -FilePath $python -Arguments @("-m", "pip", "install", "--disable-pip-version-check", "--upgrade", "pip", "setuptools", "wheel") -Description "pip bootstrap"

    Write-Host "Installing PaddlePaddle GPU $paddleVersion..."
    Invoke-M422Checked -FilePath $python -Arguments @("-m", "pip", "install", "--disable-pip-version-check", "paddlepaddle-gpu==$paddleVersion", "-i", $paddleIndex) -Description "PaddlePaddle GPU install"

    Write-Host "Installing PaddleOCR $paddleOcrVersion..."
    Invoke-M422Checked -FilePath $python -Arguments @("-m", "pip", "install", "--disable-pip-version-check", "paddleocr==$paddleOcrVersion") -Description "PaddleOCR install"

    $probeCode = "import importlib.metadata as m,json,platform,paddle; print(json.dumps({'python_version':platform.python_version(),'paddle_version':str(paddle.__version__),'paddleocr_version':m.version('paddleocr'),'device':str(paddle.device.get_device()),'compiled_with_cuda':bool(paddle.device.is_compiled_with_cuda())}, separators=(',',':')))"
    $probe = @(& $python "-c" $probeCode 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Installed Paddle/PaddleOCR environment probe failed." }
    $probeLine = @($probe | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }) | Select-Object -Last 1
    $parsed = ([string]$probeLine) | ConvertFrom-Json
    $pythonVersion = [string]$parsed.python_version
    $paddleRuntimeVersion = [string]$parsed.paddle_version
    $paddleOcrRuntimeVersion = [string]$parsed.paddleocr_version
    $paddleDevice = [string]$parsed.device
    $compiledWithCuda = [bool]$parsed.compiled_with_cuda
    if (-not $compiledWithCuda) { throw "Paddle installed but did not report CUDA compilation." }
    if ($paddleDevice -notmatch "^gpu") { throw "Paddle installed but did not select a GPU device." }
    $prepared = $true
}

$driverText = if ([string]::IsNullOrWhiteSpace($driver)) { "unavailable" } else { $driver }
$summary = @"
=================================================
M4.2.2 OCR BENCHMARK ENVIRONMENT
=================================================
schema=$schemaVersion
machine_role=$MachineRole
branch=$branch
head=$head
working_tree=clean
benchmark_root=$root
pip_cache_root=$pipCacheRoot
temp_root=$tempRoot
runner_elevated=False
python_install_manager_available=$installManagerAvailable
python_project_local_package=nuget-python-direct
python_project_local_version=$pythonNuGetVersion
python_project_local_root=$pythonPackageRoot
nvidia_driver=$driverText
prepare_paddle_gpu_requested=$([bool]$PreparePaddleGpu)
environment_prepared=$prepared
python_version=$pythonVersion
paddle_version=$paddleRuntimeVersion
paddleocr_version=$paddleOcrRuntimeVersion
paddle_device=$paddleDevice
paddle_compiled_with_cuda=$compiledWithCuda
system_python_modified=False
ocr_executed=False
benchmark_content_created=False
backend_selected=False
"@

$evidencePath = Join-Path $evidenceRoot ("m4-2-2-environment-" + [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ") + ".txt")
$utf8 = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText($evidencePath, $summary, $utf8)
try { Set-Clipboard -Value $summary } catch {}

Write-Host ""
Write-Host "=============================================="
Write-Host "M4.2.2 OCR BENCHMARK ENVIRONMENT: PASS"
Write-Host "=============================================="
Write-Host $summary
Write-Host "Evidence stored below ignored .localcopilot/ocr-benchmark/evidence."
Write-Host "Evidence summary copied to clipboard."
