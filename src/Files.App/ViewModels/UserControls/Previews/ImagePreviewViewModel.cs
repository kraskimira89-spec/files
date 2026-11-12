// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class ImagePreviewViewModel : BasePreviewModel
	{
		private ImageSource? imageSource;
		public ImageSource? ImageSource
		{
			get => imageSource;
			private set => SetProperty(ref imageSource, value);
		}

		public ImagePreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public override async Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			// Paint the list thumbnail immediately so the pane is not blank while the full image decodes.
			if (Item.FileImage is not null)
			{
				await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() =>
				{
					ImageSource = Item.FileImage;
				});
			}

			using IRandomAccessStream stream = await PreviewFile.OpenAsync(FileAccessMode.Read);
			LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();

			await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
			{
				BitmapImage bitmap = new()
				{
					// Cap decode size so large photos open faster in the pane.
					DecodePixelWidth = 2048,
				};
				await bitmap.SetSourceAsync(stream);
				LoadCancelledTokenSource.Token.ThrowIfCancellationRequested();
				ImageSource = bitmap;
			});

			return [];
		}
	}
}
