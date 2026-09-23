[CmdletBinding()]
param(
    [ValidateSet("Server", "Client")]
    [string]$MachineRole = "Server",
    [switch]$PreparePaddleGpu,
    [switch]$ValidateOnly,
    [string]$ExpectedBranch = "dev/m4-2-2-controlled-benchmark"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$schemaVersion = 1
$paddlePythonTag = "3.12"
$paddleVersion = "3.2.0"
$paddleOcrVersion = "3.7.0"
$paddleIndex = "https://www.paddlepaddle.org.cn/packages/stable/cu126/"
$pythonInstallerVersion = "3.12.10"
$pythonInstallerUrl = "https://www.python.org/ftp/python/3.12.10/python-3.12.10-amd64.exe"
$pythonInstallerSha256 = "67B5635E80EA51072B87941312D00EC8927C4DB9BA18938F7AD2D27B328B95FB"

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

function Install-M422PythonFromOfficialInstaller {
    param(
        [Parameter(Mandatory = $true)][string]$TargetRoot,
        [Parameter(Mandatory = $true)][string]$CacheRoot
    )

    New-Item -ItemType Directory -Path $CacheRoot -Force | Out-Null
    $installerPath = Join-Path $CacheRoot ("python-" + $pythonInstallerVersion + "-amd64.exe")

    if (-not (Test-Path -LiteralPath $installerPath)) {
        Invoke-WebRequest -Uri $pythonInstallerUrl -OutFile $installerPath -UseBasicParsing
    }

    $hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
    if (-not [string]::Equals($hash, $pythonInstallerSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Official Python installer SHA256 validation failed."
    }

    $signature = Get-AuthenticodeSignature -FilePath $installerPath
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Official Python installer Authenticode signature is not valid."
    }
    if ($null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Subject -notmatch "Python Software Foundation") {
        throw "Official Python installer signer is unexpected."
    }

    New-Item -ItemType Directory -Path $TargetRoot -Force | Out-Null
    $targetArgument = 'TargetDir="{0}"' -f $TargetRoot
    $arguments = @(
        "/quiet",
        "InstallAllUsers=0",
        $targetArgument,
        "Include_launcher=0",
        "InstallLauncherAllUsers=0",
        "PrependPath=0",
        "AppendPath=0",
        "AssociateFiles=0",
        "Shortcuts=0",
        "Include_test=0",
        "Include_doc=0",
        "Include_tcltk=0",
        "Include_pip=1"
    )

    $process = Start-Process -FilePath $installerPath -ArgumentList $arguments -PassThru -Wait
    if ($process.ExitCode -ne 0) {
        throw ("Official Python " + $pythonInstallerVersion + " installer failed with exit code " + $process.ExitCode + ".")
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
    if ($pythonInstallerVersion -ne "3.12.10") { throw "Unexpected fallback Python installer version." }
    if ($pythonInstallerUrl -notmatch "^https://www\.python\.org/ftp/python/3\.12\.10/python-3\.12\.10-amd64\.exe$") { throw "Unexpected fallback Python installer source." }
    if ($pythonInstallerSha256 -notmatch "^[0-9A-Fa-f]{64}$") { throw "Fallback Python installer SHA256 is invalid." }
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

$root = Join-Path $repoRoot ".localcopilot\ocr-benchmark"
$runtimeRoot = Join-Path $root "python312"
$cacheRoot = Join-Path $root "cache"
$evidenceRoot = Join-Path $root "evidence"
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null

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
            Install-M422PythonFromOfficialInstaller -TargetRoot $runtimeRoot -CacheRoot $cacheRoot
        }
    }

    $python = Resolve-M422TargetPython -TargetRoot $runtimeRoot
    Invoke-M422Checked -FilePath $python -Arguments @("-m", "ensurepip", "--upgrade") -Description "ensurepip"
    Invoke-M422Checked -FilePath $python -Arguments @("-m", "pip", "install", "--disable-pip-version-check", "--upgrade", "pip", "setuptools", "wheel") -Description "pip bootstrap"
    Invoke-M422Checked -FilePath $python -Arguments @("-m", "pip", "install", "--disable-pip-version-check", "paddlepaddle-gpu==$paddleVersion", "-i", $paddleIndex) -Description "PaddlePaddle GPU install"
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
runner_elevated=False
python_install_manager_available=$installManagerAvailable
python_fallback_installer_version=$pythonInstallerVersion
python_fallback_installer_source=python.org
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
