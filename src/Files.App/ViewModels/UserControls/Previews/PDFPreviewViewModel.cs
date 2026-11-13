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
		private const int LookAheadPages = 1;

		private PdfDocument? _pdf;
		private readonly HashSet<int> _renderedPages = [];

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
			_pdf = await PdfDocument.LoadFromFileAsync(storageFile);

			var limit = (int)Math.Clamp(_pdf.PageCount, 0u, (uint)Constants.PreviewPane.PDFPageLimit);

			if (limit == 0)
			{
				LoadingBarVisibility = Visibility.Collapsed;
				return [ GetFileProperty("PropertyPageCount", _pdf.PageCount) ];
			}

			// Pre-populate placeholders so the FlipView and PipsPager know the full page count immediately.
			await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() =>
			{
				// Page 0 may already hold the shell-thumbnail placeholder from LoadAsync.
				while (Pages.Count < limit)
					Pages.Add(new PageViewModel { PageNumber = Pages.Count, PageImage = null });
				PageCount = limit;
			});

			_ = TryRenderPageAsync(0, FirstPageRenderWidth);

			return [ GetFileProperty("PropertyPageCount", _pdf.PageCount) ];
		}

		public async Task RequestPageAsync(int pageIndex)
		{
			if (_pdf is null || pageIndex < 0 || pageIndex >= PageCount)
				return;

			var last = Math.Min(pageIndex + LookAheadPages, PageCount - 1);
			for (var i = pageIndex; i <= last; i++)
			{
				if (LoadCancelledTokenSource.Token.IsCancellationRequested)
					return;
				await TryRenderPageAsync(i, i == 0 ? FirstPageRenderWidth : LaterPageRenderWidth);
			}
		}

		private async Task TryRenderPageAsync(int pageIndex, uint destinationWidth)
		{
			if (_pdf is null || !_renderedPages.Add(pageIndex))
				return;

			try
			{
				await RenderAndPublishPageAsync(_pdf, (uint)pageIndex, destinationWidth);
			}
			catch (Exception e)
			{
				_renderedPages.Remove(pageIndex);
				Debug.WriteLine(e);
			}
			finally
			{
				if (pageIndex == 0)
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

		private async Task RenderAndPublishPageAsync(PdfDocument pdf, uint pageIndex, uint destinationWidth)
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

				if (pageIndex < Pages.Count)
					Pages[(int)pageIndex] = pageData;
				else
					Pages.Add(pageData);
			});
		}
	}

	public struct PageViewModel
	{
		public int PageNumber { get; set; }

		public BitmapImage? PageImage { get; set; }
	}
}
