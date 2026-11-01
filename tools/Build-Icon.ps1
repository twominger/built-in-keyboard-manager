param(
    [string]$Source = (Join-Path $PSScriptRoot '..\assets\app-icon-source.png'),
    [string]$IconOutput = (Join-Path $PSScriptRoot '..\assets\app-icon.ico'),
    [string]$PreviewOutput = (Join-Path $PSScriptRoot '..\assets\app-icon.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourcePath = [IO.Path]::GetFullPath($Source)
$iconPath = [IO.Path]::GetFullPath($IconOutput)
$previewPath = [IO.Path]::GetFullPath($PreviewOutput)
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

function New-PngBytes([Drawing.Image]$Image, [int]$Size) {
    $bitmap = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($Image, [Drawing.Rectangle]::new(0, 0, $Size, $Size))
        $stream = [IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            Write-Output -NoEnumerate ([Byte[]]$stream.ToArray())
        }
        finally { $stream.Dispose() }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$image = [Drawing.Image]::FromFile($sourcePath)
try {
    $entries = foreach ($size in $sizes) {
        [PSCustomObject]@{ Size = $size; Bytes = [Byte[]](New-PngBytes -Image $image -Size $size) }
    }

    $previewBytes = New-PngBytes -Image $image -Size 512
    [IO.File]::WriteAllBytes($previewPath, $previewBytes)

    $stream = [IO.File]::Create($iconPath)
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$entries.Count)
        $offset = 6 + (16 * $entries.Count)
        foreach ($entry in $entries) {
            $dimension = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
            $writer.Write([Byte]$dimension)
            $writer.Write([Byte]$dimension)
            $writer.Write([Byte]0)
            $writer.Write([Byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$entry.Bytes.Length)
            $writer.Write([UInt32]$offset)
            $offset += $entry.Bytes.Length
        }
        foreach ($entry in $entries) { $writer.Write([Byte[]]$entry.Bytes) }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}
finally { $image.Dispose() }

Write-Host "Generated $iconPath"
Write-Host "Generated $previewPath"
