// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers;
using Files.App.ViewModels.Properties;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace Files.App.ViewModels.Previews
{
	/// <summary>
	/// Renders each slide of a .pptx — its text, SmartArt diagram text and embedded images — straight
	/// from the package, avoiding the slow Shell preview handler.
	/// </summary>
	public sealed partial class PptxPreviewViewModel : BasePreviewModel
	{
		private static readonly XNamespace DrawingML = "http://schemas.openxmlformats.org/drawingml/2006/main";
		private static readonly XNamespace DiagramML = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
		private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

		private IReadOnlyList<PptxSlide> slides = [];
		public IReadOnlyList<PptxSlide> Slides
		{
			get => slides;
			private set => SetProperty(ref slides, value);
		}

		public PptxPreviewViewModel(ListedItem item)
			: base(item)
		{
		}

		public override async Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			try
			{
				var slidesData = await Task.Run(() => ExtractSlides(Item.ItemPath!));

				// Decode the images and publish the slides on the UI thread.
				await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
				{
					var built = new List<PptxSlide>();
					foreach (var data in slidesData)
					{
						var images = new List<BitmapImage>();
						foreach (var bytes in data.ImageBytes)
						{
							var image = await bytes.ToBitmapAsync();
							if (image is not null)
								images.Add(image);
						}

						built.Add(new PptxSlide(data.NumberText, data.Text, images));
					}

					Slides = built;
				});
			}
			catch (Exception e)
			{
				Debug.WriteLine(e);
			}

			return [];
		}

		private static List<PptxSlideData> ExtractSlides(string path)
		{
			var slides = new List<PptxSlideData>();

			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

			var index = 1;
			foreach (var slidePath in ReadSlidePaths(archive))
			{
				var entry = archive.GetEntry(slidePath);
				if (entry is null)
					continue;

				var (text, imageBytes) = ExtractSlide(archive, entry, slidePath);
				slides.Add(new PptxSlideData($"Slide {index}", text, imageBytes));
				index++;
			}

			return slides;
		}

		private static (string Text, List<byte[]> Images) ExtractSlide(ZipArchive archive, ZipArchiveEntry slideEntry, string slidePath)
		{
			using var slideStream = slideEntry.Open();
			var slide = XDocument.Load(slideStream);
			var text = string.Join(" ", slide.Descendants(DrawingML + "t").Select(run => run.Value));

			var images = new List<byte[]>();

			// Relationship id -> part path for this slide (images and SmartArt data).
			var targets = new Dictionary<string, string>();
			var relsEntry = archive.GetEntry(SlideRelsPath(slidePath));
			if (relsEntry is not null)
			{
				using var relsStream = relsEntry.Open();
				var rels = XDocument.Load(relsStream);
				foreach (var rel in rels.Descendants())
				{
					var id = rel.Attribute("Id")?.Value;
					var target = rel.Attribute("Target")?.Value;
					if (id is not null && target is not null)
						targets[id] = ResolveMediaPath(slidePath, target);
				}
			}

			// Embedded pictures.
			foreach (var blip in slide.Descendants(DrawingML + "blip"))
			{
				var embed = blip.Attribute(Relationships + "embed")?.Value;
				if (embed is null || !targets.TryGetValue(embed, out var mediaPath))
					continue;

				var mediaEntry = archive.GetEntry(mediaPath);
				if (mediaEntry is null)
					continue;

				using var mediaStream = mediaEntry.Open();
				using var memory = new MemoryStream();
				mediaStream.CopyTo(memory);
				images.Add(memory.ToArray());
			}

			// SmartArt / diagram text (the data part referenced by r:dm).
			var diagramText = new List<string>();
			foreach (var graphicData in slide.Descendants(DrawingML + "graphicData"))
			{
				var relIds = graphicData.Element(DiagramML + "relIds");
				if (relIds is null)
					continue;

				var dataId = relIds.Attribute(Relationships + "dm")?.Value;
				if (dataId is null || !targets.TryGetValue(dataId, out var dataPath))
					continue;

				var dataEntry = archive.GetEntry(dataPath);
				if (dataEntry is null)
					continue;

				using var dataStream = dataEntry.Open();
				var data = XDocument.Load(dataStream);
				diagramText.AddRange(data.Descendants(DiagramML + "pt")
					.SelectMany(point => point.Descendants(DrawingML + "t").Select(run => run.Value))
					.Where(value => !string.IsNullOrWhiteSpace(value)));
			}

			var fullText = text;
			if (diagramText.Count > 0)
				fullText = string.IsNullOrEmpty(text)
					? $"Схема: {string.Join(", ", diagramText)}"
					: $"{text}\nСхема: {string.Join(", ", diagramText)}";

			return (fullText, images);
		}

		private static string SlideRelsPath(string slidePath)
		{
			var directory = Path.GetDirectoryName(slidePath)?.Replace('\\', '/') ?? "ppt/slides";
			var name = Path.GetFileName(slidePath);
			return $"{directory}/_rels/{name}.rels";
		}

		private static string ResolveMediaPath(string slidePath, string target)
		{
			var directory = Path.GetDirectoryName(slidePath)?.Replace('\\', '/') ?? "ppt/slides";
			var segments = new List<string>();
			foreach (var part in $"{directory}/{target}".Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
			{
				if (part is ".")
					continue;
				if (part is "..")
				{
					if (segments.Count > 0)
						segments.RemoveAt(segments.Count - 1);
					continue;
				}
				segments.Add(part);
			}

			return string.Join('/', segments);
		}

		private static List<string> ReadSlidePaths(ZipArchive archive)
		{
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

	internal sealed record PptxSlideData(string NumberText, string Text, IReadOnlyList<byte[]> ImageBytes);

	public sealed class PptxSlide
	{
		public string NumberText { get; }
		public string Text { get; }
		public IReadOnlyList<BitmapImage> Images { get; }

		public PptxSlide(string numberText, string text, IReadOnlyList<BitmapImage> images)
		{
			NumberText = numberText;
			Text = text;
			Images = images;
		}
	}
}
