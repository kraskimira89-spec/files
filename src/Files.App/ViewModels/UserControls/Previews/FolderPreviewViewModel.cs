// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Diagnostics;
using System.IO;
using Windows.Storage;

namespace Files.App.ViewModels.Previews
{
	public sealed class FolderPreviewViewModel
	{
		private readonly InfoPaneViewModel infoPaneViewModel = Ioc.Default.GetRequiredService<InfoPaneViewModel>();
		public ListedItem Item { get; }

		public BitmapImage? Thumbnail { get; set; } = new();

		private BaseStorageFolder? Folder { get; set; }

		public FolderPreviewViewModel(ListedItem item)
			=> Item = item;

		public Task LoadAsync()
			=> LoadPreviewAndDetailsAsync();

		private async Task LoadPreviewAndDetailsAsync()
		{
			var itemPath = Item.ItemPath!;

			var resolveSw = Stopwatch.StartNew();
			BaseStorageFolder? folder;
			try
			{
				// Fast path for ordinary local folders (avoids DriveHelpers root resolution).
				folder = new SystemStorageFolder(await StorageFolder.GetFolderFromPathAsync(itemPath));
			}
			catch
			{
				var rootItem = await FilesystemTasks.WrapNullable(() => DriveHelpers.GetRootFromPathAsync(itemPath));
				folder = await StorageFileExtensions.DangerousGetFolderFromPathAsync(itemPath, rootItem.Result);
			}
			var resolveMs = resolveSw.ElapsedMilliseconds;

			if (folder is null)
				throw new InvalidOperationException("The preview folder could not be opened.");

			Folder = folder;

			var iconSw = Stopwatch.StartNew();
			var result = await FileThumbnailHelper.GetIconAsync(
				Item.ItemPath,
				Constants.ShellIconSizes.Jumbo,
				true,
				IconOptions.None);
			var iconMs = iconSw.ElapsedMilliseconds;

			var bitmapSw = Stopwatch.StartNew();
			if (result is not null)
				Thumbnail = await result.ToBitmapAsync();
			var bitmapMs = bitmapSw.ElapsedMilliseconds;

			// If the selected item is the root of a drive (e.g. "C:\") or a cloud drive,
			// we do not need to load the properties below, since they will not be shown.
			// Drive properties will be obtained through the DrivesViewModel service.
			if (Item.IsDriveRoot || infoPaneViewModel?.SelectedDriveItem is not null)
			{
				App.Logger.LogDebug("Preview.Folder: path={Path}, resolve={ResolveMs}ms, icon={IconMs}ms, bitmap={BitmapMs}ms", LogPathHelper.RedactPath(Item.ItemPath), resolveMs, iconMs, bitmapMs);
				return;
			}

			var propsSw = Stopwatch.StartNew();
			var info = await folder.GetBasicPropertiesAsync();
			var propsMs = propsSw.ElapsedMilliseconds;

			Item.FileDetails =
			[
				GetFileProperty("PropertyItemCount", infoPaneViewModel?.DirectoryItemCount),
				GetFileProperty("PropertyDateModified", info.DateModified),
				GetFileProperty("PropertyDateCreated", info.DateCreated),
				GetFileProperty("PropertyParsingPath", folder.Path),
			];

			var gitSw = Stopwatch.StartNew();
			if (GitHelpers.IsRepositoryEx(Item.ItemPath, out var repoPath) &&
				!string.IsNullOrEmpty(repoPath))
			{
				var gitDirectory = GitHelpers.GetGitRepositoryPath(folder.Path, Path.GetPathRoot(folder.Path));
				var headName = (await GitHelpers.GetRepositoryHead(gitDirectory))?.Name ?? string.Empty;
				var repositoryName = GitHelpers.GetOriginRepositoryName(gitDirectory);

				if (!string.IsNullOrEmpty(gitDirectory))
					Item.FileDetails.Add(GetFileProperty("GitOriginRepositoryName", repositoryName));

				if (!string.IsNullOrWhiteSpace(headName))
					Item.FileDetails.Add(GetFileProperty("GitCurrentBranch", headName));
			}
			var gitMs = gitSw.ElapsedMilliseconds;

			App.Logger.LogDebug(
				"Preview.Folder: path={Path}, resolve={ResolveMs}ms, icon={IconMs}ms, bitmap={BitmapMs}ms, properties={PropertiesMs}ms, git={GitMs}ms",
				LogPathHelper.RedactPath(Item.ItemPath),
				resolveMs,
				iconMs,
				bitmapMs,
				propsMs,
				gitMs);
		}

		private static FileProperty GetFileProperty(string nameResource, object? value)
			=> new() { NameResource = nameResource, Value = value };
	}
}
