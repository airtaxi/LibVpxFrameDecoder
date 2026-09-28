namespace LibVpxFrameDecoder.Webm;

/// <summary>EBML/WebM element ids this library understands.</summary>
internal static class EbmlElementId
{
	internal const uint EbmlHeader = 0x1A45DFA3;
	internal const uint DocType = 0x4282;
	internal const uint Segment = 0x18538067;
	internal const uint SeekHead = 0x114D9B74;
	internal const uint Info = 0x1549A966;
	internal const uint TimecodeScale = 0x2AD7B1;
	internal const uint Duration = 0x4489;
	internal const uint Tracks = 0x1654AE6B;
	internal const uint TrackEntry = 0xAE;
	internal const uint TrackNumber = 0xD7;
	internal const uint TrackType = 0x83;
	internal const uint CodecId = 0x86;
	internal const uint DefaultDuration = 0x23E383;
	internal const uint Video = 0xE0;
	internal const uint PixelWidth = 0xB0;
	internal const uint PixelHeight = 0xBA;
	internal const uint AlphaMode = 0x53C0;
	internal const uint Cluster = 0x1F43B675;
	internal const uint ClusterTimecode = 0xE7;
	internal const uint SimpleBlock = 0xA3;
	internal const uint BlockGroup = 0xA0;
	internal const uint Block = 0xA1;
	internal const uint BlockAdditions = 0x75A1;
	internal const uint BlockMore = 0xA6;
	internal const uint BlockAddId = 0xEE;
	internal const uint BlockAdditional = 0xA5;
	internal const uint BlockDuration = 0x9B;
	internal const uint ReferenceBlock = 0xFB;
	internal const uint Position = 0xA7;
	internal const uint PrevSize = 0xAB;
	internal const uint EncryptedBlock = 0xAF;
	internal const uint SilentTracks = 0x5854;
	internal const uint DiscardPadding = 0x75A2;
	internal const uint Cues = 0x1C53BB6B;
	internal const uint Tags = 0x1254C367;
	internal const uint Chapters = 0x1043A770;
	internal const uint Attachments = 0x1941A469;
	internal const uint Void = 0xEC;
	internal const uint Crc32 = 0xBF;
}
