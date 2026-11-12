// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Win32;
using System.IO;
using Windows.Storage;

namespace Files.App.Helpers.Associations
{
	/// <summary>
	/// Builds a read-only preview of system handlers that Files would touch
	/// when used as the default file manager (no registry writes).
	/// </summary>
	public static class AssociationsPreviewService
	{
		/// <summary>
		/// When false, UI may show preview but must not call regedit / launcher install.
		/// </summary>
		public const bool IsDefaultAssociationApplyEnabled = true;

		private static IReadOnlyList<AssociationsPreviewItem>? _sessionCache;

		public static readonly string[] PackagedArchiveExtensions =
		[
			".zip",
			".7z",
			".rar",
			".tar",
			".jar",
			".mrpack",
			".gz",
		];

		private const string HasSeenAssociationsPreviewKey = "HasSeenAssociationsPreview";

		public static bool HasSeenAssociationsPreview
		{
			get => ApplicationData.Current.LocalSettings.Values.Get(HasSeenAssociationsPreviewKey, false);
			set => ApplicationData.Current.LocalSettings.Values[HasSeenAssociationsPreviewKey] = value;
		}

		public static async Task<IReadOnlyList<AssociationsPreviewItem>> BuildPreviewAsync()
		{
			if (_sessionCache is not null)
				return _sessionCache;

			var items = new List<AssociationsPreviewItem>
			{
				new()
				{
					Title = Strings.AssociationsPreviewItemFolderOpen.GetLocalizedResource(),
					CurrentHandler = GetShellCommandHandler(@"Folder\shell\open\command"),
					Detail = @"HKCU/HKCR\Folder\shell\open\command",
				},
				new()
				{
					Title = Strings.AssociationsPreviewItemFolderExplore.GetLocalizedResource(),
					CurrentHandler = GetShellCommandHandler(@"Folder\shell\explore\command"),
					Detail = @"HKCU/HKCR\Folder\shell\explore\command",
				},
				new()
				{
					Title = Strings.AssociationsPreviewItemWinE.GetLocalizedResource(),
					CurrentHandler = GetShellCommandHandler(@"CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}\shell\opennewwindow\command"),
					Detail = @"CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}\shell\opennewwindow\command",
				},
			};

			var archiveTasks = PackagedArchiveExtensions.Select(async extension =>
			{
				var probePath = Path.Combine(Path.GetTempPath(), $"files-assoc-preview{extension}");
				var handler = await Win32Helper.GetDefaultFileAssociationAsync(probePath).ConfigureAwait(false);
				return new AssociationsPreviewItem
				{
					Title = string.Format(Strings.AssociationsPreviewItemArchive.GetLocalizedResource(), extension),
					CurrentHandler = string.IsNullOrWhiteSpace(handler)
						? Strings.AssociationsPreviewCurrentHandlerUnknown.GetLocalizedResource()
						: handler!,
					Detail = extension,
				};
			});

			items.AddRange(await Task.WhenAll(archiveTasks).ConfigureAwait(false));
			_sessionCache = items;
			return items;
		}

		/// <summary>
		/// Clears the in-process preview cache after associations may have changed.
		/// </summary>
		public static void InvalidatePreviewCache() => _sessionCache = null;

		private static string GetShellCommandHandler(string relativeSubKey)
		{
			try
			{
				using var subkey = Registry.ClassesRoot.OpenSubKey(relativeSubKey);
				var command = subkey?.GetValue(string.Empty) as string;
				if (!string.IsNullOrWhiteSpace(command))
					return command!;
			}
			catch
			{
			}

			return Strings.AssociationsPreviewCurrentHandlerUnknown.GetLocalizedResource();
		}
	}
}
