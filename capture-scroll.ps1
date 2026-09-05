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
    How long to capture. The editor is closed when PresentMon stops. Extended automatically
    when -Automated would otherwise outlast it.

.PARAMETER Automated
    Drive the wheel from the script instead of by hand: four gesture shapes, repeated, with
    the coast allowed to settle between them. Repeatable in a way a hand is not, which is what
    a before-and-after comparison needs - last time round the hand-made flings ranged from
    0.36s to 7.46s, which is several different experiments rather than one.

    It sends real WM_MOUSEWHEEL and parks the cursor over the editor, so **the wheel and
    pointer belong to the script while it runs** - about 10s per pass. The cursor is put back
    afterwards. Leave the machine alone for the duration.

.PARAMETER Repeats
    Passes through the four gestures. Three gives a usable sample in about 34s.

.PARAMETER PresentMon
    Path to PresentMon.exe. Defaults to the copy under %LOCALAPPDATA%\RaisinDocs\tools,
    falling back to the 1.9 build bundled with NVIDIA FrameView - which works, but has no
    MsAnimationError, the metric most directly about animation smoothness.

.EXAMPLE
    .\capture-scroll.ps1 -File "design\Scroll Frame Pacing.md" -Automated -Release

    Unattended. Runs the gesture sweep, captures it, and prints the analyse command.

.EXAMPLE
    .\capture-scroll.ps1 -File "design\Scroll Frame Pacing.md" -Seconds 30

    Manual - scroll by hand for 30s. The sweep covers wheel and minimap drags; the scrollbar
    thumb and keyboard navigation are still only reachable by hand.
#>
[CmdletBinding()]
param(
    [string] $File,
    [int]    $Seconds = 30,
    [string] $PresentMon,
    [string] $OutDir = "$env:LOCALAPPDATA\RaisinDocs\captures",
    [switch] $Release,
    [switch] $Automated,
    [int]    $Repeats = 3
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

<##
 # Leftover build processes, which are the commonest reason a capture on this machine is not
 # comparable with the last one.
 #
 # The .NET SDK leaves VBCSCompiler and MSBuild node processes behind, and they accumulate over a
 # session: twenty-eight of them were running after an afternoon of builds, one holding 442MB. That
 # is not idle, and a capture taken beside it measures the contention as much as the app.
 #
 # This is the same lesson a video-heavy app taught earlier the same day - two-refresh holds went
 # from 4.1% to 1.3% when it was closed, a bigger difference than any code change being measured.
 # Machine state is part of a capture, so it is reported rather than left to be remembered.
 #>
function Test-MachineQuiet {
    $noisy = @(Get-Process -Name 'VBCSCompiler','MSBuild','dotnet' -ErrorAction SilentlyContinue)
    if ($noisy.Count -eq 0) { return $true }

    $mb = [math]::Round(($noisy | Measure-Object WorkingSet64 -Sum).Sum / 1MB)
    Write-Warning ("{0} build processes are still running ({1} MB). Run .\build-safe.ps1 -Command clean, " -f $noisy.Count, $mb)
    Write-Warning "or close them, before treating this capture as comparable with another."
    return $false
}

# --- synthetic input, from the shared library --------------------------------------------------
# Raisin.WPF.Automation carries the primitives both this and StockRaisin2 need: foreground
# handling that reports its own failure, wheel notches over a moved cursor, and a stepped drag.
# The traps they exist to avoid are in that project's README rather than repeated here.
function Import-Automation {
    if ("Raisin.WPF.Automation.SyntheticInput" -as [type]) { return }

    $roots = @(
        "..\RaisinLibraries\Raisin.WPF.Automation\bin\Debug\net8.0-windows",
        "..\RaisinLibraries\Raisin.WPF.Automation\bin\Release\net8.0-windows"
    )
    foreach ($r in $roots) {
        $dll = Join-Path $r "Raisin.WPF.Automation.dll"
        if (Test-Path $dll) { Add-Type -Path (Resolve-Path $dll).Path; return }
    }
    throw "Raisin.WPF.Automation is not built. Run: dotnet build ..\RaisinLibraries\Raisin.WPF.Automation\Raisin.WPF.Automation.csproj"
}

function Invoke-ScrollSweep {
    param([IntPtr] $Window, [int] $Repeats)

    Import-Automation
    $target = [Raisin.WPF.Automation.TargetWindow]::new($Window)
    if ($target.Bounds.IsEmpty) { throw "could not locate the editor window" }

    # Left of centre horizontally, to stay clear of the minimap and scrollbar on the right edge -
    # the canvas is what should receive the wheel.
    $at = $target.PointAt(0.35, 0.5)

    # Each entry is a gesture: how many notches, how far apart, and how long to let the coast
    # settle before the next. A single flick spends most of its life in the slow tail; a long
    # sustained scroll never gets there. Between them they cover the range the speed-band table
    # in design/Scroll Pre-Buffering.md describes, repeatably - which a hand cannot do.
    $gestures = @(
        @{ Name = 'flick-1';   Notches = 1;  GapMs = 0;  SettleMs = 1800 },
        @{ Name = 'flick-3';   Notches = 3;  GapMs = 30; SettleMs = 2000 },
        @{ Name = 'flick-10';  Notches = 10; GapMs = 20; SettleMs = 2500 },
        @{ Name = 'sustained'; Notches = 30; GapMs = 60; SettleMs = 1800 }
    )

    # Where the minimap is, from the app rather than from a proportion of the window. It sits in an
    # Auto-width column between the canvas and the scrollbar, so a guess that is slightly wrong
    # lands on the canvas - where a left-drag selects text rather than scrolling, and the run
    # completes looking exactly like a successful one.
    $minimap = Get-MinimapRect

    # Refuse rather than scroll something else. A sweep sent to whatever window happened to be in
    # front still produces a capture, still looks successful, and measures nothing.
    $target.Focus([TimeSpan]::FromSeconds(3), "the editor")

    [Raisin.WPF.Automation.SyntheticInput]::PreservingCursor({
        Start-Sleep -Milliseconds 200

        for ($r = 1; $r -le $Repeats; $r++) {
            foreach ($g in $gestures) {
                # Alternate direction each gesture so the view stays off the ends of the
                # document, where a clamped coast stops early and measures nothing.
                $dir = if ((($r + $gestures.IndexOf($g)) % 2) -eq 0) { -1 } else { 1 }
                Write-Host ("  pass {0}/{1}  {2,-10} {3,2} notches {4}" -f $r, $Repeats, $g.Name, $g.Notches, $(if ($dir -lt 0) { 'down' } else { 'up' }))
                [Raisin.WPF.Automation.SyntheticInput]::WheelAt($at, $dir * $g.Notches, $g.GapMs)
                Start-Sleep -Milliseconds $g.SettleMs
            }

            if ($minimap) {
                # Down the minimap and back. Alternating keeps the view off the ends of the
                # document, where a drag clamps and reveals nothing.
                $x  = $minimap.X + [int]($minimap.Width / 2)
                $y1 = $minimap.Y + [int]($minimap.Height * 0.20)
                $y2 = $minimap.Y + [int]($minimap.Height * 0.80)
                $down = ($r % 2) -eq 1
                Write-Host ("  pass {0}/{1}  {2,-10}    drag {3}" -f $r, $Repeats, 'minimap', $(if ($down) { 'down' } else { 'up' }))
                if ($down) {
                    [Raisin.WPF.Automation.SyntheticInput]::Drag(
                        [System.Drawing.Point]::new($x, $y1), [System.Drawing.Point]::new($x, $y2), 40, 12)
                } else {
                    [Raisin.WPF.Automation.SyntheticInput]::Drag(
                        [System.Drawing.Point]::new($x, $y2), [System.Drawing.Point]::new($x, $y1), 40, 12)
                }
                Start-Sleep -Milliseconds 1200
            }
        }
    })
}

<##
 # The minimap's screen rectangle, as the editor last reported it.
 #
 # DocsEditor writes "minimap rect X,Y WxH" to the scroll log whenever it lays out, but only while
 # scroll diagnostics are on. Taking the last line means the rect matches the current window size
 # rather than some earlier run's.
 #>
function Get-MinimapRect {
    $log = "$env:LOCALAPPDATA\RaisinDocs\scroll.log"
    if (-not (Test-Path $log)) { return $null }

    $line = Get-Content $log | Select-String -Pattern 'minimap rect (-?\d+),(-?\d+) (\d+)x(\d+)' | Select-Object -Last 1
    if (-not $line) {
        Write-Warning "no minimap rect in the log - skipping the drag rather than guessing where it is"
        return $null
    }
    $m = [regex]::Match($line.Line, 'minimap rect (-?\d+),(-?\d+) (\d+)x(\d+)')
    [pscustomobject]@{
        X      = [int]$m.Groups[1].Value
        Y      = [int]$m.Groups[2].Value
        Width  = [int]$m.Groups[3].Value
        Height = [int]$m.Groups[4].Value
    }
}

$pm = Resolve-PresentMon $PresentMon
$quiet = Test-MachineQuiet
if (-not (Test-CanCapture)) {
    Write-Warning "Not elevated and not in 'Performance Log Users' - the capture will probably be empty."
}

# One pass is about 12s of gestures - four wheel shapes and a minimap drag. A capture shorter
# than the sweep stops partway through, and the last gestures go missing rather than being
# obviously absent.
if ($Automated) {
    $needed = [int]([Math]::Ceiling($Repeats * 12.0)) + 3
    if ($Seconds -lt $needed) {
        Write-Host "extending capture from ${Seconds}s to ${needed}s to cover $Repeats passes"
        $Seconds = $needed
    }
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
# Quoted: Start-Process joins an ArgumentList with spaces and quotes nothing, so a path with
# a space in it arrives as several arguments and the editor opens the first word.
$editorArgs = @('--scroll-diag')
if ($File) { $editorArgs += '"{0}"' -f (Resolve-Path $File).Path }
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
# PresentMon's own output is kept beside the capture. It reports lost ETW events there and
# nowhere else, and a capture with losses has holes that nothing in the CSV admits to.
$pmOut = "$csv.pmlog"
$pmErr = "$csv.pmerr"
$proc = Start-Process -FilePath $pm -ArgumentList $pmArgs -PassThru -NoNewWindow `
    -RedirectStandardOutput $pmOut -RedirectStandardError $pmErr

if ($Automated) {
    Start-Sleep -Milliseconds 800   # let tracing settle before the first notch
    $app.Refresh()
    if ($app.MainWindowHandle -eq [IntPtr]::Zero) {
        Write-Warning "no editor window yet - skipping the sweep, capture will be idle"
    } else {
        Write-Host "sweep running - the wheel belongs to the script until it finishes"
        Invoke-ScrollSweep -Window $app.MainWindowHandle -Repeats $Repeats
        Write-Host "sweep done"
    }
}

$proc | Wait-Process

if ($app -and -not $app.HasExited) { $app.CloseMainWindow() | Out-Null }

Write-Host ""
if ((Test-Path $csv) -and (Get-Item $csv).Length -gt 0) {
    $rows = (Get-Content $csv | Measure-Object -Line).Lines - 1
    Write-Host "captured $rows frames -> $csv"

    # Lost events mean the trace could not keep up, so the capture has gaps the CSV does not
    # mention. Said plainly here, and left in the .pmlog for whoever reads the capture later.
    $pmText = @()
    foreach ($f in @($pmOut, $pmErr)) { if (Test-Path $f) { $pmText += Get-Content $f -Raw } }
    $joined = ($pmText -join "`n") -replace "`0", ''
    if ($joined -match '(\d+)\s+ETW\s+events\s+were\s+lost') {
        Write-Warning "$($Matches[1]) ETW events were lost - this capture has gaps in it."
        Write-Warning "Treat it as indicative at best, and re-take it on a quiet machine."
    }
    if (-not $quiet) {
        Write-Warning "Build processes were running throughout - do not compare this capture with another."
    }

    Write-Host "now: .\analyse-scroll.ps1 -Csv `"$csv`""
} else {
    Write-Warning "capture is empty (PresentMon exit $($proc.ExitCode)). Usually ETW permissions, or a stale session."
}
