<#
.SYNOPSIS
    Captures what the panel actually displayed while scrolling, using PresentMon.

.DESCRIPTION
    The editor's own --scroll-diag log can only say we asked for a repaint on every composed
    frame and got one. It cannot say whether the panel showed them: presents that miss a
    composition deadline are picked up at a later vblank, and from inside the process that is
    invisible. This runs PresentMon alongside the editor to get the outside view.

    Both sides stamp QPC milliseconds - the editor logs "qpc <start>..<end>" per gesture, and
    PresentMon is run with --qpc_time_ms - so analyse-scroll.ps1 can slice the capture to
    exactly one gesture instead of lining timestamps up by eye.

.PARAMETER File
    Markdown file to open. A large document scrolls more interestingly than a small one.

.PARAMETER Seconds
    How long to capture. The editor is closed when PresentMon stops.

.PARAMETER PresentMon
    Path to PresentMon.exe. Defaults to the copy under %LOCALAPPDATA%\RaisinDocs\tools,
    falling back to the 1.9 build bundled with NVIDIA FrameView - which works, but has no
    MsAnimationError, the metric most directly about animation smoothness.

.EXAMPLE
    .\capture-scroll.ps1 -File "design\Scroll Frame Pacing.md" -Seconds 30
#>
[CmdletBinding()]
param(
    [string] $File,
    [int]    $Seconds = 30,
    [string] $PresentMon,
    [string] $OutDir = "$env:LOCALAPPDATA\RaisinDocs\captures",
    [switch] $Release
)

$ErrorActionPreference = 'Stop'

function Resolve-PresentMon {
    param([string] $Explicit)
    if ($Explicit) {
        if (-not (Test-Path $Explicit)) { throw "PresentMon not found at $Explicit" }
        return $Explicit
    }
    $candidates = @(
        "$env:LOCALAPPDATA\RaisinDocs\tools\PresentMon.exe",
        "C:\Program Files\NVIDIA Corporation\FrameView\bin\PresentMon_x64.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw @"
No PresentMon found. Download the standalone build (MIT, ~1MB, no install):
  https://github.com/GameTechDev/PresentMon/releases
and save it as $env:LOCALAPPDATA\RaisinDocs\tools\PresentMon.exe
"@
}

# Capturing needs ETW access. Membership of Performance Log Users is enough; without it
# PresentMon starts but records nothing, which looks like a successful empty capture.
function Test-CanCapture {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $isAdmin = ([Security.Principal.WindowsPrincipal]$id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($isAdmin) { return $true }
    foreach ($g in $id.Groups) {
        try { if ($g.Translate([Security.Principal.NTAccount]).Value -match 'Performance Log Users') { return $true } } catch { }
    }
    return $false
}

$pm = Resolve-PresentMon $PresentMon
if (-not (Test-CanCapture)) {
    Write-Warning "Not elevated and not in 'Performance Log Users' - the capture will probably be empty."
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$csv   = Join-Path $OutDir "scroll-$stamp.csv"

$config  = if ($Release) { "Release" } else { "Debug" }
$editor  = "RaisinDocs.Editor\bin\$config\net8.0-windows\RaisinDocs.Editor.exe"
if (-not (Test-Path $editor)) { throw "Editor not built at $editor - run .\build-safe.ps1 -Command build" }

Write-Host "PresentMon : $pm"
Write-Host "editor     : $editor"
Write-Host "capture    : $csv"
Write-Host ""

# --scroll-diag so the gesture log and the capture describe the same run.
$editorArgs = @('--scroll-diag')
if ($File) { $editorArgs += (Resolve-Path $File).Path }
$app = Start-Process -FilePath (Resolve-Path $editor).Path -ArgumentList $editorArgs -PassThru
Start-Sleep -Milliseconds 1500   # let the window come up before tracing starts

$pmArgs = @(
    '--process_id', $app.Id,
    '--output_file', $csv,
    '--qpc_time_ms',            # same clock the gesture log stamps, so slices line up
    '--timed', $Seconds,
    '--terminate_after_timed',
    '--no_console_stats',
    '--stop_existing_session'   # a stale ETW session from a killed run blocks a new one
)
$proc = Start-Process -FilePath $pm -ArgumentList $pmArgs -PassThru -Wait -NoNewWindow

if ($app -and -not $app.HasExited) { $app.CloseMainWindow() | Out-Null }

Write-Host ""
if ((Test-Path $csv) -and (Get-Item $csv).Length -gt 0) {
    $rows = (Get-Content $csv | Measure-Object -Line).Lines - 1
    Write-Host "captured $rows frames -> $csv"
    Write-Host "now: .\analyse-scroll.ps1 -Csv `"$csv`""
} else {
    Write-Warning "capture is empty (PresentMon exit $($proc.ExitCode)). Usually ETW permissions, or a stale session."
}
