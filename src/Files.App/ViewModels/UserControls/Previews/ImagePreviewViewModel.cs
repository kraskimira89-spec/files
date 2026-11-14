// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Microsoft.Extensions.Logging;
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
			var details = new List<FileProperty>();

			try
			{
				var storageFile = await PreviewFile.ToStorageFileAsync();
				var imageProperties = await storageFile.Properties.GetImagePropertiesAsync();

				if (imageProperties.Width > 0 && imageProperties.Height > 0)
					details.Add(GetFileProperty("PropertyDimensions", $"{imageProperties.Width} × {imageProperties.Height}"));

				if (imageProperties.CameraManufacturer is { Length: > 0 } manufacturer)
					details.Add(GetFileProperty("PropertyCameraManufacturer", manufacturer));

				if (imageProperties.CameraModel is { Length: > 0 } model)
					details.Add(GetFileProperty("PropertyCameraModel", model));

				if (imageProperties.DateTaken != default)
					details.Add(GetFileProperty("PropertyDateTaken", imageProperties.DateTaken.ToString("g")));
			}
			catch (Exception e)
			{
				App.Logger.LogWarning(e, "Image preview: could not read image metadata");
			}

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

			return details;
		}
	}
}
