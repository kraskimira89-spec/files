// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.UserControls.FilePreviews;
using Files.App.ViewModels.Properties;

namespace Files.App.ViewModels.Previews
{
	public partial class TextPreviewViewModel : BasePreviewModel
	{
		private string? textValue;
		public string? TextValue
		{
			get => textValue;
			protected set => SetProperty(ref textValue, value);
		}

		public TextPreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public async override Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var details = new List<FileProperty>();

			try
			{
				var text = TextValue ?? await ReadFileAsTextAsync(PreviewFile);
				var (lineCount, wordCount) = CountPreviewTextStats(text, countWords: true);

				details.Add(GetFileProperty("PropertyLineCount", lineCount));
				details.Add(GetFileProperty("PropertyWordCount", wordCount));

				TextValue = text.Left(Constants.PreviewPane.TextCharacterLimit);
			}
			catch (Exception e)
			{
				Debug.WriteLine(e);
			}

			return details;
		}

		public static async Task<TextPreview?> TryLoadAsTextAsync(ListedItem item)
		{
			string? extension = item.FileExtension?.ToLowerInvariant();
			if (ExcludedExtensions(extension) || item.FileSizeBytes is 0 or > Constants.PreviewPane.TryLoadAsTextSizeLimit)
				return null;

			try
			{
				item.ItemFile = await StorageFileExtensions.DangerousGetFileFromPathAsync(item.ItemPath!);
				if (item.ItemFile is not { } itemFile)
					return null;

				var text = await ReadFileAsTextAsync(itemFile);
				bool isBinaryFile = text.Contains("\0\0\0\0", StringComparison.Ordinal);

				if (isBinaryFile)
					return null;

				var model = new TextPreviewViewModel(item) { TextValue = text };
				await model.LoadAsync();

				return new TextPreview(model);
			}
			catch
			{
				return null;
			}
		}

		private static bool ExcludedExtensions(string? extension)
			=> extension is ".iso";
	}
}
