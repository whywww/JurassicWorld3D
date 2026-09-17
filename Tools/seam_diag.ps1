# seam_diag.ps1 - diagnostic crops + ASCII classification + vertical edge analysis
# Input : 187x261 gameplay screenshot (PNG)
# Output: seam_cropA/B/C.png, seam_full3x.png, seam_line8x.png (bonus zoom)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$srcPath = 'C:\Users\qqq\AppData\Roaming\QoderCN\SharedClientCache\cache\images\task-ff5\eatlif50-17375436.png'
$outDir  = 'C:\Users\qqq\AppData\Local\Temp'

if (-not (Test-Path -LiteralPath $srcPath)) { Write-Output 'ERROR: source image not found'; exit 1 }
if (-not (Test-Path -LiteralPath $outDir))  { New-Item -ItemType Directory -Path $outDir | Out-Null }

$img = [System.Drawing.Image]::FromFile($srcPath)
$W = $img.Width
$H = $img.Height
Write-Output ("SOURCE : {0}" -f $srcPath)
Write-Output ("SIZE   : {0} x {1}" -f $W, $H)
Write-Output ''

# --- 24bpp working copy + locked bytes ---
$work = [System.Drawing.Bitmap]::new($W, $H, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$gfx  = [System.Drawing.Graphics]::FromImage($work)
$gfx.DrawImageUnscaled($img, 0, 0)
$gfx.Dispose()

$rect   = [System.Drawing.Rectangle]::new(0, 0, $W, $H)
$data   = $work.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$stride = $data.Stride
$bytes  = [byte[]]::new($stride * $H)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
$work.UnlockBits($data)

# --- crop + nearest-neighbor integer scale + save PNG ---
function Save-CropScale {
    param([int]$x0, [int]$y0, [int]$cw, [int]$ch, [int]$scale, [string]$path)
    $dw = $cw * $scale
    $dh = $ch * $scale
    $dst   = [System.Drawing.Bitmap]::new($dw, $dh, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $drect = [System.Drawing.Rectangle]::new(0, 0, $dw, $dh)
    $ddata = $dst.LockBits($drect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $dstride = $ddata.Stride
    $dbytes  = [byte[]]::new($dstride * $dh)
    $dx3 = [int[]]::new($dw)
    for ($dx = 0; $dx -lt $dw; $dx++) { $dx3[$dx] = ($x0 + [int][math]::Floor($dx / $scale)) * 3 }
    $buf = [byte[]]::new($dw * 3)
    for ($sy = 0; $sy -lt $ch; $sy++) {
        $sRow = ($y0 + $sy) * $stride
        for ($dx = 0; $dx -lt $dw; $dx++) {
            $si = $sRow + $dx3[$dx]
            $bi = $dx * 3
            $buf[$bi]     = $bytes[$si]
            $buf[$bi + 1] = $bytes[$si + 1]
            $buf[$bi + 2] = $bytes[$si + 2]
        }
        for ($k = 0; $k -lt $scale; $k++) {
            $dy = $sy * $scale + $k
            [System.Array]::Copy($buf, 0, $dbytes, $dy * $dstride, $buf.Length)
        }
    }
    [System.Runtime.InteropServices.Marshal]::Copy($dbytes, 0, $ddata.Scan0, $dbytes.Length)
    $dst.UnlockBits($ddata)
    $dst.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $dst.Dispose()
    Write-Output ("SAVED {0}  ({1}x{2}px, src rect x{3}..{4} y{5}..{6}, scale {7}x nearest-neighbor)" -f $path, $dw, $dh, $x0, ($x0 + $cw - 1), $y0, ($y0 + $ch - 1), $scale)
}

# --- crop rectangles (percent -> pixels, floor) ---
$Ax0 = [int][math]::Floor($W * 0.50); $Ay0 = 0
$Acw = $W - $Ax0; $Ach = [int][math]::Floor($H * 0.70)
$Bx0 = [int][math]::Floor($W * 0.55); $By0 = [int][math]::Floor($H * 0.15)
$Bcw = [int][math]::Floor($W * 0.85) - $Bx0; $Bch = [int][math]::Floor($H * 0.65) - $By0
$Cx0 = 0; $Cy0 = 0
$Ccw = [int][math]::Floor($W * 0.50); $Cch = [int][math]::Floor($H * 0.70)

Write-Output ("Rect A : x {0}..{1}  y {2}..{3}  ({4}x{5})" -f $Ax0, ($Ax0 + $Acw - 1), $Ay0, ($Ay0 + $Ach - 1), $Acw, $Ach)
Write-Output ("Rect B : x {0}..{1}  y {2}..{3}  ({4}x{5})" -f $Bx0, ($Bx0 + $Bcw - 1), $By0, ($By0 + $Bch - 1), $Bcw, $Bch)
Write-Output ("Rect C : x {0}..{1}  y {2}..{3}  ({4}x{5})" -f $Cx0, ($Cx0 + $Ccw - 1), $Cy0, ($Cy0 + $Cch - 1), $Ccw, $Cch)
Write-Output ''

Save-CropScale -x0 $Ax0 -y0 $Ay0 -cw $Acw -ch $Ach -scale 4 -path (Join-Path $outDir 'seam_cropA.png')
Save-CropScale -x0 $Bx0 -y0 $By0 -cw $Bcw -ch $Bch -scale 4 -path (Join-Path $outDir 'seam_cropB.png')
Save-CropScale -x0 $Cx0 -y0 $Cy0 -cw $Ccw -ch $Cch -scale 4 -path (Join-Path $outDir 'seam_cropC.png')
Save-CropScale -x0 0 -y0 0 -cw $W -ch $H -scale 3 -path (Join-Path $outDir 'seam_full3x.png')
Write-Output ''

# --- pixel classification for ASCII map ---
function Classify-Px {
    param([double]$R, [double]$G, [double]$Bv)
    $lum = ($R + $G + $Bv) / 3.0
    if (($R -lt 160) -and ($Bv -gt 200) -and (($Bv - $R) -gt 40)) { return 'B' }
    if (($lum -gt 190) -and ($R -gt 180) -and (($Bv - $R) -lt 30)) { return 'C' }
    if (($lum -gt 170) -and ($R -gt $Bv)) { return 'F' }
    if ($lum -lt 120) { return 'T' }
    if (($G -ge $R) -and ($G -ge $Bv) -and ($lum -lt 150)) { return 'T' }
    return '.'
}

$COLS = 25; $ROWS = 20
$map = @(); $hexLines = @()
for ($r = 0; $r -lt $ROWS; $r++) {
    $y0c = [int][math]::Floor($r * $H / $ROWS)
    $y1c = [int][math]::Floor(($r + 1) * $H / $ROWS)
    $cy0 = [int][math]::Floor($y0c + ($y1c - $y0c) * 0.2)
    $cy1 = [int][math]::Ceiling($y1c - ($y1c - $y0c) * 0.2)
    if ($cy1 -le $cy0) { $cy1 = $cy0 + 1 }
    $rowChars = [System.Text.StringBuilder]::new()
    $hexRow   = [System.Text.StringBuilder]::new()
    for ($c = 0; $c -lt $COLS; $c++) {
        $x0c = [int][math]::Floor($c * $W / $COLS)
        $x1c = [int][math]::Floor(($c + 1) * $W / $COLS)
        $cx0 = [int][math]::Floor($x0c + ($x1c - $x0c) * 0.2)
        $cx1 = [int][math]::Ceiling($x1c - ($x1c - $x0c) * 0.2)
        if ($cx1 -le $cx0) { $cx1 = $cx0 + 1 }
        $sr = 0.0; $sg = 0.0; $sb = 0.0; $n = 0
        for ($yy = $cy0; $yy -lt $cy1; $yy++) {
            $rowBase = $yy * $stride
            for ($xx = $cx0; $xx -lt $cx1; $xx++) {
                $ii = $rowBase + $xx * 3
                $sb += $bytes[$ii]; $sg += $bytes[$ii + 1]; $sr += $bytes[$ii + 2]
                $n++
            }
        }
        $RR = $sr / $n; $GG = $sg / $n; $BB = $sb / $n
        [void]$rowChars.Append((Classify-Px $RR $GG $BB))
        [void]$hexRow.Append(('{0:X2}{1:X2}{2:X2} ' -f ([int]$RR), ([int]$GG), ([int]$BB)))
    }
    $map += $rowChars.ToString()
    $hexLines += $hexRow.ToString()
}

Write-Output ("ASCII MAP  {0} cols x {1} rows  (cell = avg RGB of central ~60% area)" -f $COLS, $ROWS)
Write-Output "Legend: B=blue sky  C=bright cloud  F=haze/beige band  T=dark tree  .=intermediate"
Write-Output ("      " + ("0123456789" * 3).Substring(0, $COLS))
for ($r = 0; $r -lt $ROWS; $r++) {
    Write-Output (" r{0:D2}| {1}" -f $r, $map[$r])
}
$counts = @{}
foreach ($r in $map) { foreach ($chx in $r.ToCharArray()) { $s = [string]$chx; if ($counts.ContainsKey($s)) { $counts[$s]++ } else { $counts[$s] = 1 } } }
Write-Output ("Class counts: " + (($counts.GetEnumerator() | Sort-Object Name | ForEach-Object { '{0}={1}' -f $_.Key, $_.Value }) -join '  '))
Write-Output ''
Write-Output "Per-cell average hex RGB (row label | 25 cells, each RRGGBB):"
for ($r = 0; $r -lt $ROWS; $r++) {
    Write-Output (" r{0:D2}| {1}" -f $r, $hexLines[$r].TrimEnd())
}
Write-Output ''

# --- vertical edge analysis on a region ---
function Analyze-Vertical {
    param([int]$x0, [int]$y0, [int]$cw, [int]$ch, [string]$label)
    $x1 = $x0 + $cw - 1
    Write-Output ("==== {0} ====" -f $label)
    Write-Output ("Region: x {0}..{1} (crop-local 0..{2}), y {3}..{4}, {5} rows" -f $x0, $x1, ($cw - 1), $y0, ($y0 + $ch - 1), $ch)

    # column-mean adjacent diff, keyed by global x (edge between x-1 and x)
    $colDiff = @{}
    for ($x = $x0 + 1; $x -le $x1; $x++) {
        $s = 0.0
        for ($y = $y0; $y -lt ($y0 + $ch); $y++) {
            $i1 = $y * $stride + $x * 3
            $i0 = $y * $stride + ($x - 1) * 3
            $s += ([math]::Abs($bytes[$i1] - $bytes[$i0]) + [math]::Abs($bytes[$i1 + 1] - $bytes[$i0 + 1]) + [math]::Abs($bytes[$i1 + 2] - $bytes[$i0 + 2])) / 3.0
        }
        $colDiff[$x] = $s / $ch
    }
    $sorted = $colDiff.GetEnumerator() | Sort-Object Value -Descending
    Write-Output "Top adjacent-column mean |dRGB| (global x : value | crop-local x):"
    for ($k = 0; $k -lt 10 -and $k -lt $sorted.Count; $k++) {
        $e = $sorted[$k]
        Write-Output ("   x={0,4} : {1,6:N2}   | local x={2}" -f $e.Key, $e.Value, ($e.Key - $x0))
    }
    $seed = [int]$sorted[0].Key
    $seedVal = [double]$sorted[0].Value
    $vals = @($colDiff.Values)
    $srtV = $vals | Sort-Object
    $med = [double]$srtV[[int]($vals.Count / 2)]
    Write-Output ("   -> strongest column: global x={0} (crop-local {1}), mean diff={2:N2}" -f $seed, ($seed - $x0), $seedVal)
    Write-Output ("   -> median colDiff in region = {0:N2}; peak/median ratio = {1:N1}" -f $med, ($seedVal / [math]::Max($med, 0.01)))

    # contiguous width around peak at >=50% of peak
    $wd = 1
    $xx = $seed - 1
    while ($xx -gt $x0 -and $colDiff[$xx] -ge $seedVal * 0.5) { $wd++; $xx-- }
    $xx = $seed + 1
    while ($xx -le $x1 -and $colDiff[$xx] -ge $seedVal * 0.5) { $wd++; $xx++ }
    Write-Output ("   -> contiguous width at >=50% of peak: {0} column(s)" -f $wd)
    Write-Output ("Column-mean profile x={0}..{1}:" -f [math]::Max($seed - 8, $x0 + 1), [math]::Min($seed + 8, $x1))
    for ($x = [math]::Max($seed - 8, $x0 + 1); $x -le [math]::Min($seed + 8, $x1); $x++) {
        Write-Output ("   x={0,4} : {1,6:N2}" -f $x, $colDiff[$x])
    }

    # per-row strongest edge + strength at seed
    $rowData = @()
    for ($y = $y0; $y -lt ($y0 + $ch); $y++) {
        $best = -1.0; $bx = -1
        $dbuf = [double[]]::new($cw)
        for ($x = $x0 + 1; $x -le $x1; $x++) {
            $i1 = $y * $stride + $x * 3
            $i0 = $y * $stride + ($x - 1) * 3
            $d = ([math]::Abs($bytes[$i1] - $bytes[$i0]) + [math]::Abs($bytes[$i1 + 1] - $bytes[$i0 + 1]) + [math]::Abs($bytes[$i1 + 2] - $bytes[$i0 + 2])) / 3.0
            $dbuf[$x - $x0 - 1] = $d
            if ($d -gt $best) { $best = $d; $bx = $x }
        }
        $rowData += [pscustomobject]@{ y = $y; best = $best; bx = $bx; dbuf = $dbuf }
    }
    $hist = $rowData | Group-Object bx | Sort-Object Count -Descending | Select-Object -First 6
    Write-Output "Per-row strongest-edge x histogram (x : #rows):"
    foreach ($grp in $hist) { Write-Output ("   x={0,4} : {1} rows" -f ([int]$grp.Name), ($grp.Count)) }

    $deviations = @()
    foreach ($rd in $rowData) { if ([math]::Abs($rd.bx - $seed) -gt 1) { $deviations += ("y{0}:x{1}" -f $rd.y, $rd.bx) } }
    Write-Output ("Rows whose strongest edge is NOT at seed+-1: {0} of {1}" -f $deviations.Count, $ch)
    if ($deviations.Count -gt 40) {
        $show = @($deviations[0..9]) + @('...') + @($deviations[($deviations.Count - 10)..($deviations.Count - 1)])
        Write-Output ('   ' + ($show -join ' '))
    } elseif ($deviations.Count -gt 0) {
        Write-Output ('   ' + ($deviations -join ' '))
    }

    # rows where the seed edge is present (>=60% of that row's max, >=20 abs)
    $run = 0; $bestRun = 0; $runStart = -1; $bestStart = -1; $bestEnd = -1; $prevY = -99999
    $edgeRows = 0
    $localMax = [System.Collections.Generic.List[int]]::new()
    foreach ($rd in $rowData) {
        $s = 0.0
        for ($o = -1; $o -le 1; $o++) {
            $idx = ($seed + $o) - $x0 - 1
            if ($idx -ge 0 -and $idx -le ($cw - 2)) { if ($rd.dbuf[$idx] -gt $s) { $s = $rd.dbuf[$idx] } }
        }
        $ok = ($rd.best -gt 0) -and ($s -ge 0.6 * $rd.best) -and ($s -ge 20)
        if ($ok) {
            $edgeRows++
            if ($rd.y -eq $prevY + 1 -and $run -gt 0) { $run++ } else { $run = 1; $runStart = $rd.y }
            if ($run -gt $bestRun) { $bestRun = $run; $bestStart = $runStart; $bestEnd = $rd.y }
            $prevY = $rd.y
            $lo = [int][math]::Max(($seed - 6) - $x0 - 1, 0)
            $hi = [int][math]::Min(($seed + 6) - $x0 - 1, $cw - 2)
            $b = -1.0; $bxx = -1
            for ($i = $lo; $i -le $hi; $i++) { if ($rd.dbuf[$i] -gt $b) { $b = $rd.dbuf[$i]; $bxx = $i + $x0 + 1 } }
            $localMax.Add($bxx)
        } else { $run = 0 }
    }
    Write-Output ("Rows where seed-x edge present (>=60% of row max, >=20 abs): {0} of {1}" -f $edgeRows, $ch)
    if ($edgeRows -gt 0) {
        Write-Output ("Longest consecutive run: {0} rows, y {1}..{2}" -f $bestRun, $bestStart, $bestEnd)
        $mn = ($localMax | Measure-Object -Minimum).Minimum
        $mx = ($localMax | Measure-Object -Maximum).Maximum
        $avg = ($localMax | Measure-Object -Average).Average
        $distinct = ($localMax | Sort-Object -Unique).Count
        Write-Output ("Drift of per-row edge x (local argmax within seed+-6): min={0} max={1} range={2} avg={3:N1} distinct-values={4}" -f $mn, $mx, ($mx - $mn), $avg, $distinct)
        $dev = 0.0
        if ($localMax.Count -gt 1) {
            $sumsq = 0.0
            foreach ($v in $localMax) { $sumsq += [math]::Pow($v - $avg, 2) }
            $dev = [math]::Sqrt($sumsq / $localMax.Count)
        }
        Write-Output ("StdDev of edge-x drift: {0:N2} px" -f $dev)
    }

    # edge presence by y bucket
    $bucket = 20
    $b0 = $y0
    Write-Output ("Edge presence at seed-x by y-bucket (bucket={0} rows):" -f $bucket)
    while ($b0 -lt ($y0 + $ch)) {
        $b1 = [int][math]::Min($b0 + $bucket, $y0 + $ch)
        $cnt = 0; $sum = 0.0; $nn = 0
        foreach ($rd in $rowData) {
            if ($rd.y -ge $b0 -and $rd.y -lt $b1) {
                $s = 0.0
                for ($o = -1; $o -le 1; $o++) {
                    $idx = ($seed + $o) - $x0 - 1
                    if ($idx -ge 0 -and $idx -le ($cw - 2)) { if ($rd.dbuf[$idx] -gt $s) { $s = $rd.dbuf[$idx] } }
                }
                $sum += $s; $nn++
                if (($rd.best -gt 0) -and ($s -ge 0.6 * $rd.best) -and ($s -ge 20)) { $cnt++ }
            }
        }
        Write-Output ("   y {0,3}..{1,3} : edge-present {2,3}/{3} rows, mean s at seed = {4,6:N2}" -f $b0, ($b1 - 1), $cnt, $nn, ($sum / [math]::Max($nn, 1)))
        $b0 = $b1
    }

    # full-height scan at seed column (all rows 0..H-1)
    $cnt2 = 0; $run2 = 0; $best2 = 0; $bs2 = -1; $be2 = -1; $prev2 = -99999; $st2 = -1
    for ($y = 0; $y -lt $H; $y++) {
        $i1 = $y * $stride + $seed * 3
        $i0 = $y * $stride + ($seed - 1) * 3
        $d = ([math]::Abs($bytes[$i1] - $bytes[$i0]) + [math]::Abs($bytes[$i1 + 1] - $bytes[$i0 + 1]) + [math]::Abs($bytes[$i1 + 2] - $bytes[$i0 + 2])) / 3.0
        if ($d -ge 25) {
            $cnt2++
            if ($y -eq $prev2 + 1 -and $run2 -gt 0) { $run2++ } else { $run2 = 1; $st2 = $y }
            if ($run2 -gt $best2) { $best2 = $run2; $bs2 = $st2; $be2 = $y }
            $prev2 = $y
        } else { $run2 = 0 }
    }
    Write-Output ("Full-height check at x={0} (all {1} rows): rows with >=25 abs diff = {2}; longest consecutive y {3}..{4} ({5} rows)" -f $seed, $H, $cnt2, $bs2, $be2, $best2)
    Write-Output ''
    $script:lastSeed = $seed
}

$script:lastSeed = -1
Analyze-Vertical -x0 $Ax0 -y0 $Ay0 -cw $Acw -ch $Ach -label 'CROP A (right-top, x50-100% y0-70%)'
Analyze-Vertical -x0 $Cx0 -y0 $Cy0 -cw $Ccw -ch $Cch -label 'CROP C (left control, x0-50% y0-70%)'
Analyze-Vertical -x0 $Bx0 -y0 $By0 -cw $Bcw -ch $Bch -label 'CROP B (core zone, x55-85% y15-65%)'

# bonus: 8x zoom around strongest column of crop A (recomputed)
$colBestA = -1.0; $seedA = -1
for ($x = $Ax0 + 1; $x -le ($Ax0 + $Acw - 1); $x++) {
    $s = 0.0
    for ($y = $Ay0; $y -lt ($Ay0 + $Ach); $y++) {
        $i1 = $y * $stride + $x * 3
        $i0 = $y * $stride + ($x - 1) * 3
        $s += ([math]::Abs($bytes[$i1] - $bytes[$i0]) + [math]::Abs($bytes[$i1 + 1] - $bytes[$i0 + 1]) + [math]::Abs($bytes[$i1 + 2] - $bytes[$i0 + 2])) / 3.0
    }
    $s = $s / $Ach
    if ($s -gt $colBestA) { $colBestA = $s; $seedA = $x }
}
Write-Output ("Bonus crop around crop-A strongest column x={0} (mean diff {1:N2})" -f $seedA, $colBestA)
$zx0 = [int][math]::Max($seedA - 9, 0)
$zcw = [int][math]::Min(19, $W - $zx0)
Save-CropScale -x0 $zx0 -y0 0 -cw $zcw -ch $Ach -scale 8 -path (Join-Path $outDir 'seam_line8x.png')
Write-Output ''

# whole-image vertical column scan
Write-Output ("==== WHOLE IMAGE column scan (all y 0..{0}) ====" -f ($H - 1))
$wc = @{}
for ($x = 1; $x -lt $W; $x++) {
    $s = 0.0
    for ($y = 0; $y -lt $H; $y++) {
        $i1 = $y * $stride + $x * 3
        $i0 = $y * $stride + ($x - 1) * 3
        $s += ([math]::Abs($bytes[$i1] - $bytes[$i0]) + [math]::Abs($bytes[$i1 + 1] - $bytes[$i0 + 1]) + [math]::Abs($bytes[$i1 + 2] - $bytes[$i0 + 2])) / 3.0
    }
    $wc[$x] = $s / $H
}
$srtW = $wc.GetEnumerator() | Sort-Object Value -Descending
for ($k = 0; $k -lt 8 -and $k -lt $srtW.Count; $k++) {
    $e = $srtW[$k]
    Write-Output ("   x={0,4} : {1,6:N2}" -f $e.Key, $e.Value)
}
Write-Output ''

$img.Dispose()
Write-Output 'DONE'
