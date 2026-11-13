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
			var isDriveRoot = Item.IsDriveRoot || infoPaneViewModel?.SelectedDriveItem is not null;

			// Load folder, icon, properties and git info off the UI thread so the preview does
			// not compete with the folder listing for the UI thread.
			var load = await Task.Run(async () =>
			{
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

				var iconSw = Stopwatch.StartNew();
				var iconBytes = await FileThumbnailHelper.GetIconAsync(itemPath, Constants.ShellIconSizes.Jumbo, true, IconOptions.None);
				var iconMs = iconSw.ElapsedMilliseconds;

				DateTimeOffset? dateModified = null;
				DateTimeOffset? dateCreated = null;
				var gitDetails = new List<FileProperty>();
				long propsMs = 0;
				long gitMs = 0;

				if (!isDriveRoot)
				{
					var propsSw = Stopwatch.StartNew();
					var info = await folder.GetBasicPropertiesAsync();
					propsMs = propsSw.ElapsedMilliseconds;
					dateModified = info.DateModified;
					dateCreated = info.DateCreated;

					var gitSw = Stopwatch.StartNew();
					if (GitHelpers.IsRepositoryEx(itemPath, out var repoPath) && !string.IsNullOrEmpty(repoPath))
					{
						var gitDirectory = GitHelpers.GetGitRepositoryPath(folder.Path, Path.GetPathRoot(folder.Path));
						var headName = (await GitHelpers.GetRepositoryHead(gitDirectory))?.Name ?? string.Empty;
						var repositoryName = GitHelpers.GetOriginRepositoryName(gitDirectory);

						if (!string.IsNullOrEmpty(gitDirectory))
							gitDetails.Add(GetFileProperty("GitOriginRepositoryName", repositoryName));
						if (!string.IsNullOrWhiteSpace(headName))
							gitDetails.Add(GetFileProperty("GitCurrentBranch", headName));
					}
					gitMs = gitSw.ElapsedMilliseconds;
				}

				return (folder, iconBytes, dateModified, dateCreated, gitDetails, resolveMs, iconMs, propsMs, gitMs);
			});

			// Convert the icon and publish the details on the UI thread.
			var bitmapSw = Stopwatch.StartNew();
			if (load.iconBytes is not null)
				Thumbnail = await load.iconBytes.ToBitmapAsync(Constants.ShellIconSizes.Jumbo);
			var bitmapMs = bitmapSw.ElapsedMilliseconds;

			Folder = load.folder;

			if (isDriveRoot)
			{
				App.Logger.LogDebug("Preview.Folder: path={Path}, resolve={ResolveMs}ms, icon={IconMs}ms, bitmap={BitmapMs}ms", LogPathHelper.RedactPath(itemPath), load.resolveMs, load.iconMs, bitmapMs);
				return;
			}

			Item.FileDetails =
			[
				GetFileProperty("PropertyItemCount", infoPaneViewModel?.DirectoryItemCount),
				GetFileProperty("PropertyDateModified", load.dateModified),
				GetFileProperty("PropertyDateCreated", load.dateCreated),
				GetFileProperty("PropertyParsingPath", load.folder.Path),
			];

			foreach (var detail in load.gitDetails)
				Item.FileDetails.Add(detail);

			App.Logger.LogDebug(
				"Preview.Folder: path={Path}, resolve={ResolveMs}ms, icon={IconMs}ms, bitmap={BitmapMs}ms, properties={PropertiesMs}ms, git={GitMs}ms",
				LogPathHelper.RedactPath(itemPath),
				load.resolveMs,
				load.iconMs,
				bitmapMs,
				load.propsMs,
				load.gitMs);
		}

		private static FileProperty GetFileProperty(string nameResource, object? value)
			=> new() { NameResource = nameResource, Value = value };
	}
}
