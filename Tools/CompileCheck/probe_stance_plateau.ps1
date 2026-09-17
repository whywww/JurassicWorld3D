# Decisive probe: which bones ever enter the runtime plant band, and what the
# horizontal speed looks like during the "plateau" part of each stance run.
# Runtime equivalents: releaseHeight=0.27, dy=-0.347  =>  CSV-space band y <= 0.617.
# Usage: powershell -NoProfile -File probe_stance_plateau.ps1 [-Clip walk_slow_loop]
param([string]$Clip = 'walk_slow_loop')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$csv = (Get-ChildItem (Join-Path $repo ('Logs\footprobe_*' + $Clip + '.csv'))).FullName
$rows = Import-Csv -Path $csv
$first = $rows | Where-Object { [int] $_.foot -eq 0 -and [int] $_.frame -le 1 } | Sort-Object { [int] $_.frame }
$dt = [double] $first[1].time - [double] $first[0].time
$bandThr = 0.617
Write-Host ("file={0}" -f $csv)
Write-Host ("dt={0:F5}s bandThr(y<={1:F3})" -f $dt, $bandThr)
Write-Host ""
Write-Host ("{0,-5} {1,-13} {2,-7} {3,-9} {4,-10} {5,-10} {6,-10} {7,-24}" -f 'foot','yMin/yMax','f@min','run[a,b]','platLen','platMinSp','platMedSp','riseFrames(1mm,1cm,3cm)')

foreach ($g in ($rows | Group-Object foot)) {
    $pts = $g.Group | Sort-Object { [int] $_.frame }
    $n = $pts.Count
    $ys = @(); $xs = @(); $zs = @()
    for ($i = 0; $i -lt $n; $i++) {
        $ys += [double] $pts[$i].y
        $xs += [double] $pts[$i].relX
        $zs += [double] $pts[$i].relZ
    }
    $ymin = [double]::MaxValue; $ymax = [double]::MinValue; $fmin = 0
    for ($i = 0; $i -lt $n; $i++) {
        if ($ys[$i] -lt $ymin) { $ymin = $ys[$i]; $fmin = $i }
        if ($ys[$i] -gt $ymax) { $ymax = $ys[$i] }
    }
    $head = "{0,-5} {1,-13} {2,-7}" -f ([int] $pts[0].foot), ("{0:F3}/{1:F3}" -f $ymin, $ymax), $fmin

    # contiguous runs of y <= bandThr
    $s = -1; $firstRun = $true
    for ($i = 0; $i -le $n; $i++) {
        $inBand = $i -lt $n -and $ys[$i] -le $bandThr
        if ($inBand) { if ($s -lt 0) { $s = $i } }
        elseif ($s -ge 0) {
            $a = $s; $b = $i - 1; $s = -1
            if ($b - $a -lt 4) { continue }
            # plateau = frames within 3cm of the run's own min
            $runMin = [double]::MaxValue; $runArg = $a
            for ($j = $a; $j -le $b; $j++) { if ($ys[$j] -lt $runMin) { $runMin = $ys[$j]; $runArg = $j } }
            $platThr = $runMin + 0.03
            $p0 = -1; $p1 = -1
            for ($j = $a; $j -le $b; $j++) { if ($ys[$j] -le $platThr) { if ($p0 -lt 0) { $p0 = $j }; $p1 = $j } }
            # speeds inside plateau
            $spd = New-Object System.Collections.Generic.List[double]
            for ($j = $p0 + 1; $j -le $p1; $j++) {
                $ddx = $xs[$j] - $xs[$j - 1]; $ddz = $zs[$j] - $zs[$j - 1]
                $spd.Add([Math]::Sqrt($ddx * $ddx + $ddz * $ddz) / $dt)
            }
            $pmin = -1.0; $pmed = -1.0
            if ($spd.Count -gt 0) {
                $pmin = ($spd | Measure-Object -Minimum).Minimum
                $srt = $spd | Sort-Object
                $pmed = $srt[[int] ($srt.Count / 2)]
            }
            # rise frames after argmin within the run
            $r1 = -1; $r2 = -1; $r3 = -1
            for ($j = $runArg + 1; $j -le $b; $j++) {
                $delta = $ys[$j] - $runMin
                if ($r1 -lt 0 -and $delta -gt 0.001) { $r1 = $j - $runArg }
                if ($r2 -lt 0 -and $delta -gt 0.01) { $r2 = $j - $runArg }
                if ($r3 -lt 0 -and $delta -gt 0.03) { $r3 = $j - $runArg }
            }
            $label = if ($firstRun) { $head } else { "" }
            $firstRun = $false
            Write-Host ("{0,-5} {1,-13} {2,-7} {3,-9} {4,-10} {5,-10} {6,-10} {7,-24}" -f `
                $label, "", "", ("[$a,$b]"), ("{0}([{1},{2}])" -f ($p1 - $p0 + 1), $p0, $p1), ("{0:F3}" -f $pmin), ("{0:F3}" -f $pmed), ("{0},{1},{2}" -f $r1, $r2, $r3))
        }
    }
    if ($firstRun) { Write-Host $head }   # no band runs at all
}
