$ErrorActionPreference = 'Continue'
Get-Process -Name Files,Files.App.Server -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

$out = 'D:\Files\src\Files.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'
$srcTiles = 'D:\Files\src\Files.App\Assets\AppTiles'
$dstTiles = Join-Path $out 'Assets\AppTiles'

Write-Host '=== Copy AppTiles into package layout ==='
New-Item -ItemType Directory -Force -Path $dstTiles | Out-Null
robocopy $srcTiles $dstTiles /E /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
Write-Host "ROBOCOPY=$LASTEXITCODE"
Write-Host "Logo.ico present: $(Test-Path (Join-Path $dstTiles 'Dev\Logo.ico'))"
Write-Host "StoreLogo.scale-200 present: $(Test-Path (Join-Path $dstTiles 'Dev\StoreLogo.scale-200.png'))"
Write-Host "Square44 scale-200 present: $(Test-Path (Join-Path $dstTiles 'Dev\Square44x44Logo.scale-200.png'))"

Write-Host '=== Clear icon caches ==='
$iconCache = Join-Path $env:LOCALAPPDATA 'IconCache.db'
Remove-Item $iconCache -Force -ErrorAction SilentlyContinue
Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Explorer') -Filter 'iconcache_*.db' -ErrorAction SilentlyContinue |
	Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host '=== Re-register package ==='
# Force reinstall of same layout so Start picks up new logos
Get-AppxPackage FilesDev -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Add-AppxPackage -Register (Join-Path $out 'AppxManifest.xml')
Start-Sleep -Seconds 1

Write-Host '=== Restart Explorer to refresh Start/taskbar ==='
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) {
	Start-Process explorer.exe
}
Start-Sleep -Seconds 3

Write-Host '=== Launch FilesDev ==='
explorer.exe 'shell:AppsFolder\FilesDev_ykqwq8d6ps0ag!App'
Start-Sleep -Seconds 8
Get-Process -Name Files,Files.App.Server -ErrorAction SilentlyContinue |
	Format-Table Id, ProcessName, Responding, MainWindowTitle -AutoSize

Write-Host '=== StartApps icon path check ==='
Get-StartApps | Where-Object { $_.Name -match 'Files' } | Format-List
