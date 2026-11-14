// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.IO.Compression;

namespace Files.App.Helpers
{
	/// <summary>
	/// Detects the real file format from its magic bytes so a mislabeled file (e.g. a .doc renamed
	/// to .pdf) still opens with the correct preview.
	/// </summary>
	public static class PreviewFormatSniffer
	{
		public static string? DetectExtension(string? path)
		{
			if (string.IsNullOrEmpty(path))
				return null;

			try
			{
				using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
				Span<byte> head = stackalloc byte[12];
				var read = stream.Read(head);
				if (read < 4)
					return null;

				if (head[0] == 0x25 && head[1] == 0x50 && head[2] == 0x44 && head[3] == 0x46)
					return ".pdf";

				if (head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0)
					return ".doc";

				if (head[0] == 0x50 && head[1] == 0x4B && head[2] == 0x03 && head[3] == 0x04)
				{
					using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
					if (archive.GetEntry("word/document.xml") is not null)
						return ".docx";
					if (archive.GetEntry("xl/workbook.xml") is not null)
						return ".xlsx";
					if (archive.GetEntry("ppt/presentation.xml") is not null)
						return ".pptx";
					return null;
				}

				// Common image and rich-text containers, so a mislabeled file still previews.
				if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47)
					return ".png";
				if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
					return ".jpg";
				if (head[0] == 0x47 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x38)
					return ".gif";
				if (head[0] == 0x42 && head[1] == 0x4D)
					return ".bmp";
				if ((head[0] == 0x49 && head[1] == 0x49 && head[2] == 0x2A && head[3] == 0x00) ||
					(head[0] == 0x4D && head[1] == 0x4D && head[2] == 0x00 && head[3] == 0x2A))
					return ".tif";
				if (head[0] == 0x7B && head[1] == 0x5C && head[2] == 0x72 && head[3] == 0x74)
					return ".rtf";

				if (read >= 12)
				{
					// WebP: "RIFF" .... "WEBP".
					if (head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46 &&
						head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50)
						return ".webp";

					// HEIF/HEIC: ISO-BMFF "ftyp" box followed by a known brand.
					if (head[4] == 0x66 && head[5] == 0x74 && head[6] == 0x79 && head[7] == 0x70)
					{
						var brand = $"{(char)head[8]}{(char)head[9]}{(char)head[10]}{(char)head[11]}";
						if (brand is "heic" or "heix" or "hevc" or "hevx" or "mif1" or "msf1")
							return ".heic";
					}
				}

				return null;
			}
			catch
			{
				return null;
			}
		}
	}
}
