# Tạo icon app: ô vuông bo góc xanh + "treemap" trắng (các ô lồng nhau). Kết quả: ICO nhiều cỡ (PNG nén).
param([string]$Out = (Join-Path $PSScriptRoot "..\src\FolderSizePro.App\Assets\app.ico"))
Add-Type -AssemblyName System.Drawing

function New-Frame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)

    $r = [single]($s * 0.22)
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($s - $d, 0, $d, $d, 270, 90)
    $path.AddArc($s - $d, $s - $d, $d, $d, 0, 90); $path.AddArc(0, $s - $d, $d, $d, 90, 90); $path.CloseFigure()
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 37, 99, 235)), ([System.Drawing.Color]::FromArgb(255, 14, 165, 233)), 45
    $g.FillPath($grad, $path)

    # treemap: một ô lớn bên trái, hai ô xếp dọc bên phải, ô nhỏ trong ô trên-phải
    $m = $s * 0.16; $gap = [Math]::Max(1.0, $s * 0.045)
    $w = $s - 2 * $m; $h = $s - 2 * $m
    $splitX = $m + $w * 0.56
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245, 255, 255, 255))
    $soft = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(190, 255, 255, 255))
    $faint = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(130, 255, 255, 255))
    $g.FillRectangle($white, $m, $m, $splitX - $m - $gap / 2, $h)
    $midY = $m + $h * 0.58
    $g.FillRectangle($soft, $splitX + $gap / 2, $m, $m + $w - $splitX - $gap / 2, $midY - $m - $gap / 2)
    $g.FillRectangle($faint, $splitX + $gap / 2, $midY + $gap / 2, $m + $w - $splitX - $gap / 2, $m + $h - $midY - $gap / 2)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Frame $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}
New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $pngs[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $bw.Write($data) }
$bw.Flush(); $fs.Close()
Write-Host "Đã tạo $Out ($((Get-Item $Out).Length) byte)"
