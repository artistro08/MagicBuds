<#
    Outputs into src/MagicBuds.App/Assets: the app icon (AppIcon.ico, also the exe's icon), the
    MSIX logos at every scale and target size Windows asks for, and the two tray icons
    (TrayLight.ico for light taskbars, TrayDark.ico for dark ones).

    Everything is drawn from the charging case shape in assets-src/buds.svg. The tray icons are
    the shape in one flat color; the app icon is a Fluent-style rendition of it (porcelain case,
    lit from the top left, dark bud pockets, soft shadow). Rendering uses WPF, so run it from
    an STA PowerShell:

        pwsh -STA -File .\scripts\New-AppAssets.ps1

    Uses WPF drawing (https://learn.microsoft.com/dotnet/desktop/wpf/graphics-multimedia/drawing-objects-overview)
    and System.Drawing (https://learn.microsoft.com/dotnet/api/system.drawing) for the .ico files.
#>

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase, System.Drawing

$assets = Join-Path $PSScriptRoot '..\src\MagicBuds.App\Assets'
$source = Join-Path $PSScriptRoot '..\assets-src\buds.svg'
New-Item -ItemType Directory -Force -Path $assets | Out-Null

# The Case Shape: the SVG's one path. Its coordinates put the lid seam at y = 119.
[xml]$svg = Get-Content $source
$namespaces = New-Object System.Xml.XmlNamespaceManager $svg.NameTable
$namespaces.AddNamespace('s', 'http://www.w3.org/2000/svg')
$caseGeometry = [System.Windows.Media.Geometry]::Parse($svg.SelectSingleNode('//s:path', $namespaces).GetAttribute('d'))
$caseGeometry.Freeze()
$bounds = $caseGeometry.Bounds
$seamY = 119.057

# =========================================================================
# RENDERING
# =========================================================================

# Transform that fits the case into a square of $Size pixels with $Padding around it
function Get-FitTransform {
    param(
        [double] $Size,
        [double] $Padding,
        [double] $OffsetY = 0
    )

    $scale = [Math]::Min(($Size - 2 * $Padding) / $bounds.Width, ($Size - 2 * $Padding) / $bounds.Height)
    $x = ($Size / 2) - (($bounds.X + $bounds.Width / 2) * $scale)
    $y = ($Size / 2) - (($bounds.Y + $bounds.Height / 2) * $scale) + $OffsetY
    return New-Object System.Windows.Media.MatrixTransform $scale, 0, 0, $scale, $x, $y
}

# Renders a WPF visual into a System.Drawing bitmap (for the .ico writer and PNG saving)
function ConvertTo-Bitmap {
    param(
        [System.Windows.Media.Visual] $Visual,
        [int] $Size
    )

    $target = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $Size, $Size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $target.Render($Visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($target))
    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    $stream.Position = 0
    return , (New-Object System.Drawing.Bitmap $stream)
}

function New-Brush {
    param(
        [string] $From,
        [string] $To
    )

    $brush = New-Object System.Windows.Media.LinearGradientBrush (
        [System.Windows.Media.ColorConverter]::ConvertFromString($From),
        [System.Windows.Media.ColorConverter]::ConvertFromString($To),
        (New-Object System.Windows.Point 0.3, 0),
        (New-Object System.Windows.Point 0.7, 1))
    $brush.Freeze()
    return $brush
}

# Tray: the case in one flat color, almost edge to edge like the system tray glyphs
function New-TrayBitmap {
    param(
        [int] $Size,
        [System.Windows.Media.Color] $Color
    )

    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $context.PushTransform((Get-FitTransform -Size $Size -Padding ($Size * 0.04)))
    $context.DrawGeometry((New-Object System.Windows.Media.SolidColorBrush $Color), $null, $caseGeometry)
    $context.Pop()
    $context.Close()
    return , (ConvertTo-Bitmap -Visual $visual -Size $Size)
}

# App icon: Fluent rendition. Pockets go under the case so they show through its cutouts,
# then lid and body get their own gradients, a seam line, and a blurred shadow underneath.
function New-AppIconBitmap {
    param(
        [int] $Size
    )

    $padding = $Size * 0.08
    $fit = Get-FitTransform -Size $Size -Padding $padding -OffsetY (-$Size * 0.02)
    $root = New-Object System.Windows.Media.ContainerVisual

    # Shadow
    $shadow = New-Object System.Windows.Media.DrawingVisual
    $shadowContext = $shadow.RenderOpen()
    $shadowContext.PushTransform((New-Object System.Windows.Media.TranslateTransform 0, ($Size * 0.035)))
    $shadowContext.PushTransform($fit)
    $shadowContext.DrawGeometry((New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromArgb(90, 0, 0, 0))), $null, $caseGeometry)
    $shadowContext.Pop()
    $shadowContext.Pop()
    $shadowContext.Close()
    $shadow.Effect = New-Object System.Windows.Media.Effects.BlurEffect -Property @{ Radius = [Math]::Max(1, $Size * 0.04) }
    $root.Children.Add($shadow) | Out-Null

    # Case
    $case = New-Object System.Windows.Media.DrawingVisual
    $context = $case.RenderOpen()
    $context.PushTransform($fit)

    # Bud Pockets and Bud Tops, under the case's cutouts
    $pocketBrush = New-Brush '#3A3A3A' '#121212'
    $budBrush = New-Brush '#FFFDF8' '#D9CFBF'
    foreach ($centerX in -174.9, -100.0) {
        $context.DrawEllipse($pocketBrush, $null, (New-Object System.Windows.Point $centerX, $seamY), 33, 33)
        # Each bud fills its pocket, leaving a thin dark gap around it
        $context.DrawEllipse($budBrush, $null, (New-Object System.Windows.Point $centerX, ($seamY + 4)), 25, 25)
    }

    # Lid and Body
    $lid = [System.Windows.Media.Geometry]::Combine($caseGeometry, (New-Object System.Windows.Media.RectangleGeometry (New-Object System.Windows.Rect -300, 0, 400, $seamY)), 'Intersect', $null)
    $body = [System.Windows.Media.Geometry]::Combine($caseGeometry, (New-Object System.Windows.Media.RectangleGeometry (New-Object System.Windows.Rect -300, $seamY, 400, 200)), 'Intersect', $null)
    $context.DrawGeometry((New-Brush '#FFFFFF' '#E4DED4'), $null, $lid)
    $context.DrawGeometry((New-Brush '#FFFFFF' '#D3CCC0'), $null, $body)

    # Seam and Rim
    $seamPen = New-Object System.Windows.Media.Pen ((New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromArgb(110, 120, 110, 95)))), 1.6
    $context.DrawLine($seamPen, (New-Object System.Windows.Point $bounds.X, ($seamY + 0.8)), (New-Object System.Windows.Point ($bounds.X + $bounds.Width), ($seamY + 0.8)))
    $rimPen = New-Object System.Windows.Media.Pen ((New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromArgb(70, 90, 80, 65)))), 1.2
    $context.DrawGeometry($null, $rimPen, $caseGeometry)

    $context.Pop()
    $context.Close()
    $root.Children.Add($case) | Out-Null

    return , (ConvertTo-Bitmap -Visual $root -Size $Size)
}

# =========================================================================
# ICO FILES
# =========================================================================

# Classic icon image: BITMAPINFOHEADER (height doubled), 32-bit BGRA rows bottom-up, then
# the AND mask. Windows draws small PNG-compressed entries as blank.
function ConvertTo-IconImage {
    param(
        [System.Drawing.Bitmap] $Bitmap
    )

    $size = $Bitmap.Width
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $stream
    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
    $writer.Write([uint32]40)
    $writer.Write([int32]$size)
    $writer.Write([int32]($size * 2))
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]0)
    $writer.Write([uint32](($size * $size * 4) + ($maskStride * $size)))
    $writer.Write([int32]0)
    $writer.Write([int32]0)
    $writer.Write([uint32]0)
    $writer.Write([uint32]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $pixel = $Bitmap.GetPixel($x, $y)
            $writer.Write([byte]$pixel.B)
            $writer.Write([byte]$pixel.G)
            $writer.Write([byte]$pixel.R)
            $writer.Write([byte]$pixel.A)
        }
    }
    # AND mask: 1 = transparent. Set it wherever alpha is 0 so the background stays
    # transparent even where Windows ignores the alpha channel (the tray did).
    for ($y = $size - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $maskStride
        for ($x = 0; $x -lt $size; $x++) {
            if ($Bitmap.GetPixel($x, $y).A -eq 0) {
                $row[[int][Math]::Floor($x / 8)] = $row[[int][Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8))
            }
        }
        $writer.Write($row)
    }
    $writer.Flush()

    return , $stream.ToArray()
}

# Write the icon directory and images; a 256 entry is stored as its size 0
function Write-IconFile {
    param(
        [int[]] $Sizes,
        [object[]] $Images,
        [string] $Path
    )

    $file = [System.IO.File]::Create($Path)
    $writer = New-Object System.IO.BinaryWriter $file
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$Sizes.Count)

    $offset = 6 + (16 * $Sizes.Count)
    for ($i = 0; $i -lt $Sizes.Count; $i++) {
        $writer.Write([byte]($Sizes[$i] % 256))
        $writer.Write([byte]($Sizes[$i] % 256))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$Images[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $Images[$i].Length
    }
    foreach ($image in $Images) {
        $writer.Write([byte[]]$image)
    }
    $writer.Dispose()
    Write-Output "Wrote $Path"
}

# =========================================================================
# OUTPUT
# =========================================================================

# App Icon
$iconSizes = 16, 20, 24, 32, 40, 48, 64, 256
$iconImages = foreach ($size in $iconSizes) { , (ConvertTo-IconImage -Bitmap (New-AppIconBitmap -Size $size)) }
Write-IconFile -Sizes $iconSizes -Images $iconImages -Path (Join-Path $assets 'AppIcon.ico')

# Draw the app icon centered on a transparent canvas, at $Scale of the shorter side
function New-AppIconImage {
    param(
        [int] $Width,
        [int] $Height,
        [double] $Scale,
        [string] $Path
    )

    $side = [int][Math]::Round([Math]::Min($Width, $Height) * $Scale)
    $art = New-AppIconBitmap -Size $side
    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.DrawImage($art, [int](($Width - $side) / 2), [int](($Height - $side) / 2), $side, $side)
    $graphics.Dispose()
    $art.Dispose()

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Write-Output "Wrote $Path"
}

# Package Logos, one file per size Windows asks for. The manifest names the plain file
# (e.g. Square44x44Logo.png) and Windows picks the qualified one that fits:
# https://learn.microsoft.com/windows/apps/design/style/iconography/app-icon-construction
foreach ($scale in 100, 125, 150, 200, 400) {
    $factor = $scale / 100
    New-AppIconImage -Width ([int](44 * $factor)) -Height ([int](44 * $factor)) -Scale 1 -Path (Join-Path $assets "Square44x44Logo.scale-$scale.png")
    New-AppIconImage -Width ([int](150 * $factor)) -Height ([int](150 * $factor)) -Scale 0.6 -Path (Join-Path $assets "Square150x150Logo.scale-$scale.png")
    New-AppIconImage -Width ([int](310 * $factor)) -Height ([int](150 * $factor)) -Scale 0.6 -Path (Join-Path $assets "Wide310x150Logo.scale-$scale.png")
    New-AppIconImage -Width ([int](50 * $factor)) -Height ([int](50 * $factor)) -Scale 1 -Path (Join-Path $assets "StoreLogo.scale-$scale.png")
}

# Exact-size app icons (notifications, taskbar, Start's app list). The icon brings its own
# colors, so the plated and unplated (dark and light surface) versions are the same image.
foreach ($target in 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256) {
    foreach ($suffix in '', '_altform-unplated', '_altform-lightunplated') {
        New-AppIconImage -Width $target -Height $target -Scale 1 -Path (Join-Path $assets "Square44x44Logo.targetsize-$target$suffix.png")
    }
}

# Tray Icons
$traySizes = 16, 20, 24, 32, 40, 48, 64
foreach ($tray in @(@{ Name = 'TrayLight.ico'; Color = [System.Windows.Media.Colors]::Black }, @{ Name = 'TrayDark.ico'; Color = [System.Windows.Media.Colors]::White })) {
    $images = foreach ($size in $traySizes) { , (ConvertTo-IconImage -Bitmap (New-TrayBitmap -Size $size -Color $tray.Color)) }
    Write-IconFile -Sizes $traySizes -Images $images -Path (Join-Path $assets $tray.Name)
}
