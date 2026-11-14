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
				Span<byte> head = stackalloc byte[8];
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
