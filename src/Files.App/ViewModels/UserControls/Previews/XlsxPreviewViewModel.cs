// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using System.IO;
using System.IO.Compression;
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

			var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
			if (sheetEntry is null)
				return string.Empty;

			using var sheetStream = sheetEntry.Open();
			var sheet = XDocument.Load(sheetStream);
			var ns = sheet.Root!.Name.Namespace;

			var lines = new List<string>();
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
					lines.Add(string.Concat(cells.OrderBy(c => c.Column).Select(c => FormatCell(c.Value))));
			}

			return string.Join(Environment.NewLine, lines);
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
