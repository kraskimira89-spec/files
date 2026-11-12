$ErrorActionPreference = 'Continue'
Get-Process -Name Files -ErrorAction SilentlyContinue | Stop-Process -Force

$serverExe = 'D:\Files\src\Files.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Files.App.Server\Files.App.Server.exe'
Write-Host ("Server present: {0} size={1}" -f (Test-Path $serverExe), ((Get-Item $serverExe -ErrorAction SilentlyContinue).Length))

Add-AppxPackage -Register 'D:\Files\src\Files.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\AppxManifest.xml' -ForceApplicationShutdown
Start-Sleep -Seconds 1

$code = @'
using System;
using System.Runtime.InteropServices;
public static class AppActivator5 {
  [DllImport("ole32.dll")]
  public static extern int CoCreateInstance([In] ref Guid clsid, IntPtr pUnkOuter, int dwClsCtx, [In] ref Guid riid, out IntPtr ppv);
  public static uint Activate(string aumid) {
    Guid clsid = new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C");
    Guid iid = new Guid("2e941141-7f97-4756-ba1d-9decde894a3d");
    IntPtr punk; CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out punk);
    IntPtr vtbl = Marshal.ReadIntPtr(punk);
    var activate = Marshal.GetDelegateForFunctionPointer<ActivateApplicationDelegate>(Marshal.ReadIntPtr(vtbl, IntPtr.Size * 3));
    uint pid; activate(punk, aumid, null, 0, out pid); return pid;
  }
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  private delegate int ActivateApplicationDelegate(IntPtr thisPtr, [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] string arguments, int options, out uint processId);
}
'@
Add-Type -TypeDefinition $code -Language CSharp
$pidLaunch = [AppActivator5]::Activate('FilesDev_ykqwq8d6ps0ag!App')
Write-Host ("PID={0}" -f $pidLaunch)

for ($i = 1; $i -le 40; $i++) {
	Start-Sleep -Milliseconds 500
	$p = Get-Process -Id $pidLaunch -ErrorAction SilentlyContinue
	if (-not $p) {
		Write-Host ("DEAD at {0}s" -f ($i * 0.5))
		$log = "$env:LOCALAPPDATA\Packages\FilesDev_ykqwq8d6ps0ag\LocalState\debug_unprivileged.log"
		if (Test-Path $log) {
			Write-Host '=== log tail ==='
			Get-Content $log -Tail 40
		}
		break
	}
	Write-Host ("{0}s alive title='{1}' handle={2}" -f ($i * 0.5), $p.MainWindowTitle, $p.MainWindowHandle)
	if ($p.MainWindowHandle -ne [IntPtr]::Zero) {
		Write-Host 'WINDOW_OK'
		Get-Process -Name 'Files*','Files.App.Server' -ErrorAction SilentlyContinue | Format-Table Id, ProcessName, Responding -AutoSize
		break
	}
}
