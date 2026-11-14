// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using SevenZip;
using System.IO;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class ArchivePreviewViewModel : BasePreviewModel
	{
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

		public ArchivePreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public override async Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var details = new List<FileProperty>();

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

			var folderCount = 0;
			var fileCount = 0;
			ulong totalSize = 0;

			foreach (ArchiveFileInfo entry in zipFile.ArchiveFileData)
			{
				if (!entry.IsDirectory)
				{
					++fileCount;
					totalSize += entry.Size;
				}
			}

			folderCount = (int)zipFile.FilesCount - fileCount;

			entries = zipFile.ArchiveFileData
				.Select(entry => new ArchiveEntry(
					entry.FileName,
					entry.IsDirectory ? string.Empty : entry.Size.ToLongSizeString(),
					entry.IsDirectory,
					entry.Size))
				.ToList();

			RebuildTree();

			string propertyItemCount = Strings.DetailsArchiveItems.GetLocalizedFormatResource(zipFile.FilesCount, fileCount, folderCount);
			details.Add(GetFileProperty("PropertyItemCount", propertyItemCount));
			details.Add(GetFileProperty("PropertyUncompressedSize", totalSize.ToLongSizeString()));

			// Loads the thumbnail preview
			_ = await base.LoadPreviewAndDetailsAsync();
			return details;
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
		public List<ArchiveNode> Children { get; } = [];

		public ArchiveNode(string name, bool isDirectory)
		{
			Name = name;
			IsDirectory = isDirectory;
			DisplayName = isDirectory ? $"{name}/" : name;
		}
	}

	public sealed record ArchiveEntry(string Name, string SizeText, bool IsDirectory, ulong Size);
}
