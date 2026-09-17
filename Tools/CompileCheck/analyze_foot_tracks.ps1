# Offline analysis of footprobe CSV: measure true stance speed from raw tracks.
# Usage: powershell -NoProfile -File analyze_foot_tracks.ps1 [-Clip walk_slow_loop]
param([string]$Clip = 'walk_slow_loop')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$csv = (Get-ChildItem (Join-Path $repo ('Logs\footprobe_*' + $Clip + '.csv'))).FullName
$rows = Import-Csv -Path $csv
$groups = $rows | Group-Object foot
Write-Host ("file={0}" -f $csv)
Write-Host ("feet={0} rows={1}" -f $groups.Count, $rows.Count)

# dt from the CSV itself: time column step of shot 1..2
$first = $rows | Where-Object { [int] $_.foot -eq 0 -and [int] $_.frame -le 1 } | Sort-Object { [int] $_.frame }
$dt = [double] $first[1].time - [double] $first[0].time
$clipLen = [double] $first[1].time * ($groups[0].Count - 1)
Write-Host ("dt={0:F5}s clipLen={1:F3}s" -f $dt, $clipLen)

foreach ($g in $groups) {
    $pts = $g.Group | Sort-Object { [int] $_.frame }
    $n = $pts.Count - 1
    $ys = @(); $xs = @(); $zs = @(); $ins = @()
    for ($i = 0; $i -lt $n; $i++) {
        $ys += [double] $pts[$i].y
        $xs += [double] $pts[$i].relX
        $zs += [double] $pts[$i].relZ
        $ins += [int] $pts[$i].inStance
    }
    $ymin = ($ys | Measure-Object -Minimum).Minimum
    $ymax = ($ys | Measure-Object -Maximum).Maximum
    $yband = $ymin + [Math]::Max(($ymax - $ymin) * 0.12, 0.03)
    $lowCount = 0
    $spd = New-Object System.Collections.Generic.List[double]
    for ($i = 1; $i -lt $n; $i++) {
        if ($ys[$i] -le $yband -and $ys[$i - 1] -le $yband) {
            $dx = $xs[$i] - $xs[$i - 1]
            $dz = $zs[$i] - $zs[$i - 1]
            $spd.Add([Math]::Sqrt($dx * $dx + $dz * $dz) / $dt)
        }
    }
    for ($i = 0; $i -lt $n; $i++) {
        if ($ys[$i] -le $yband) { $lowCount++ }
    }
    $insCount = 0
    for ($i = 0; $i -lt $n; $i++) { $insCount += $ins[$i] }
    $srt = $spd | Sort-Object
    $med = -1.0
    if ($srt.Count -gt 0) { $med = $srt[[int] ($srt.Count / 2)] }

    # longest consecutive run inside the low band
    $bestStart = -1; $bestLen = 0; $curStart = -1; $curLen = 0
    for ($i = 0; $i -lt $n; $i++) {
        if ($ys[$i] -le $yband) {
            if ($curStart -lt 0) { $curStart = $i; $curLen = 1 } else { $curLen++ }
            if ($curLen -gt $bestLen) { $bestLen = $curLen; $bestStart = $curStart }
        }
        else { $curStart = -1; $curLen = 0 }
    }
    $runInfo = "run=n/a"
    if ($bestLen -gt 1) {
        $dxb = $xs[$bestStart + $bestLen - 1] - $xs[$bestStart]
        $dzb = $zs[$bestStart + $bestLen - 1] - $zs[$bestStart]
        $chord = [Math]::Sqrt($dxb * $dxb + $dzb * $dzb)
        $runInfo = ("run[{0}..{1}] len={2} chord={3:F3} chordSpd={4:F3}" -f $bestStart, ($bestStart + $bestLen - 1), $bestLen, $chord, ($chord / ($bestLen * $dt)))
    }
    # band sensitivity: frame-pair speed median inside low band at 12/15/20 percent of y-range (+3cm floor)
    $bandMed = @()
    foreach ($k in 0.12, 0.15, 0.20) {
        $bandK = $ymin + [Math]::Max(($ymax - $ymin) * $k, 0.03)
        $spdK = New-Object System.Collections.Generic.List[double]
        for ($i = 1; $i -lt $n; $i++) {
            if ($ys[$i] -le $bandK -and $ys[$i - 1] -le $bandK) {
                $dxk = $xs[$i] - $xs[$i - 1]
                $dzk = $zs[$i] - $zs[$i - 1]
                $spdK.Add([Math]::Sqrt($dxk * $dxk + $dzk * $dzk) / $dt)
            }
        }
        $srtK = $spdK | Sort-Object
        if ($srtK.Count -gt 0) { $bandMed += $srtK[[int] ($srtK.Count / 2)] } else { $bandMed += -1.0 }
    }
    Write-Host ("foot {0}: n={1} y[{2:F3},{3:F3}] lowFrac={4:F3} lowSpdMed={5:F3} insFrac={6:F3} {7}" -f $g.Name, $n, $ymin, $ymax, ($lowCount / $n), $med, ($insCount / $n), $runInfo)
    Write-Host ("    bandMed 12/15/20%: {0:F3} {1:F3} {2:F3}" -f $bandMed[0], $bandMed[1], $bandMed[2])
}

# frame-by-frame detail for the first foot (every 4th frame)
$g0 = $groups[0]
$pts = $g0.Group | Sort-Object { [int] $_.frame }
Write-Host ""
Write-Host ("--- detail foot {0} ---" -f $g0.Name)
for ($i = 0; $i -lt $pts.Count; $i++) {
    if ($i % 4 -eq 0 -or $i -eq $pts.Count - 1) {
        $p = $pts[$i]
        $sp = -1.0
        if ($i -gt 0) {
            $dx = [double] $p.relX - [double] $pts[$i - 1].relX
            $dz = [double] $p.relZ - [double] $pts[$i - 1].relZ
            $sp = [Math]::Sqrt($dx * $dx + $dz * $dz) / $dt
        }
        Write-Host ("f={0,3} y={1:F3} x={2,7:F3} z={3,7:F3} sp={4:F3} in={5}" -f [int] $p.frame, [double] $p.y, [double] $p.relX, [double] $p.relZ, $sp, $p.inStance)
    }
}
