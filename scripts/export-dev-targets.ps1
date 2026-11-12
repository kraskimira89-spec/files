$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$masterPath = 'D:\Files\src\Files.App\Assets\AppTiles\_work\master.jpg'
$outDir = 'D:\Files\src\Files.App\Assets\AppTiles\Dev'

function New-BitmapSquare([int]$size) {
	$src = [System.Drawing.Image]::FromFile($masterPath)
	try {
		# Slightly crop edges of master if it has a soft outer glow frame
		$crop = [int][Math]::Round($src.Width * 0.04)
		$srcRect = New-Object System.Drawing.Rectangle $crop, $crop, ($src.Width - 2 * $crop), ($src.Height - 2 * $crop)

		$bmp = New-Object System.Drawing.Bitmap $size, $size
		$bmp.SetResolution(96, 96)
		$g = [System.Drawing.Graphics]::FromImage($bmp)
		try {
			$g.Clear([System.Drawing.Color]::Black)
			$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
			$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
			$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
			$g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

			# Tiny sizes: less padding so the folder fills the glyph
			$padRatio = 0.06
			if ($size -ge 48) { $padRatio = 0.10 }
			$pad = [int][Math]::Round($size * $padRatio)
			$dest = New-Object System.Drawing.Rectangle $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad)
			$g.DrawImage($src, $dest, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
		}
		finally { $g.Dispose() }
		return $bmp
	}
	finally { $src.Dispose() }
}

$targets = @(16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)
foreach ($t in $targets) {
	$bmp = New-BitmapSquare $t
	$path = Join-Path $outDir ("Square44x44Logo.targetsize-{0}.png" -f $t)
	$bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
	$bmp.Dispose()
	Copy-Item $path (Join-Path $outDir ("Square44x44Logo.targetsize-{0}_altform-unplated.png" -f $t)) -Force
	Copy-Item $path (Join-Path $outDir ("Square44x44Logo.targetsize-{0}_altform-lightunplated_theme-light.png" -f $t)) -Force
	Write-Host "Wrote targetsize-$t"
}
Write-Host 'DONE'
