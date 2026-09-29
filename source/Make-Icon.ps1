# Re-encode the supplied artwork at standard Windows icon sizes.
param([string]$InputImage = (Join-Path $PSScriptRoot '..\assets\logo.png'), [string]$OutputIcon = (Join-Path $PSScriptRoot '..\assets\PR-Radios.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$radioImage = [Drawing.Image]::FromFile((Resolve-Path -LiteralPath $InputImage).Path)
$radioFrames = @()
$radioSizes = @(16,24,32,48,64,128,256)
try {
    foreach($radioSize in $radioSizes) {
        $radioBitmap = New-Object Drawing.Bitmap $radioSize,$radioSize
        $radioGraphics = [Drawing.Graphics]::FromImage($radioBitmap)
        $radioStream = New-Object IO.MemoryStream
        try {
            $radioGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $radioGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $radioGraphics.DrawImage($radioImage,0,0,$radioSize,$radioSize)
            $radioBitmap.Save($radioStream,[Drawing.Imaging.ImageFormat]::Png)
            $radioFrames += ,$radioStream.ToArray()
        } finally { $radioStream.Dispose(); $radioGraphics.Dispose(); $radioBitmap.Dispose() }
    }
    $radioFile = [IO.File]::Create([IO.Path]::GetFullPath($OutputIcon))
    $radioWriter = New-Object IO.BinaryWriter $radioFile
    try {
        $radioWriter.Write([uint16]0); $radioWriter.Write([uint16]1); $radioWriter.Write([uint16]$radioSizes.Count)
        $radioOffset = 6 + 16 * $radioSizes.Count
        for($radioIndex=0; $radioIndex -lt $radioSizes.Count; $radioIndex++) {
            $radioDimension = if($radioSizes[$radioIndex] -eq 256) {0} else {$radioSizes[$radioIndex]}
            $radioWriter.Write([byte]$radioDimension); $radioWriter.Write([byte]$radioDimension)
            $radioWriter.Write([byte]0); $radioWriter.Write([byte]0)
            $radioWriter.Write([uint16]1); $radioWriter.Write([uint16]32)
            $radioWriter.Write([uint32]$radioFrames[$radioIndex].Length); $radioWriter.Write([uint32]$radioOffset)
            $radioOffset += $radioFrames[$radioIndex].Length
        }
        foreach($radioFrame in $radioFrames) { $radioWriter.Write([byte[]]$radioFrame) }
    } finally { $radioWriter.Dispose(); $radioFile.Dispose() }
} finally { $radioImage.Dispose() }
