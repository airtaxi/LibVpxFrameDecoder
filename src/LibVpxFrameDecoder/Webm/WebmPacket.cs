namespace LibVpxFrameDecoder.Webm;

/// <summary>
/// One demuxed video block. The payload lives in the demuxer's reusable buffers,
/// so it is only valid until the next read.
/// </summary>
internal readonly struct WebmPacket(TimeSpan timestamp, bool isKeyFrame, int dataLength, int alphaDataLength)
{
	internal TimeSpan Timestamp { get; } = timestamp;

	internal bool IsKeyFrame { get; } = isKeyFrame;

	internal int DataLength { get; } = dataLength;

	internal int AlphaDataLength { get; } = alphaDataLength;

	internal bool HasAlpha => AlphaDataLength > 0;
}
