# Search the clip for the best loop window [s, s+L] whose start/end poses match
# closely (minimum per-bone position mismatch), i.e. the place where looping the
# clip produces the smallest phase pop. Probe set = representative bones.
# Usage: powershell -NoProfile -File search_loop_window.ps1 [-Clip walk_slow_loop]
param([string]$Clip = 'walk_slow_loop')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$csv = (Get-ChildItem (Join-Path $repo ('Logs\footprobe_*' + $Clip + '.csv'))).FullName
$rows = Import-Csv -Path $csv

# per-foot arrays indexed by frame
$boneX = @{}; $boneZ = @{}; $boneY = @{}; $nFrames = 0
foreach ($g in ($rows | Group-Object foot)) {
    $f = [int] $g.Name
    $pts = $g.Group | Sort-Object { [int] $_.frame }
    $n = $pts.Count
    if ($n -gt $nFrames) { $nFrames = $n }
    $rx = New-Object 'double[]' $n
    $rz = New-Object 'double[]' $n
    $yy = New-Object 'double[]' $n
    for ($i = 0; $i -lt $n; $i++) {
        $rx[$i] = [double] $pts[$i].relX
        $rz[$i] = [double] $pts[$i].relZ
        $yy[$i] = [double] $pts[$i].y
    }
    $boneX[$f] = $rx; $boneZ[$f] = $rz; $boneY[$f] = $yy
}
$allFeet = @($boneX.Keys | Sort-Object)
$probe = @(0, 1, 5, 8, 9, 10, 11)
Write-Host ("frames={0} bones={1}" -f $nFrames, $allFeet.Count)

$results = New-Object System.Collections.Generic.List[object]
for ($L = 156; $L -le 178; $L++) {
    for ($s = 0; $s -le ($nFrames - 1 - $L); $s++) {
        $score = 0.0; $worst = 0.0
        foreach ($f in $probe) {
            $e = $s + $L
            $dx = $boneX[$f][$e] - $boneX[$f][$s]
            $dz = $boneZ[$f][$e] - $boneZ[$f][$s]
            $d = [Math]::Sqrt($dx * $dx + $dz * $dz)
            $score += $d
            if ($d -gt $worst) { $worst = $d }
        }
        $results.Add([object[]]@($score, $worst, $s, $L))
    }
}
$top = $results | Sort-Object { $_[0] } | Select-Object -First 12
Write-Host ""
Write-Host ("{0,-4} {1,-4} {2,-9} {3,-9} {4,-6} {5,-6}" -f 's', 'L', 'scoreSum', 'worstBone', 'tS', 'tL')
foreach ($r in $top) {
    Write-Host ("{0,-4} {1,-4} {2,-9:F4} {3,-9:F4} {4,-6:F3} {5,-6:F3}" -f ([int] $r[2]), ([int] $r[3]), $r[0], $r[1], ($r[2] * 0.016616), ($r[3] * 0.016616))
}

# full-bone detail for the winner
$w = $top[0]
$ws = [int] $w[2]; $wl = [int] $w[3]
Write-Host ""
Write-Host ("winner s={0} L={1} detail:" -f $ws, $wl)
foreach ($f in $allFeet) {
    $e = $ws + $wl
    $dx = $boneX[$f][$e] - $boneX[$f][$ws]
    $dz = $boneZ[$f][$e] - $boneZ[$f][$ws]
    $d = [Math]::Sqrt($dx * $dx + $dz * $dz)
    $dy = $boneY[$f][$e] - $boneY[$f][$ws]
    Write-Host ("foot {0,2}: dRel={1,6:F4} dY={2,7:F4}" -f $f, $d, $dy)
}
