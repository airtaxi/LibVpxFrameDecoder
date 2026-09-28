namespace LibVpxFrameDecoder.Webm;

/// <summary>Maps a cluster start time to its file position. Used for seeking.</summary>
internal readonly struct WebmClusterIndexEntry(TimeSpan timestamp, long position)
{
	internal TimeSpan Timestamp { get; } = timestamp;

	internal long Position { get; } = position;
}
