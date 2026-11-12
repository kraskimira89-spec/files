$root = 'D:\Files\src\Files.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'
Write-Host '=== Server folder ==='
$serverDir = Join-Path $root 'Files.App.Server'
Write-Host "Exists: $(Test-Path $serverDir)"
if (Test-Path $serverDir) {
	Get-ChildItem $serverDir | Format-Table Name, Length, LastWriteTime -AutoSize
} else {
	Write-Host 'MISSING Files.App.Server directory in package output'
}

Write-Host '=== Server project outputs ==='
Get-ChildItem 'D:\Files\src\Files.App.Server\bin' -Recurse -Filter 'Files.App.Server.exe' -ErrorAction SilentlyContinue |
	Format-Table FullName, Length, LastWriteTime -AutoSize

Write-Host '=== Manifest OutOfProcess path ==='
Select-String -Path (Join-Path $root 'AppxManifest.xml') -Pattern 'OutOfProcessServer|Files.App.Server' |
	ForEach-Object { $_.Line.Trim() }
