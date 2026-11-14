// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Microsoft.Extensions.Logging;
using SevenZip;
using System.Collections.Concurrent;
using System.IO;

namespace Files.App.ViewModels.Previews
{
	/// <summary>
	/// Renders an archive's contents as a lazily materialized folder tree. Each folder builds its
	/// direct children on first access, so a 100k-entry archive no longer allocates every node up front.
	/// </summary>
	public sealed partial class ArchivePreviewViewModel : BasePreviewModel
	{
		private static readonly ConcurrentDictionary<string, (long Stamp, IReadOnlyList<ArchiveEntry> Entries)> ArchiveCatalogCache = new(StringComparer.OrdinalIgnoreCase);

		private IReadOnlyList<ArchiveEntry> entries = [];
		private IReadOnlyDictionary<string, ulong> folderSizes = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
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

		public ArchivePreviewViewModel(ListedItem item)
			: base(item)
		{
		}

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

			folderSizes = ComputeFolderSizes();
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
			var root = new ArchiveNode(string.Empty, string.Empty, true, BuildChildren);
			var children = root.Children;

			foreach (var child in children)
				child.IsExpanded = children.Count <= 10;

			RootNodes = children;
		}

		/// <summary>
		/// Builds the direct children of a folder on demand (called lazily by <see cref="ArchiveNode.Children"/>).
		/// </summary>
		private IReadOnlyList<ArchiveNode> BuildChildren(string parentPath)
		{
			var prefix = parentPath.Length == 0 ? string.Empty : parentPath + "/";
			var search = SearchText?.Trim() ?? string.Empty;
			var byName = new Dictionary<string, ArchiveNode>(StringComparer.OrdinalIgnoreCase);
			var nodes = new List<ArchiveNode>();

			foreach (var entry in entries)
			{
				var full = entry.Name.Replace('\\', '/');
				if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
					continue;

				var rest = full[prefix.Length..];
				if (rest.Length == 0)
					continue;

				var slash = rest.IndexOf('/');
				var isDirect = slash < 0;
				var name = isDirect ? rest : rest[..slash];
				if (name.Length == 0)
					continue;

				if (!byName.TryGetValue(name, out var node))
				{
					var nodePath = prefix + name;
					node = new ArchiveNode(name, nodePath, !isDirect || entry.IsDirectory, BuildChildren);
					byName[name] = node;
					nodes.Add(node);
				}

				if (isDirect && !entry.IsDirectory)
				{
					node.SizeText = entry.SizeText;
					node.Size = entry.Size;
					node.EntryIndex = entry.Index;
				}
			}

			foreach (var node in nodes)
			{
				if (node.IsDirectory)
				{
					node.Size = folderSizes.GetValueOrDefault(node.Path);
					node.SizeText = node.Size.ToLongSizeString();
				}
			}

			if (search.Length > 0)
				nodes.RemoveAll(node => !node.IsDirectory && !node.Name.Contains(search, StringComparison.OrdinalIgnoreCase));

			nodes.Sort((a, b) => CompareNodes(a, b, SortIndex));

			return nodes;
		}

		/// <summary>
		/// Pre-computes every folder's total size in a single pass so lazy nodes do not recurse.
		/// </summary>
		private Dictionary<string, ulong> ComputeFolderSizes()
		{
			var sizes = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);

			foreach (var entry in entries)
			{
				if (entry.IsDirectory)
					continue;

				var full = entry.Name.Replace('\\', '/');
				var index = full.LastIndexOf('/');
				while (index > 0)
				{
					var folder = full[..index];
					sizes[folder] = sizes.GetValueOrDefault(folder) + entry.Size;
					index = folder.LastIndexOf('/');
				}
			}

			return sizes;
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
	}

	public sealed class ArchiveNode
	{
		private readonly Func<string, IReadOnlyList<ArchiveNode>>? childrenFactory;
		private IReadOnlyList<ArchiveNode>? children;

		public string Name { get; }
		public string Path { get; }
		public string DisplayName { get; }
		public bool IsDirectory { get; }
		public bool IsExpanded { get; set; }
		public ulong Size { get; set; }
		public string SizeText { get; set; } = string.Empty;
		public int EntryIndex { get; set; } = -1;

		public IReadOnlyList<ArchiveNode> Children => children ??= childrenFactory?.Invoke(Path) ?? [];

		public ArchiveNode(string name, string path, bool isDirectory, Func<string, IReadOnlyList<ArchiveNode>>? childrenFactory = null)
		{
			Name = name;
			Path = path;
			IsDirectory = isDirectory;
			DisplayName = isDirectory ? $"{name}/" : name;
			this.childrenFactory = childrenFactory;
		}
	}

	public sealed record ArchiveEntry(string Name, string SizeText, bool IsDirectory, ulong Size, int Index);
}
