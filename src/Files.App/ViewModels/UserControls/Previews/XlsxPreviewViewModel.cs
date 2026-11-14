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
	/// Renders the first sheet of an .xlsx as a fixed-width text table straight from the package,
	/// avoiding the slow Shell preview handler.
	/// </summary>
	public sealed partial class XlsxPreviewViewModel : TextPreviewViewModel
	{
		private const int CellWidth = 20;

		public XlsxPreviewViewModel(ListedItem item)
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

			var sharedStrings = ReadSharedStrings(archive);
			var sheets = ReadSheets(archive);
			if (sheets.Count == 0)
				sheets.Add(("Sheet1", "xl/worksheets/sheet1.xml"));

			var builder = new StringBuilder();
			foreach (var (name, sheetPath) in sheets)
			{
				var entry = archive.GetEntry(sheetPath);
				if (entry is null)
					continue;

				using var sheetStream = entry.Open();
				var sheet = XDocument.Load(sheetStream);
				var ns = sheet.Root!.Name.Namespace;

				builder.AppendLine($"=== {name} ===");
				foreach (var row in sheet.Descendants(ns + "row"))
				{
					var cells = new List<(int Column, string Value)>();
					foreach (var cell in row.Descendants(ns + "c"))
					{
						var text = ReadCellValue(cell, ns, sharedStrings);
						if (!string.IsNullOrEmpty(text))
							cells.Add((ColumnIndex(cell.Attribute("r")?.Value), text));
					}

					if (cells.Count > 0)
						builder.AppendLine(string.Concat(cells.OrderBy(c => c.Column).Select(c => FormatCell(c.Value))));
				}

				builder.AppendLine();
			}

			return builder.ToString();
		}

		private static List<(string Name, string Path)> ReadSheets(ZipArchive archive)
		{
			var targets = new Dictionary<string, string>();
			var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
			if (relsEntry is not null)
			{
				using var relsStream = relsEntry.Open();
				var rels = XDocument.Load(relsStream);
				foreach (var rel in rels.Descendants())
				{
					var id = rel.Attribute("Id")?.Value;
					var target = rel.Attribute("Target")?.Value;
					if (id is not null && target is not null)
						targets[id] = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target.Replace('\\', '/');
				}
			}

			var sheets = new List<(string, string)>();
			var workbookEntry = archive.GetEntry("xl/workbook.xml");
			if (workbookEntry is null)
				return sheets;

			using var workbookStream = workbookEntry.Open();
			var workbook = XDocument.Load(workbookStream);
			var ns = workbook.Root!.Name.Namespace;
			foreach (var sheet in workbook.Descendants(ns + "sheet"))
			{
				var name = sheet.Attribute("name")?.Value ?? "Sheet";
				var relId = sheet.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "id")?.Value;
				if (relId is not null && targets.TryGetValue(relId, out var sheetPath))
					sheets.Add((name, sheetPath));
			}

			return sheets;
		}

		private static List<string> ReadSharedStrings(ZipArchive archive)
		{
			var strings = new List<string>();
			var entry = archive.GetEntry("xl/sharedStrings.xml");
			if (entry is null)
				return strings;

			using var stream = entry.Open();
			var document = XDocument.Load(stream);
			var ns = document.Root!.Name.Namespace;
			foreach (var item in document.Descendants(ns + "si"))
				strings.Add(string.Concat(item.Descendants(ns + "t").Select(run => run.Value)));

			return strings;
		}

		private static string ReadCellValue(XElement cell, XNamespace ns, List<string> sharedStrings)
		{
			var type = cell.Attribute("t")?.Value;
			if (type is "inlineStr")
				return string.Concat(cell.Descendants(ns + "t").Select(run => run.Value));

			var value = cell.Element(ns + "v")?.Value ?? string.Empty;
			if (type is "s" && int.TryParse(value, out var index) && index >= 0 && index < sharedStrings.Count)
				return sharedStrings[index];

			return value;
		}

		private static int ColumnIndex(string? reference)
		{
			var index = 0;
			if (reference is null)
				return index;

			foreach (var ch in reference)
			{
				if (!char.IsLetter(ch))
					break;
				index = (index * 26) + (char.ToUpperInvariant(ch) - 'A' + 1);
			}

			return index;
		}

		private static string FormatCell(string value)
		{
			var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
			if (singleLine.Length > CellWidth)
				return string.Concat(singleLine.AsSpan(0, CellWidth - 1), "…");

			return singleLine.PadRight(CellWidth);
		}
	}
}
