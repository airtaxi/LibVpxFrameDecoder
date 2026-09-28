using System.Text;

namespace LibVpxFrameDecoder.Webm;

/// <summary>
/// Minimal EBML reader over a seekable stream. It tracks absolute positions so callers can seek and skip.
/// </summary>
internal sealed class EbmlReader(Stream stream)
{
	private readonly Stream _stream = stream;

	internal long Position { get => _stream.Position; set => _stream.Position = value; }

	internal long Length => _stream.Length;

	internal bool IsEndOfStream => _stream.Position >= _stream.Length;

	/// <summary>Reads the next element header. Returns false at the end of the stream.</summary>
	internal bool TryReadElementHeader(out uint elementId, out ulong elementSize, out bool hasUnknownSize)
	{
		elementId = 0;
		elementSize = 0;
		hasUnknownSize = false;
		if (IsEndOfStream) return false;

		elementId = ReadElementId();
		elementSize = ReadVariableSizeInteger(out hasUnknownSize);
		return true;
	}

	/// <summary>Reads an element id. The marker bits are part of the id, so the raw value is returned.</summary>
	internal uint ReadElementId()
	{
		var firstByte = ReadByte();
		var length = GetVariableSizeLength(firstByte);
		if (length > 4) throw new WebmFormatException($"EBML element id length {length} is not supported.");

		uint value = firstByte;
		for (var index = 1; index < length; index++) value = (value << 8) | ReadByte();
		return value;
	}

	/// <summary>Reads a variable size integer. The marker bits are stripped from the returned value.</summary>
	internal ulong ReadVariableSizeInteger(out bool hasUnknownSize)
	{
		var firstByte = ReadByte();
		var length = GetVariableSizeLength(firstByte);
		var value = (ulong)(firstByte & (0xFF >> length));
		for (var index = 1; index < length; index++) value = (value << 8) | ReadByte();

		hasUnknownSize = value == (1UL << (7 * length)) - 1;
		return value;
	}

	internal byte ReadByte()
	{
		var value = _stream.ReadByte();
		if (value < 0) throw new WebmFormatException("The EBML stream ended unexpectedly.");
		return (byte)value;
	}

	internal void ReadExactly(Span<byte> destination) => _stream.ReadExactly(destination);

	internal ulong ReadUnsignedInteger(int byteCount)
	{
		ulong value = 0;
		for (var index = 0; index < byteCount; index++) value = (value << 8) | ReadByte();
		return value;
	}

	internal double ReadFloat(int byteCount)
	{
		Span<byte> buffer = stackalloc byte[8];
		var target = buffer[..byteCount];
		ReadExactly(target);
		target.Reverse();
		return byteCount switch
		{
			4 => BitConverter.ToSingle(target),
			8 => BitConverter.ToDouble(target),
			_ => throw new WebmFormatException($"Unsupported EBML float size {byteCount}."),
		};
	}

	internal string ReadString(int byteCount)
	{
		if (byteCount <= 0) return string.Empty;
		var buffer = new byte[byteCount];
		ReadExactly(buffer);
		var length = Array.IndexOf(buffer, (byte)0);
		if (length < 0) length = byteCount;
		return Encoding.UTF8.GetString(buffer, 0, length);
	}

	internal void Skip(ulong byteCount) => _stream.Seek((long)byteCount, SeekOrigin.Current);

	/// <summary>Reads a variable size integer from a buffer. Used for block payloads.</summary>
	internal static ulong ReadVariableSizeInteger(ReadOnlySpan<byte> source, ref int position)
	{
		var firstByte = source[position++];
		var length = GetVariableSizeLength(firstByte);
		var value = (ulong)(firstByte & (0xFF >> length));
		for (var index = 1; index < length; index++) value = (value << 8) | source[position++];
		return value;
	}

	/// <summary>Length in bytes of a variable size value from its leading byte.</summary>
	internal static int GetVariableSizeLength(byte firstByte)
	{
		if (firstByte == 0) throw new WebmFormatException("The leading byte of an EBML variable size value is zero.");

		var length = 1;
		for (var mask = 0x80; (firstByte & mask) == 0; mask >>= 1) length++;
		return length;
	}
}
