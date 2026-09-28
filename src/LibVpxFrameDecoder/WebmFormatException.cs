namespace LibVpxFrameDecoder;

/// <summary>Thrown when the structure of a WebM/EBML stream is not valid.</summary>
public sealed class WebmFormatException : Exception
{
	public WebmFormatException() { }

	public WebmFormatException(string message) : base(message) { }

	public WebmFormatException(string message, Exception innerException) : base(message, innerException) { }
}
