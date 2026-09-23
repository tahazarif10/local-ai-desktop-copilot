[CmdletBinding()]
param(
    [switch]$ValidateOnly,
    [switch]$GenerateOnly,
    [string]$BenchmarkRoot = "D:\LocalAI-Prerequisites",
    [string]$ExpectedBranch = "dev/m4-2-2-benchmark-runner"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0
$schemaVersion = 1
$categories = @("persian-ui","english-ui","mixed-fa-en","terminal-console","dialog","browser-ui","desktop-app-ui")

if ($ValidateOnly) {
    if ($schemaVersion -ne 1 -or $categories.Count -ne 7) { throw "Invalid controlled UI corpus contract." }
    Write-Host "M4.2.2 controlled UI corpus validation: PASS"
    return
}
if ($env:OS -ne "Windows_NT") { throw "Controlled UI corpus generation requires Windows." }

function Test-Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
if (Test-Elevated) { throw "Run from a normal, non-Administrator PowerShell." }

$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$status = git -C $repoRoot status --short | Out-String -Width 4096
if ($LASTEXITCODE -ne 0) { throw "Unable to read Git status." }
if (-not [string]::IsNullOrWhiteSpace($status)) { throw "Controlled UI benchmark requires a clean working tree." }
$branch = (git -C $repoRoot branch --show-current | Out-String).Trim()
$head = (git -C $repoRoot rev-parse HEAD | Out-String).Trim()
if ($branch -ne $ExpectedBranch) { throw "Expected branch '$ExpectedBranch'; current branch '$branch'." }

$root = [IO.Path]::GetFullPath($BenchmarkRoot)
$corpusRoot = Join-Path $root "corpus"
$imagesRoot = Join-Path $corpusRoot "images"
$truthRoot = Join-Path $corpusRoot "ground-truth"
$manifestPath = Join-Path $corpusRoot "manifest.json"
$provenancePath = Join-Path $corpusRoot "controlled-ui-provenance.json"
New-Item -ItemType Directory -Path $imagesRoot -Force | Out-Null
New-Item -ItemType Directory -Path $truthRoot -Force | Out-Null

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

function From-CodePoints([int[]]$CodePoints) {
    $b = New-Object Text.StringBuilder
    foreach ($cp in $CodePoints) { [void]$b.Append([char]$cp) }
    return $b.ToString()
}
function Persian([string]$Kind) {
    switch ($Kind) {
        "settings" { return From-CodePoints @(1578,1606,1592,1740,1605,1575,1578,32,1583,1587,1578,1711,1575,1607) }
        "pressure" { return From-CodePoints @(1601,1588,1575,1585,32,1705,1575,1585,1740,32,54,32,1576,1575,1585) }
        "speed" { return From-CodePoints @(1587,1585,1593,1578,32,1581,1585,1705,1578,32,49,50,48,32,1605,1740,1604,1740,32,1605,1578,1585,32,1576,1585,32,1579,1575,1606,1740,1607) }
        "axis" { return From-CodePoints @(1605,1581,1608,1585,32,65,49,32,1570,1605,1575,1583,1607) }
        "warning" { return From-CodePoints @(1607,1588,1583,1575,1585,32,1587,1740,1587,1578,1605) }
        "save" { return From-CodePoints @(1570,1740,1575,32,1578,1606,1592,1740,1605,1575,1578,32,1584,1582,1740,1585,1607,32,1588,1608,1583) }
        "ready" { return From-CodePoints @(1608,1590,1593,1740,1578,32,1583,1587,1578,1711,1575,1607,32,1570,1605,1575,1583,1607,32,1576,1607,32,1705,1575,1585) }
        default { throw "Unknown Persian fixture kind." }
    }
}

function New-Form([string]$Title, [Drawing.Color]$BackColor = [Drawing.Color]::FromArgb(248,248,248)) {
    $f = New-Object Windows.Forms.Form
    $f.Text = $Title
    $f.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $f.Location = New-Object Drawing.Point(80,80)
    $f.ClientSize = New-Object Drawing.Size(1120,360)
    $f.FormBorderStyle = [Windows.Forms.FormBorderStyle]::FixedSingle
    $f.MaximizeBox = $false
    $f.MinimizeBox = $false
    $f.ShowInTaskbar = $false
    $f.TopMost = $true
    $f.BackColor = $BackColor
    return $f
}
function Add-Label($Form,[string]$Text,[int]$X,[int]$Y,[switch]$Rtl,[string]$FontName="Segoe UI",[float]$FontSize=22) {
    $l = New-Object Windows.Forms.Label
    $l.Text = $Text
    $l.Location = New-Object Drawing.Point($X,$Y)
    $l.Size = New-Object Drawing.Size(960,50)
    $l.Font = New-Object Drawing.Font($FontName,$FontSize,[Drawing.FontStyle]::Regular,[Drawing.GraphicsUnit]::Pixel)
    $l.ForeColor = if ($Form.BackColor.R -lt 40) { [Drawing.Color]::White } else { [Drawing.Color]::Black }
    $l.BackColor = $Form.BackColor
    if ($Rtl) {
        $l.RightToLeft = [Windows.Forms.RightToLeft]::Yes
        $l.TextAlign = [Drawing.ContentAlignment]::MiddleRight
    } else {
        $l.TextAlign = [Drawing.ContentAlignment]::MiddleLeft
    }
    $Form.Controls.Add($l)
}
function Save-Client($Form,[string]$Path) {
    $Form.Show()
    $Form.Activate()
    $Form.BringToFront()
    [Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 700
    [Windows.Forms.Application]::DoEvents()
    $origin = $Form.PointToScreen([Drawing.Point]::Empty)
    $size = $Form.ClientSize
    $bmp = New-Object Drawing.Bitmap($size.Width,$size.Height)
    $g = [Drawing.Graphics]::FromImage($bmp)
    try {
        $g.CopyFromScreen($origin.X,$origin.Y,0,0,$size,[Drawing.CopyPixelOperation]::SourceCopy)
        $bmp.Save($Path,[Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $g.Dispose()
        $bmp.Dispose()
        $Form.Hide()
        $Form.Dispose()
    }
}
function Write-Sample([string]$Id,[string]$Category,$Form,[string[]]$Truth) {
    $imagePath = Join-Path $imagesRoot ($Id + ".png")
    $truthPath = Join-Path $truthRoot ($Id + ".txt")
    Save-Client $Form $imagePath
    [IO.File]::WriteAllText($truthPath,($Truth -join [Environment]::NewLine),(New-Object Text.UTF8Encoding($true)))
    return [ordered]@{ id=$Id; category=$Category; image="images/$Id.png"; ground_truth_file="ground-truth/$Id.txt" }
}

foreach ($id in @("s001","s002","s003","s004","s005","s006","s007")) {
    Remove-Item (Join-Path $imagesRoot ($id + ".png")) -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $truthRoot ($id + ".txt")) -Force -ErrorAction SilentlyContinue
}
Remove-Item $manifestPath -Force -ErrorAction SilentlyContinue
Remove-Item $provenancePath -Force -ErrorAction SilentlyContinue

$samples = @()

$f = New-Form "M422-s001"
$p1=Persian "settings"; $p2=Persian "pressure"; $p3=Persian "speed"
Add-Label $f $p1 80 35 -Rtl; Add-Label $f $p2 80 130 -Rtl; Add-Label $f $p3 80 225 -Rtl
$samples += Write-Sample "s001" "persian-ui" $f @($p1,$p2,$p3)

$f = New-Form "M422-s002"
$e1="Machine Settings"; $e2="Working Pressure 6 bar"; $e3="Axis Speed 120 mm per second"
Add-Label $f $e1 80 35; Add-Label $f $e2 80 130; Add-Label $f $e3 80 225
$samples += Write-Sample "s002" "english-ui" $f @($e1,$e2,$e3)

$f = New-Form "M422-s003"
$m1="Servo A1 Ready"; $m2=Persian "axis"; $m3="Error Code ER01"
Add-Label $f $m1 80 35; Add-Label $f $m2 80 130 -Rtl; Add-Label $f $m3 80 225
$samples += Write-Sample "s003" "mixed-fa-en" $f @($m1,$m2,$m3)

$f = New-Form "M422-s004" ([Drawing.Color]::FromArgb(24,24,24))
$t1="PS D:\local-ai-desktop-copilot>"; $t2="Build succeeded."; $t3="0 Warning(s)  0 Error(s)"
Add-Label $f $t1 55 45 -FontName "Consolas" -FontSize 21
Add-Label $f $t2 55 135 -FontName "Consolas" -FontSize 21
Add-Label $f $t3 55 225 -FontName "Consolas" -FontSize 21
$samples += Write-Sample "s004" "terminal-console" $f @($t1,$t2,$t3)

$f = New-Form "M422-s005"
$d1=Persian "warning"; $d2=Persian "save"
Add-Label $f $d1 80 45 -Rtl; Add-Label $f $d2 80 130 -Rtl
$ok=New-Object Windows.Forms.Button; $ok.Text="OK"; $ok.Location=New-Object Drawing.Point(785,240); $ok.Size=New-Object Drawing.Size(120,46); $f.Controls.Add($ok)
$cancel=New-Object Windows.Forms.Button; $cancel.Text="Cancel"; $cancel.Location=New-Object Drawing.Point(925,240); $cancel.Size=New-Object Drawing.Size(120,46); $f.Controls.Add($cancel)
$samples += Write-Sample "s005" "dialog" $f @($d1,$d2,"OK","Cancel")

$f = New-Form "M422-s006"
$b0="http://127.0.0.1/local-ai"; $b1="Local AI Dashboard"; $b2="Server Status Connected"; $b3="127.0.0.1"
$box=New-Object Windows.Forms.TextBox; $box.Text=$b0; $box.ReadOnly=$true; $box.Location=New-Object Drawing.Point(60,35); $box.Size=New-Object Drawing.Size(1000,42); $box.Font=New-Object Drawing.Font("Segoe UI",18,[Drawing.FontStyle]::Regular,[Drawing.GraphicsUnit]::Pixel); $f.Controls.Add($box)
Add-Label $f $b1 80 110 -FontSize 24; Add-Label $f $b2 80 185 -FontSize 21; Add-Label $f $b3 80 255 -FontName "Consolas" -FontSize 20
$samples += Write-Sample "s006" "browser-ui" $f @($b0,$b1,$b2,$b3)

$f = New-Form "M422-s007"
$a1="Production Control"; $a2="Job E-CT-2140"; $a3=Persian "ready"
Add-Label $f $a1 70 45 -FontSize 24; Add-Label $f $a2 70 135 -FontSize 21; Add-Label $f $a3 70 225 -Rtl -FontSize 21
$samples += Write-Sample "s007" "desktop-app-ui" $f @($a1,$a2,$a3)

[ordered]@{schema=$schemaVersion;samples=$samples} | ConvertTo-Json -Depth 6 | Set-Content $manifestPath -Encoding UTF8
[ordered]@{
    schema=$schemaVersion
    mode="controlled-os-rendered-ui"
    branch=$branch
    head=$head
    sample_count=$samples.Count
    categories=$categories
    capture="visible WinForms client-area screen capture"
    raw_content_in_console=$false
    source_images_committed=$false
    ground_truth_committed=$false
    full_screen_capture=$false
    synthetic_bitmap_text_generation=$false
} | ConvertTo-Json -Depth 4 | Set-Content $provenancePath -Encoding UTF8

Write-Host ""
Write-Host "M4.2.2 CONTROLLED OS-RENDERED UI CORPUS: READY"
Write-Host ("head=" + $head)
Write-Host ("corpus_root=" + $corpusRoot)
Write-Host ("sample_count=" + $samples.Count)
Write-Host "raw_ground_truth_printed=False"
Write-Host "full_screen_capture=False"
Write-Host "synthetic_bitmap_text_generation=False"

if ($GenerateOnly) {
    Write-Host "benchmark_executed=False"
    return
}

$runner = Join-Path $repoRoot "run-m4-2-ocr-benchmark.ps1"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner -Run -BenchmarkRoot $root -ExpectedBranch $ExpectedBranch
if ($LASTEXITCODE -ne 0) { throw "Controlled UI OCR benchmark failed with exit code $LASTEXITCODE." }
