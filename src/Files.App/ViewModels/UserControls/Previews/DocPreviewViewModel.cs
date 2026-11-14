// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using System.IO;
using System.Text;

namespace Files.App.ViewModels.Previews
{
	/// <summary>
	/// Best-effort text preview of a legacy .doc (OLE2 compound file) straight from the file, avoiding
	/// the slow Shell preview handler. Reads the WordDocument stream and the FIB text range.
	/// </summary>
	public sealed partial class DocPreviewViewModel : TextPreviewViewModel
	{
		private const uint EndOfChain = 0xFFFFFFFE;
		private const uint FreeSector = 0xFFFFFFFF;

		public DocPreviewViewModel(ListedItem item)
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
			var data = File.ReadAllBytes(path);
			var wordDocument = ReadCompoundStream(data, "WordDocument");
			if (wordDocument is null || wordDocument.Length < 0x20)
				return string.Empty;

			var fcMin = BitConverter.ToInt32(wordDocument, 0x18);
			var fcMac = BitConverter.ToInt32(wordDocument, 0x1C);
			if (fcMin < 0 || fcMac > wordDocument.Length || fcMin >= fcMac)
				return string.Empty;

			var flags = BitConverter.ToUInt16(wordDocument, 0x0A);
			var extended = (flags & 0x1000) != 0; // fExtChar

			var span = wordDocument.AsSpan(fcMin, fcMac - fcMin);
			var text = extended ? Encoding.Unicode.GetString(span) : Encoding.Latin1.GetString(span);

			return Sanitize(text);
		}

		private static string Sanitize(string text)
		{
			var builder = new StringBuilder(text.Length);
			foreach (var ch in text)
			{
				if (ch is '\n' or '\t' || ch >= ' ')
					builder.Append(ch);
			}

			return builder.ToString();
		}

		private static byte[]? ReadCompoundStream(byte[] data, string streamName)
		{
			if (data.Length < 512 || data[0] != 0xD0 || data[1] != 0xCF || data[2] != 0x11 || data[3] != 0xE0)
				return null;

			var sectorSize = 1 << BitConverter.ToUInt16(data, 0x1E);
			var firstDirectorySector = BitConverter.ToUInt32(data, 0x30);
			var difatSectorCount = BitConverter.ToUInt32(data, 0x48);

			// FAT sector list: the header DIFAT plus any DIFAT sectors.
			var fatSectors = new List<uint>();
			for (var i = 0; i < 109; i++)
			{
				var sector = BitConverter.ToUInt32(data, 0x4C + i * 4);
				if (sector == FreeSector)
					break;
				fatSectors.Add(sector);
			}

			var difatSector = BitConverter.ToUInt32(data, 0x44);
			var entriesPerDifatSector = (sectorSize / 4) - 1;
			for (var i = 0; i < difatSectorCount && difatSector != EndOfChain && difatSector != FreeSector; i++)
			{
				var offset = SectorOffset(difatSector, sectorSize);
				for (var j = 0; j < entriesPerDifatSector && offset + j * 4 + 4 <= data.Length; j++)
				{
					var sector = BitConverter.ToUInt32(data, (int)(offset + j * 4));
					if (sector != FreeSector)
						fatSectors.Add(sector);
				}

				if (offset + entriesPerDifatSector * 4 + 4 > data.Length)
					break;
				difatSector = BitConverter.ToUInt32(data, (int)(offset + entriesPerDifatSector * 4));
			}

			var fat = new List<uint>();
			foreach (var sector in fatSectors)
			{
				var offset = SectorOffset(sector, sectorSize);
				for (var i = 0; i < sectorSize / 4 && offset + i * 4 + 4 <= data.Length; i++)
					fat.Add(BitConverter.ToUInt32(data, (int)(offset + i * 4)));
			}

			var directory = ReadChain(data, fat, firstDirectorySector, sectorSize);
			for (var i = 0; i + 128 <= directory.Length; i += 128)
			{
				var nameLength = BitConverter.ToUInt16(directory, i + 64);
				if (nameLength is 0 || nameLength > 64)
					continue;

				var name = Encoding.Unicode.GetString(directory, i, nameLength - 2);
				if (!string.Equals(name, streamName, StringComparison.OrdinalIgnoreCase))
					continue;

				var type = directory[i + 66];
				var start = BitConverter.ToUInt32(directory, i + 116);
				var size = BitConverter.ToUInt32(directory, i + 120);
				if (type != 2)
					return null;

				var stream = ReadChain(data, fat, start, sectorSize);
				return size > 0 && size < stream.Length ? stream[..(int)size] : stream;
			}

			return null;
		}

		private static long SectorOffset(uint sector, int sectorSize)
			=> 512 + ((long)sector * sectorSize);

		private static byte[] ReadChain(byte[] data, List<uint> fat, uint start, int sectorSize)
		{
			using var builder = new MemoryStream();
			var sector = start;
			var guard = 0;
			while (sector != EndOfChain && sector != FreeSector && sector < (uint)fat.Count && guard++ < 1_000_000)
			{
				var offset = SectorOffset(sector, sectorSize);
				if (offset >= data.Length)
					break;

				builder.Write(data, (int)offset, (int)Math.Min(sectorSize, data.Length - offset));
				sector = fat[(int)sector];
			}

			return builder.ToArray();
		}
	}
}
