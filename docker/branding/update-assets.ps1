[CmdletBinding()]
param(
    [string]$ArchivePath
)

$ErrorActionPreference = 'Stop'
if (-not $ArchivePath) { $ArchivePath = Join-Path $PSScriptRoot '../../resources/branding/Tanuki_Logo.zip' }
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repositoryPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$temporaryPath = Join-Path ([IO.Path]::GetTempPath()) ('tanukarr-artwork-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryPath | Out-Null

function Write-Logo([string]$RelativePath, [int]$Width, [int]$Height, [bool]$Opaque = $false) {
    $destinationPath = Join-Path $repositoryPath $RelativePath
    $bitmap = New-Object Drawing.Bitmap($Width, $Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        if ($Opaque) { $graphics.Clear([Drawing.Color]::FromArgb(255, 32, 37, 49)) }
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $side = [Math]::Min($Width, $Height)
        $graphics.DrawImage($script:sourceImage, [int](($Width-$side)/2), [int](($Height-$side)/2), $side, $side)
        $format = if ($RelativePath.EndsWith('.bmp')) { [Drawing.Imaging.ImageFormat]::Bmp } else { [Drawing.Imaging.ImageFormat]::Png }
        $bitmap.Save($destinationPath, $format)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}

function Write-Icon([string]$RelativePath) {
    $sizes = @(16, 32, 48, 64, 128, 256)
    $images = @(foreach ($size in $sizes) { ,([IO.File]::ReadAllBytes((Join-Path $repositoryPath "Logo/$size.png"))) })
    $stream = [IO.File]::Create((Join-Path $repositoryPath $RelativePath))
    $writer = New-Object IO.BinaryWriter($stream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$images[$index].Length); $writer.Write([uint32]$offset)
            $offset += $images[$index].Length
        }
        foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
    } finally { $writer.Dispose() }
}

function Write-BigEndian([IO.BinaryWriter]$Writer, [uint32]$Value) {
    $bytes = [BitConverter]::GetBytes($Value)
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($bytes) }
    $Writer.Write($bytes)
}

try {
    $archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ArchivePath))
    try {
        foreach ($entry in $archive.Entries) {
            $targetPath = [IO.Path]::GetFullPath((Join-Path $temporaryPath $entry.FullName))
            if (-not $targetPath.StartsWith($temporaryPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe artwork archive path' }
            if ($entry.FullName.EndsWith('/')) { continue }
            New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $targetPath, $false)
        }
    } finally { $archive.Dispose() }

    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $temporaryPath 'Logo') -File) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $repositoryPath ('Logo/' + $file.Name))
    }
    $script:sourceImage = [Drawing.Image]::FromFile((Join-Path $temporaryPath 'Logo/1024.png'))
    Copy-Item -LiteralPath (Join-Path $temporaryPath 'Logo/Sonarr.svg') -Destination (Join-Path $repositoryPath 'frontend/src/Content/Images/logo.svg')
    $iconDirectory = 'frontend/src/Content/Images/Icons'
    foreach ($size in @(16,32)) {
        foreach ($suffix in @('', '-debug')) {
            Copy-Item -LiteralPath (Join-Path $temporaryPath "Logo/$size.png") -Destination (Join-Path $repositoryPath "$iconDirectory/favicon$suffix-${size}x$size.png")
        }
    }
    foreach ($variant in @(
        @('android-chrome-192x192.png',192,192), @('android-chrome-512x512.png',512,512),
        @('apple-touch-icon.png',180,180), @('mstile-70x70.png',70,70),
        @('mstile-144x144.png',144,144), @('mstile-150x150.png',150,150),
        @('mstile-310x310.png',310,310), @('mstile-310x150.png',310,150)
    )) { Write-Logo "$iconDirectory/$($variant[0])" $variant[1] $variant[2] }
    Write-Logo 'frontend/src/Content/Images/404.png' 638 805
    Write-Logo 'frontend/src/Content/Images/error.png' 1205 556
    Write-Logo 'frontend/src/Content/Images/poster-dark.png' 168 247 $true
    Write-Logo 'frontend/src/Content/Images/banner-dark.png' 379 70 $true
    Write-Logo 'distribution/windows/setup/inno/WizModernImage.bmp' 164 314 $true
    Write-Logo 'distribution/windows/setup/inno/WizModernSmallImage.bmp' 55 55 $true
    Write-Icon "$iconDirectory/favicon.ico"
    Write-Icon "$iconDirectory/favicon-debug.ico"
    Write-Icon 'src/NzbDrone.Host/Sonarr.ico'

    # The archive's 96px white outline still contains the old upstream glyph.
    # Derive the notification outline and Safari monochrome mask from the Tanuki.
    $maskSource = New-Object Drawing.Bitmap($script:sourceImage, 96, 96)
    $outline = New-Object Drawing.Bitmap(96,96)
    $paths = New-Object Text.StringBuilder
    try {
        for ($row=0; $row -lt 96; $row++) {
            $start = -1
            for ($column=0; $column -le 96; $column++) {
                $dark = $false
                if ($column -lt 96) {
                    $pixel = $maskSource.GetPixel($column,$row)
                    $dark = $pixel.A -gt 80 -and (0.2126*$pixel.R + 0.7152*$pixel.G + 0.0722*$pixel.B) -lt 105
                    if ($dark) { $outline.SetPixel($column,$row,[Drawing.Color]::White) }
                }
                if ($dark -and $start -lt 0) { $start = $column }
                if (-not $dark -and $start -ge 0) {
                    $length = $column - $start
                    [void]$paths.Append("M$start $row h$length v1 h-$length z ")
                    $start = -1
                }
            }
        }
        $outline.Save((Join-Path $repositoryPath 'Logo/96-Outline-White.png'),[Drawing.Imaging.ImageFormat]::Png)
        $mask = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 96 96"><path fill="black" d="' + $paths.ToString() + '"/></svg>'
        [IO.File]::WriteAllText((Join-Path $repositoryPath "$iconDirectory/safari-pinned-tab.svg"),$mask,(New-Object Text.UTF8Encoding($false)))
    } finally { $maskSource.Dispose(); $outline.Dispose() }

    # macOS's small 1x frames need straight ARGB planes, not PNG in icp4/icp5.
    # Format verified against electron-userland/electron-builder-binaries
    # commit 40acc0b8ad6886b99d9878fd00ff475cce2ee4e3 by mmaietta.
    # Literal-only ICNS PackBits blocks keep this encoder independent and simple.
    $chunks = New-Object 'Collections.Generic.List[object]'
    foreach ($size in @(16,32)) {
        $smallImage = New-Object Drawing.Bitmap((Join-Path $repositoryPath "Logo/$size.png"))
        $payloadStream = New-Object IO.MemoryStream
        $payloadWriter = New-Object IO.BinaryWriter($payloadStream)
        try {
            $payloadWriter.Write([Text.Encoding]::ASCII.GetBytes('ARGB'))
            foreach ($channel in @('A','R','G','B')) {
                $plane = New-Object byte[]($size * $size)
                for ($row=0; $row -lt $size; $row++) {
                    for ($column=0; $column -lt $size; $column++) {
                        $pixel = $smallImage.GetPixel($column,$row)
                        $plane[$row*$size+$column] = $pixel.$channel
                    }
                }
                for ($position=0; $position -lt $plane.Length; $position+=128) {
                    $literalLength = [Math]::Min(128,$plane.Length-$position)
                    $payloadWriter.Write([byte]($literalLength-1))
                    $payloadWriter.Write($plane,$position,$literalLength)
                }
            }
            $payloadWriter.Flush()
            $type = if ($size -eq 16) { 'ic04' } else { 'ic05' }
            $chunks.Add(@{Type=$type;Bytes=$payloadStream.ToArray()})
        } finally { $payloadWriter.Dispose(); $payloadStream.Dispose(); $smallImage.Dispose() }
    }
    $types = [ordered]@{ic07=128;ic08=256;ic09=512;ic10=1024;ic11=32;ic12=64;ic13=256;ic14=512}
    foreach ($frame in $types.GetEnumerator()) {
        $chunks.Add(@{Type=$frame.Key;Bytes=[IO.File]::ReadAllBytes((Join-Path $repositoryPath "Logo/$($frame.Value).png"))})
    }
    $length = 8
    foreach ($chunk in $chunks) { $length += 8 + $chunk.Bytes.Length }
    $stream = [IO.File]::Create((Join-Path $repositoryPath 'distribution/macOS/Sonarr.app/Contents/Resources/sonarr.icns'))
    $writer = New-Object IO.BinaryWriter($stream)
    try {
        $writer.Write([Text.Encoding]::ASCII.GetBytes('icns')); Write-BigEndian $writer $length
        foreach ($chunk in $chunks) {
            $writer.Write([Text.Encoding]::ASCII.GetBytes($chunk.Type)); Write-BigEndian $writer ($chunk.Bytes.Length+8); $writer.Write([byte[]]$chunk.Bytes)
        }
    } finally { $writer.Dispose() }
    Write-Output 'Tanukarr artwork updated from Tanuki_Logo.zip.'
} finally {
    if ($script:sourceImage) { $script:sourceImage.Dispose() }
    $resolvedTemporary = [IO.Path]::GetFullPath($temporaryPath)
    $expectedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTemporary.StartsWith($expectedParent,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolvedTemporary).StartsWith('tanukarr-artwork-')) {
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
