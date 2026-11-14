// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Microsoft.Extensions.Logging;
using SevenZip;
using System.Collections.Concurrent;
using System.IO;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class ArchivePreviewViewModel : BasePreviewModel
	{
		private static readonly ConcurrentDictionary<string, (long Stamp, IReadOnlyList<ArchiveEntry> Entries)> ArchiveCatalogCache = new(StringComparer.OrdinalIgnoreCase);

		private IReadOnlyList<ArchiveEntry> entries = [];
		private IReadOnlyList<ArchiveNode> rootNodes = [];
		private string searchText = string.Empty;
		private int sortIndex;

		public IReadOnlyList<ArchiveNode> RootNodes
		{
			get => rootNodes;
			private set => SetProperty(ref rootNodes, value);
		}

		public string SearchText
		{
			get => searchText;
			set
			{
				if (SetProperty(ref searchText, value))
					RebuildTree();
			}
		}

		public int SortIndex
		{
			get => sortIndex;
			set
			{
				if (SetProperty(ref sortIndex, value))
					RebuildTree();
			}
		}

		private ArchiveNode? selectedEntry;
		public ArchiveNode? SelectedEntry
		{
			get => selectedEntry;
			set
			{
				if (SetProperty(ref selectedEntry, value))
					OnPropertyChanged(nameof(CanExtractSelected));
			}
		}

		public bool CanExtractSelected => SelectedEntry is { EntryIndex: >= 0 };

		/// <summary>
		/// Extracts the selected archive entry next to the archive file.
		/// </summary>
		public async Task ExtractSelectedAsync()
		{
			if (SelectedEntry is not { EntryIndex: >= 0 } selected)
				return;

			var archivePath = Item.ItemPath!;
			var directory = Path.GetDirectoryName(archivePath) ?? string.Empty;
			var destination = Path.Combine(directory, selected.Name);

			try
			{
				using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
				using var extractor = new SevenZipExtractor(stream);
				await using var destinationStream = File.Create(destination);
				await extractor.ExtractFileAsync(selected.EntryIndex, destinationStream);

				App.Logger.LogInformation("Archive: extracted an entry from the preview");
			}
			catch (Exception e)
			{
				App.Logger.LogWarning(e, "Archive: could not extract an entry from the preview");
			}
		}

		public ArchivePreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public override async Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var details = new List<FileProperty>();

			var path = Item.ItemPath!;
			var stamp = GetFileStamp(path);

			if (stamp != 0 && ArchiveCatalogCache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
			{
				entries = cached.Entries;
				BuildDetailsAndTree(details);
				return details;
			}

			var zipResult = await FilesystemTasks.WrapNullable<SevenZipExtractor>(async () =>
			{
				var arch = new SevenZipExtractor(await PreviewFile.OpenStreamForReadAsync());

				// Force load archive (1665013614u)
				if (arch.ArchiveFileData is null)
				{
					arch.Dispose();
					return null;
				}

				return arch;
			});
			using var zipFile = zipResult.Result;

			if (zipFile is null)
			{
				// Loads the thumbnail preview
				_ = await base.LoadPreviewAndDetailsAsync();

				return details;
			}

			entries = zipFile.ArchiveFileData
				.Select(entry => new ArchiveEntry(
					entry.FileName,
					entry.IsDirectory ? string.Empty : entry.Size.ToLongSizeString(),
					entry.IsDirectory,
					entry.Size,
					(int)entry.Index))
				.ToList();

			if (stamp != 0)
				ArchiveCatalogCache[path] = (stamp, entries);

			BuildDetailsAndTree(details);

			// Loads the thumbnail preview
			_ = await base.LoadPreviewAndDetailsAsync();
			return details;
		}

		private void BuildDetailsAndTree(List<FileProperty> details)
		{
			var fileCount = 0;
			var folderCount = 0;
			ulong totalSize = 0;

			foreach (var entry in entries)
			{
				if (entry.IsDirectory)
					++folderCount;
				else
				{
					++fileCount;
					totalSize += entry.Size;
				}
			}

			RebuildTree();

			string propertyItemCount = Strings.DetailsArchiveItems.GetLocalizedFormatResource(entries.Count, fileCount, folderCount);
			details.Add(GetFileProperty("PropertyItemCount", propertyItemCount));
			details.Add(GetFileProperty("PropertyUncompressedSize", totalSize.ToLongSizeString()));
		}

		private static long GetFileStamp(string path)
		{
			try
			{
				var info = new FileInfo(path);
				return info.Exists ? info.LastWriteTimeUtc.Ticks ^ info.Length : 0;
			}
			catch
			{
				return 0;
			}
		}

		private void RebuildTree()
		{
			var search = SearchText?.Trim() ?? string.Empty;
			var root = new ArchiveNode(string.Empty, true);

			foreach (var entry in entries)
			{
				var parts = entry.Name.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length == 0)
					continue;

				var node = root;
				for (var i = 0; i < parts.Length; i++)
				{
					var isLeaf = i == parts.Length - 1;
					var name = parts[i];
					var child = node.Children.FirstOrDefault(existing => existing.Name == name);
					if (child is null)
					{
						child = new ArchiveNode(name, !isLeaf || entry.IsDirectory);
						node.Children.Add(child);
					}

					if (isLeaf && !entry.IsDirectory)
					{
						child.SizeText = entry.SizeText;
						child.Size = entry.Size;
						child.EntryIndex = entry.Index;
					}

					node = child;
				}
			}

			ComputeSizes(root);
			SortNode(root, SortIndex);

			if (search.Length > 0)
				FilterNode(root, search);

			foreach (var child in root.Children)
				child.IsExpanded = root.Children.Count <= 10;

			RootNodes = root.Children;
		}

		private static ulong ComputeSizes(ArchiveNode node)
		{
			if (!node.IsDirectory)
				return node.Size;

			var total = 0UL;
			foreach (var child in node.Children)
				total += ComputeSizes(child);

			node.Size = total;
			node.SizeText = total.ToLongSizeString();

			return total;
		}

		private static void SortNode(ArchiveNode node, int sortIndex)
		{
			node.Children.Sort((a, b) => CompareNodes(a, b, sortIndex));

			foreach (var child in node.Children)
				SortNode(child, sortIndex);
		}

		private static int CompareNodes(ArchiveNode a, ArchiveNode b, int sortIndex)
		{
			// Keep folders above files within the same level.
			if (a.IsDirectory != b.IsDirectory)
				return a.IsDirectory ? -1 : 1;

			return sortIndex switch
			{
				1 => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase),
				2 => a.Size.CompareTo(b.Size),
				3 => b.Size.CompareTo(a.Size),
				_ => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
			};
		}

		private static bool FilterNode(ArchiveNode node, string search)
		{
			node.Children.RemoveAll(child => !FilterNode(child, search));

			return node.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || node.Children.Count > 0;
		}
	}

	public sealed class ArchiveNode
	{
		public string Name { get; }
		public string DisplayName { get; }
		public bool IsDirectory { get; }
		public bool IsExpanded { get; set; }
		public ulong Size { get; set; }
		public string SizeText { get; set; } = string.Empty;
		public int EntryIndex { get; set; } = -1;
		public List<ArchiveNode> Children { get; } = [];

		public ArchiveNode(string name, bool isDirectory)
		{
			Name = name;
			IsDirectory = isDirectory;
			DisplayName = isDirectory ? $"{name}/" : name;
		}
	}

	public sealed record ArchiveEntry(string Name, string SizeText, bool IsDirectory, ulong Size, int Index);
}
