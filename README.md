# LibVpxFrameDecoder

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

🌐 English | [한국어](README.ko.md)

LibVpxFrameDecoder decodes VP8 and VP9 video from WebM files for .NET. It calls the native libvpx API directly and keeps the alpha channel that most platform decoders drop.

## Features

- VP8 and VP9 video from WebM (Matroska) files, including the alpha frame stored in `BlockAdditional` (`BlockAddID = 1`), decoded into RGBA/BGRA output.
- I420 to RGBA/BGRA conversion with libyuv: alpha merge, optional premultiplied alpha, and BT.601/BT.709 studio or full range matrices.
- Frames are written into a caller owned buffer, so steady state decoding does not allocate per frame.
- No UI or game framework dependency, and the assembly is AOT compatible.
- libvpx and libyuv are licensed under BSD style terms. No LGPL or GPL code is involved.

## Requirements

- .NET 10 SDK.
- Native `vpx` and `libyuv` binaries. The NuGet package carries the binaries for every supported platform. Building them yourself needs Visual Studio with the C++ toolchain, Git and network access on Windows, or Xcode on macOS.

## Platform support

| Platform | Architectures | Native binaries |
|---|---|---|
| Windows | x64, ARM64 | shared libraries |
| Linux | x64, ARM64 | shared libraries |
| macOS (Apple silicon) | ARM64 | shared libraries |
| Android | ARM64, x64 | shared libraries, static libc++ |
| iOS | ARM64 | static libraries, device and Apple silicon simulator |
| Mac Catalyst | ARM64 | static libraries |

`.github/workflows/native-libraries.yml` builds all of them and packs them into the NuGet package. At run time the assembly loads `vpx` and `libyuv` from `runtimes/<rid>/native/`, where `<rid>` matches the runtime identifier of the process (`win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-arm64`, `android-arm64`, `android-x64`). A native library pair added to `native/<rid>/` in the repository is picked up by the build. Intel macOS is out of scope.

The Android shared libraries do not depend on `libc++_shared.so`, and their LOAD segments are aligned for the 16 KB pages of newer devices.

Apple platforms cannot ship dylibs, so iOS and Mac Catalyst link the static libraries into the app through `buildTransitive/LibVpxFrameDecoder.targets` (arm64 slices only, iOS 15.0 and Mac Catalyst 15.0 minimum). The package also carries a small C shim, `native/shim/lvpx_shim.c`, because libyuv exports its color matrices as data symbols that a static link cannot resolve with `dlsym`.

## Build

The managed library:

```powershell
dotnet build LibVpxFrameDecoder.slnx
```

The project copies every `native\<rid>\` folder of the repository into `runtimes\<rid>\native\` under the output folder, so one build covers native and x64 emulation runs. `dotnet pack src\LibVpxFrameDecoder\LibVpxFrameDecoder.csproj` turns those folders into the `runtimes\<rid>\native\` assets of the NuGet package and ships the Apple static libraries under `static/`, together with the license and patent texts.

Native binaries for Windows (Visual Studio with the C++ toolchain, PowerShell):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-native-libs.ps1 -VcpkgRoot C:\vcpkg
```

The script sets up [vcpkg](https://github.com/microsoft/vcpkg) and installs libvpx and libyuv through the overlay ports in `ports\`, which make libvpx build as a DLL on MSVC and leave libyuv's optional JPEG helpers out of the build. The pinned libvpx version and the `VPX_DECODER_ABI_VERSION` constant in `src/LibVpxFrameDecoder/Interop/VpxNative.cs` have to match; they are currently `1.16.0` and `12`.

Native binaries for iOS and Mac Catalyst (macOS with Xcode):

```bash
bash scripts/build-native-apple-static.sh ios
bash scripts/build-native-apple-static.sh maccatalyst
```

The script writes them into `native/apple/`. Built libraries are not committed: `.github/workflows/native-libraries.yml` builds them for every platform and packs the package, and a push to `main` runs it only when the `<Version>` of the library project increases. The workflow also builds the apps under `tests/probes/` to verify the NuGet package end to end.

## Usage

```csharp
using LibVpxFrameDecoder;

using var video = WebmVideo.Open("clip.webm");

Console.WriteLine(video.Info.CodecId);   // V_VP9
Console.WriteLine(video.Info.HasAlpha);  // true when the file carries alpha

while (video.TryReadFrame(out var frame))
{
    // video.Pixels holds the frame as RGBA bytes (premultiplied by default).
    // frame.Timestamp, frame.Width, frame.Height, frame.HasAlpha
}

video.Seek(TimeSpan.FromSeconds(2.5));
video.Rewind();
```

`Pixels` and `PixelBuffer` stay valid until the next read or dispose.

### Options

| Option | Default | Description |
| --- | --- | --- |
| `PixelFormat` | `Rgba` | Output byte order, `Rgba` or `Bgra`. |
| `PremultiplyAlpha` | `true` | Multiplies RGB by alpha for premultiplied alpha blending. |
| `ThreadCount` | half of the processors, capped at 8 | Decoder threads passed to libvpx. |
| `ColorConversion` | `Auto` | YUV to RGB matrix and range. `Auto` follows the color space in the bitstream. |

### API summary

- `WebmVideo`: opens a file, reads frames, seeks, and owns the decoder and the pixel buffer.
- `WebmVideoInfo`: codec id, size, duration, frame rate and alpha mode.
- `VpxFrameInfo`: timestamp and geometry of one decoded frame.
- `VpxFrameDecoderOptions`: decoding and conversion options.
- `VpxRuntime.LibraryVersionString`: version of the loaded native library, useful as a load check.

## Verification tool

`tools/FrameDump` decodes files and writes reports and PNG dumps.

```
FrameDump info    <file|dir|pattern>...
FrameDump dump    <file|dir|pattern>... [--frame N | --all [--max N] [--step N]] [--out dir]
FrameDump compare <file|dir|pattern>... [--frame N] [--ffmpeg path] [--mean 2.0] [--max 8]
FrameDump premultiply <file> [--frame N]
FrameDump concurrent <file>... [--frames 300] [--warmup 60] [--threads N] [--mode sequential|parallel|both]
FrameDump stress  <file> [--cycles 200] [--frames 10]
FrameDump bench   <file> [--warmup 30] [--frames 300] [--threads N]
FrameDump all     <dir|pattern>... [--out dir] [--compare] [--ffmpeg path]
```

`info` prints codec, size, frame rate, duration and alpha mode per file, `dump` writes PNGs with the alpha channel intact, and `bench` reports milliseconds per frame and frames per second after a warmup. `compare` checks the RGB channels against an ffmpeg decode; ffmpeg does not output alpha, so alpha is covered by the PNG dumps and the statistics that `all` prints. `premultiply` checks the premultiplied output against `round(channel * alpha / 255)`. `concurrent` decodes several files side by side and reports whether they fit inside a 60 fps frame budget. `stress` loops open, decode and dispose, then reports memory deltas and whether the file handle was released. `all` runs a full pass over a directory and writes `report.txt` next to the PNG dumps.

The clips under `tests/assets/` are synthetic 2 second WebM files (VP9 and VP8 with alpha, VP9 without) generated by `python scripts/generate-test-assets.py`.

Example:

```powershell
dotnet run --project tools/FrameDump -- all "C:\videos" --out artifacts --compare --ffmpeg ffmpeg
```

## Limitations

- WebM containers with VP8 or VP9 video only. H.264, AAC and MP4 are out of scope.
- I420 8 bit only (VP9 profile 0). Other profiles or formats throw `VpxException`.
- No audio decoding. Audio tracks are skipped.
- Laced blocks use the first frame only. Video blocks are rarely laced.
- Seeking works at cluster granularity: the demuxer seeks to the cluster and decodes forward, skipping frames until the requested time.
- Decoding runs on the CPU. There is no hardware decoder integration.

## License

LibVpxFrameDecoder is licensed under the [MIT License](LICENSE.txt).

## Third-party notices

The native libraries under `native/` are builds of [libvpx](https://github.com/webmproject/libvpx), licensed under `BSD-3-Clause AND ISC` and covered by the Google WebM patent grant for VP8 and VP9. The license and patent texts ship with the binaries (`native/libvpx-LICENSE.txt`, `native/libvpx-PATENTS.txt`).

`native/libyuv.dll` is a build of [libyuv](https://chromium.googlesource.com/libyuv/libyuv), licensed under a BSD style license with an additional patent grant. The texts ship with the binary (`native/libyuv-LICENSE.txt`, `native/libyuv-PATENTS.txt`).
