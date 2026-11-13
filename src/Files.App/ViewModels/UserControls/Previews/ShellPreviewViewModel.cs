// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers;
using Files.App.ViewModels.Properties;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Content;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.DirectComposition;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT;
using WNDPROC = Windows.Win32.Extras.ManagedWNDPROC;

#pragma warning disable CS8305 // Type is for evaluation purposes only and is subject to change or removal in future updates.

namespace Files.App.ViewModels.Previews
{
	public sealed partial class ShellPreviewViewModel : BasePreviewModel
	{
		// Fields

		ContentExternalOutputLink? _contentExternalOutputLink;
		PreviewHandler? _previewHandler;
		ID3D11Device? _d3d11Device;
		ID3D11DeviceContext? _d3d11DeviceContext;
		IDCompositionDevice? _dCompositionDevice;
		IDCompositionVisual? _childVisual;
		object? _controlSurface;
		WNDCLASSEXW _windowClass;
		WNDPROC _windProc = null!;
		HWND _hWnd = HWND.Null;
		bool _isOfficePreview = false;
		bool _unloaded;
		volatile bool _stopRequested;
		Thread? _hostThread;
		TaskCompletionSource<HWND>? _windowReady;
		unsafe char* _pszClassName;

		// Constructor

		public ShellPreviewViewModel(ListedItem item) : base(item)
		{
		}

		// Methods

		public override Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
			=> Task.FromResult<List<FileProperty>>([]);

		public static unsafe Guid? FindPreviewHandlerFor(string extension, nint hwnd)
		{
			if (string.IsNullOrEmpty(extension))
				return null;

			try
			{
				fixed (char* pszAssoc = extension,
					pszExtra = "{8895b1c6-b41f-4c1c-a562-0d564250836f}",
					pszOutput = new char[1024])
				{
					PWSTR pwszAssoc = new(pszAssoc);
					PWSTR pwszExtra = new(pszExtra);
					PWSTR pwszOutput = new(pszOutput);
					uint cchOutput = 2024;

					// Try to find registered preview handler associated with specified extension name
					var res = PInvoke.AssocQueryString(
						ASSOCF.ASSOCF_NOTRUNCATE,
						ASSOCSTR.ASSOCSTR_SHELLEXTENSION,
						pwszAssoc,
						pwszExtra,
						pwszOutput,
						&cchOutput);

					return Guid.Parse(pwszOutput.ToString());
				}
			}
			catch
			{
				return null;
			}
		}

		public void SizeChanged(RECT size)
		{
			if (_hWnd != HWND.Null)
				PInvoke.SetWindowPos(_hWnd, new(0), size.left, size.top, size.Width, size.Height, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

			_previewHandler?.ResetBounds(new(0, 0, size.Width, size.Height));

			if (_contentExternalOutputLink is not null)
				_contentExternalOutputLink.PlacementVisual.Size = new(size.Width, size.Height);
		}

		private unsafe LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
		{
			if (msg is PInvoke.WM_CREATE)
			{
				var clsid = FindPreviewHandlerFor(Item.FileExtension!, hwnd);

				_isOfficePreview = new Guid?[]
				{
					Guid.Parse("84F66100-FF7C-4fb4-B0C0-02CD7FB668FE"), // preview handler for Word files
					Guid.Parse("65235197-874B-4A07-BDC5-E65EA825B718"), // preview handler for PowerPoint files
					Guid.Parse("00020827-0000-0000-C000-000000000046")  // preview handler for Excel files
				}.Contains(clsid);

				try
				{
					_previewHandler = new PreviewHandler(clsid!.Value, hwnd);
					_previewHandler.InitWithFileWithEveryWay(Item.ItemPath!);
					_previewHandler.DoPreview();
				}
				catch
				{
					// Do not call UnloadPreview during WM_CREATE: _hWnd is not assigned yet and the
					// window is still being created. Dispose the handler and fail the window creation.
					_previewHandler?.Dispose();
					_previewHandler = null;
					return new LRESULT(-1);
				}
			}
			else if (msg is PInvoke.WM_CLOSE)
			{
				PInvoke.DestroyWindow(hwnd);
				return new LRESULT(0);
			}
			else if (msg is PInvoke.WM_DESTROY)
			{
				if (_previewHandler is not null)
				{
					_previewHandler.Dispose();
					_previewHandler = null;
				}

				_hWnd = HWND.Null;
				PInvoke.PostQuitMessage(0);
			}

			return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
		}

		public async Task LoadPreviewAsync(UIElement presenter)
		{
			App.Logger.LogInformation($"ShellPreview.LoadPreview: Item={LogPathHelper.RedactPath(Item?.ItemPath)}");

			var parent = MainWindow.Instance.WindowHandle;
			var hInst = PInvoke.GetModuleHandle(default(PWSTR));
			var szClassName = $"{nameof(ShellPreviewViewModel)}-{Guid.NewGuid()}";

			_windowReady = new TaskCompletionSource<HWND>(TaskCreationOptions.RunContinuationsAsynchronously);
			_hostThread = new Thread(() => HostThreadProc(parent, hInst, szClassName));
			_hostThread.IsBackground = true;
			_hostThread.SetApartmentState(ApartmentState.STA);
			_hostThread.Start();

			HWND hwnd;
			try
			{
				hwnd = await _windowReady.Task;
			}
			catch
			{
				hwnd = HWND.Null;
			}

			if (hwnd == HWND.Null || _unloaded)
			{
				UnloadPreview();
				return;
			}

			_ = ChildWindowToXaml(parent, presenter);
		}

		private unsafe void HostThreadProc(nint parent, HINSTANCE hInst, string szClassName)
		{
			PInvoke.OleInitialize();
			try
			{
				_pszClassName = (char*)Marshal.StringToHGlobalUni(szClassName);
				_windProc = new(WndProc);
				var pWindProc = Marshal.GetFunctionPointerForDelegate<WNDPROC>(_windProc);
				var pfnWndProc = (delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT>)pWindProc;

				_windowClass = new WNDCLASSEXW()
				{
					cbSize = (uint)sizeof(WNDCLASSEXW),
					lpfnWndProc = pfnWndProc,
					hInstance = hInst,
					lpszClassName = _pszClassName,
					style = 0,
					hIcon = default,
					hIconSm = default,
					hCursor = default,
					hbrBackground = default,
					lpszMenuName = null,
					cbClsExtra = 0,
					cbWndExtra = 0,
				};

				PInvoke.RegisterClassEx(_windowClass);

				var szWindowName = "Preview";
				fixed (char* pszWindowName = szWindowName)
				{
					_hWnd = PInvoke.CreateWindowEx(
						WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_COMPOSITED,
						_pszClassName,
						pszWindowName,
						WINDOW_STYLE.WS_CHILD | WINDOW_STYLE.WS_CLIPSIBLINGS | WINDOW_STYLE.WS_VISIBLE,
						0, 0, 0, 0,
						new(parent),
						HMENU.Null,
						hInst);
				}

				_windowReady!.TrySetResult(_hWnd);

				if (_hWnd == HWND.Null || _stopRequested)
					return;

				MSG msg = default;
				while (PInvoke.GetMessage(out msg, HWND.Null, 0, 0))
				{
					PInvoke.TranslateMessage(&msg);
					PInvoke.DispatchMessage(&msg);
				}
			}
			catch (Exception ex)
			{
				_windowReady!.TrySetException(ex);
				App.Logger.LogWarning(ex, "ShellPreview host thread failed.");
			}
			finally
			{
				if (_hWnd != HWND.Null)
				{
					PInvoke.DestroyWindow(_hWnd);
					_hWnd = HWND.Null;
				}

				if (_previewHandler is not null)
				{
					_previewHandler.Dispose();
					_previewHandler = null;
				}

				PInvoke.UnregisterClass(_windowClass.lpszClassName, PInvoke.GetModuleHandle(default(PWSTR)));

				if (_pszClassName is not null)
				{
					Marshal.FreeHGlobal((nint)_pszClassName);
					_pszClassName = null;
				}

				PInvoke.OleUninitialize();
			}
		}

		private unsafe bool ChildWindowToXaml(nint parent, UIElement presenter)
		{
			D3D_DRIVER_TYPE[] driverTypes =
			[
				D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
				D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP,
			];

			HRESULT hr = default;

			// Create the D3D11 device
			foreach (var driverType in driverTypes)
			{
				hr = PInvoke.D3D11CreateDevice(
					null!, driverType, new(nint.Zero),
					D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
					ReadOnlySpan<D3D_FEATURE_LEVEL>.Empty, /* SDKVersion */ 7,
					out _d3d11Device,
					out _d3d11DeviceContext);

				if (hr.Succeeded)
					break;
			}

			if (_d3d11Device is null)
				return false;

			// Create the DComp device
			var pDXGIDevice = (IDXGIDevice)_d3d11Device;
			hr = PInvoke.DCompositionCreateDevice(pDXGIDevice, out _dCompositionDevice);
			if (hr.Failed || _dCompositionDevice is null)
				return false;

			// Create the visual
			hr = _dCompositionDevice.CreateVisual(out _childVisual);
			if (hr.Failed)
				return false;

			hr = _dCompositionDevice.CreateSurfaceFromHwnd(_hWnd, out _controlSurface);
			if (hr.Failed)
				return false;

			hr = _childVisual.SetContent(_controlSurface);
			if (hr.Failed || _childVisual is null || _controlSurface is null)
				return false;

			// Get the compositor and set the visual on it
			var compositor = ElementCompositionPreview.GetElementVisual(presenter).Compositor;
			_contentExternalOutputLink = ContentExternalOutputLink.Create(compositor);

			var target = _contentExternalOutputLink.As<Windows.Win32.Extras.IDCompositionTarget>();
			target.SetRoot(_childVisual);

			_contentExternalOutputLink.PlacementVisual.Size = new(0, 0);
			_contentExternalOutputLink.PlacementVisual.Scale = new(1 / (float)presenter.XamlRoot.RasterizationScale);
			ElementCompositionPreview.SetElementChildVisual(presenter, _contentExternalOutputLink.PlacementVisual);

			// Commit the all pending DComp commands
			_dCompositionDevice.Commit();

			var dwAttrib = Convert.ToUInt32(true);

			return
				PInvoke.DwmSetWindowAttribute(
					new((nint)_hWnd),
					DWMWINDOWATTRIBUTE.DWMWA_CLOAK,
					&dwAttrib,
					(uint)Marshal.SizeOf(dwAttrib))
				.Succeeded;
		}

		public unsafe void UnloadPreview()
		{
			// Main-window teardown and Unloaded can both request cleanup.
			if (_unloaded)
				return;
			_unloaded = true;
			_stopRequested = true;

			var hwnd = _windowReady is { Task.IsCompletedSuccessfully: true } ? _windowReady.Task.Result : HWND.Null;
			if (hwnd != HWND.Null)
			{
				PInvoke.PostMessage(hwnd, PInvoke.WM_CLOSE, default, default);
				App.Logger.LogInformation($"ShellPreview.UnloadPreview: HWND={((nint)hwnd)}, Item={LogPathHelper.RedactPath(Item?.ItemPath)}");
			}
			else
				App.Logger.LogInformation($"ShellPreview.UnloadPreview: HWND=, Item={LogPathHelper.RedactPath(Item?.ItemPath)}");

			_contentExternalOutputLink?.Dispose();
			_contentExternalOutputLink = null;

			// Release the composition chain leaf-to-root so nothing is left for finalizer-thread teardown
			ReleaseComObject(ref _childVisual);
			ReleaseComObject(ref _controlSurface);
			ReleaseComObject(ref _dCompositionDevice);
			ReleaseComObject(ref _d3d11DeviceContext);
			ReleaseComObject(ref _d3d11Device);
		}

		private static void ReleaseComObject<T>(ref T? comInterface) where T : class
		{
			if ((object?)comInterface is ComObject comObject)
				comObject.FinalRelease();

			comInterface = null;
		}

		public unsafe void PointerEntered(bool onPreview)
		{
			if (onPreview)
			{
				var dwAttrib = Convert.ToUInt32(false);

				PInvoke.DwmSetWindowAttribute(
					new((nint)_hWnd),
					DWMWINDOWATTRIBUTE.DWMWA_CLOAK,
					&dwAttrib,
					(uint)Marshal.SizeOf(dwAttrib));

				if (_isOfficePreview)
					PInvoke.SetWindowLongPtr(new((nint)_hWnd), WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, 0);
			}
			else
			{
				PInvoke.SetWindowLongPtr(
					new((nint)_hWnd),
					WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
					(nint)(WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_COMPOSITED));

				var dwAttrib = Convert.ToUInt32(true);

				PInvoke.DwmSetWindowAttribute(
					new((nint)_hWnd),
					DWMWINDOWATTRIBUTE.DWMWA_CLOAK,
					&dwAttrib,
					(uint)Marshal.SizeOf(dwAttrib));
			}
		}
	}
}

#pragma warning restore CS8305 // Type is for evaluation purposes only and is subject to change or removal in future updates.
