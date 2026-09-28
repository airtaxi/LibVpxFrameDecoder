using System.Diagnostics;
using LibVpxFrameDecoder;

namespace FrameDump;

internal static class Program
{
	private const double DisplayFrameBudgetMilliseconds = 1000d / 60d;

	private static readonly VpxFrameDecoderOptions s_toolOptions = new() { PremultiplyAlpha = false };

	private static int Main(string[] arguments)
	{
		if (arguments.Length == 0)
		{
			PrintUsage();
			return 1;
		}

		var command = arguments[0].ToLowerInvariant();
		var options = ToolOptions.Parse(arguments[1..]);
		try
		{
			return command switch
			{
				"info" => RunInfo(options),
				"dump" => RunDump(options),
				"compare" => RunCompare(options),
				"premultiply" => RunPremultiply(options),
				"concurrent" => RunConcurrent(options),
				"stress" => RunStress(options),
				"bench" => RunBench(options),
				"all" => RunAll(options),
				_ => RunUnknown(command),
			};
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine($"[error] {exception.Message}");
			if (exception.InnerException is not null) Console.Error.WriteLine($"[detail] {exception.InnerException.Message}");
			return 2;
		}
	}

	private static int RunUnknown(string command)
	{
		Console.Error.WriteLine($"[error] unknown command '{command}'.");
		PrintUsage();
		return 1;
	}

	private static void PrintUsage() =>
	    Console.WriteLine("""
			FrameDump - verification tool for the LibVpxFrameDecoder WebM (VP8/VP9) decoder

			Usage:
			  FrameDump info    <file|dir|pattern>...
			  FrameDump dump    <file|dir|pattern>... [--frame N | --all [--max N] [--step N]] [--out dir]
			  FrameDump compare <file|dir|pattern>... [--frame N] [--ffmpeg path] [--format rgba|bgra] [--mean 2.0] [--max 8]
			  FrameDump premultiply <file> [--frame N]
			  FrameDump concurrent <file>... [--frames 300] [--warmup 60] [--threads N] [--mode sequential|parallel|both]
			  FrameDump stress  <file> [--cycles 200] [--frames 10]
			  FrameDump bench   <file> [--warmup 30] [--frames 300] [--threads N]
			  FrameDump all     <dir|pattern>... [--out dir] [--compare] [--ffmpeg path]
			""");

	private static int RunInfo(ToolOptions options)
	{
		var files = GetFiles(options);
		if (files.Count == 0) throw new InvalidOperationException("no input file was found.");

		Console.WriteLine($"native libvpx: {VpxRuntime.LibraryVersionString}");
		Console.WriteLine($"{"file",-40} {"codec",-8} {"width",6} {"height",7} {"fps",8} {"seconds",9} {"alpha",6}");
		foreach (var file in files)
		{
			try
			{
				using var video = WebmVideo.Open(file, s_toolOptions);
				var info = video.Info;
				Console.WriteLine($"{Path.GetFileName(file),-40} {info.CodecId,-8} {info.Width,6} {info.Height,7} {info.FramesPerSecond,8:0.###} {info.Duration.TotalSeconds,9:0.###} {info.HasAlpha,6}");
			}
			catch (Exception exception) { Console.WriteLine($"{Path.GetFileName(file), -40} error: {exception.Message}"); }
		}

		return 0;
	}

	private static int RunDump(ToolOptions options)
	{
		var files = GetFiles(options);
		if (files.Count == 0) throw new InvalidOperationException("no input file was found.");

		var outputDirectory = options.GetString("out", ".");
		Directory.CreateDirectory(outputDirectory);
		var dumpAll = options.HasFlag("all");
		var singleFrame = options.GetInt32("frame", 0);
		var maximumFrames = Math.Max(1, options.GetInt32("max", 3));
		var step = Math.Max(1, options.GetInt32("step", 1));

		foreach (var file in files)
		{
			using var video = WebmVideo.Open(file, s_toolOptions);
			var frameIndex = 0;
			var dumpedFrames = 0;
			while (video.TryReadFrame(out _))
			{
				var shouldDump = dumpAll ? frameIndex % step == 0 && dumpedFrames < maximumFrames : frameIndex == singleFrame;
				if (shouldDump)
				{
					var outputPath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(file)}_frame{frameIndex:D4}.png");
					PngWriter.WriteRgba(outputPath, video.Pixels, video.Info.Width, video.Info.Height);
					Console.WriteLine($"{Path.GetFileName(file)} frame {frameIndex} -> {outputPath}");
					dumpedFrames++;
					if (!dumpAll) break;
				}

				frameIndex++;
			}
		}

		return 0;
	}

	private static int RunCompare(ToolOptions options)
	{
		var files = GetFiles(options);
		if (files.Count == 0) throw new InvalidOperationException("no input file was found.");

		var frameIndex = options.GetInt32("frame", 0);
		var ffmpegPath = options.GetString("ffmpeg", "ffmpeg");
		var meanTolerance = options.GetDouble("mean", 2.0);
		var maximumTolerance = options.GetDouble("max", 8.0);
		var pixelFormatName = options.GetString("format", "rgba").ToLowerInvariant();
		if (pixelFormatName != "rgba" && pixelFormatName != "bgra") throw new InvalidOperationException($"unsupported format '{pixelFormatName}' (use rgba or bgra).");
		var videoOptions = pixelFormatName == "bgra" ? new VpxFrameDecoderOptions { PremultiplyAlpha = false, PixelFormat = VpxPixelFormat.Bgra } : s_toolOptions;
		var failures = 0;

		foreach (var file in files)
		{
			using var video = WebmVideo.Open(file, videoOptions);
			if (!DecodeToFrame(video, frameIndex))
			{
				Console.WriteLine($"{Path.GetFileName(file)} frame {frameIndex}: frame not found");
				failures++;
				continue;
			}

			var reference = FfmpegRawFrameReader.ReadFrame(ffmpegPath, file, frameIndex, video.Info.Width, video.Info.Height, pixelFormatName);
			var (meanDifference, maximumDifference) = CompareRgb(video.Pixels, reference, video.Info.Width, video.Info.Height);
			var passed = meanDifference <= meanTolerance && maximumDifference <= maximumTolerance;
			Console.WriteLine($"{Path.GetFileName(file)} frame {frameIndex}: mean={meanDifference:0.###} max={maximumDifference} tolerance(mean<={meanTolerance}, max<={maximumTolerance}) -> {(passed ? "PASS" : "FAIL")}");
			if (!passed) failures++;
		}

		return failures == 0 ? 0 : 3;
	}

	/// <summary>Checks the premultiplied output against round(channel * alpha / 255) computed from the straight output.</summary>
	private static int RunPremultiply(ToolOptions options)
	{
		var file = GetSingleFile(options);
		var frameIndex = options.GetInt32("frame", 0);

		using var straightVideo = WebmVideo.Open(file, s_toolOptions);
		if (!DecodeToFrame(straightVideo, frameIndex))
		{
			Console.WriteLine($"{Path.GetFileName(file)} frame {frameIndex}: frame not found");
			return 3;
		}

		var straightPixels = straightVideo.Pixels;
		using var premultipliedVideo = WebmVideo.Open(file, new VpxFrameDecoderOptions { PremultiplyAlpha = true });
		if (!DecodeToFrame(premultipliedVideo, frameIndex))
		{
			Console.WriteLine($"{Path.GetFileName(file)} frame {frameIndex}: frame not found");
			return 3;
		}

		var premultipliedPixels = premultipliedVideo.Pixels;
		if (straightPixels.Length != premultipliedPixels.Length) throw new InvalidOperationException("the two decodes produced different frame sizes.");

		var maximumDifference = 0;
		var differentPixels = 0;
		for (var offset = 0; offset < premultipliedPixels.Length; offset += 4)
		{
			var alpha = straightPixels[offset + 3];
			var pixelDifference = 0;
			for (var channel = 0; channel < 3; channel++)
			{
				var expected = (straightPixels[offset + channel] * alpha + 127) / 255;
				var difference = Math.Abs(premultipliedPixels[offset + channel] - expected);
				if (difference > pixelDifference) pixelDifference = difference;
			}

			if (pixelDifference > 0) differentPixels++;
			if (pixelDifference > maximumDifference) maximumDifference = pixelDifference;
		}

		// libyuv rounds with (channel * alpha + 255) >> 8, which can differ from / 255 by one.
		var passed = maximumDifference <= 1;
		Console.WriteLine($"{Path.GetFileName(file)} frame {frameIndex} ({straightVideo.Info.Width}x{straightVideo.Info.Height}): max difference={maximumDifference}, pixels off by one or more={differentPixels} -> {(passed ? "PASS" : "FAIL")}");
		return passed ? 0 : 3;
	}

	/// <summary>Measures whether several 60 fps videos can be decoded together inside one 60 fps display frame.</summary>
	private static int RunConcurrent(ToolOptions options)
	{
		var files = GetFiles(options);
		if (files.Count < 2) throw new InvalidOperationException("the concurrent benchmark needs at least two input files.");

		var frameCount = Math.Max(1, options.GetInt32("frames", 300));
		var warmupFrames = Math.Max(0, options.GetInt32("warmup", 60));
		var threadCount = (uint)Math.Max(1, options.GetInt32("threads", (int)VpxFrameDecoderOptions.Default.ThreadCount));
		var mode = options.GetString("mode", "both").ToLowerInvariant();
		if (mode is not ("both" or "sequential" or "parallel")) throw new InvalidOperationException($"unsupported mode '{mode}' (use sequential, parallel or both).");

		Console.WriteLine($"files: {files.Count}, ticks: {frameCount}, warmup: {warmupFrames}, decoder threads per file: {threadCount}, processors: {Environment.ProcessorCount}");
		if (mode is "both" or "sequential") MeasureConcurrent(files, frameCount, warmupFrames, threadCount, false);
		if (mode is "both" or "parallel") MeasureConcurrent(files, frameCount, warmupFrames, threadCount, true);
		return 0;
	}

	/// <summary>Decodes one frame of every file per tick, either back to back or on all cores, and reports the tick times.</summary>
	private static void MeasureConcurrent(List<string> files, int frameCount, int warmupFrames, uint threadCount, bool parallel)
	{
		var videos = new List<WebmVideo>();
		var fileNames = new List<string>();
		var frameTimes = new double[frameCount];
		var videoFrameTimes = new double[files.Count];
		try
		{
			foreach (var file in files)
			{
				videos.Add(WebmVideo.Open(file, new VpxFrameDecoderOptions { ThreadCount = threadCount }));
				fileNames.Add(Path.GetFileName(file));
			}

			for (var warmup = 0; warmup < warmupFrames; warmup++)
			{
				foreach (var video in videos)
				{
				    ReadNextFrame(video);
				}
			}

			for (var tick = 0; tick < frameCount; tick++)
			{
				var tickStart = Stopwatch.GetTimestamp();
				if (parallel)
				{
					Parallel.For(0, videos.Count, index =>
					{
						var decodeStart = Stopwatch.GetTimestamp();
						ReadNextFrame(videos[index]);
						videoFrameTimes[index] += Stopwatch.GetElapsedTime(decodeStart).TotalMilliseconds;
					});
				}
				else
				{
					for (var index = 0; index < videos.Count; index++)
					{
						var decodeStart = Stopwatch.GetTimestamp();
						ReadNextFrame(videos[index]);
						videoFrameTimes[index] += Stopwatch.GetElapsedTime(decodeStart).TotalMilliseconds;
					}
				}

				frameTimes[tick] = Stopwatch.GetElapsedTime(tickStart).TotalMilliseconds;
			}

			ReportConcurrent(fileNames, videoFrameTimes, frameTimes, frameCount, parallel);
		}
		finally
		{
			foreach (var video in videos)
			{
			    video.Dispose();
			}
		}
	}

	/// <summary>Decodes the next frame of a video and rewinds it when the stream ends so the benchmark can keep running.</summary>
	private static void ReadNextFrame(WebmVideo video)
	{
		if (video.TryReadFrame(out _)) return;
		video.Seek(TimeSpan.Zero);
		video.TryReadFrame(out _);
	}

	private static void ReportConcurrent(List<string> fileNames, double[] videoFrameTimes, double[] frameTimes, int frameCount, bool parallel)
	{
		var sorted = frameTimes.OrderBy(value => value).ToArray();
		var mean = frameTimes.Average();
		var median = sorted[sorted.Length / 2];
		var p95 = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.95))];
		var p99 = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.99))];
		var maximum = sorted[^1];
		var overBudget = frameTimes.Count(value => value > DisplayFrameBudgetMilliseconds);

		Console.WriteLine();
		Console.WriteLine($"=== {(parallel ? "parallel" : "sequential")} mode: {frameCount} ticks, {fileNames.Count} videos ===");
		for (var index = 0; index < fileNames.Count; index++) Console.WriteLine($"  {fileNames[index], -36} {videoFrameTimes[index] / frameCount:0.###} ms/frame");
		Console.WriteLine($"  tick mean {mean:0.###} ms ({1000 / mean:0.#} fps), median {median:0.###}, p95 {p95:0.###}, p99 {p99:0.###}, max {maximum:0.###}");
		Console.WriteLine($"  ticks over {DisplayFrameBudgetMilliseconds:0.##} ms (60 fps budget): {overBudget} / {frameCount} ({100d * overBudget / frameCount:0.#}%)");
		Console.WriteLine($"  sustained worst case from p99: {1000 / p99:0.#} fps, from max: {1000 / maximum:0.#} fps");
	}

	private static int RunStress(ToolOptions options)
	{
		var file = GetSingleFile(options);
		var cycles = Math.Max(1, options.GetInt32("cycles", 200));
		var framesPerCycle = Math.Max(1, options.GetInt32("frames", 10));

		for (var cycle = 0; cycle < 5; cycle++)
		{
			using var warmupVideo = WebmVideo.Open(file);
			for (var index = 0; index < framesPerCycle; index++)
			{
				if (!warmupVideo.TryReadFrame(out _))
				{
				    break;
				}
			}
		}

		CollectGarbage();
		var managedBefore = GC.GetTotalMemory(true);
		var privateBytesBefore = Process.GetCurrentProcess().PrivateMemorySize64;

		for (var cycle = 0; cycle < cycles; cycle++)
		{
			using var video = WebmVideo.Open(file);
			for (var index = 0; index < framesPerCycle; index++)
			{
				if (!video.TryReadFrame(out _))
				{
				    break;
				}
			}
		}

		CollectGarbage();
		var managedAfter = GC.GetTotalMemory(true);
		var privateBytesAfter = Process.GetCurrentProcess().PrivateMemorySize64;

		Console.WriteLine($"file: {file}");
		Console.WriteLine($"cycles: {cycles}, frames per cycle: {framesPerCycle}");
		Console.WriteLine($"managed memory : {managedBefore:N0} -> {managedAfter:N0} (delta {managedAfter - managedBefore:+#,##0;-#,##0;0} bytes)");
		Console.WriteLine($"private bytes  : {privateBytesBefore:N0} -> {privateBytesAfter:N0} (delta {privateBytesAfter - privateBytesBefore:+#,##0;-#,##0;0} bytes)");

		using var exclusiveHandle = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
		Console.WriteLine("file handles released: ok");
		return 0;
	}

	private static int RunBench(ToolOptions options)
	{
		var file = GetSingleFile(options);
		var warmupFrames = Math.Max(0, options.GetInt32("warmup", 30));
		var framesToDecode = Math.Max(1, options.GetInt32("frames", 300));
		var threadCount = (uint)Math.Max(1, options.GetInt32("threads", (int)VpxFrameDecoderOptions.Default.ThreadCount));

		using var video = WebmVideo.Open(file, new VpxFrameDecoderOptions { ThreadCount = threadCount });
		for (var index = 0; index < warmupFrames; index++)
		{
			if (!video.TryReadFrame(out _))
			{
				video.Rewind();
				break;
			}
		}

		var stopwatch = Stopwatch.StartNew();
		var decodedFrames = 0;
		var decodeTime = TimeSpan.Zero;
		var convertTime = TimeSpan.Zero;
		while (decodedFrames < framesToDecode)
		{
			if (!video.TryReadFrame(out _)) break;
			decodeTime += video.LastDecodeTime;
			convertTime += video.LastConvertTime;
			decodedFrames++;
		}

		stopwatch.Stop();
		var framesPerSecond = 1000d / Math.Max(0.0001, stopwatch.Elapsed.TotalMilliseconds / Math.Max(1, decodedFrames));
		Console.WriteLine($"file: {file}");
		Console.WriteLine($"decoded {decodedFrames} frames in {stopwatch.Elapsed.TotalSeconds:0.###} s (threads: {threadCount})");
		Console.WriteLine($"{stopwatch.Elapsed.TotalMilliseconds / Math.Max(1, decodedFrames):0.###} ms/frame, {framesPerSecond:0.##} fps");
		Console.WriteLine($"decode {decodeTime.TotalMilliseconds / Math.Max(1, decodedFrames):0.###} ms/frame, convert {convertTime.TotalMilliseconds / Math.Max(1, decodedFrames):0.###} ms/frame");
		return 0;
	}

	private static int RunAll(ToolOptions options)
	{
		var files = GetFiles(options);
		if (files.Count == 0) throw new InvalidOperationException("no input file was found.");

		var outputDirectory = options.GetString("out", "artifacts");
		Directory.CreateDirectory(outputDirectory);
		var compareWithFfmpeg = options.HasFlag("compare");
		var ffmpegPath = options.GetString("ffmpeg", "ffmpeg");
		var failures = 0;

		var report = new List<string>
		{
			$"native libvpx: {VpxRuntime.LibraryVersionString}",
			$"files: {files.Count}",
			"",
			$"{"file",-40} {"frames",6} {"seconds",9} {"fps",8} {"alpha",6} {"a-frames",8} {"a-min",6} {"a-max",6} {"a-mean",7} {"rgb-mean",9}",
			new string('-', 116),
		};

		foreach (var file in files)
		{
			var name = Path.GetFileName(file);
			using var video = WebmVideo.Open(file, s_toolOptions);
			var info = video.Info;

			long totalAlpha = 0;
			long alphaSamples = 0;
			var minimumAlpha = 255;
			var maximumAlpha = 0;
			var frameCount = 0;
			var framesWithAlpha = 0;
			var lastTimestamp = TimeSpan.Zero;

			while (video.TryReadFrame(out var frame))
			{
				if (frameCount == 0) PngWriter.WriteRgba(Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(file)}_frame0000.png"), video.Pixels, frame.Width, frame.Height);
				if (frame.HasAlpha) framesWithAlpha++;
				AccumulateAlpha(video.Pixels, frame, ref minimumAlpha, ref maximumAlpha, ref totalAlpha, ref alphaSamples);
				lastTimestamp = frame.Timestamp;
				frameCount++;
			}

			var measuredFramesPerSecond = lastTimestamp.TotalSeconds > 0 ? frameCount / lastTimestamp.TotalSeconds : 0d;
			var alphaMean = alphaSamples > 0 ? (double)totalAlpha / alphaSamples : 0d;
			var rgbMean = double.NaN;
			if (compareWithFfmpeg && frameCount > 0)
			{
				using var compareVideo = WebmVideo.Open(file, s_toolOptions);
				rgbMean = CompareFrameWithFfmpeg(ffmpegPath, file, compareVideo, frameCount / 2);
			}

			report.Add($"{name,-40} {frameCount,6} {lastTimestamp.TotalSeconds,9:0.###} {measuredFramesPerSecond,8:0.##} {info.HasAlpha,6} {framesWithAlpha,8} {minimumAlpha,6} {maximumAlpha,6} {alphaMean,7:0.##} {rgbMean,9:0.###}");

			if (frameCount == 0) failures++;
			if (info.HasAlpha && framesWithAlpha == 0)
			{
				report.Add($"{"",-40} warning: the container declares alpha but no frame carried an alpha frame.");
				failures++;
			}
			if (framesWithAlpha > 0 && minimumAlpha == maximumAlpha) report.Add($"{"",-40} note: every sampled alpha value is {minimumAlpha}.");
		}

		foreach (var line in report) Console.WriteLine(line);

		var reportPath = Path.Combine(outputDirectory, "report.txt");
		File.WriteAllLines(reportPath, report);
		Console.WriteLine();
		Console.WriteLine($"report written: {reportPath}");
		return failures == 0 ? 0 : 3;
	}

	private static double CompareFrameWithFfmpeg(string ffmpegPath, string file, WebmVideo video, int frameIndex)
	{
		if (!DecodeToFrame(video, frameIndex)) return double.NaN;

		var reference = FfmpegRawFrameReader.ReadFrame(ffmpegPath, file, frameIndex, video.Info.Width, video.Info.Height, "rgba");
		var (meanDifference, _) = CompareRgb(video.Pixels, reference, video.Info.Width, video.Info.Height);
		return meanDifference;
	}

	private static bool DecodeToFrame(WebmVideo video, int frameIndex)
	{
		for (var index = 0; index <= frameIndex; index++)
		{
			if (!video.TryReadFrame(out _))
			{
				return false;
			}
		}

		return true;
	}

	private static (double MeanDifference, int MaximumDifference) CompareRgb(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> reference, int width, int height)
	{
		long totalDifference = 0;
		var maximumDifference = 0;
		var sampleCount = 0;
		var pixelCount = width * height;

		for (var pixel = 0; pixel < pixelCount; pixel++)
		{
			var offset = pixel * 4;
			for (var channel = 0; channel < 3; channel++)
			{
				var difference = Math.Abs(actual[offset + channel] - reference[offset + channel]);
				totalDifference += difference;
				if (difference > maximumDifference) maximumDifference = difference;
				sampleCount++;
			}
		}

		return ((double)totalDifference / Math.Max(1, sampleCount), maximumDifference);
	}

	private static void AccumulateAlpha(ReadOnlySpan<byte> pixels, VpxFrameInfo frame, ref int minimumAlpha, ref int maximumAlpha, ref long totalAlpha, ref long alphaSamples)
	{
		// Every fourth pixel is enough for a per file alpha summary and keeps the scan cheap.
		for (var offset = 3; offset < frame.PixelLength; offset += 16)
		{
			var alpha = pixels[offset];
			if (alpha < minimumAlpha) minimumAlpha = alpha;
			if (alpha > maximumAlpha) maximumAlpha = alpha;
			totalAlpha += alpha;
			alphaSamples++;
		}
	}

	private static void CollectGarbage()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
	}

	private static string GetSingleFile(ToolOptions options)
	{
		var files = GetFiles(options);
		if (files.Count == 0) throw new InvalidOperationException("no input file was found.");
		return files[0];
	}

	private static List<string> GetFiles(ToolOptions options)
	{
		var files = new List<string>();
		foreach (var argument in options.Positional)
		{
			if (Directory.Exists(argument))
			{
				files.AddRange(Directory.EnumerateFiles(argument, "*.webm", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
				continue;
			}

			if (File.Exists(argument))
			{
				files.Add(argument);
				continue;
			}

			var pattern = Path.GetFileName(argument);
			if (!pattern.Contains('*') && !pattern.Contains('?')) continue;

			var directory = Path.GetDirectoryName(argument);
			var searchDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
			if (Directory.Exists(searchDirectory)) files.AddRange(Directory.EnumerateFiles(searchDirectory, pattern).OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
		}

		return files;
	}
}
