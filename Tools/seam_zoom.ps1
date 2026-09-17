# seam_zoom.ps1 - two 6x zoom strips: sky seam zone and canopy band
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$srcPath = 'C:\Users\qqq\AppData\Roaming\QoderCN\SharedClientCache\cache\images\task-ff5\eatlif50-17375436.png'
$outDir = 'C:\Users\qqq\AppData\Local\Temp'
$img = [System.Drawing.Image]::FromFile($srcPath)
$W = $img.Width; $H = $img.Height
$work = [System.Drawing.Bitmap]::new($W, $H, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($work); $g.DrawImageUnscaled($img, 0, 0); $g.Dispose()
$rect = [System.Drawing.Rectangle]::new(0, 0, $W, $H)
$data = $work.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$stride = $data.Stride
$bytes = [byte[]]::new($stride * $H)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
$work.UnlockBits($data)

function Save-CropScale {
    param([int]$x0, [int]$y0, [int]$cw, [int]$ch, [int]$scale, [string]$path)
    $dw = $cw * $scale; $dh = $ch * $scale
    $dst = [System.Drawing.Bitmap]::new($dw, $dh, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $drect = [System.Drawing.Rectangle]::new(0, 0, $dw, $dh)
    $ddata = $dst.LockBits($drect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $dstride = $ddata.Stride
    $dbytes = [byte[]]::new($dstride * $dh)
    $dx3 = [int[]]::new($dw)
    for ($dx = 0; $dx -lt $dw; $dx++) { $dx3[$dx] = ($x0 + [int][math]::Floor($dx / $scale)) * 3 }
    $buf = [byte[]]::new($dw * 3)
    for ($sy = 0; $sy -lt $ch; $sy++) {
        $sRow = ($y0 + $sy) * $stride
        for ($dx = 0; $dx -lt $dw; $dx++) {
            $si = $sRow + $dx3[$dx]
            $bi = $dx * 3
            $buf[$bi] = $bytes[$si]; $buf[$bi + 1] = $bytes[$si + 1]; $buf[$bi + 2] = $bytes[$si + 2]
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
    Write-Output ("SAVED {0} ({1}x{2}px, src x{3}..{4} y{5}..{6}, {7}x nearest)" -f $path, $dw, $dh, $x0, ($x0 + $cw - 1), $y0, ($y0 + $ch - 1), $scale)
}

# sky seam zone: x112..136, y55..155
Save-CropScale -x0 112 -y0 55 -cw 25 -ch 101 -scale 6 -path (Join-Path $outDir 'seam_skyline6x.png')
# canopy band: x115..141, y140..240
Save-CropScale -x0 115 -y0 140 -cw 27 -ch 101 -scale 6 -path (Join-Path $outDir 'seam_canopy6x.png')
$img.Dispose()
Write-Output 'DONE'
