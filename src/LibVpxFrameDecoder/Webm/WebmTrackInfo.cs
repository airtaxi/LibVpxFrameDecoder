namespace LibVpxFrameDecoder.Webm;

/// <summary>Video track information parsed from the Tracks element.</summary>
internal sealed class WebmTrackInfo
{
	internal ulong TrackNumber { get; set; }
	internal string CodecId { get; set; } = string.Empty;
	internal int PixelWidth { get; set; }
	internal int PixelHeight { get; set; }
	internal ulong DefaultDurationNanoseconds { get; set; }
	internal bool AlphaMode { get; set; }
	internal bool IsVp9 => CodecId == "V_VP9";
	internal bool IsVp8 => CodecId == "V_VP8";
}
