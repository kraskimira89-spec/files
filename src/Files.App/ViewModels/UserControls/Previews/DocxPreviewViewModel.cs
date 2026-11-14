// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace Files.App.ViewModels.Previews
{
	/// <summary>
	/// Renders a .docx as plain text straight from the package, avoiding the slow Shell preview handler.
	/// </summary>
	public sealed partial class DocxPreviewViewModel : TextPreviewViewModel
	{
		public DocxPreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public override async Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var details = new List<FileProperty>();

			try
			{
				var text = await Task.Run(() => ExtractText(Item.ItemPath!));
				var (lineCount, wordCount) = CountPreviewTextStats(text, countWords: true);

				details.Add(GetFileProperty("PropertyLineCount", lineCount));
				details.Add(GetFileProperty("PropertyWordCount", wordCount));

				var value = text.Left(Constants.PreviewPane.TextCharacterLimit);
				await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() => TextValue = value);
			}
			catch (Exception e)
			{
				Debug.WriteLine(e);
			}

			return details;
		}

		private static string ExtractText(string path)
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

			var entry = archive.GetEntry("word/document.xml");
			if (entry is null)
				return string.Empty;

			using var entryStream = entry.Open();
			var document = XDocument.Load(entryStream);
			var ns = document.Root!.Name.Namespace;
			var paragraphs = document.Descendants(ns + "p")
				.Select(paragraph => string.Concat(paragraph.Descendants(ns + "t").Select(run => run.Value)));

			return string.Join(Environment.NewLine, paragraphs);
		}
	}
}
