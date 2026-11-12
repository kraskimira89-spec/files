$ErrorActionPreference = 'Continue'
Get-Process -Name Files,Files.App.Server -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

$vs = 'D:\VisualStudio\2026\Community\Common7\Tools\Microsoft.VisualStudio.DevShell.dll'
Import-Module $vs
Enter-VsDevShell -VsInstallPath 'D:\VisualStudio\2026\Community' -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null

msbuild 'D:\Files\src\Files.App\Files.App.csproj' -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
Write-Host "MSBUILD_EXIT=$LASTEXITCODE"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$server = 'D:\Files\src\Files.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Files.App.Server\Files.App.Server.exe'
if (-not (Test-Path $server)) {
	$src = 'D:\Files\src\Files.App.Server\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\publish'
	$dst = Split-Path $server -Parent
	New-Item -ItemType Directory -Force -Path $dst | Out-Null
	robocopy $src $dst /E /XF *.winmd *.pdb /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
}

Add-AppxPackage -Register 'D:\Files\src\Files.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\AppxManifest.xml' -ForceApplicationShutdown
Start-Sleep -Seconds 1
explorer.exe 'shell:AppsFolder\FilesDev_ykqwq8d6ps0ag!App'
Start-Sleep -Seconds 8
Get-Process -Name Files,Files.App.Server -ErrorAction SilentlyContinue | Format-Table Id, ProcessName, Responding, MainWindowTitle -AutoSize
