# Export purple Dev AppTiles from master JPG with HQ resize + simplified tiny glyphs.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$masterPath = 'D:\Files\src\Files.App\Assets\AppTiles\_work\master.jpg'
$outDir = 'D:\Files\src\Files.App\Assets\AppTiles\Dev'
$workDir = 'D:\Files\src\Files.App\Assets\AppTiles\_work'

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

			# Keep ~10% padding; tiny sizes use less crop so glyph stays readable
			$padRatio = if ($simplified) { 0.08 } else { 0.10 }
			$pad = [int][Math]::Round($size * $padRatio)
			$dest = New-Object System.Drawing.Rectangle $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad)
			$g.DrawImage($src, $dest)

			if ($simplified -and $size -le 32) {
				# Slight contrast boost via overlay of source at higher opacity already; thicken by redraw inset
				$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(40, 158, 93, 229)), 1
				$g.DrawRectangle($pen, $pad, $pad, ($size - 2 * $pad - 1), ($size - 2 * $pad - 1))
				$pen.Dispose()
			}
		}
		finally { $g.Dispose() }
		return $bmp
	}
	finally { $src.Dispose() }
}

function Save-Png([System.Drawing.Bitmap]$bmp, [string]$path) {
	$dir = Split-Path $path -Parent
	if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
	$bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}

function Save-SizedPng([int]$size, [string]$path, [bool]$simplified) {
	$bmp = New-BitmapSquare $size $simplified
	try { Save-Png $bmp $path }
	finally { $bmp.Dispose() }
}

# Tile logical sizes -> pixel sizes at scales
$tiles = @(
	@{ Name = 'StoreLogo'; W = 50; H = 50 },
	@{ Name = 'Square44x44Logo'; W = 44; H = 44 },
	@{ Name = 'Small71x71Logo'; W = 71; H = 71 },
	@{ Name = 'Square150x150Logo'; W = 150; H = 150 },
	@{ Name = 'Wide310x150Logo'; W = 310; H = 150 },
	@{ Name = 'Large310x310Logo'; W = 310; H = 310 },
	@{ Name = 'SplashScreen'; W = 620; H = 300 },
	@{ Name = 'BadgeLogo'; W = 24; H = 24 }
)

$scales = @(100, 125, 150, 200, 400)

foreach ($tile in $tiles) {
	foreach ($scale in $scales) {
		$pw = [int][Math]::Round($tile.W * $scale / 100.0)
		$ph = [int][Math]::Round($tile.H * $scale / 100.0)
		$path = Join-Path $outDir ("{0}.scale-{1}.png" -f $tile.Name, $scale)

		$src = [System.Drawing.Image]::FromFile($masterPath)
		try {
			$bmp = New-Object System.Drawing.Bitmap $pw, $ph
			$bmp.SetResolution(96, 96)
			$g = [System.Drawing.Graphics]::FromImage($bmp)
			try {
				$g.Clear([System.Drawing.Color]::Black)
				$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
				$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
				$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
				$g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

				# Fit square icon into tile with padding
				$side = [Math]::Min($pw, $ph)
				$pad = [int][Math]::Round($side * 0.10)
				$drawSide = $side - 2 * $pad
				$x = [int](($pw - $drawSide) / 2)
				$y = [int](($ph - $drawSide) / 2)
				$g.DrawImage($src, (New-Object System.Drawing.Rectangle $x, $y, $drawSide, $drawSide))
			}
			finally { $g.Dispose() }
			Save-Png $bmp $path
			$bmp.Dispose()
		}
		finally { $src.Dispose() }
		Write-Host "Wrote $path ($pw x $ph)"
	}
}

# targetsize + altforms
$targets = @(16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)
foreach ($t in $targets) {
	$simplified = $t -le 32
	$base = Join-Path $outDir ("Square44x44Logo.targetsize-{0}.png" -f $t)
	Save-SizedPng $t $base $simplified
	Copy-Item $base (Join-Path $outDir ("Square44x44Logo.targetsize-{0}_altform-unplated.png" -f $t)) -Force
	Copy-Item $base (Join-Path $outDir ("Square44x44Logo.targetsize-{0}_altform-lightunplated_theme-light.png" -f $t)) -Force
	Write-Host "Wrote targetsize-$t"
}

# Build multi-size ICO (PNG-compressed entries via .NET icon writer is limited;
# write classic BMP-based ICO for 16,24,32,48,64,256)
function Save-Ico([string]$icoPath, [int[]]$sizes) {
	$streams = @()
	foreach ($s in $sizes) {
		$bmp = New-BitmapSquare $s ($s -le 32)
		$ms = New-Object System.IO.MemoryStream
		# ICO needs 32bpp BMP without BITMAPFILEHEADER compression quirks — use PNG in ICO (Vista+)
		$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
		$bmp.Dispose()
		$streams += , @($s, $ms.ToArray())
		$ms.Dispose()
	}

	$fs = [System.IO.File]::Create($icoPath)
	$bw = New-Object System.IO.BinaryWriter $fs
	try {
		$bw.Write([uint16]0)      # reserved
		$bw.Write([uint16]1)      # type icon
		$bw.Write([uint16]$streams.Count)
		$offset = 6 + (16 * $streams.Count)
		foreach ($entry in $streams) {
			$s = [int]$entry[0]
			$data = [byte[]]$entry[1]
			$dim = 0
			if ($s -lt 256) { $dim = $s }
			$bw.Write([byte]$dim) # width
			$bw.Write([byte]$dim) # height
			$bw.Write([byte]0)  # colors
			$bw.Write([byte]0)  # reserved
			$bw.Write([uint16]1) # planes
			$bw.Write([uint16]32) # bitcount
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
}

$icoPath = Join-Path $outDir 'Logo.ico'
Save-Ico $icoPath @(16, 20, 24, 32, 40, 48, 64, 256)
Write-Host "Wrote $icoPath"
Write-Host 'DONE'
