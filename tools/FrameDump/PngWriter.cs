using System.IO.Compression;

namespace FrameDump;

/// <summary>Minimal PNG writer for 8 bit RGBA frames, with no external dependencies.</summary>
internal static class PngWriter
{
	private static readonly uint[] s_crcTable = CreateCrcTable();

	internal static void WriteRgba(string filePath, ReadOnlySpan<byte> pixels, int width, int height)
	{
		var stride = width * 4;
		using var stream = File.Create(filePath);
		stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

		Span<byte> header = stackalloc byte[13];
		WriteBigEndian(header[0..4], (uint)width);
		WriteBigEndian(header[4..8], (uint)height);
		header[8] = 8;  // bit depth
		header[9] = 6;  // color type: RGBA
		header[10] = 0; // compression method
		header[11] = 0; // filter method
		header[12] = 0; // interlace method
		WriteChunk(stream, "IHDR"u8, header);

		using var compressed = new MemoryStream();
		using (var deflate = new ZLibStream(compressed, CompressionLevel.Fastest, true))
		{
			var row = new byte[stride + 1];
			for (var y = 0; y < height; y++)
			{
				row[0] = 0;
				pixels.Slice(y * stride, stride).CopyTo(row.AsSpan(1));
				deflate.Write(row);
			}
		}

		WriteChunk(stream, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
		WriteChunk(stream, "IEND"u8, []);
	}

	private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
	{
		Span<byte> lengthBuffer = stackalloc byte[4];
		WriteBigEndian(lengthBuffer, (uint)data.Length);
		stream.Write(lengthBuffer);
		stream.Write(type);
		stream.Write(data);

		Span<byte> crcBuffer = stackalloc byte[4];
		WriteBigEndian(crcBuffer, ComputeCrc32(type, data));
		stream.Write(crcBuffer);
	}

	private static void WriteBigEndian(Span<byte> destination, uint value)
	{
		destination[0] = (byte)(value >> 24);
		destination[1] = (byte)(value >> 16);
		destination[2] = (byte)(value >> 8);
		destination[3] = (byte)value;
	}

	private static uint ComputeCrc32(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
	{
		var crc = 0xFFFFFFFFu;
		crc = UpdateCrc(crc, first);
		crc = UpdateCrc(crc, second);
		return crc ^ 0xFFFFFFFFu;
	}

	private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
	{
		foreach (var value in data) crc = s_crcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
		return crc;
	}

	private static uint[] CreateCrcTable()
	{
		var table = new uint[256];
		for (var index = 0; index < table.Length; index++)
		{
			var value = (uint)index;
			for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
			table[index] = value;
		}

		return table;
	}
}
