$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$masterSrc = 'C:\Users\user\.cursor\projects\d-Files\assets\files-dev-icon-rounded-square.jpg'
$workDir = 'D:\Files\src\Files.App\Assets\AppTiles\_work'
$outDir = 'D:\Files\src\Files.App\Assets\AppTiles\Dev'
New-Item -ItemType Directory -Force -Path $workDir | Out-Null
Copy-Item $masterSrc (Join-Path $workDir 'master.jpg') -Force
$masterPath = Join-Path $workDir 'master.jpg'

function New-RoundedSquareBitmap([int]$size) {
	$bmp = New-Object System.Drawing.Bitmap $size, $size
	$bmp.SetResolution(96, 96)
	$g = [System.Drawing.Graphics]::FromImage($bmp)
	try {
		$g.Clear([System.Drawing.Color]::Black)
		$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
		$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
		$g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

		# Draw a crisp purple rounded square (do not rely on soft AI JPG alone for tiny sizes)
		$pad = [Math]::Max(1, [int][Math]::Round($size * 0.12))
		$side = $size - 2 * $pad
		$radius = [Math]::Max(2, [int][Math]::Round($side * 0.22))
		$rect = New-Object System.Drawing.RectangleF $pad, $pad, $side, $side
		$path = New-Object System.Drawing.Drawing2D.GraphicsPath
		$d = $radius * 2
		$path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
		$path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
		$path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
		$path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
		$path.CloseFigure()

		# #9B5DE5 with slight top highlight
		$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (
			(New-Object System.Drawing.Point $pad, $pad),
			(New-Object System.Drawing.Point $pad, ($pad + $side)),
			[System.Drawing.Color]::FromArgb(255, 198, 143, 230),
			[System.Drawing.Color]::FromArgb(255, 94, 35, 157)
		)
		$g.FillPath($brush, $path)
		$brush.Dispose()
		$path.Dispose()
	}
	finally { $g.Dispose() }
	return $bmp
}

function Save-Png([System.Drawing.Bitmap]$bmp, [string]$path) {
	$bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}

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
		$side = [Math]::Min($pw, $ph)
		$icon = New-RoundedSquareBitmap $side
		$bmp = New-Object System.Drawing.Bitmap $pw, $ph
		$g = [System.Drawing.Graphics]::FromImage($bmp)
		try {
			$g.Clear([System.Drawing.Color]::Black)
			$x = [int](($pw - $side) / 2)
			$y = [int](($ph - $side) / 2)
			$g.DrawImage($icon, $x, $y, $side, $side)
		}
		finally { $g.Dispose(); $icon.Dispose() }
		Save-Png $bmp $path
		$bmp.Dispose()
		Write-Host "Wrote $path"
	}
}

$targets = @(16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)
foreach ($t in $targets) {
	$bmp = New-RoundedSquareBitmap $t
	$base = Join-Path $outDir ("Square44x44Logo.targetsize-{0}.png" -f $t)
	Save-Png $bmp $base
	$bmp.Dispose()
	Copy-Item $base (Join-Path $outDir ("Square44x44Logo.targetsize-{0}_altform-unplated.png" -f $t)) -Force
	Copy-Item $base (Join-Path $outDir ("Square44x44Logo.targetsize-{0}_altform-lightunplated_theme-light.png" -f $t)) -Force
	Write-Host "Wrote targetsize-$t"
}

# ICO
$sizes = @(16, 20, 24, 32, 40, 48, 64, 256)
$streams = New-Object System.Collections.Generic.List[object]
foreach ($s in $sizes) {
	$bmp = New-RoundedSquareBitmap $s
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
	$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$streams.Count)
	$offset = 6 + (16 * $streams.Count)
	foreach ($entry in $streams) {
		$s = [int]$entry[0]; $data = [byte[]]$entry[1]
		$dim = 0; if ($s -lt 256) { $dim = $s }
		$bw.Write([byte]$dim); $bw.Write([byte]$dim)
		$bw.Write([byte]0); $bw.Write([byte]0)
		$bw.Write([uint16]1); $bw.Write([uint16]32)
		$bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
		$offset += $data.Length
	}
	foreach ($entry in $streams) { $bw.Write([byte[]]$entry[1]) }
}
finally { $bw.Dispose(); $fs.Dispose() }
Write-Host "Wrote $icoPath"
Remove-Item $workDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'DONE'
