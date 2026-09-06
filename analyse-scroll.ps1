<#
.SYNOPSIS
    Reports what the panel displayed, from a PresentMon capture, sliced per scroll gesture.

.DESCRIPTION
    Answers the question the in-process log cannot: of the frames we presented, how many did
    the display actually show, and how evenly.

    "228 presents a second into a sink that changed 140 times a second, with 13% dropped" -
    the figure design/Scroll Frame Pacing.md is built on - is the presented rate, the displayed
    rate, and the dropped share reported here.

    Slices by the QPC range the editor logs per gesture ("qpc <start>..<end>" in scroll.log),
    so wheel, smooth and direct gestures are reported separately rather than averaged into one
    another. Without -ScrollLog it reports the capture as a whole.

    Handles both column sets: PresentMon 2.x (MsBetweenPresents, DisplayedTime,
    MsAnimationError) and the 1.9 build bundled with FrameView (msBetweenPresents, Dropped).

.EXAMPLE
    .\analyse-scroll.ps1 -Csv "$env:LOCALAPPDATA\RaisinDocs\captures\scroll-20260905-140000.csv"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Csv,
    [string] $ScrollLog = "$env:LOCALAPPDATA\RaisinDocs\scroll.log",
    [double] $MinSeconds = 0.25
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Csv)) { throw "no capture at $Csv" }

$rows = Import-Csv $Csv
if ($rows.Count -eq 0) { throw "capture is empty" }

# --- column naming differs between PresentMon 1.x and 2.x -----------------------------------
$cols = $rows[0].PSObject.Properties.Name
function Find-Col { param([string[]] $Names)
    foreach ($n in $Names) { $hit = $cols | Where-Object { $_ -ieq $n }; if ($hit) { return $hit } }
    return $null
}
# CPUStartQPCTimeInMs is what 2.x emits under --qpc_time_ms, and it is the column the editor's
# "qpc a..b" stamps are in. TimeInSeconds is last and a poor substitute: it is a capture-relative
# clock the gesture log knows nothing about, and despite the name this build reports it in ms.
$cTime    = Find-Col @('CPUStartQPCTimeInMs','CPUStartTimeInMs','CPUStartTime','QPCTime','TimeInSeconds')
$cPresent = Find-Col @('MsBetweenPresents','msBetweenPresents')
$cDisplay = Find-Col @('MsBetweenDisplayChange','msBetweenDisplayChange')
$cAnim    = Find-Col @('MsAnimationError')
$cShown   = Find-Col @('DisplayedTime')      # 2.x: 'NA' when never displayed
$cDropped = Find-Col @('Dropped')            # 1.x: 1 when never displayed

if (-not $cTime)    { throw "no timestamp column found. Capture with --qpc_time_ms." }
if (-not $cPresent) { throw "no present-interval column found; is this a PresentMon CSV?" }

# A capture with lost ETW events has gaps that nothing in the CSV admits to, so the warning has to
# survive to whoever reads it later rather than scrolling past at capture time.
$lostEvents = 0
foreach ($side in @("$Csv.pmlog", "$Csv.pmerr")) {
    if (-not (Test-Path $side)) { continue }
    $text = (Get-Content $side -Raw) -replace "`0", ''
    if ($text -match '(\d+)\s+ETW\s+events\s+were\s+lost') { $lostEvents = [int]$Matches[1] }
}
if ($lostEvents -gt 0) {
    # Scaled, so a handful of lost events does not carry the same warning as hundreds.
    $share = 100.0 * $lostEvents / [Math]::Max(1, $rows.Count)
    if ($share -ge 1.0) {
        Write-Warning ("This capture lost {0} ETW events, about {1:N1}% of it - read it as indicative, not as a measurement." -f $lostEvents, $share)
    } else {
        Write-Host ("note: {0} ETW events lost ({1:N2}% of frames)" -f $lostEvents, $share)
    }
}

# The conditions the capture was taken under, if recorded. Comparing two captures without them is
# how a refresh-rate difference gets read as a code difference.
if (Test-Path "$Csv.meta") {
    $metaLines = Get-Content "$Csv.meta"
    $metaLines | ForEach-Object { Write-Host "  $_" }
    Write-Host ""

    # Interference is not a footnote. A capture taken while someone was using the machine is a
    # measurement of the harness mixed with a person, and it is indistinguishable from a good one
    # by the time anybody reads it back.
    $bad = $metaLines | Where-Object { $_ -match '^interfered:' -and $_ -notmatch 'none detected|not watched' }
    if ($bad) {
        Write-Warning "THIS CAPTURE WAS INTERFERED WITH - do not quote these numbers."
        $bad | ForEach-Object { Write-Warning "  $_" }
        Write-Host ""
    }
}

Write-Host ("capture : {0}" -f (Split-Path $Csv -Leaf))
Write-Host ("frames  : {0:N0}   columns: {1}" -f $rows.Count, ($(if ($cAnim) { "2.x (has MsAnimationError)" } else { "1.x (no MsAnimationError)" })))

function Get-Num { param($v)
    if ($null -eq $v -or $v -eq '' -or $v -eq 'NA') { return $null }
    $d = 0.0; if ([double]::TryParse($v, [ref] $d)) { return $d } else { return $null }
}

<##
 # Was this present ever put on the panel?
 #
 # Three column sets, because PresentMon renames these between builds and the 2.5.1 binary this
 # harness pins emits none of the two that were originally coded for. It has
 # MsBetweenDisplayChange, which is NA exactly when a present was never displayed.
 #
 # The old fallback returned true - "nothing to go on, assume it was shown" - which meant every
 # capture taken with 2.5.1 reported 0.0% dropped unconditionally. That is not a conservative
 # default, it is a fabricated one: it reports the good answer when it knows nothing. Several
 # conclusions were drawn from it before anyone noticed, so it now refuses instead.
 #>
function Test-Shown { param($row)
    if ($cShown)     { return (Get-Num $row.$cShown) -ne $null }
    if ($cDropped)   { return (Get-Num $row.$cDropped) -eq 0 }
    if ($cDisplay)   { return (Get-Num $row.$cDisplay) -ne $null }
    throw "This capture has no column saying whether a present was displayed - looked for " +
          "DisplayedTime, Dropped and MsBetweenDisplayChange. Refusing to report a dropped share."
}

function Show-Stats {
    param([string] $Label, [object[]] $Slice)

    $n = $Slice.Count
    if ($n -lt 10) { Write-Host ("  {0,-9} too few frames ({1})" -f $Label, $n); return }

    $shown   = @($Slice | Where-Object { Test-Shown $_ }).Count
    $dropPct = 100.0 * ($n - $shown) / $n

    $t0 = Get-Num $Slice[0].$cTime; $t1 = Get-Num $Slice[-1].$cTime
    $span = if ($t0 -ne $null -and $t1 -ne $null) { ($t1 - $t0) / 1000.0 } else { 0 }
    if ($cTime -ieq 'TimeInSeconds') { $span = $t1 - $t0 }

    $pres = @($Slice | ForEach-Object { Get-Num $_.$cPresent } | Where-Object { $_ -ne $null -and $_ -gt 0 })
    $disp = if ($cDisplay) { @($Slice | ForEach-Object { Get-Num $_.$cDisplay } | Where-Object { $_ -ne $null -and $_ -gt 0 }) } else { @() }

    $presMed = if ($pres.Count) { ($pres | Sort-Object)[[int]($pres.Count/2)] } else { 0 }
    $dispMed = if ($disp.Count) { ($disp | Sort-Object)[[int]($disp.Count/2)] } else { 0 }

    # The share of display intervals that ran long is the visible stutter: a frame held for two
    # refreshes instead of one is a hitch however even the presents were.
    $late = if ($disp.Count -and $dispMed -gt 0) { 100.0 * (@($disp | Where-Object { $_ -gt $dispMed * 1.5 }).Count) / $disp.Count } else { 0 }

    Write-Host ("  {0,-9} {1,6:N2}s  presented {2,6:N0}/s  displayed {3,6:N0}/s  unshown {4,5:N1}%  displayInterval {5,5:N2}ms  over1.5x {6,5:N1}%" -f `
        $Label, $span,
        $(if ($span -gt 0) { $n / $span } else { 0 }),
        $(if ($span -gt 0) { $shown / $span } else { 0 }),
        $dropPct, $dispMed, $late)

    if ($cAnim) {
        $ae = @($Slice | ForEach-Object { Get-Num $_.$cAnim } | Where-Object { $_ -ne $null } | ForEach-Object { [Math]::Abs($_) })
        if ($ae.Count) {
            $sorted = $ae | Sort-Object
            Write-Host ("  {0,-9} animation error  median {1,5:N2}ms  p95 {2,5:N2}ms  max {3,6:N2}ms" -f `
                '', $sorted[[int]($ae.Count/2)], $sorted[[int]($ae.Count*0.95)], $sorted[-1])
        }
    }
}

# --- slice by gesture, using the QPC range the editor logs ----------------------------------
$gestures = @()
if (Test-Path $ScrollLog) {
    $log = Get-Content $ScrollLog
    for ($i = 0; $i -lt $log.Count; $i++) {
        if ($log[$i] -match '(wheel|smooth|direct) gesture (\d+\.\d+)s') {
            $kind = $Matches[1]; $dur = [double]$Matches[2]
            if ($dur -lt $MinSeconds) { continue }
            for ($j = $i + 1; $j -lt [Math]::Min($i + 4, $log.Count); $j++) {
                if ($log[$j] -match 'qpc ([\d.]+)\.\.([\d.]+)') {
                    $gestures += [pscustomobject]@{ Kind = $kind; Dur = $dur; From = [double]$Matches[1]; To = [double]$Matches[2] }
                    break
                }
            }
        }
    }
}

Write-Host ""
if ($gestures.Count -eq 0) {
    Write-Host "no gesture ranges in the log (older build, or none recorded) - reporting the whole capture"
    Show-Stats 'all' $rows
    return
}

# Only gestures the capture actually covers.
$first = Get-Num $rows[0].$cTime; $last = Get-Num $rows[-1].$cTime
$covered = @($gestures | Where-Object { $_.From -ge $first -and $_.To -le $last })
Write-Host ("gestures in log: {0}, within this capture: {1}" -f $gestures.Count, $covered.Count)
Write-Host ""

foreach ($g in $covered) {
    $slice = @($rows | Where-Object { $t = Get-Num $_.$cTime; $t -ne $null -and $t -ge $g.From -and $t -le $g.To })
    Show-Stats $g.Kind $slice
}

Write-Host ""
Show-Stats 'all' $rows
