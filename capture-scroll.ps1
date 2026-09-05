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

.NOTES
    While an automated sweep runs it owns the pointer. Hold ESC to cancel: the gesture is
    abandoned, the mouse button is released if a drag was in flight, the cursor goes back where it
    was, and the part-finished capture is deleted rather than left to be read later as a short run.

    If anything else takes the machine during a sweep - a click elsewhere, a nudge of the mouse, a
    window stealing focus - the capture records it and analyse-scroll.ps1 refuses to let it pass as
    a clean one. Those numbers are the harness mixed with a person and cannot be separated after
    the fact.

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

.PARAMETER Monitor
    Which display to measure on, matched loosely against the device name - "DISPLAY8" is enough.
    The frame budget *is* the refresh period, and it ranges from 3.57ms to 16.67ms across the
    panels here, so this is the single setting that most changes what a capture says.

.PARAMETER Maximise
    Fill the chosen display's working area - the screen less the taskbar. The largest window the
    panel can hold, and the repeatable way to ask for it.

.PARAMETER Size
    An explicit window size as WxH, for example 1920x1032, placed at the top-left of the chosen
    display's working area. Window height multiplies the per-frame work, because it sets how many
    lines have to be produced.

    Vary size or refresh rate, not both at once: a capture that changed each cannot be attributed
    to either. The 1920x1080 panels are the refresh sweep at constant size; size comparisons stay
    on one panel. See design\Scroll Frame Pacing.md.

.EXAMPLE
    .\capture-scroll.ps1 -File "design\Scroll Frame Pacing.md" -Automated -Release

    Unattended. Runs the gesture sweep, captures it, and prints the analyse command.

.EXAMPLE
    .\capture-scroll.ps1 -Automated -Release -File "design\Scroll Frame Pacing.md" -Monitor DISPLAY8 -Maximise

    One cell of the baseline set: the 60Hz panel, filling its working area. The capture records
    the display, its refresh rate and the window rectangle beside the CSV, so it stays comparable.

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
    [int]    $Repeats = 3,
    [string] $Monitor,
    [switch] $Maximise,
    [string] $Size
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
<##
 # Places the editor on a chosen display, at a chosen size, before anything is measured.
 #
 # Both change the numbers, and for different reasons. Window height multiplies whatever the
 # per-frame work scales with - here, how many lines have to be produced. Refresh rate *is* the
 # frame budget: 16.67ms at 60Hz against 3.57ms at 280, a factor of nearly five, so a result that
 # holds on one panel and not another says the work is near the limit rather than broken.
 #
 # Vary one at a time. Moving to a slower panel that is also smaller changes both and neither
 # number can then be attributed - which is why the 1920x1080 displays here are the refresh sweep
 # and the size comparison stays on one panel.
 #>
function Resolve-Screen {
    param([string] $Wanted)
    Add-Type -AssemblyName System.Windows.Forms
    $screens = [System.Windows.Forms.Screen]::AllScreens
    if (-not $Wanted) { return $null }

    $hit = $screens | Where-Object { $_.DeviceName -like "*$Wanted*" } | Select-Object -First 1
    if (-not $hit) {
        $names = ($screens | ForEach-Object { $_.DeviceName }) -join ', '
        throw "no display matching '$Wanted'. Available: $names"
    }
    return $hit
}

function Set-WindowPlacement {
    param([IntPtr] $Window, $Screen, [switch] $Max, [string] $WxH)

    Import-Automation
    $t = [Raisin.WPF.Automation.TargetWindow]::new($Window)

    if ($Max -and $Screen) {
        $wa = $Screen.WorkingArea
        $t.FillWorkingArea([System.Drawing.Rectangle]::new($wa.X, $wa.Y, $wa.Width, $wa.Height))
    }
    elseif ($WxH) {
        if ($WxH -notmatch '^(\d+)x(\d+)$') { throw "-Size wants WxH, for example 1920x1032" }
        $w = [int]$Matches[1]; $h = [int]$Matches[2]
        # Top-left of the chosen screen's working area, so the window lands wholly on it.
        $o = if ($Screen) { $Screen.WorkingArea } else { [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea }
        $t.PlaceAt([System.Drawing.Rectangle]::new($o.X, $o.Y, $w, $h))
    }
    elseif ($Screen) {
        $wa = $Screen.WorkingArea
        $t.FillWorkingArea([System.Drawing.Rectangle]::new($wa.X, $wa.Y, $wa.Width, $wa.Height))
    }
    else { return $null }

    Start-Sleep -Milliseconds 700   # let the resize settle and the minimap re-report its rect
    return $t.Bounds
}

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

    # Watches for the machine being taken back, and arms Escape as a way to ask for it back.
    $guard = [Raisin.WPF.Automation.RunGuard]::new($target)

    [Raisin.WPF.Automation.SyntheticInput]::PreservingCursor({
        Wait-Cancellable 200

        for ($r = 1; $r -le $Repeats; $r++) {
            foreach ($g in $gestures) {
                # Alternate direction each gesture so the view stays off the ends of the
                # document, where a clamped coast stops early and measures nothing.
                $dir = if ((($r + $gestures.IndexOf($g)) % 2) -eq 0) { -1 } else { 1 }
                Write-Host ("  pass {0}/{1}  {2,-10} {3,2} notches {4}" -f $r, $Repeats, $g.Name, $g.Notches, $(if ($dir -lt 0) { 'down' } else { 'up' }))
                [Raisin.WPF.Automation.SyntheticInput]::WheelAt($at, $dir * $g.Notches, $g.GapMs)
                Wait-Cancellable $g.SettleMs
                $guard.Check("$($g.Name) pass $r")
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
                Wait-Cancellable 1200
                $guard.Check("minimap drag pass $r")
            }
        }
    })

    return $guard
}

<##
 # Sleeps, but stays interruptible.
 #
 # The settle after a gesture is where most of a sweep's wall-clock time goes - up to 2.5s at a
 # stretch - so a cancel key polled only between gestures would feel unresponsive exactly when the
 # script appears to be doing nothing. Sliced, Escape is picked up within a tenth of a second
 # wherever it is pressed.
 #>
function Wait-Cancellable {
    param([int] $Milliseconds)

    $end = (Get-Date).AddMilliseconds($Milliseconds)
    while ((Get-Date) -lt $end) {
        if ([Raisin.WPF.Automation.RunGuard]::CancelKeyDown) {
            throw [OperationCanceledException]::new("Escape held - run cancelled, the machine is yours.")
        }
        Start-Sleep -Milliseconds 50
    }
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

# Wait for the window rather than sleeping a guessed interval. A fixed 1.5s was enough most of
# the time and once was not, and the run that lost the race placed nothing, measured the primary,
# and labelled itself "unknown" - a capture that looks like every other one.
$deadline = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $deadline) {
    $app.Refresh()
    if ($app.HasExited) { throw "the editor exited before showing a window (exit code $($app.ExitCode))" }
    if ($app.MainWindowHandle -ne [IntPtr]::Zero) { break }
    Start-Sleep -Milliseconds 100
}
if ($app.MainWindowHandle -eq [IntPtr]::Zero) { throw "the editor never showed a window within 20s" }
Start-Sleep -Milliseconds 400   # first layout, before anything is asked of the window

# Placed before tracing begins, so the resize is not part of what is captured.
$screen = Resolve-Screen $Monitor
if ($screen -or $Maximise -or $Size) {
    [void] (Set-WindowPlacement -Window $app.MainWindowHandle -Screen $screen -Max:$Maximise -WxH $Size)
}

# Where it ended up, asked of the window rather than assumed from what was requested - so a run
# that was never placed is labelled just as fully as one that was, and a placement that did not
# take is visible in the capture instead of silently mislabelling it.
Import-Automation
$windowRect = [Raisin.WPF.Automation.TargetWindow]::new($app.MainWindowHandle).Bounds
$panel      = [Raisin.WPF.Automation.Displays]::For($windowRect)

# A placement that was asked for and did not happen is fatal, not a warning. The capture would
# still be taken, still be full of frames, and describe a different panel than the one named in
# the command - which is worse than no capture, because it reads as data weeks later.
if ($screen -and $panel -and $panel.DeviceName -ne $screen.DeviceName) {
    $app.CloseMainWindow() | Out-Null
    throw "asked for $($screen.DeviceName) but the window is on $($panel.DeviceName). Refusing to capture."
}
Write-Host ("window  : {0}" -f $(if ($windowRect) { "$($windowRect.Width)x$($windowRect.Height) at $($windowRect.X),$($windowRect.Y)" } else { 'unknown' }))
Write-Host ("display : {0}" -f $(if ($panel) { "$panel  budget $('{0:F2}' -f $panel.FramePeriodMs)ms" } else { 'unknown' }))

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

$interference = 'not watched - manual run'
if ($Automated) {
    Start-Sleep -Milliseconds 800   # let tracing settle before the first notch
    $app.Refresh()
    if ($app.MainWindowHandle -eq [IntPtr]::Zero) {
        Write-Warning "no editor window yet - skipping the sweep, capture will be idle"
    } else {
        Write-Host "sweep running - the wheel belongs to the script until it finishes"
        Write-Host "hold ESC to cancel and get the machine back" -ForegroundColor Yellow
        try {
            $guard = Invoke-ScrollSweep -Window $app.MainWindowHandle -Repeats $Repeats
            Write-Host "sweep done"
            $interference = $guard.Summary()
            if (-not $guard.Clean) {
                Write-Warning "interference during the run: $interference"
                Write-Warning "the numbers describe the harness mixed with someone using the machine."
            }
        }
        catch [OperationCanceledException] {
            # Stop the trace and take the capture with it. A cancelled sweep has a partial set of
            # gestures and no record of which ones completed, so keeping the CSV only invites it to
            # be read later as a short run rather than an abandoned one.
            Write-Host ""
            Write-Host "cancelled - stopping the capture and discarding it" -ForegroundColor Yellow
            if ($proc -and -not $proc.HasExited) { $proc.Kill() }
            if ($app -and -not $app.HasExited) { $app.CloseMainWindow() | Out-Null }
            Remove-Item "$csv*" -Force -ErrorAction SilentlyContinue
            Write-Host "the machine is yours. Nothing was written."
            return
        }
        finally {
            if ($guard) { $guard.Dispose() }
        }
    }
}

$proc | Wait-Process

if ($app -and -not $app.HasExited) { $app.CloseMainWindow() | Out-Null }

Write-Host ""
if ((Test-Path $csv) -and (Get-Item $csv).Length -gt 0) {
    $rows = @(Get-Content $csv).Count - 1
    Write-Host "captured $rows frames -> $csv"

    # Lost events mean the trace could not keep up, so the capture has gaps the CSV does not
    # mention. Said plainly here, and left in the .pmlog for whoever reads the capture later.
    $pmText = @()
    foreach ($f in @($pmOut, $pmErr)) { if (Test-Path $f) { $pmText += Get-Content $f -Raw } }
    $joined = ($pmText -join "`n") -replace "`0", ''
    # Scaled against the capture. A handful of lost events in three thousand frames is noise; the
    # noisy run earlier lost 661. Warning identically for both would teach everyone to ignore it.
    if ($joined -match '(\d+)\s+ETW\s+events\s+were\s+lost') {
        $lost = [int]$Matches[1]
        $share = 100.0 * $lost / [Math]::Max(1, $rows)
        if ($share -ge 1.0) {
            Write-Warning ("{0} ETW events lost, about {1:N1}% of this capture - it has real gaps." -f $lost, $share)
            Write-Warning "Re-take it on a quiet machine before reading anything into it."
        } else {
            Write-Host ("note: {0} ETW events lost ({1:N2}% of frames) - small enough to ignore." -f $lost, $share)
        }
    }
    if (-not $quiet) {
        Write-Warning "Build processes were running throughout - do not compare this capture with another."
    }

    # What this capture was taken under. Refresh rate and window size both change the numbers, so a
    # capture that does not carry them cannot be compared with another - which is the whole point
    # of taking a set across displays.
    $meta = @()
    $meta += "capture   : $(Split-Path $csv -Leaf)"
    $meta += "taken     : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    $meta += "editor    : $config"
    $meta += "document  : $(if ($File) { "$(Split-Path $File -Leaf), $(@(Get-Content $File).Count) lines" } else { '(none)' })"
    $meta += "window    : $(if ($windowRect) { "$($windowRect.Width)x$($windowRect.Height) at $($windowRect.X),$($windowRect.Y)" } else { 'unknown' })"
    $meta += "display   : $(if ($panel) { $panel.DeviceName } else { 'unknown' })"
    $meta += "refresh   : $(if ($panel) { "$($panel.RefreshHz) Hz - $('{0:F2}' -f $panel.FramePeriodMs)ms budget" } else { 'unknown' })"
    $meta += "quiet     : $(if ($quiet) { 'yes' } else { 'no - build processes were running' })"
    $meta += "interfered: $interference"
    $meta += "frames    : $rows"
    $meta | Set-Content -Path "$csv.meta" -Encoding utf8

    Write-Host "now: .\analyse-scroll.ps1 -Csv `"$csv`""
} else {
    Write-Warning "capture is empty (PresentMon exit $($proc.ExitCode)). Usually ETW permissions, or a stale session."
}
