param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePng,

    [Parameter(Mandatory = $true)]
    [string]$OutputIco
)

Add-Type -AssemblyName System.Drawing

$resolvedSource = Resolve-Path -LiteralPath $SourcePng
$outputDirectory = Split-Path -Parent $OutputIco
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$sizes = @(256, 128, 64, 48, 32, 16)
$images = New-Object System.Collections.Generic.List[byte[]]
$source = [System.Drawing.Image]::FromFile($resolvedSource)
try {
    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

                $scale = [Math]::Min($size / $source.Width, $size / $source.Height)
                $width = [Math]::Round($source.Width * $scale)
                $height = [Math]::Round($source.Height * $scale)
                $left = [Math]::Round(($size - $width) / 2)
                $top = [Math]::Round(($size - $height) / 2)
                $graphics.DrawImage($source, $left, $top, $width, $height)
            }
            finally {
                $graphics.Dispose()
            }

            $stream = New-Object System.IO.MemoryStream
            try {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $images.Add($stream.ToArray())
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }
}
finally {
    $source.Dispose()
}

$fileStream = [System.IO.File]::Create($OutputIco)
try {
    $writer = New-Object System.IO.BinaryWriter $fileStream
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$images.Count)

        $offset = 6 + (16 * $images.Count)
        for ($index = 0; $index -lt $images.Count; $index++) {
            $size = $sizes[$index]
            $bytes = $images[$index]
            $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
            $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$bytes.Length)
            $writer.Write([UInt32]$offset)
            $offset += $bytes.Length
        }

        foreach ($bytes in $images) {
            $writer.Write($bytes)
        }
    }
    finally {
        $writer.Dispose()
    }
}
finally {
    $fileStream.Dispose()
}
