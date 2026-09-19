# =============================================================================
#  Generate-AppIcon.ps1
#  Genera Assets/app.ico (multi-resolucion: 16,24,32,48,64,128,256) para
#  "Rclone Commander Advanced" sin dependencias externas (solo System.Drawing).
#
#  Diseno: cuadrado redondeado oscuro (slate #13171F) con un motivo de
#  "nube + unidad de disco" en azul electrico (#58A6FF) y acento esmeralda
#  (#3FB950), coherente con la identidad visual del panel.
#
#  Uso:  powershell -ExecutionPolicy Bypass -File tools\Generate-AppIcon.ps1
# =============================================================================

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

# --- Rutas -------------------------------------------------------------------
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Split-Path -Parent $scriptDir
$assetsDir = Join-Path $projectDir 'src\RcloneCommanderAdvanced\Assets'
$icoPath = Join-Path $assetsDir 'app.ico'

if (-not (Test-Path $assetsDir)) {
    New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
}

# --- Paleta ------------------------------------------------------------------
$bgDeep   = [System.Drawing.Color]::FromArgb(255, 0x13, 0x17, 0x1F)
$bgPanel  = [System.Drawing.Color]::FromArgb(255, 0x1E, 0x22, 0x2B)
$accent   = [System.Drawing.Color]::FromArgb(255, 0x58, 0xA6, 0xFF)
$emerald  = [System.Drawing.Color]::FromArgb(255, 0x3F, 0xB9, 0x50)
$cyan     = [System.Drawing.Color]::FromArgb(255, 0x06, 0xB6, 0xD4)

$sizes = @(16, 24, 32, 48, 64, 128, 256)

function New-RoundedRectPath {
    param([float]$x, [float]$y, [float]$w, [float]$h, [float]$r)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$size)

    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$size

    # --- Fondo: cuadrado redondeado con degradado sutil ----------------------
    $pad = $s * 0.045
    $radius = $s * 0.22
    $bgPath = New-RoundedRectPath -x $pad -y $pad -w ($s - 2 * $pad) -h ($s - 2 * $pad) -r $radius

    $gradRect = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $gradRect, $bgPanel, $bgDeep, 90.0)
    $g.FillPath($grad, $bgPath)

    # Borde sutil
    $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(90, 0x2D, 0x37, 0x48), [Math]::Max(1.0, $s * 0.012))
    $g.DrawPath($borderPen, $bgPath)

    # --- Nube (motivo remoto) ------------------------------------------------
    $cloudColor = $accent
    $cloudBrush = New-Object System.Drawing.SolidBrush($cloudColor)

    $cx = $s * 0.50
    $cy = $s * 0.40
    $cw = $s * 0.62
    $ch = $s * 0.26

    $cloudPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    # base
    $cloudPath.AddEllipse($cx - $cw * 0.50, $cy - $ch * 0.10, $cw * 0.62, $ch * 0.95)
    $cloudPath.AddEllipse($cx - $cw * 0.18, $cy - $ch * 0.55, $cw * 0.52, $ch * 1.10)
    $cloudPath.AddEllipse($cx + $cw * 0.10, $cy - $ch * 0.20, $cw * 0.42, $ch * 0.95)
    $cloudPath.AddRectangle((New-Object System.Drawing.RectangleF(
        ($cx - $cw * 0.50), ($cy + $ch * 0.20), ($cw * 1.00), ($ch * 0.35))))
    $g.FillPath($cloudBrush, $cloudPath)

    # --- Unidad de disco (motivo almacenamiento) -----------------------------
    $dw = $s * 0.56
    $dh = $s * 0.20
    $dx = ($s - $dw) / 2.0
    $dy = $s * 0.60

    $drivePath = New-RoundedRectPath -x $dx -y $dy -w $dw -h $dh -r ($dh * 0.28)
    $driveBrush = New-Object System.Drawing.SolidBrush($emerald)
    $g.FillPath($driveBrush, $drivePath)

    # Ranura / LED del disco
    $ledSize = [Math]::Max(1.5, $s * 0.045)
    $ledBrush = New-Object System.Drawing.SolidBrush($bgDeep)
    $g.FillEllipse($ledBrush, ($dx + $dw - $ledSize * 2.4), ($dy + $dh / 2 - $ledSize / 2), $ledSize, $ledSize)

    # Linea de conexion nube -> disco (acento cian)
    $connPen = New-Object System.Drawing.Pen($cyan, [Math]::Max(1.0, $s * 0.035))
    $connPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $connPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($connPen, $cx, ($cy + $ch * 0.55), $cx, ($dy - $s * 0.01))

    # --- Limpieza ------------------------------------------------------------
    $connPen.Dispose(); $ledBrush.Dispose(); $driveBrush.Dispose()
    $cloudBrush.Dispose(); $cloudPath.Dispose(); $borderPen.Dispose()
    $grad.Dispose(); $bgPath.Dispose(); $drivePath.Dispose()
    $g.Dispose()

    return $bmp
}

# --- Construir todos los tamanos --------------------------------------------
$bitmaps = New-Object 'System.Collections.Generic.List[System.Drawing.Bitmap]'
foreach ($sz in $sizes) {
    $bitmaps.Add((New-IconBitmap -size $sz))
}

# --- Serializar PNG de cada tamano ------------------------------------------
function Get-PngBytes {
    param([System.Drawing.Bitmap]$bmp)
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return $bytes
}

$pngBlobs = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($b in $bitmaps) {
    $pngBlobs.Add((Get-PngBytes -bmp $b))
}

# --- Escribir el contenedor .ICO --------------------------------------------
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR
$bw.Write([UInt16]0)                 # reserved
$bw.Write([UInt16]1)                 # type = icon
$bw.Write([UInt16]$sizes.Count)      # image count

# Calcular offset inicial de los datos
$headerSize = 6 + (16 * $sizes.Count)
$offset = $headerSize

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $blob = $pngBlobs[$i]

    $wByte = if ($sz -ge 256) { 0 } else { $sz }
    $hByte = if ($sz -ge 256) { 0 } else { $sz }

    $bw.Write([Byte]$wByte)          # width
    $bw.Write([Byte]$hByte)          # height
    $bw.Write([Byte]0)               # color palette
    $bw.Write([Byte]0)               # reserved
    $bw.Write([UInt16]1)             # color planes
    $bw.Write([UInt16]32)            # bits per pixel
    $bw.Write([UInt32]$blob.Length)  # size of image data
    $bw.Write([UInt32]$offset)       # offset of image data

    $offset += $blob.Length
}

foreach ($blob in $pngBlobs) { $bw.Write($blob) }

$bw.Flush(); $bw.Dispose(); $fs.Dispose()

foreach ($b in $bitmaps) { $b.Dispose() }

Write-Host "Icono generado: $icoPath"
Write-Host ("Resoluciones: " + ($sizes -join ', '))
Write-Host ("Tamano total: {0:N0} bytes" -f (Get-Item $icoPath).Length)
