$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sourcePath = Join-Path $PSScriptRoot 'halloween-parade-thumbnail-photo.png'
$outputPath = Join-Path $PSScriptRoot 'halloween-parade-ce-thumbnail-1024.png'
$previewPath = Join-Path $PSScriptRoot 'halloween-parade-ce-preview-256.png'
$paper = [Drawing.ColorTranslator]::FromHtml('#F7F4EC')
$forest = [Drawing.ColorTranslator]::FromHtml('#364E3C')
$ink = [Drawing.ColorTranslator]::FromHtml('#303A30')
$wood = [Drawing.ColorTranslator]::FromHtml('#846449')
$bitmap = [Drawing.Bitmap]::new(1024, 1024, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
function Draw-Label([string]$text, [single]$size, [Drawing.Color]$color, [Drawing.RectangleF]$box, [bool]$bold) {
    $style = if ($bold) { [Drawing.FontStyle]::Bold } else { [Drawing.FontStyle]::Regular }
    $font = [Drawing.Font]::new('Yu Gothic', $size, $style, [Drawing.GraphicsUnit]::Pixel)
    $brush = [Drawing.SolidBrush]::new($color)
    $format = [Drawing.StringFormat]::GenericTypographic.Clone()
    $format.FormatFlags = [Drawing.StringFormatFlags]::NoWrap
    $format.LineAlignment = [Drawing.StringAlignment]::Center
    try {
        if ($g.MeasureString($text, $font, 2000, $format).Width -gt $box.Width) { throw "Text exceeds box: $text" }
        $g.DrawString($text, $font, $brush, $box, $format)
    } finally { $format.Dispose(); $brush.Dispose(); $font.Dispose() }
}
try {
    $g.Clear($paper)
    $accent = [Drawing.SolidBrush]::new($wood)
    try { $g.FillRectangle($accent, 64, 48, 56, 6) } finally { $accent.Dispose() }
    Draw-Label 'ハロウィンパレード' 59 $forest ([Drawing.RectangleF]::new(60, 83, 906, 85)) $true
    Draw-Label '犬たちと楽しむハロウィンライブ' 34 $wood ([Drawing.RectangleF]::new(64, 178, 900, 54)) $true
    # Preserve the complete 16:9 capture. No crop, added scene elements or color retouching.
    $photo = [Drawing.Image]::FromFile($sourcePath)
    $clip = [Drawing.Drawing2D.GraphicsPath]::new()
    try {
        $clip.AddArc(40, 265, 48, 48, 180, 90)
        $clip.AddArc(936, 265, 48, 48, 270, 90)
        $clip.AddArc(936, 748, 48, 48, 0, 90)
        $clip.AddArc(40, 748, 48, 48, 90, 90)
        $clip.CloseFigure()
        $state = $g.Save()
        $g.SetClip($clip)
        $g.DrawImage($photo, [Drawing.RectangleF]::new(40, 265, 944, 531))
        $g.Restore($state)
    } finally { $clip.Dispose(); $photo.Dispose() }
    Draw-Label 'かぼちゃ灯りに包まれる、にぎやかな坂道行進。' 30 $ink ([Drawing.RectangleF]::new(64, 824, 900, 65)) $false
    $rule = [Drawing.Pen]::new([Drawing.Color]::FromArgb(65, $forest), 1)
    try { $g.DrawLine($rule, 64, 913, 960, 913) } finally { $rule.Dispose() }
    Draw-Label '古民家の裏山' 29 $forest ([Drawing.RectangleF]::new(64, 932, 600, 52)) $true
    Draw-Label 'VAnimal' 27 $wood ([Drawing.RectangleF]::new(825, 932, 135, 52)) $true
    $bitmap.Save($outputPath, [Drawing.Imaging.ImageFormat]::Png)
    $preview = [Drawing.Bitmap]::new(256, 256)
    $pg = [Drawing.Graphics]::FromImage($preview)
    try {
        $pg.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $pg.DrawImage($bitmap, 0, 0, 256, 256)
        $preview.Save($previewPath, [Drawing.Imaging.ImageFormat]::Png)
    } finally { $pg.Dispose(); $preview.Dispose() }
} finally { $g.Dispose(); $bitmap.Dispose() }
$check = [Drawing.Image]::FromFile($outputPath)
try {
    if ($check.Width -ne 1024 -or $check.Height -ne 1024) { throw 'Unexpected thumbnail size' }
    [pscustomobject]@{ file=$outputPath; width=$check.Width; height=$check.Height; bytes=(Get-Item $outputPath).Length; sha256=(Get-FileHash $outputPath -Algorithm SHA256).Hash } | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $PSScriptRoot 'ce-thumbnail-verification.json')
} finally { $check.Dispose() }
Write-Output $outputPath
