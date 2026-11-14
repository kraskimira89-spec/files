// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Files.App.ViewModels.Previews
{
	/// <summary>
	/// Renders the text of each slide in a .pptx straight from the package, avoiding the slow Shell
	/// preview handler.
	/// </summary>
	public sealed partial class PptxPreviewViewModel : TextPreviewViewModel
	{
		private static readonly XNamespace DrawingML = "http://schemas.openxmlformats.org/drawingml/2006/main";
		private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

		public PptxPreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public override async Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			try
			{
				var text = await Task.Run(() => ExtractText(Item.ItemPath!));
				TextValue = text.Left(Constants.PreviewPane.TextCharacterLimit);
			}
			catch (Exception e)
			{
				Debug.WriteLine(e);
			}

			return [];
		}

		private static string ExtractText(string path)
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

			var builder = new StringBuilder();
			var index = 1;
			foreach (var slidePath in ReadSlidePaths(archive))
			{
				var entry = archive.GetEntry(slidePath);
				if (entry is null)
					continue;

				using var slideStream = entry.Open();
				var slide = XDocument.Load(slideStream);
				var runs = slide.Descendants(DrawingML + "t").Select(run => run.Value);

				builder.AppendLine($"=== Slide {index} ===");
				builder.AppendLine(string.Join(" ", runs));
				builder.AppendLine();
				index++;
			}

			return builder.ToString();
		}

		private static List<string> ReadSlidePaths(ZipArchive archive)
		{
			// Relationship id -> slide part path.
			var targets = new Dictionary<string, string>();
			var relsEntry = archive.GetEntry("ppt/_rels/presentation.xml.rels");
			if (relsEntry is not null)
			{
				using var relsStream = relsEntry.Open();
				var rels = XDocument.Load(relsStream);
				foreach (var rel in rels.Descendants())
				{
					var id = rel.Attribute("Id")?.Value;
					var target = rel.Attribute("Target")?.Value;
					if (id is not null && target is not null)
						targets[id] = target.StartsWith('/') ? target.TrimStart('/') : "ppt/" + target.Replace('\\', '/');
				}
			}

			var slides = new List<string>();
			var presentationEntry = archive.GetEntry("ppt/presentation.xml");
			if (presentationEntry is not null)
			{
				using var presentationStream = presentationEntry.Open();
				var presentation = XDocument.Load(presentationStream);
				var ns = presentation.Root!.Name.Namespace;
				foreach (var slideId in presentation.Descendants(ns + "sldId"))
				{
					var relId = slideId.Attribute(Relationships + "id")?.Value;
					if (relId is not null && targets.TryGetValue(relId, out var slidePath))
						slides.Add(slidePath);
				}
			}

			// Fallback to the raw part order when the presentation part is missing or malformed.
			if (slides.Count == 0)
			{
				slides.AddRange(archive.Entries
					.Where(entry => entry.FullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal) &&
						entry.FullName.EndsWith(".xml", StringComparison.Ordinal))
					.Select(entry => entry.FullName)
					.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
			}

			return slides;
		}
	}
}
