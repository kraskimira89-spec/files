$ErrorActionPreference = 'Continue'
Get-Process -Name Files,Files.App.Server,msbuild,VBCSCompiler -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Write-Host '=== Unregister FilesDev ==='
Get-AppxPackage FilesDev -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

$out = 'D:\Files\src\Files.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'
Write-Host '=== Rename output folder to release locks ==='
if (Test-Path $out) {
	$bak = "$out.lockbak_$(Get-Date -Format 'HHmmss')"
	try {
		Rename-Item $out $bak -ErrorAction Stop
		Write-Host "Renamed to $bak"
	} catch {
		Write-Host "Rename failed: $($_.Exception.Message)"
		# Best-effort delete of pri/dll that block
		Get-ChildItem $out -Filter '*.pri' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
	}
}

# Keep work assets out of PRI if SDK picks up everything under Assets
$work = 'D:\Files\src\Files.App\Assets\AppTiles\_work'
if (Test-Path $work) {
	Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
	Write-Host 'Removed AppTiles/_work'
}

Import-Module 'D:\VisualStudio\2026\Community\Common7\Tools\Microsoft.VisualStudio.DevShell.dll'
Enter-VsDevShell -VsInstallPath 'D:\VisualStudio\2026\Community' -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null

msbuild 'D:\Files\src\Files.App\Files.App.csproj' -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
Write-Host "MSBUILD_EXIT=$LASTEXITCODE"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$server = Join-Path $out 'Files.App.Server\Files.App.Server.exe'
if (-not (Test-Path $server)) {
	$src = 'D:\Files\src\Files.App.Server\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\publish'
	$dst = Join-Path $out 'Files.App.Server'
	New-Item -ItemType Directory -Force -Path $dst | Out-Null
	robocopy $src $dst /E /XF *.winmd *.pdb /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
}

Add-AppxPackage -Register (Join-Path $out 'AppxManifest.xml') -ForceApplicationShutdown
Start-Sleep -Seconds 1
explorer.exe 'shell:AppsFolder\FilesDev_ykqwq8d6ps0ag!App'
Start-Sleep -Seconds 9
Get-Process -Name Files,Files.App.Server -ErrorAction SilentlyContinue | Format-Table Id, ProcessName, Responding, MainWindowTitle -AutoSize
