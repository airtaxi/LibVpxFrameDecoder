namespace LibVpxFrameDecoder.Webm;

/// <summary>
/// WebM/Matroska demuxer. Emits blocks of the first video track and keeps the alpha frame
/// carried in BlockAdditional (BlockAddID = 1) alongside the color frame, which is how WebM stores VP8/VP9 alpha.
/// </summary>
internal sealed class WebmDemuxer : IDisposable
{
	private const int InitialBufferSize = 256 * 1024;

	private readonly BufferedStream _stream;
	private readonly EbmlReader _reader;
	private byte[] _blockBuffer = new byte[InitialBufferSize];
	private byte[] _alphaBuffer = new byte[InitialBufferSize];

	private int _blockOffset;
	private int _blockLength;
	private int _alphaOffset;
	private int _alphaLength;
	private long _segmentEnd;
	private long _firstClusterPosition = -1;
	private long _clusterEnd = -1;
	private bool _inCluster;
	private long _clusterTimecodeNanoseconds;
	private ulong _timecodeScale = 1_000_000;
	private List<WebmClusterIndexEntry> _clusterIndex;
	private bool _disposed;

	private WebmDemuxer(FileStream fileStream)
	{
		_stream = new BufferedStream(fileStream, 64 * 1024);
		_reader = new EbmlReader(_stream);
	}

	internal WebmTrackInfo VideoTrack { get; private set; }

	internal TimeSpan Duration { get; private set; }

	internal ReadOnlySpan<byte> PacketData => _blockBuffer.AsSpan(_blockOffset, _blockLength);

	/// <summary>The alpha packet as a segment so it can be handed to the parallel alpha decode thread.</summary>
	internal ArraySegment<byte> AlphaPacketSegment => new(_alphaBuffer, _alphaOffset, _alphaLength);

	internal static WebmDemuxer Open(string filePath)
	{
		ArgumentException.ThrowIfNullOrEmpty(filePath);

		var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
		var demuxer = new WebmDemuxer(fileStream);
		try
		{
			demuxer.Initialize();
			return demuxer;
		}
		catch
		{
			demuxer.Dispose();
			throw;
		}
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_stream.Dispose();
	}

	/// <summary>Moves the read position to the cluster that contains the given time.</summary>
	internal void Seek(TimeSpan position)
	{
		_clusterIndex ??= BuildClusterIndex();

		var target = position < TimeSpan.Zero ? TimeSpan.Zero : position;
		var selectedPosition = _firstClusterPosition;
		if (_clusterIndex.Count > 0)
		{
			selectedPosition = _clusterIndex[0].Position;
			foreach (var entry in _clusterIndex)
			{
				if (entry.Timestamp > target) break;
				selectedPosition = entry.Position;
			}
		}

		_reader.Position = selectedPosition;
		_inCluster = false;
		_clusterEnd = -1;
		_clusterTimecodeNanoseconds = 0;
	}

	/// <summary>Reads the next block of the video track. Returns false at the end of the stream.</summary>
	internal bool TryReadPacket(out WebmPacket packet)
	{
		packet = default;
		while (true)
		{
			if (!_inCluster)
			{
				if (!_reader.TryReadElementHeader(out var elementId, out var elementSize, out var hasUnknownSize)) return false;
				if (elementId == EbmlElementId.Cluster)
				{
					_inCluster = true;
					_clusterEnd = hasUnknownSize ? -1 : _reader.Position + (long)elementSize;
					_clusterTimecodeNanoseconds = 0;
					continue;
				}
				if (hasUnknownSize) throw new WebmFormatException($"Element 0x{elementId:X} has an unknown size outside a cluster.");
				_reader.Skip(elementSize);
				continue;
			}

			if (_clusterEnd >= 0 && _reader.Position >= _clusterEnd)
			{
				_inCluster = false;
				continue;
			}
			if (_reader.IsEndOfStream) return false;

			var childPosition = _reader.Position;
			if (!_reader.TryReadElementHeader(out var childId, out var childSize, out var childHasUnknownSize)) return false;
			if (_clusterEnd < 0 && !IsClusterChildElement(childId))
			{
				// Unknown size cluster: rewind so the outer loop can handle the next segment level element.
				_reader.Position = childPosition;
				_inCluster = false;
				continue;
			}
			if (childHasUnknownSize) throw new WebmFormatException($"Cluster child 0x{childId:X} has an unknown size.");

			switch (childId)
			{
				case EbmlElementId.ClusterTimecode:
					_clusterTimecodeNanoseconds = (long)ReadUnsignedIntegerElement(childSize) * (long)_timecodeScale;
					break;
				case EbmlElementId.SimpleBlock:
					if (ReadSimpleBlock(childSize, out packet)) return true;
					break;
				case EbmlElementId.BlockGroup:
					if (ReadBlockGroup(childSize, out packet)) return true;
					break;
				default:
					_reader.Skip(childSize);
					break;
			}
		}
	}

	private void Initialize()
	{
		ReadEbmlHeader();
		ReadMetadata(ReadSegmentEnd());
	}

	private void ReadEbmlHeader()
	{
		if (_reader.Length < 4) throw new WebmFormatException("The file is empty.");

		Span<byte> magic = stackalloc byte[4];
		_reader.ReadExactly(magic);
		if (magic[0] != 0x1A || magic[1] != 0x45 || magic[2] != 0xDF || magic[3] != 0xA3) throw new WebmFormatException("The file is not a WebM/Matroska file (the EBML header is missing).");

		var elementSize = _reader.ReadVariableSizeInteger(out var hasUnknownSize);
		if (hasUnknownSize) throw new WebmFormatException("The EBML header must have a known size.");

		var elementEnd = _reader.Position + (long)elementSize;
		while (_reader.Position < elementEnd)
		{
			if (!_reader.TryReadElementHeader(out var childId, out var childSize, out var childHasUnknownSize)) throw new WebmFormatException("The EBML header is truncated.");
			if (childHasUnknownSize) throw new WebmFormatException("An EBML header child has an unknown size.");

			if (childId == EbmlElementId.DocType)
			{
				var docType = _reader.ReadString((int)childSize);
				if (docType != "webm" && docType != "matroska") throw new WebmFormatException($"Unsupported EBML DocType '{docType}'.");
			}
			else _reader.Skip(childSize);
		}

		_reader.Position = elementEnd;
	}

	private long ReadSegmentEnd()
	{
		if (!_reader.TryReadElementHeader(out var elementId, out var elementSize, out var hasUnknownSize)) throw new WebmFormatException("The segment element is missing.");
		if (elementId != EbmlElementId.Segment) throw new WebmFormatException($"Expected a Segment element but found 0x{elementId:X}.");
		if (hasUnknownSize) return _reader.Length;

		var segmentEnd = _reader.Position + (long)elementSize;
		if (segmentEnd > _reader.Length) throw new WebmFormatException("The segment size exceeds the file size.");
		return segmentEnd;
	}

	private void ReadMetadata(long segmentEnd)
	{
		_segmentEnd = segmentEnd;
		while (_reader.Position < segmentEnd)
		{
			var elementPosition = _reader.Position;
			if (!_reader.TryReadElementHeader(out var elementId, out var elementSize, out var hasUnknownSize)) break;
			if (elementId == EbmlElementId.Cluster)
			{
				_firstClusterPosition = elementPosition;
				_reader.Position = elementPosition;
				break;
			}
			if (hasUnknownSize) throw new WebmFormatException($"Element 0x{elementId:X} before the first cluster has an unknown size.");

			switch (elementId)
			{
				case EbmlElementId.Info:
					ReadInfoElement(elementSize);
					break;
				case EbmlElementId.Tracks:
					ReadTracksElement(elementSize);
					break;
				default:
					_reader.Skip(elementSize);
					break;
			}
		}

		if (VideoTrack == null) throw new WebmFormatException("The file has no video track.");
		if (_firstClusterPosition < 0) throw new WebmFormatException("The file has no cluster.");
	}

	private void ReadInfoElement(ulong elementSize)
	{
		var elementEnd = _reader.Position + (long)elementSize;
		var rawDuration = 0d;
		var hasDuration = false;

		while (_reader.Position < elementEnd)
		{
			if (!_reader.TryReadElementHeader(out var childId, out var childSize, out var childHasUnknownSize)) throw new WebmFormatException("The Info element is truncated.");
			if (childHasUnknownSize) throw new WebmFormatException("An Info child has an unknown size.");

			switch (childId)
			{
				case EbmlElementId.TimecodeScale:
				{
					var timecodeScale = ReadUnsignedIntegerElement(childSize);
					if (timecodeScale > 0) _timecodeScale = timecodeScale;
					break;
				}
				case EbmlElementId.Duration:
					rawDuration = _reader.ReadFloat((int)childSize);
					hasDuration = true;
					break;
				default:
					_reader.Skip(childSize);
					break;
			}
		}

		_reader.Position = elementEnd;
		if (hasDuration && rawDuration > 0) Duration = FromNanoseconds((long)(rawDuration * _timecodeScale));
	}

	private void ReadTracksElement(ulong elementSize)
	{
		var elementEnd = _reader.Position + (long)elementSize;
		while (_reader.Position < elementEnd)
		{
			if (!_reader.TryReadElementHeader(out var childId, out var childSize, out var childHasUnknownSize)) throw new WebmFormatException("The Tracks element is truncated.");
			if (childHasUnknownSize) throw new WebmFormatException("A Tracks child has an unknown size.");

			if (childId == EbmlElementId.TrackEntry) ReadTrackEntry(childSize);
			else _reader.Skip(childSize);
		}

		_reader.Position = elementEnd;
	}

	private void ReadTrackEntry(ulong elementSize)
	{
		var track = new WebmTrackInfo();
		var trackType = 0UL;
		var elementEnd = _reader.Position + (long)elementSize;

		while (_reader.Position < elementEnd)
		{
			if (!_reader.TryReadElementHeader(out var childId, out var childSize, out var childHasUnknownSize)) throw new WebmFormatException("A TrackEntry is truncated.");
			if (childHasUnknownSize) throw new WebmFormatException("A TrackEntry child has an unknown size.");

			switch (childId)
			{
				case EbmlElementId.TrackNumber:
					track.TrackNumber = ReadUnsignedIntegerElement(childSize);
					break;
				case EbmlElementId.TrackType:
					trackType = ReadUnsignedIntegerElement(childSize);
					break;
				case EbmlElementId.CodecId:
					track.CodecId = _reader.ReadString((int)childSize);
					break;
				case EbmlElementId.DefaultDuration:
					track.DefaultDurationNanoseconds = ReadUnsignedIntegerElement(childSize);
					break;
				case EbmlElementId.Video:
					ReadVideoElement(track, childSize);
					break;
				default:
					_reader.Skip(childSize);
					break;
			}
		}

		_reader.Position = elementEnd;
		if (trackType == 1 && VideoTrack == null) VideoTrack = track;
	}

	private void ReadVideoElement(WebmTrackInfo track, ulong elementSize)
	{
		var elementEnd = _reader.Position + (long)elementSize;
		while (_reader.Position < elementEnd)
		{
			if (!_reader.TryReadElementHeader(out var childId, out var childSize, out var childHasUnknownSize)) throw new WebmFormatException("The Video element is truncated.");
			if (childHasUnknownSize) throw new WebmFormatException("A Video child has an unknown size.");

			switch (childId)
			{
				case EbmlElementId.PixelWidth:
					track.PixelWidth = (int)ReadUnsignedIntegerElement(childSize);
					break;
				case EbmlElementId.PixelHeight:
					track.PixelHeight = (int)ReadUnsignedIntegerElement(childSize);
					break;
				case EbmlElementId.AlphaMode:
					track.AlphaMode = ReadUnsignedIntegerElement(childSize) != 0;
					break;
				default:
					_reader.Skip(childSize);
					break;
			}
		}

		_reader.Position = elementEnd;
	}

	private bool ReadSimpleBlock(ulong payloadSize, out WebmPacket packet)
	{
		packet = default;
		_blockOffset = 0;
		_blockLength = 0;
		_alphaOffset = 0;
		_alphaLength = 0;

		var payloadLength = checked((int)payloadSize);
		_blockBuffer = EnsureCapacity(_blockBuffer, payloadLength);
		_reader.ReadExactly(_blockBuffer.AsSpan(0, payloadLength));

		var payload = _blockBuffer.AsSpan(0, payloadLength);
		var position = 0;
		var trackNumber = EbmlReader.ReadVariableSizeInteger(payload, ref position);
		if (trackNumber != VideoTrack.TrackNumber) return false;
		if (position + 3 > payload.Length) throw new WebmFormatException("A SimpleBlock header is truncated.");

		var relativeTimestamp = (short)((payload[position] << 8) | payload[position + 1]);
		var flags = payload[position + 2];
		position += 3;

		var isKeyFrame = (flags & 0x80) != 0;
		var frameLength = GetFirstFrameLength(payload, ref position, (flags >> 1) & 0x03);
		if (frameLength < 0 || frameLength > payload.Length - position) throw new WebmFormatException("A SimpleBlock frame length is invalid.");

		_blockOffset = position;
		_blockLength = frameLength;
		packet = new WebmPacket(FromNanoseconds(_clusterTimecodeNanoseconds + relativeTimestamp * (long)_timecodeScale), isKeyFrame, frameLength, 0);
		return true;
	}

	private bool ReadBlockGroup(ulong payloadSize, out WebmPacket packet)
	{
		packet = default;
		var groupEnd = _reader.Position + (long)payloadSize;
		var trackNumber = 0UL;
		var blockFound = false;
		var isKeyFrame = true;
		var blockRelativeTimestamp = 0;
		var blockOffset = 0;
		var blockLength = 0;
		_alphaOffset = 0;
		_alphaLength = 0;

		while (_reader.Position < groupEnd)
		{
			if (!_reader.TryReadElementHeader(out var childId, out var childSize, out var childHasUnknownSize)) throw new WebmFormatException("A BlockGroup is truncated.");
			if (childHasUnknownSize) throw new WebmFormatException("A BlockGroup child has an unknown size.");

			switch (childId)
			{
				case EbmlElementId.Block:
				{
					var payloadLength = checked((int)childSize);
					_blockBuffer = EnsureCapacity(_blockBuffer, payloadLength);
					_reader.ReadExactly(_blockBuffer.AsSpan(0, payloadLength));

					var payload = _blockBuffer.AsSpan(0, payloadLength);
					var position = 0;
					trackNumber = EbmlReader.ReadVariableSizeInteger(payload, ref position);
					if (position + 3 > payload.Length) throw new WebmFormatException("A Block header is truncated.");

					blockRelativeTimestamp = (short)((payload[position] << 8) | payload[position + 1]);
					var flags = payload[position + 2];
					position += 3;

					var frameLength = GetFirstFrameLength(payload, ref position, (flags >> 1) & 0x03);
					if (frameLength < 0 || frameLength > payload.Length - position) throw new WebmFormatException("A Block frame length is invalid.");

					blockOffset = position;
					blockLength = frameLength;
					blockFound = true;
					break;
				}
				case EbmlElementId.BlockAdditions:
					ReadBlockAdditions(childSize);
					break;
				case EbmlElementId.ReferenceBlock:
					isKeyFrame = false;
					_reader.Skip(childSize);
					break;
				default:
					_reader.Skip(childSize);
					break;
			}
		}

		_reader.Position = groupEnd;
		if (!blockFound || trackNumber != VideoTrack.TrackNumber) return false;

		_blockOffset = blockOffset;
		_blockLength = blockLength;
		packet = new WebmPacket(FromNanoseconds(_clusterTimecodeNanoseconds + blockRelativeTimestamp * (long)_timecodeScale), isKeyFrame, blockLength, _alphaLength);
		return true;
	}

	/// <summary>Reads BlockAdditions. WebM alpha lives in the BlockMore child with BlockAddID 1.</summary>
	private void ReadBlockAdditions(ulong payloadSize)
	{
		var elementEnd = _reader.Position + (long)payloadSize;
		while (_reader.Position < elementEnd)
		{
			if (!_reader.TryReadElementHeader(out var childId, out var childSize, out var childHasUnknownSize)) throw new WebmFormatException("BlockAdditions is truncated.");
			if (childHasUnknownSize) throw new WebmFormatException("A BlockAdditions child has an unknown size.");

			if (childId == EbmlElementId.BlockMore) ReadBlockMore(childSize);
			else _reader.Skip(childSize);
		}

		_reader.Position = elementEnd;
	}

	private void ReadBlockMore(ulong payloadSize)
	{
		var elementEnd = _reader.Position + (long)payloadSize;
		var blockAddId = 1UL;
		var alphaOffset = 0;
		var alphaLength = 0;
		var hasAdditional = false;

		while (_reader.Position < elementEnd)
		{
			if (!_reader.TryReadElementHeader(out var childId, out var childSize, out var childHasUnknownSize)) throw new WebmFormatException("BlockMore is truncated.");
			if (childHasUnknownSize) throw new WebmFormatException("A BlockMore child has an unknown size.");

			switch (childId)
			{
				case EbmlElementId.BlockAddId:
					blockAddId = ReadUnsignedIntegerElement(childSize);
					break;
				case EbmlElementId.BlockAdditional:
				{
					var payloadLength = checked((int)childSize);
					_alphaBuffer = EnsureCapacity(_alphaBuffer, payloadLength);
					_reader.ReadExactly(_alphaBuffer.AsSpan(0, payloadLength));
					alphaOffset = 0;
					alphaLength = payloadLength;
					hasAdditional = true;
					break;
				}
				default:
					_reader.Skip(childSize);
					break;
			}
		}

		_reader.Position = elementEnd;
		if (!hasAdditional || blockAddId != 1) return;

		// BlockAddID 1 carries the alpha frame. A later BlockMore would replace an earlier one.
		_alphaOffset = alphaOffset;
		_alphaLength = alphaLength;
	}

	private List<WebmClusterIndexEntry> BuildClusterIndex()
	{
		var index = new List<WebmClusterIndexEntry>();
		var savedPosition = _reader.Position;

		_reader.Position = _firstClusterPosition;
		while (_reader.Position < _segmentEnd)
		{
			var elementPosition = _reader.Position;
			if (!_reader.TryReadElementHeader(out var elementId, out var elementSize, out var hasUnknownSize)) break;
			if (elementId != EbmlElementId.Cluster)
			{
				if (hasUnknownSize) break;
				_reader.Skip(elementSize);
				continue;
			}

			var clusterEnd = hasUnknownSize ? -1 : _reader.Position + (long)elementSize;
			var timecode = ReadClusterTimecode(clusterEnd);
			index.Add(new WebmClusterIndexEntry(FromNanoseconds(timecode), elementPosition));

			if (clusterEnd >= 0) _reader.Position = clusterEnd;
			else SkipUnknownSizeCluster();
		}

		_reader.Position = savedPosition;
		return index;
	}

	private void SkipUnknownSizeCluster()
	{
		while (_reader.Position < _segmentEnd)
		{
			var childPosition = _reader.Position;
			if (!_reader.TryReadElementHeader(out var childId, out var childSize, out var childHasUnknownSize)) return;
			if (!IsClusterChildElement(childId))
			{
				_reader.Position = childPosition;
				return;
			}
			if (childHasUnknownSize) throw new WebmFormatException("A cluster child has an unknown size.");
			_reader.Skip(childSize);
		}
	}

	private long ReadClusterTimecode(long clusterEnd)
	{
		while (_reader.Position < _segmentEnd && (clusterEnd < 0 || _reader.Position < clusterEnd))
		{
			var childPosition = _reader.Position;
			if (!_reader.TryReadElementHeader(out var childId, out var childSize, out var childHasUnknownSize)) break;
			if (clusterEnd < 0 && !IsClusterChildElement(childId))
			{
				_reader.Position = childPosition;
				break;
			}
			if (childHasUnknownSize) throw new WebmFormatException("A cluster child has an unknown size.");
			if (childId == EbmlElementId.ClusterTimecode) return (long)ReadUnsignedIntegerElement(childSize) * (long)_timecodeScale;
			_reader.Skip(childSize);
		}

		return 0;
	}

	/// <summary>Returns the length of the first frame of a block, applying the lacing mode.</summary>
	private static int GetFirstFrameLength(ReadOnlySpan<byte> payload, ref int position, int lacing)
	{
		var remaining = payload.Length - position;
		switch (lacing)
		{
			case 0:
				return remaining;
			case 1:
			{
				// Xiph lacing stores the size of every frame except the last, so the first run is the first frame's length.
				var length = 0;
				byte value;
				do
				{
					value = payload[position++];
					length += value;
				}
				while (value == 255);
				return length;
			}
			case 3:
				return (int)EbmlReader.ReadVariableSizeInteger(payload, ref position);
			default:
				// Fixed lacing does not store the frame count, so the whole payload is treated as one frame.
				return remaining;
		}
	}

	private static bool IsClusterChildElement(uint elementId) =>
	    elementId is EbmlElementId.ClusterTimecode or EbmlElementId.SimpleBlock or EbmlElementId.BlockGroup or EbmlElementId.Position or EbmlElementId.PrevSize or EbmlElementId.EncryptedBlock or EbmlElementId.SilentTracks or EbmlElementId.Void or EbmlElementId.Crc32;

	private ulong ReadUnsignedIntegerElement(ulong elementSize)
	{
		if (elementSize > 8) throw new WebmFormatException($"An unsigned integer element is {elementSize} bytes long.");
		return _reader.ReadUnsignedInteger((int)elementSize);
	}

	private static byte[] EnsureCapacity(byte[] buffer, int requiredLength)
	{
		if (buffer.Length >= requiredLength) return buffer;
		var newLength = buffer.Length;
		while (newLength < requiredLength) newLength *= 2;
		return new byte[newLength];
	}

	private static TimeSpan FromNanoseconds(long nanoseconds) => TimeSpan.FromTicks(nanoseconds / 100);
}
