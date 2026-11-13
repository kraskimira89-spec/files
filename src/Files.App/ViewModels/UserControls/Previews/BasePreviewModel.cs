// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Diagnostics;
using System.IO;
using System.Text;
using Files.App.ViewModels.Properties;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace Files.App.ViewModels.Previews
{
	public abstract partial class BasePreviewModel : ObservableObject
	{
		private readonly IUserSettingsService userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();

		public ListedItem Item { get; }

		protected BaseStorageFile PreviewFile
			=> Item.ItemFile ?? throw new InvalidOperationException("The preview file has not been loaded.");

		private BitmapImage? fileImage;
		public BitmapImage? FileImage
		{
			get => fileImage;
			protected set => SetProperty(ref fileImage, value);
		}

		public List<FileProperty>? DetailsFromPreview { get; set; }

		/// <summary>
		/// This is cancelled when the user has selected another file or closed the pane.
		/// </summary>
		public CancellationTokenSource LoadCancelledTokenSource { get; } = new CancellationTokenSource();

		public BasePreviewModel(ListedItem item) : base()
			=> Item = item;

		public delegate void LoadedEventHandler(object? sender, EventArgs e);

		public static Task LoadDetailsOnlyAsync(ListedItem item, List<FileProperty>? details = null)
		{
			var temp = new DetailsOnlyPreviewModel(item) { DetailsFromPreview = details };
			return temp.LoadAsync();
		}

		/// <summary>
		/// Reads preview text with BOM / UTF-8 / Windows-1251 detection so Cyrillic files without a BOM render correctly.
		/// </summary>
		public static async Task<string> ReadFileAsTextAsync(BaseStorageFile file, int maxLength = 10 * 1024 * 1024)
		{
			using var inputStream = await file.OpenReadAsync()
				?? throw new IOException("The file could not be opened for reading.");
			await using var stream = inputStream.AsStreamForRead();

			// Cap byte reads; UTF-8 worst-case is 4 bytes per char for the character limit.
			var byteLimit = maxLength < 0 ? int.MaxValue : Math.Min(checked(maxLength * 4), 32 * 1024 * 1024);
			using var memory = new MemoryStream(Math.Min(byteLimit, 64 * 1024));
			var buffer = new byte[8192];
			while (memory.Length < byteLimit)
			{
				var toRead = (int)Math.Min(buffer.Length, byteLimit - memory.Length);
				var read = await stream.ReadAsync(buffer.AsMemory(0, toRead));
				if (read <= 0)
					break;

				memory.Write(buffer, 0, read);
			}

			var bytes = memory.ToArray();
			if (bytes.Length == 0)
				return string.Empty;

			var encoding = DetectPreviewTextEncoding(bytes);
			var text = encoding.GetString(bytes);
			if (maxLength >= 0 && text.Length > maxLength)
				return text[..maxLength];

			return text;
		}

		/// <summary>
		/// Counts lines (same as <c>Split('\n').Length</c>) and optionally words
		/// (same as <c>Split([' ', '\n'], RemoveEmptyEntries).Length</c>) in one pass.
		/// </summary>
		protected static (int LineCount, int WordCount) CountPreviewTextStats(string text, bool countWords)
		{
			var lineCount = 1;
			var wordCount = 0;
			var inWord = false;

			foreach (var ch in text)
			{
				if (ch == '\n')
				{
					lineCount++;
					inWord = false;
					continue;
				}

				if (!countWords)
					continue;

				if (ch == ' ')
				{
					inWord = false;
				}
				else if (!inWord)
				{
					inWord = true;
					wordCount++;
				}
			}

			return (lineCount, wordCount);
		}

		private static Encoding DetectPreviewTextEncoding(ReadOnlySpan<byte> bytes)
		{
			if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
				return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

			if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
				return Encoding.Unicode;

			if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
				return Encoding.BigEndianUnicode;

			var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
			try
			{
				_ = utf8Strict.GetString(bytes);
				return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
			}
			catch (DecoderFallbackException)
			{
				// Not valid UTF-8 — common for Russian Windows-1251 text without a BOM.
			}

			var utf8Loose = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
			var windows1251 = Encoding.GetEncoding(1251);
			var asUtf8 = utf8Loose.GetString(bytes);
			var as1251 = windows1251.GetString(bytes);

			return ScoreDecodedPreviewText(as1251) > ScoreDecodedPreviewText(asUtf8)
				? windows1251
				: utf8Loose;
		}

		private static int ScoreDecodedPreviewText(string text)
		{
			var score = 0;
			foreach (var ch in text)
			{
				if (ch == '\uFFFD')
					score -= 40;
				else if (ch is >= '\u0400' and <= '\u04FF')
					score += 3; // Cyrillic
				else if (char.IsControl(ch) && ch is not ('\r' or '\n' or '\t'))
					score -= 4;
			}

			return score;
		}

		/// <summary>
		/// Call this function when you are ready to load the preview and details.
		/// Override if you need custom loading code.
		/// </summary>
		/// <returns>The task to run</returns>
		public virtual async Task LoadAsync()
		{
			List<FileProperty> detailsFull = [];
			var totalSw = Stopwatch.StartNew();
			long resolveMs = 0;
			long contentMs = 0;
			long propertiesMs = 0;

			if (Item.ItemFile is null)
			{
				var resolveSw = Stopwatch.StartNew();
				var itemPath = Item.ItemPath!;
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

				resolveMs = resolveSw.ElapsedMilliseconds;
			}

			await Task.Run(async () =>
			{
				var contentSw = Stopwatch.StartNew();
				var contentTask = LoadPreviewAndDetailsAsync();

				Task<List<FileProperty>?>? propertiesTask = null;
				if (userSettingsService.InfoPaneSettingsService.SelectedTab == InfoPaneTabs.Details)
					propertiesTask = GetSystemFilePropertiesAsync();

				DetailsFromPreview = await contentTask;
				contentMs = contentSw.ElapsedMilliseconds;

				if (propertiesTask is not null)
				{
					// Add the details from the preview function, then the system file properties
					DetailsFromPreview?.ForEach(i => detailsFull.Add(i));
					var propertiesSw = Stopwatch.StartNew();
					List<FileProperty>? props = await propertiesTask;
					propertiesMs = propertiesSw.ElapsedMilliseconds;
					if (props is not null)
						detailsFull.AddRange(props);
				}
			});

			Item.FileDetails = new System.Collections.ObjectModel.ObservableCollection<FileProperty>(detailsFull);

			App.Logger.LogDebug(
				"Preview.LoadAsync: path={Path}, resolve={ResolveMs}ms, content={ContentMs}ms, properties={PropertiesMs}ms, total={TotalMs}ms",
				LogPathHelper.RedactPath(Item.ItemPath),
				resolveMs,
				contentMs,
				propertiesMs,
				totalSw.ElapsedMilliseconds);
		}

		/// <summary>
		/// Override this and place the code to load the file preview here.
		/// You can return details that may have been obtained while loading the preview (eg. word count).
		/// This details will be displayed *before* the system file properties.
		/// If there are none, return an empty list.
		/// </summary>
		/// <returns>A list of details</returns>
		public async virtual Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var iconSw = Stopwatch.StartNew();

			// First paint from the list thumbnail immediately, then fetch the Jumbo icon and swap it in.
			var reusedListImage = Item.FileImage is not null;
			if (reusedListImage)
				await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() => FileImage ??= Item.FileImage);

			var result = await FileThumbnailHelper.GetIconAsync(
				Item.ItemPath,
				Constants.ShellIconSizes.Jumbo,
				false,
				IconOptions.None);

			if (result is not null)
				await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () => FileImage = await result.ToBitmapAsync(Constants.ShellIconSizes.Jumbo));
			else
				FileImage ??= await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() => new BitmapImage());

			App.Logger.LogDebug(
				"Preview.Icon: path={Path}, reusedListImage={ReusedListImage}, icon={IconMs}ms",
				LogPathHelper.RedactPath(Item.ItemPath),
				reusedListImage,
				iconSw.ElapsedMilliseconds);

			return [];
		}

		/// <summary>
		/// Override this if the preview control needs to handle the unloaded event.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		public virtual void PreviewControlBase_Unloaded(object? sender, RoutedEventArgs e)
			=> LoadCancelledTokenSource.Cancel();

		protected static FileProperty GetFileProperty(string nameResource, object? value)
			=> new() { NameResource = nameResource, Value = value };

		private async Task<List<FileProperty>?> GetSystemFilePropertiesAsync()
		{
			if (Item.IsShortcut)
				return null;
			if (Item.ItemFile is null)
				throw new InvalidOperationException("The preview item could not be opened as a file.");

			var list = await FileProperty.RetrieveAndInitializePropertiesAsync(Item.ItemFile,
				Constants.ResourceFilePaths.PreviewPaneDetailsPropertiesJsonPath);

			var address = list.Find(x => x.ID is "address")
				?? throw new InvalidDataException("The preview property definition is missing the address field.");
			var latitude = list.Find(x => x.Property is "System.GPS.LatitudeDecimal")
				?? throw new InvalidDataException("The preview property definition is missing the latitude field.");
			var longitude = list.Find(x => x.Property is "System.GPS.LongitudeDecimal")
				?? throw new InvalidDataException("The preview property definition is missing the longitude field.");

			// Adds the value for the file tag
			var fileTag = list.FirstOrDefault(x => x.ID is "filetag")
				?? throw new InvalidDataException("The preview property definition is missing the file tag field.");
			fileTag.Value = Item.FileTagsUI is not null
				? string.Join(',', Item.FileTagsUI.Select(x => x.Name))
				: null;

			var result = list.Where(i => i.ValueText is not null || i.ID is "address").ToList();

			// Reverse geocode off the hot path — update Address when Bing responds.
			_ = FillAddressAsync(address, latitude, longitude);

			return result;
		}

		private static async Task FillAddressAsync(FileProperty address, FileProperty latitude, FileProperty longitude)
		{
			try
			{
				address.Value = await LocationHelpers.GetAddressFromCoordinatesAsync(
					(double?)latitude.Value,
					(double?)longitude.Value);
			}
			catch
			{
			}
		}

		private sealed partial class DetailsOnlyPreviewModel : BasePreviewModel
		{
			public DetailsOnlyPreviewModel(ListedItem item) : base(item) { }

			public override Task<List<FileProperty>> LoadPreviewAndDetailsAsync() => Task.FromResult(DetailsFromPreview ?? []);
		}
	}
}
