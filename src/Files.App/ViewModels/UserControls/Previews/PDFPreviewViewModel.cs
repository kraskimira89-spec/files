// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.ObjectModel;
using System.IO;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class PDFPreviewViewModel : BasePreviewModel
	{
		private const uint FirstPageRenderWidth = 512;
		private const uint LaterPageRenderWidth = 720;

		private Visibility loadingBarVisibility;
		public Visibility LoadingBarVisibility
		{
			get => loadingBarVisibility;
			private set => SetProperty(ref loadingBarVisibility, value);
		}

		// The pips pager will crash when binding directly to Pages.Count, so count the pages here
		private int pageCount;
		public int PageCount
		{
			get => pageCount;
			set => SetProperty(ref pageCount, value);
		}

		public ObservableCollection<PageViewModel> Pages { get; } = [];

		public PDFPreviewViewModel(ListedItem item)
			: base(item)
		{
			LoadingBarVisibility = Visibility.Visible;
		}

		/// <summary>
		/// Faster than the base path: open the StorageFile directly and start the first PDF page ASAP.
		/// </summary>
		public override async Task LoadAsync()
		{
			try
			{
				await EnsurePreviewFileAsync();

				// Show the Explorer thumbnail immediately so the pane is not blank.
				if (Item.FileImage is not null)
				{
					await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() =>
					{
						if (Pages.Count == 0)
						{
							Pages.Add(new PageViewModel
							{
								PageImage = Item.FileImage,
								PageNumber = 0,
							});
							PageCount = 1;
						}
					});
				}

				var details = await LoadPreviewAndDetailsAsync();
				Item.FileDetails = new ObservableCollection<FileProperty>(details);
			}
			catch (Exception e)
			{
				Debug.WriteLine(e);
			}
		}

		public async override Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var storageFile = await PreviewFile.ToStorageFileAsync();
			var pdf = await PdfDocument.LoadFromFileAsync(storageFile);
			_ = TryLoadPagesAsync(pdf);

			return
			[
				GetFileProperty("PropertyPageCount", pdf.PageCount)
			];
		}

		public async Task TryLoadPagesAsync(PdfDocument pdf)
		{
			try
			{
				await LoadPagesAsync(pdf);
			}
			catch (Exception e)
			{
				Debug.WriteLine(e);
			}
			finally
			{
				LoadingBarVisibility = Visibility.Collapsed;
			}
		}

		private async Task EnsurePreviewFileAsync()
		{
			if (Item.ItemFile is not null)
				return;

			var itemPath = Item.ItemPath
				?? throw new InvalidOperationException("The preview item path is missing.");

			try
			{
				// Fast path for ordinary local files (avoids DriveHelpers root resolution).
				Item.ItemFile = new SystemStorageFile(await StorageFile.GetFileFromPathAsync(itemPath));
			}
			catch
			{
				var rootItem = await FilesystemTasks.WrapNullable(() => DriveHelpers.GetRootFromPathAsync(itemPath));
				Item.ItemFile = await StorageFileExtensions.DangerousGetFileFromPathAsync(itemPath, rootItem.Result);
			}
		}

		private async Task LoadPagesAsync(PdfDocument pdf)
		{
			var limit = Math.Clamp(pdf.PageCount, 0u, (uint)Constants.PreviewPane.PDFPageLimit);
			if (limit == 0)
				return;

			// First page at lower resolution — this is what the user sees first.
			await RenderAndPublishPageAsync(pdf, 0, FirstPageRenderWidth, replaceExisting: true);

			LoadingBarVisibility = Visibility.Collapsed;

			// Remaining pages after the first paint.
			for (uint i = 1; i < limit; i++)
			{
				if (LoadCancelledTokenSource.Token.IsCancellationRequested)
					return;

				await RenderAndPublishPageAsync(pdf, i, LaterPageRenderWidth, replaceExisting: false);
			}
		}

		private async Task RenderAndPublishPageAsync(PdfDocument pdf, uint pageIndex, uint destinationWidth, bool replaceExisting)
		{
			using PdfPage page = pdf.GetPage(pageIndex);
			using InMemoryRandomAccessStream stream = new();
			await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = destinationWidth });

			await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
			{
				if (LoadCancelledTokenSource.Token.IsCancellationRequested)
					return;

				BitmapImage src = new();
				await src.SetSourceAsync(stream);

				var pageData = new PageViewModel
				{
					PageImage = src,
					PageNumber = (int)pageIndex,
				};

				if (replaceExisting && Pages.Count > 0 && Pages[0].PageNumber == 0)
				{
					Pages[0] = pageData;
				}
				else if (pageIndex == 0 && Pages.Count == 0)
				{
					Pages.Add(pageData);
					PageCount = 1;
				}
				else
				{
					Pages.Add(pageData);
					PageCount = Pages.Count;
				}
			});
		}
	}

	public struct PageViewModel
	{
		public int PageNumber { get; set; }

		public BitmapImage PageImage { get; set; }
	}
}
