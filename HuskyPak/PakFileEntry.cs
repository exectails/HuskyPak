namespace HuskyPak
{
	internal class PakFileEntry
	{
		public int Unk1 { get; set; }
		public int Unk2 { get; set; }
		public int DataOffset { get; set; }
		public int SizeCompressed { get; set; }
		public int SizeUncompressed { get; set; }
		public string FileName { get; set; }
		public int PrevEntryOffset { get; set; }
	}
}
