namespace LibVpxFrameDecoder;

/// <summary>Thrown when a native libvpx call fails.</summary>
public sealed class VpxException : Exception
{
	public VpxException() { }

	public VpxException(string message) : base(message) { }

	public VpxException(string message, Exception innerException) : base(message, innerException) { }
}
