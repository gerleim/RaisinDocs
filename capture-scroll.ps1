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

    Manual - scroll by hand for 30s, including minimap drags, which the sweep does not do.
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

# --- synthetic wheel input ------------------------------------------------------------------
# Real WM_MOUSEWHEEL, not the Test* hooks. Notch merging and the message pump are part of what
# is being measured, and driving the canvas directly would step over exactly that.
#
# Windows delivers wheel messages to the window under the cursor, so the cursor is parked over
# the editor and put back afterwards. While a sweep runs the wheel belongs to the script.
$script:InputSender = @'
using System;
using System.Runtime.InteropServices;
public static class Wheel
{
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public MOUSEINPUT mi; }

    const uint INPUT_MOUSE = 0, MOUSEEVENTF_WHEEL = 0x0800;

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>One wheel notch. Negative scrolls down, which is how a reader moves forward.</summary>
    public static void Notch(int count)
    {
        var input = new INPUT[1];
        input[0].type = INPUT_MOUSE;
        input[0].mi.mouseData = unchecked((uint)(count * 120));
        input[0].mi.dwFlags = MOUSEEVENTF_WHEEL;
        SendInput(1, input, Marshal.SizeOf(typeof(INPUT)));
    }
}
'@

function Invoke-ScrollSweep {
    param([IntPtr] $Window, [int] $Repeats)

    if (-not ("Wheel" -as [type])) { Add-Type -TypeDefinition $script:InputSender }

    $rect = New-Object Wheel+RECT
    if (-not [Wheel]::GetWindowRect($Window, [ref] $rect)) { throw "could not locate the editor window" }
    $cx = [int](($rect.Left + $rect.Right) / 2)
    $cy = [int](($rect.Top + $rect.Bottom) / 2)

    $saved = New-Object Wheel+POINT
    [void][Wheel]::GetCursorPos([ref] $saved)

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

    try {
        [void][Wheel]::SetForegroundWindow($Window)
        Start-Sleep -Milliseconds 400
        [void][Wheel]::SetCursorPos($cx, $cy)
        Start-Sleep -Milliseconds 200

        for ($r = 1; $r -le $Repeats; $r++) {
            foreach ($g in $gestures) {
                # Alternate direction each gesture so the view stays off the ends of the
                # document, where a clamped coast stops early and measures nothing.
                $dir = if ((($r + $gestures.IndexOf($g)) % 2) -eq 0) { -1 } else { 1 }
                Write-Host ("  pass {0}/{1}  {2,-10} {3,2} notches {4}" -f $r, $Repeats, $g.Name, $g.Notches, $(if ($dir -lt 0) { 'down' } else { 'up' }))
                for ($i = 0; $i -lt $g.Notches; $i++) {
                    [Wheel]::Notch($dir)
                    if ($g.GapMs -gt 0) { Start-Sleep -Milliseconds $g.GapMs }
                }
                Start-Sleep -Milliseconds $g.SettleMs
            }
        }
    }
    finally {
        [void][Wheel]::SetCursorPos($saved.X, $saved.Y)
    }
}

$pm = Resolve-PresentMon $PresentMon
if (-not (Test-CanCapture)) {
    Write-Warning "Not elevated and not in 'Performance Log Users' - the capture will probably be empty."
}

# One pass is about 10.2s of gestures; a capture shorter than the sweep would stop partway
# through and the last gestures would be missing rather than obviously absent.
if ($Automated) {
    $needed = [int]([Math]::Ceiling($Repeats * 10.2)) + 3
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
$proc = Start-Process -FilePath $pm -ArgumentList $pmArgs -PassThru -NoNewWindow

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
    Write-Host "now: .\analyse-scroll.ps1 -Csv `"$csv`""
} else {
    Write-Warning "capture is empty (PresentMon exit $($proc.ExitCode)). Usually ETW permissions, or a stale session."
}
