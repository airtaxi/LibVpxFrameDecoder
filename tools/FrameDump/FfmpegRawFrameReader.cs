using System.Diagnostics;

namespace FrameDump;

/// <summary>Decodes a single frame with ffmpeg as raw pixels in the requested packed format, used as a reference for comparison.</summary>
internal static class FfmpegRawFrameReader
{
	internal static byte[] ReadFrame(string ffmpegPath, string filePath, int frameIndex, int width, int height, string pixelFormat)
	{
		var expectedLength = width * height * 4;
		var arguments = $"-v error -i \"{filePath}\" -map 0:v:0 -vf \"select=eq(n\\,{frameIndex})\" -frames:v 1 -pix_fmt {pixelFormat} -f rawvideo -";

		using var process = new Process();
		process.StartInfo = new ProcessStartInfo(ffmpegPath, arguments)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};

		process.Start();
		var errorTask = process.StandardError.ReadToEndAsync();
		using var output = new MemoryStream();
		process.StandardOutput.BaseStream.CopyTo(output);
		process.WaitForExit();
		var error = errorTask.GetAwaiter().GetResult();

		if (process.ExitCode != 0) throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode}: {error.Trim()}");
		if (output.Length != expectedLength) throw new InvalidOperationException($"ffmpeg returned {output.Length} bytes, expected {expectedLength}.");

		return output.ToArray();
	}
}
