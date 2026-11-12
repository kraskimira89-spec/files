$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$masterPath = 'D:\Files\src\Files.App\Assets\AppTiles\_work\master.jpg'
$outDir = 'D:\Files\src\Files.App\Assets\AppTiles\Dev'

function New-BitmapSquare([int]$size, [bool]$simplified) {
	$src = [System.Drawing.Image]::FromFile($masterPath)
	try {
		$bmp = New-Object System.Drawing.Bitmap $size, $size
		$bmp.SetResolution(96, 96)
		$g = [System.Drawing.Graphics]::FromImage($bmp)
		try {
			$g.Clear([System.Drawing.Color]::Black)
			$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
			$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
			$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
			$g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
			$padRatio = 0.08
			if (-not $simplified) { $padRatio = 0.10 }
			$pad = [int][Math]::Round($size * $padRatio)
			$dest = New-Object System.Drawing.Rectangle $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad)
			$g.DrawImage($src, $dest)
		}
		finally { $g.Dispose() }
		return $bmp
	}
	finally { $src.Dispose() }
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 256)
$streams = New-Object System.Collections.Generic.List[object]
foreach ($s in $sizes) {
	$simplified = $false
	if ($s -le 32) { $simplified = $true }
	$bmp = New-BitmapSquare $s $simplified
	$ms = New-Object System.IO.MemoryStream
	$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
	$bmp.Dispose()
	$streams.Add(@($s, $ms.ToArray()))
	$ms.Dispose()
}

$icoPath = Join-Path $outDir 'Logo.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter $fs
try {
	$bw.Write([uint16]0)
	$bw.Write([uint16]1)
	$bw.Write([uint16]$streams.Count)
	$offset = 6 + (16 * $streams.Count)
	foreach ($entry in $streams) {
		$s = [int]$entry[0]
		$data = [byte[]]$entry[1]
		$dim = 0
		if ($s -lt 256) { $dim = $s }
		$bw.Write([byte]$dim)
		$bw.Write([byte]$dim)
		$bw.Write([byte]0)
		$bw.Write([byte]0)
		$bw.Write([uint16]1)
		$bw.Write([uint16]32)
		$bw.Write([uint32]$data.Length)
		$bw.Write([uint32]$offset)
		$offset += $data.Length
	}
	foreach ($entry in $streams) {
		$bw.Write([byte[]]$entry[1])
	}
}
finally {
	$bw.Dispose()
	$fs.Dispose()
}
Write-Host "Wrote $icoPath size=$((Get-Item $icoPath).Length)"
