$ErrorActionPreference = 'Continue'
Get-Process -Name Files,Files.App.Server -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Get-AppxPackage FilesDev -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

$out = 'D:\Files\src\Files.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'
if (Test-Path $out) {
	$bak = "$out.lockbak_$(Get-Date -Format 'HHmmss')"
	Rename-Item $out $bak -ErrorAction SilentlyContinue
}

Import-Module 'D:\VisualStudio\2026\Community\Common7\Tools\Microsoft.VisualStudio.DevShell.dll'
Enter-VsDevShell -VsInstallPath 'D:\VisualStudio\2026\Community' -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null

msbuild 'D:\Files\src\Files.App\Files.App.csproj' -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
Write-Host "MSBUILD_EXIT=$LASTEXITCODE"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not (Test-Path (Join-Path $out 'Assets\AppTiles\Dev\Logo.ico'))) {
	robocopy 'D:\Files\src\Files.App\Assets\AppTiles' (Join-Path $out 'Assets\AppTiles') /E /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
}
if (-not (Test-Path (Join-Path $out 'Files.App.Server\Files.App.Server.exe'))) {
	robocopy 'D:\Files\src\Files.App.Server\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\publish' (Join-Path $out 'Files.App.Server') /E /XF *.winmd *.pdb /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
}

Add-AppxPackage -Register (Join-Path $out 'AppxManifest.xml')
Start-Sleep -Seconds 1
explorer.exe 'shell:AppsFolder\FilesDev_ykqwq8d6ps0ag!App'
Start-Sleep -Seconds 7
Get-Process -Name Files -ErrorAction SilentlyContinue | Format-Table Id, Responding, MainWindowTitle -AutoSize
