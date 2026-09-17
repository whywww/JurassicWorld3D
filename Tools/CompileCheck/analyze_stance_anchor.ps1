# Offline simulation of the runtime foot-lock cycle on the footprobe CSV:
#   plant  = "lowest point" of each low-band run (Y no longer makes a new low)
#   release= first frame whose Y rises more than +1mm vs the previous frame (releaseOnFootRise)
# Measures the HORIZONTAL displacement between the anchor and the animation position at
# release time (= the visible pop when the lock lets go), per foot bone and per stance run.
# Also reports in-run frame-pair speed spread (P10/P50/P90).
# Usage: powershell -NoProfile -File analyze_stance_anchor.ps1 [-Clip walk_slow_loop]
param([string]$Clip = 'walk_slow_loop')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$csv = (Get-ChildItem (Join-Path $repo ('Logs\footprobe_*' + $Clip + '.csv'))).FullName
$rows = Import-Csv -Path $csv
Write-Host ("file={0}" -f $csv)

$first = $rows | Where-Object { [int] $_.foot -eq 0 -and [int] $_.frame -le 1 } | Sort-Object { [int] $_.frame }
$dt = [double] $first[1].time - [double] $first[0].time
Write-Host ("dt={0:F5}s" -f $dt)

# Collective minimum-Y ridge: which bone owns the lowest Y per frame (runtime lowestFootIndex proxy)
$byFrame = @{}
foreach ($r in $rows) {
    $f = [int] $r.frame
    if (-not $byFrame.ContainsKey($f)) { $byFrame[$f] = New-Object System.Collections.Generic.List[object] }
    $byFrame[$f].Add($r)
}
$frames = $byFrame.Keys | Sort-Object
$ridgeOwner = @()
foreach ($f in $frames) {
    $bestY = [double]::MaxValue; $bestFoot = -1
    foreach ($r in $byFrame[$f]) {
        $y = [double] $r.y
        if ($y -lt $bestY) { $bestY = $y; $bestFoot = [int] $r.foot }
    }
    $ridgeOwner += ,@($f, $bestFoot, $bestY)
}
$switches = 0
for ($i = 1; $i -lt $ridgeOwner.Count; $i++) {
    # count switches that are not left/right alternation but same-side jitter is not knowable here;
    # just report raw switch rate on the min-ridge owner.
    if ($ridgeOwner[$i][1] -ne $ridgeOwner[$i - 1][1]) { $switches++ }
}
Write-Host ("minRidge: frames={0} ownerSwitches={1} ({2:P1})" -f $ridgeOwner.Count, $switches, ($switches / [double] $ridgeOwner.Count))

$groups = $rows | Group-Object foot
Write-Host ""
Write-Host ("{0,-5} {1,-7} {2,-7} {3,-7} {4,-10} {5,-10} {6,-9} {7,-9} {8,-9} {9,-10} {10,-9} {11,-9}" -f 'foot','runSt','runEnd','anchorF','popM','runM','spdP10','spdP50','spdP90','spdNet','dtAR','popWorld')
foreach ($g in $groups) {
    $pts = $g.Group | Sort-Object { [int] $_.frame }
    $n = $pts.Count
    $ys = @(); $xs = @(); $zs = @(); $ins = @()
    for ($i = 0; $i -lt $n; $i++) {
        $ys += [double] $pts[$i].y
        $xs += [double] $pts[$i].relX
        $zs += [double] $pts[$i].relZ
        $ins += [int] $pts[$i].inStance
    }
    # stance runs from the inStance flag
    $runList = New-Object System.Collections.Generic.List[object]
    $s = -1
    for ($i = 0; $i -lt $n; $i++) {
        if ($ins[$i] -eq 1) { if ($s -lt 0) { $s = $i } }
        elseif ($s -ge 0) { $runList.Add([object[]]@($s, ($i - 1))); $s = -1 }
    }
    if ($s -ge 0) { $runList.Add([object[]]@($s, ($n - 1))) }

    foreach ($run in $runList) {
        $a = $run[0]; $b = $run[1]
        if ($b - $a -lt 4) { continue }
        # anchor = argmin Y inside the run
        $anchorF = $a; $anchorY = $ys[$a]
        for ($i = $a + 1; $i -le $b; $i++) { if ($ys[$i] -lt $anchorY) { $anchorY = $ys[$i]; $anchorF = $i } }
        # release = first frame after anchor whose Y rises > +1mm vs previous frame (also allow a small grace window: search whole tail)
        $releaseF = $b
        for ($i = $anchorF + 1; $i -le $b; $i++) {
            if ($ys[$i] - $ys[$i - 1] -gt 0.001) { $releaseF = $i; break }
        }
        # pop = horizontal distance between anchor sample and release sample
        $dx = $xs[$releaseF] - $xs[$anchorF]
        $dz = $zs[$releaseF] - $zs[$anchorF]
        $pop = [Math]::Sqrt($dx * $dx + $dz * $dz)
        # run horizontal extent (same metric from run start to end) for scale
        $dxr = $xs[$b] - $xs[$a]
        $dzr = $zs[$b] - $zs[$a]
        $runM = [Math]::Sqrt($dxr * $dxr + $dzr * $dzr)
        # world-frame pop: add the root displacement along the travel direction.
        # rel runs from front to back, so travel forward = -(run direction).
        $dtAR = ($releaseF - $anchorF) * $dt
        $popWorld = -1.0
        if ($runM -gt 0.1) {
            $fwdX = -$dxr / $runM
            $fwdZ = -$dzr / $runM
            $wdx = $dx + $fwdX * 2.349 * $dtAR
            $wdz = $dz + $fwdZ * 2.349 * $dtAR
            $popWorld = [Math]::Sqrt($wdx * $wdx + $wdz * $wdz)
        }
        # frame-pair speeds inside the run
        $spd = New-Object System.Collections.Generic.List[double]
        for ($i = $a + 1; $i -le $b; $i++) {
            $ddx = $xs[$i] - $xs[$i - 1]
            $ddz = $zs[$i] - $zs[$i - 1]
            $spd.Add([Math]::Sqrt($ddx * $ddx + $ddz * $ddz) / $dt)
        }
        $srt = $spd | Sort-Object
        $p10 = $srt[[int] ($srt.Count * 0.1)]
        $p50 = $srt[[int] ($srt.Count * 0.5)]
        $p90 = $srt[[Math]::Min($srt.Count - 1, [int] ($srt.Count * 0.9))]
        $spdNet = $runM / (($b - $a) * $dt)
        Write-Host ("{0,-5} {1,-7} {2,-7} {3,-7} {4,-10:F3} {5,-10:F3} {6,-9:F3} {7,-9:F3} {8,-9:F3} {9,-10:F3} {10,-9:F3} {11,-9:F3}" -f `
            ([int] $pts[0].foot), $a, $b, $anchorF, $pop, $runM, $p10, $p50, $p90, $spdNet, $dtAR, $popWorld)
    }
}
