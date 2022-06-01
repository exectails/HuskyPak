using System.IO;

namespace HuskyPak
{
	internal class PakFileStream : FileStream
	{
		public PakFileStream(string path, FileMode mode) : base(path, mode)
		{
		}

		public override int Read(byte[] array, int offset, int count)
		{
			var result = base.Read(array, offset, count);
			DecodeBuffer(ref array, offset, count);

			return result;
		}

		public override int ReadByte()
		{
			return (byte)(base.ReadByte() ^ 0x59);
		}

		public override void Write(byte[] array, int offset, int count)
		{
			EncodeBuffer(ref array, offset, count);
			base.Write(array, offset, count);
		}

		public override void WriteByte(byte value)
		{
			base.WriteByte((byte)(value ^ 0x59));
		}

		static void DecodeBuffer(ref byte[] data, int offset, int count)
		{
			for (var i = offset; i < offset + count; ++i)
				data[i] ^= 0x59;
		}

		static void EncodeBuffer(ref byte[] data, int offset, int count)
			=> DecodeBuffer(ref data, offset, count);
	}
}
