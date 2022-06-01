using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Ionic.Zlib;

namespace HuskyPak
{
	internal class Program
	{
		static void Main(string[] args)
		{
			CultureInfo.CurrentCulture =
			CultureInfo.CurrentUICulture =
			CultureInfo.DefaultThreadCurrentUICulture =
			CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

			if (args.Length == 0)
			{
				Console.WriteLine("Usage: HuskyPak.exe <action> [arguments...]");
				Console.WriteLine("");
				Console.WriteLine("Usage: HuskyPak.exe list <pakFile>");
				Console.WriteLine("Usage: HuskyPak.exe extract <pakFile> [folder = ./output]");
				Console.WriteLine("Usage: HuskyPak.exe pack <folder> <pakFile> [splitEnd = false]");
				Console.WriteLine("");
				Console.WriteLine("Actions:");
				Console.WriteLine("  list        Lists files in given pak file.");
				Console.WriteLine("  extract     Extracts files from given pak file.");
				Console.WriteLine("  pack        Packs contents of a folder into a new pak file.");
				Console.WriteLine("");
				Console.WriteLine("Arguments:");
				Console.WriteLine("  pakFile     Path to a pak file.");
				Console.WriteLine("  folder      Path to an input or output folder.");
				Console.WriteLine("  splitEnd    Either 'true' or 'false', denoting whether the create file is the last split file.");

				return;
			}

			try
			{
				var action = args[0];
				switch (action)
				{
					case "list":
					{
						ListPakContents(args);
						break;
					}
					case "extract":
					{
						ExtractPakFile(args);
						break;
					}
					case "pack":
					{
						CreatePackFile(args);
						break;
					}
					default:
					{
						Console.WriteLine("Unknown action '{0}'.", action);
						break;
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error: {0}", ex.Message);
			}
		}

		static void ListPakContents(string[] args)
		{
			if (args.Length < 2)
			{
				Console.WriteLine("Usage: HuskyPak.exe list <pakFile>");
				return;
			}

			var filePath = args[1];
			if (!File.Exists(filePath))
			{
				Console.WriteLine("Pak file not found.");
				return;
			}

			using (var fs = new PakFileStream(filePath, FileMode.Open))
			using (var br = new BinaryReader(fs))
			{
				Console.WriteLine("Pak File: {0}", Path.GetFileName(filePath));
				Console.WriteLine("");
				Console.WriteLine("Contents:");

				var entries = ReadFileEntries(br);
				var longestNameSize = entries.Max(a => a.FileName.Length);

				foreach (var entry in entries)
					Console.WriteLine("  {0} {1} ({2})", entry.FileName.PadRight(longestNameSize + 2), GetFormattedSize(entry.SizeCompressed), GetFormattedSize(entry.SizeUncompressed));
			}
		}

		static void ExtractPakFile(string[] args)
		{
			if (args.Length < 2)
			{
				Console.WriteLine("Usage: HuskyPak.exe extract <pakFile> [outDir = ./output]");
				return;
			}

			var filePath = args[1];
			var outPath = "./output/";

			if (args.Length > 2)
				outPath = args[2];

			if (!File.Exists(filePath))
			{
				Console.WriteLine("Pak file not found.");
				return;
			}

			using (var fs = new PakFileStream(filePath, FileMode.Open))
			using (var br = new BinaryReader(fs))
			{
				Console.WriteLine("Pak File: {0}", Path.GetFileName(filePath));
				Console.WriteLine("Out Path: {0}", Path.GetFullPath(outPath));
				Console.WriteLine("");
				Console.WriteLine("Extracting...");

				var entries = ReadFileEntries(br);
				foreach (var entry in entries)
				{
					Console.WriteLine("  {0}", entry.FileName);

					br.BaseStream.Seek(entry.DataOffset, SeekOrigin.Begin);
					var compressedData = br.ReadBytes(entry.SizeCompressed);

					var outFilePath = Path.Combine(outPath, entry.FileName);
					var outDirPath = Path.GetDirectoryName(outFilePath);

					if (!Directory.Exists(outDirPath))
						Directory.CreateDirectory(outDirPath);

					using (var msCompressed = new MemoryStream(compressedData))
					using (var outFs = new FileStream(outFilePath, FileMode.Create))
					{
						using (var zlib = new ZlibStream(msCompressed, CompressionMode.Decompress))
							zlib.CopyTo(outFs);
					}
				}

				Console.WriteLine("Extracted {0} files.", entries.Count);
			}
		}

		static void CreatePackFile(string[] args)
		{
			if (args.Length < 3)
			{
				Console.WriteLine("Usage: HuskyPak.exe pack <folder> <pakFile>");
				return;
			}

			var inFolderPath = Path.GetFullPath(args[1]);
			var outFilePath = Path.GetFullPath(args[2]);
			var lastSplit = false;

			if (args.Length > 3 && args[3] == "true")
				lastSplit = true;

			if (!Directory.Exists(inFolderPath))
			{
				Console.WriteLine("Folder not found.");
				return;
			}

			var outFileParentPath = Path.GetDirectoryName(outFilePath);
			if (!Directory.Exists(outFileParentPath))
				Directory.CreateDirectory(outFileParentPath);

			Console.WriteLine("New Pak File: {0}", Path.GetFileName(outFilePath));
			Console.WriteLine("Input Folder: {0}", inFolderPath);
			Console.WriteLine("");
			Console.WriteLine("Packing...");

			var filePaths = Directory.GetFiles(inFolderPath, "*", SearchOption.AllDirectories).OrderBy(a => a);

			using (var fs = new PakFileStream(outFilePath, FileMode.Create))
			using (var bw = new BinaryWriter(fs))
			{
				bw.Write(new byte[] { 0x42, 0x50, 0x46, 0x53 }); // signature
				bw.Write(new byte[] { 0x01, 0x02, 0x00, 0x00 }); // version

				var afterVersionOffset = bw.BaseStream.Position;
				bw.Write(0); // lastEntryOffset ("firstheader")
				bw.Write(0); // splitFlag ("split")

				for (var i = 0; i < 0x7C; ++i)
					bw.Write((byte)0);

				var prevEntryOffset = 0;

				foreach (var filePath in filePaths)
				{
					var fileName = filePath.Substring(inFolderPath.Length).TrimStart('\\', '/');
					Console.WriteLine("  {0}", fileName);

					var fileNameBytes = Encoding.Unicode.GetBytes(fileName);
					var dataUncompressed = File.ReadAllBytes(filePath);
					var dataCompressed = ZlibStream.CompressBuffer(dataUncompressed, CompressionLevel.BestSpeed);

					var entryOffset = bw.BaseStream.Position;
					var entryDataOffset = (int)bw.BaseStream.Position + sizeof(int) * 6 + fileNameBytes.Length + sizeof(int) + 20;

					bw.Write(0);
					bw.Write(1);
					bw.Write(entryDataOffset);
					bw.Write(dataCompressed.Length);
					bw.Write(dataUncompressed.Length);
					bw.Write(fileName.Length);
					bw.Write(fileNameBytes);

					bw.Write(20);
					for (var i = 0; i < 16; ++i)
						bw.Write((byte)0);
					bw.Write(prevEntryOffset);

					bw.Write(dataCompressed);

					prevEntryOffset = (int)entryOffset;
				}

				// The pak files are basically a split archive and the
				// client keeps reading the data files until it encounters
				// the last one, denoted by a "4" split flag. All other
				// files, including the base data.pak, have a "2" flag.
				var splitFlag = !lastSplit ? 2 : 4;
				var encodedPrevHeader = prevEntryOffset ^ RSHash("firstheader");
				var encodedSplitFlag = splitFlag ^ RSHash("split");

				var curPos = bw.BaseStream.Position;
				bw.BaseStream.Seek(afterVersionOffset, SeekOrigin.Begin);
				bw.Write(encodedPrevHeader);
				bw.Write(encodedSplitFlag);
				bw.BaseStream.Seek(curPos, SeekOrigin.Begin);
			}

			Console.WriteLine("Packed {0} files.", filePaths.Count());
		}

		static List<PakFileEntry> ReadFileEntries(BinaryReader br)
		{
			if (br.BaseStream.Length < 0x8C)
				throw new InvalidDataException("Invalid file.");

			var signature = Encoding.ASCII.GetString(br.ReadBytes(4));
			if (signature != "BPFS")
				throw new InvalidDataException("Invalid file signature.");

			var version = br.ReadInt32();
			if (version != 0x201)
				throw new InvalidDataException("Unsupported file version.");

			// These two values need to be xored with RSHash("firstheader")
			// and RSHash("split") respectively to get the actual values.
			_ = br.ReadInt32(); // lastEntryOffset
			_ = br.ReadInt32(); // split (2 if not last file, 4 if last file)

			// These 124 byte seem random and the client doesn't care
			// about them.
			br.BaseStream.Seek(0x7C, SeekOrigin.Current);

			var entries = new List<PakFileEntry>();

			while (br.BaseStream.Position < br.BaseStream.Length)
			{
				var entry = new PakFileEntry();

				entry.Unk1 = br.ReadInt32(); // Always 0?
				entry.Unk2 = br.ReadInt32(); // Always 1?
				entry.DataOffset = br.ReadInt32();
				entry.SizeCompressed = br.ReadInt32();
				entry.SizeUncompressed = br.ReadInt32();

				var nameSize = br.ReadInt32();
				entry.FileName = Encoding.Unicode.GetString(br.ReadBytes(nameSize * 2));

				// This integer was always 20 in the client I was working
				// with, and the data that followed it was 20 bytes. The
				// last four byte of this section were an integer that held
				// the offset of the previous entry in the file, or 0 if
				// it was the first file. The client reads the entries
				// from the bottom to the top, using these offsets as
				// jumping points. The rest of the data seems to be
				// random and the client doesn't care about it.
				var dummyCount = br.ReadInt32();
				if (dummyCount != 20)
					throw new InvalidDataException($"Unexpected dummy data count on file '{entry.FileName}'.");

				br.BaseStream.Seek(16, SeekOrigin.Current);
				entry.PrevEntryOffset = br.ReadInt32();

				br.BaseStream.Seek(entry.SizeCompressed, SeekOrigin.Current);

				entries.Add(entry);
			}

			return entries;
		}

		static string GetFormattedSize(int size)
		{
			if (size > 1024 * 1024)
				return string.Format("{0:0.#} MB", size / 1024f / 1024f);
			else if (size > 1024)
				return string.Format("{0:0.#} KB", size / 1024f);
			else
				return string.Format("{0:0.#} B", size);
		}

		static int RSHash(string str)
		{
			var b = 0x5C6B7u;
			var a = 0x0F8C9u;
			var hash = 0u;

			for (var i = 0; i < str.Length; ++i)
			{
				hash = hash * a + ((byte)str[i]);
				a *= b;
			}

			return (int)hash;
		}
	}
}
