# Generates the two tray icons used by TrayHelper (PNG-encoded .ico, 32x32).
#   app-light.ico  -> shown under the LIGHT system theme
#   app-dark.ico   -> shown under the DARK system theme
# Re-run to regenerate. Replace with your real brand art any time (keep the file names).

Add-Type -AssemblyName System.Drawing

function New-Ico {
    param([string]$Path, [System.Drawing.Color]$Fill, [System.Drawing.Color]$Outline)

    $size = 32
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $brush = New-Object System.Drawing.SolidBrush($Fill)
    $pen = New-Object System.Drawing.Pen($Outline, 2)
    $g.FillEllipse($brush, 3, 3, ($size - 6), ($size - 6))
    $g.DrawEllipse($pen, 3, 3, ($size - 6), ($size - 6))
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $png = $ms.ToArray()
    $ms.Dispose(); $bmp.Dispose()

    $fs = [System.IO.File]::Create($Path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)   # reserved, type=icon, count=1
    $bw.Write([Byte]$size); $bw.Write([Byte]$size)                     # width, height
    $bw.Write([Byte]0); $bw.Write([Byte]0)                            # colors, reserved
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)                        # planes, bpp
    $bw.Write([UInt32]$png.Length)                                     # size of PNG
    $bw.Write([UInt32]22)                                              # offset to PNG
    $bw.Write($png)
    $bw.Flush(); $fs.Close()
    Write-Host "wrote $Path ($($png.Length) bytes)"
}

$dir = Join-Path (Split-Path -Parent $PSScriptRoot) 'DesktopBridge\TrayHelper'
# Light theme -> lighter icon; dark theme -> darker icon (deliberately distinct so the swap is visible).
New-Ico (Join-Path $dir 'app-light.ico') ([System.Drawing.Color]::FromArgb(245,245,245)) ([System.Drawing.Color]::FromArgb(110,110,110))
New-Ico (Join-Path $dir 'app-dark.ico')  ([System.Drawing.Color]::FromArgb(32,32,32))    ([System.Drawing.Color]::FromArgb(210,210,210))
