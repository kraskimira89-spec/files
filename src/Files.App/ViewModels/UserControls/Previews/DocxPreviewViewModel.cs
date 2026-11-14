// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace Files.App.ViewModels.Previews
{
	/// <summary>
	/// Renders a .docx with its structure (headings, lists, bold/italic runs) straight from the
	/// package, avoiding the slow Shell preview handler.
	/// </summary>
	public sealed partial class DocxPreviewViewModel : TextPreviewViewModel
	{
		private static readonly string[] HeadingPrefixes = ["Heading", "Заголовок"];

		private IReadOnlyList<DocxParagraph> paragraphs = [];
		public IReadOnlyList<DocxParagraph> Paragraphs
		{
			get => paragraphs;
			private set => SetProperty(ref paragraphs, value);
		}

		public DocxPreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public override async Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var details = new List<FileProperty>();

			try
			{
				var extracted = await Task.Run(() => ExtractParagraphs(Item.ItemPath!));
				var plainText = string.Join(Environment.NewLine, extracted.Select(paragraph => paragraph.Text));
				var (lineCount, wordCount) = CountPreviewTextStats(plainText, countWords: true);

				details.Add(GetFileProperty("PropertyLineCount", lineCount));
				details.Add(GetFileProperty("PropertyWordCount", wordCount));

				// Cap the rendered paragraph count so a huge document cannot stall the pane.
				var trimmed = new List<DocxParagraph>();
				var used = 0;
				foreach (var paragraph in extracted)
				{
					trimmed.Add(paragraph);
					used += paragraph.Text.Length;
					if (used >= Constants.PreviewPane.TextCharacterLimit)
						break;
				}

				await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(() =>
				{
					Paragraphs = trimmed;
					TextValue = plainText.Left(Constants.PreviewPane.TextCharacterLimit);
				});
			}
			catch (Exception e)
			{
				Debug.WriteLine(e);
			}

			return details;
		}

		private static List<DocxParagraph> ExtractParagraphs(string path)
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

			var entry = archive.GetEntry("word/document.xml");
			if (entry is null)
				return [];

			using var entryStream = entry.Open();
			var document = XDocument.Load(entryStream);
			var ns = document.Root!.Name.Namespace;

			var result = new List<DocxParagraph>();
			foreach (var paragraph in document.Descendants(ns + "p"))
			{
				var properties = paragraph.Element(ns + "pPr");
				var styleId = properties?.Element(ns + "pStyle")?.Attribute(ns + "val")?.Value ?? string.Empty;
				var headingLevel = GetHeadingLevel(styleId);
				var isListItem = properties?.Element(ns + "numPr") is not null;

				var runs = new List<DocxRun>();
				foreach (var run in paragraph.Descendants(ns + "r"))
				{
					var text = string.Concat(run.Elements(ns + "t").Select(element => element.Value));
					if (text.Length == 0)
						continue;

					var runProperties = run.Element(ns + "rPr");
					runs.Add(new DocxRun(
						text,
						runProperties?.Element(ns + "b") is not null,
						runProperties?.Element(ns + "i") is not null));
				}

				if (runs.Count == 0)
					continue;

				result.Add(new DocxParagraph(headingLevel, isListItem, runs));
			}

			return result;
		}

		private static int GetHeadingLevel(string styleId)
		{
			if (styleId.Equals("Title", StringComparison.OrdinalIgnoreCase) ||
				styleId.Equals("Название", StringComparison.OrdinalIgnoreCase))
				return 1;

			foreach (var prefix in HeadingPrefixes)
			{
				if (styleId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
					int.TryParse(styleId.AsSpan(prefix.Length), out var level))
					return Math.Clamp(level, 1, 6);
			}

			return 0;
		}
	}

	public sealed record DocxRun(string Text, bool Bold, bool Italic);

	public sealed class DocxParagraph
	{
		public int HeadingLevel { get; }
		public bool IsListItem { get; }
		public IReadOnlyList<DocxRun> Runs { get; }
		public string Text { get; }

		public DocxParagraph(int headingLevel, bool isListItem, IReadOnlyList<DocxRun> runs)
		{
			HeadingLevel = headingLevel;
			IsListItem = isListItem;
			Runs = runs;
			Text = string.Concat(runs.Select(run => run.Text));
		}
	}
}
