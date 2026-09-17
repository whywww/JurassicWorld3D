# seam_scan2.ps1 - corrected crop C analysis + focused per-row seam trajectory scan
# Window of interest: x=112..136 (suspected thin bright vertical line around x=126-127)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$srcPath = 'C:\Users\qqq\AppData\Roaming\QoderCN\SharedClientCache\cache\images\task-ff5\eatlif50-17375436.png'
$img = [System.Drawing.Image]::FromFile($srcPath)
$W = $img.Width; $H = $img.Height
$work = [System.Drawing.Bitmap]::new($W, $H, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($work); $g.DrawImageUnscaled($img, 0, 0); $g.Dispose()
$rect = [System.Drawing.Rectangle]::new(0, 0, $W, $H)
$data = $work.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$stride = $data.Stride
$px = [byte[]]::new($stride * $H)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $px, 0, $px.Length)
$work.UnlockBits($data)

function D-At {
    param([int]$x, [int]$y)
    $i1 = $y * $stride + $x * 3
    $i0 = $y * $stride + ($x - 1) * 3
    return ([math]::Abs([int]$px[$i1] - [int]$px[$i0]) + [math]::Abs([int]$px[$i1 + 1] - [int]$px[$i0 + 1]) + [math]::Abs([int]$px[$i1 + 2] - [int]$px[$i0 + 2])) / 3.0
}

# ---- corrected region analysis (safe variable names) ----
function Analyze2 {
    param([int]$rx0, [int]$ry0, [int]$rcw, [int]$rch, [string]$label)
    $rx1 = $rx0 + $rcw - 1
    Write-Output ("==== {0} ==== region x {1}..{2}, y {3}..{4}, {5} rows" -f $label, $rx0, $rx1, $ry0, ($ry0 + $rch - 1), $rch)
    $cd = @{}
    for ($x = $rx0 + 1; $x -le $rx1; $x++) {
        $s = 0.0
        for ($y = $ry0; $y -lt ($ry0 + $rch); $y++) { $s += (D-At $x $y) }
        $cd[$x] = $s / $rch
    }
    $srt = $cd.GetEnumerator() | Sort-Object Value -Descending
    Write-Output 'Top adjacent-column mean |dRGB| (global x : value | local x):'
    for ($k = 0; $k -lt 10 -and $k -lt $srt.Count; $k++) {
        $e = $srt[$k]
        Write-Output ("   x={0,4} : {1,6:N2} | local {2}" -f $e.Key, $e.Value, ($e.Key - $rx0))
    }
    $sd = [int]$srt[0].Key
    Write-Output ("   -> strongest: global x={0}, mean diff={1:N2}" -f $sd, [double]$srt[0].Value)
    $hist = @{}
    for ($y = $ry0; $y -lt ($ry0 + $rch); $y++) {
        $b = -1.0; $bx = -1
        for ($x = $rx0 + 1; $x -le $rx1; $x++) { $d = D-At $x $y; if ($d -gt $b) { $b = $d; $bx = $x } }
        if ($hist.ContainsKey($bx)) { $hist[$bx]++ } else { $hist[$bx] = 1 }
    }
    Write-Output 'Per-row argmax histogram (top 6):'
    $hist.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 6 | ForEach-Object { Write-Output ("   x={0,4} : {1} rows" -f $_.Key, $_.Value) }
    $run = 0; $br = 0; $curStart = -1; $bestS = -1; $be = -1; $prev = -999; $cntE = 0
    for ($y = $ry0; $y -lt ($ry0 + $rch); $y++) {
        $b = -1.0
        for ($x = $rx0 + 1; $x -le $rx1; $x++) { $d = D-At $x $y; if ($d -gt $b) { $b = $d } }
        $s = 0.0
        foreach ($o in -1, 0, 1) {
            $xx = $sd + $o
            if ($xx -gt $rx0 -and $xx -le $rx1) { $d = D-At $xx $y; if ($d -gt $s) { $s = $d } }
        }
        if (($b -gt 0) -and ($s -ge 0.6 * $b) -and ($s -ge 20)) {
            $cntE++
            if ($run -gt 0 -and $y -eq $prev + 1) { $run++ } else { $run = 1; $curStart = $y }
            if ($run -gt $br) { $br = $run; $bestS = $curStart; $be = $y }
            $prev = $y
        } else { $run = 0 }
    }
    Write-Output ("Rows with seed edge present (>=60% of row max, >=20 abs): {0} of {1}" -f $cntE, $rch)
    if ($br -gt 0) { Write-Output ("Longest consecutive run: {0} rows, y {1}..{2}" -f $br, $bestS, $be) }
    Write-Output ''
}

Analyze2 -rx0 0 -ry0 0 -rcw 93 -rch 182 -label 'CROP C corrected (left control, x0-50% y0-70%)'
Analyze2 -rx0 93 -ry0 0 -rcw 94 -rch 182 -label 'CROP A recheck (right-top, x50-100% y0-70%)'

# ---- focused seam trajectory scan ----
Write-Output '==== FOCUSED SEAM SCAN: window x=112..136, all rows y=0..260 ===='
$rws = @()
for ($y = 0; $y -lt $H; $y++) {
    $s3 = 0.0; $p3 = -1
    for ($x = 125; $x -le 130; $x++) { $d = D-At $x $y; if ($d -gt $s3) { $s3 = $d; $p3 = $x } }
    $sw = 0.0; $svx = -1
    for ($x = 112; $x -le 136; $x++) { $d = D-At $x $y; if ($d -gt $sw) { $sw = $d; $svx = $x } }
    $rws += [pscustomobject]@{ y = $y; p3 = $p3; s3 = $s3; sw = $sw; svx = $svx }
}
$c5 = @($rws | Where-Object { $_.s3 -ge 5 })
Write-Output ("Rows with s3 (max diff at x=125..130) >= 5 : {0} of {1}" -f $c5.Count, $H)
Write-Output 'Seam-position p3 distribution (rows with s3>=5):'
$c5 | Group-Object p3 | Sort-Object Count -Descending | ForEach-Object { Write-Output ("   x={0} : {1} rows" -f $_.Name, $_.Count) }
$runs = @(); $run = 0; $rs = -1; $prev = -999
foreach ($r in $rws) {
    if ($r.s3 -ge 5) {
        if ($run -gt 0 -and $r.y -eq $prev + 1) { $run++ } else { if ($run -gt 0) { $runs += ("y{0}..{1}({2})" -f $rs, $prev, $run) }; $run = 1; $rs = $r.y }
        $prev = $r.y
    } else {
        if ($run -gt 0) { $runs += ("y{0}..{1}({2})" -f $rs, $prev, $run); $run = 0 }
    }
}
if ($run -gt 0) { $runs += ("y{0}..{1}({2})" -f $rs, $prev, $run) }
Write-Output ("Runs with s3>=5 : " + ($runs -join ', '))
Write-Output 'Trajectory (every 5 rows): y | seam pos p3 (s3) | strongest-in-window x (value)'
for ($y = 0; $y -lt $H; $y += 5) {
    $r = $rws[$y]
    Write-Output ("   y={0,3} | x={1,4} s={2,6:N1} | strong x={3,4} v={4,6:N1}" -f $r.y, $r.p3, $r.s3, $r.svx, $r.sw)
}
Write-Output 'Buckets of 20 rows: #rows s3>=5 | median p3 | mean s3 | mean window-strong v'
$b0 = 0
while ($b0 -lt $H) {
    $b1 = [int][math]::Min($b0 + 20, $H)
    $sub = @($rws | Where-Object { $_.y -ge $b0 -and $_.y -lt $b1 })
    $su = @($sub | Where-Object { $_.s3 -ge 5 })
    $medp = -1
    if ($su.Count -gt 0) { $pl = @($su | ForEach-Object { $_.p3 } | Sort-Object); $medp = $pl[[int]($pl.Count / 2)] }
    $meanS = ($sub | Measure-Object s3 -Average).Average
    $meanW = ($sub | Measure-Object sw -Average).Average
    Write-Output ("   y {0,3}..{1,3}: {2,2} rows | median pos x={3} | mean s3={4,6:N1} | mean strong v={5,6:N1}" -f $b0, ($b1 - 1), $su.Count, $medp, $meanS, $meanW)
    $b0 = $b1
}
foreach ($ys in 5, 20, 40, 60, 90, 120, 150, 170, 200, 230) {
    Write-Output ("Row y={0}: x=120..134 RGB and adjacent-diff d(x):" -f $ys)
    $line1 = '   x:  '; $line2 = '   RGB:'; $line3 = '   d:  '
    for ($x = 120; $x -le 134; $x++) {
        $i = $ys * $stride + $x * 3
        $line1 += ("{0,4}" -f $x)
        $line2 += (' {0:X2}{1:X2}{2:X2}' -f $px[$i + 2], $px[$i + 1], $px[$i])
        $line3 += ("{0,4:N0}" -f (D-At $x $ys))
    }
    Write-Output $line1
    Write-Output $line2
    Write-Output $line3
}
$img.Dispose()
Write-Output 'DONE'
