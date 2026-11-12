// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class PDFPreviewViewModel : BasePreviewModel
	{
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

		public async override Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var fileStream = await PreviewFile.OpenReadAsync();
			var pdf = await PdfDocument.LoadFromStreamAsync(fileStream);
			_ = TryLoadPagesAsync(pdf, fileStream);

			var details = new List<FileProperty>
			{
				// Add the number of pages to the details
				GetFileProperty("PropertyPageCount", pdf.PageCount)
			};

			return details;
		}

		public async Task TryLoadPagesAsync(PdfDocument pdf, IRandomAccessStream fileStream)
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
				fileStream.Dispose();
			}
		}

		private async Task LoadPagesAsync(PdfDocument pdf)
		{
			// Cap pages and render width so the first page shows quickly in the info pane.
			var limit = Math.Clamp(pdf.PageCount, 0, Constants.PreviewPane.PDFPageLimit);
			var renderOptions = new PdfPageRenderOptions
			{
				DestinationWidth = 960,
			};

			for (uint i = 0; i < limit; i++)
			{
				// Stop loading if the user has cancelled
				if (LoadCancelledTokenSource.Token.IsCancellationRequested)
					return;

				using PdfPage page = pdf.GetPage(i);
				using InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
				// Skip PreparePageAsync — RenderToStreamAsync is enough for pane thumbnails.
				await page.RenderToStreamAsync(stream, renderOptions);

				await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
				{
					BitmapImage src = new();
					PageViewModel pageData = new()
					{
						PageImage = src,
						PageNumber = (int)i,
					};

					await src.SetSourceAsync(stream);
					Pages.Add(pageData);

					++PageCount;
				});

				// Yield so the first page can paint before continuing.
				if (i == 0)
					await Task.Yield();
			}

			LoadingBarVisibility = Visibility.Collapsed;
		}
	}

	public struct PageViewModel
	{
		public int PageNumber { get; set; }

		public BitmapImage PageImage { get; set; }
	}
}
