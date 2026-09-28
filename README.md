# LibVpxFrameDecoder

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

🌐 English | [한국어](README.ko.md)

LibVpxFrameDecoder is a WebM (VP8/VP9) frame decoder for .NET. It calls the native libvpx API directly, writes decoded frames into a reusable pixel buffer, and keeps the WebM alpha channel that many platform decoders drop.

## Highlights

- Decodes VP8 and VP9 video from WebM (Matroska) files.
- Reads the alpha frame stored in `BlockAdditional` (`BlockAddID = 1`) and produces RGBA/BGRA frames with a real alpha channel.
- Converts I420 to RGBA/BGRA with libyuv, including the alpha merge, optional premultiplied alpha, and BT.601/BT.709 studio or full range matrices.
- Writes into a caller owned buffer, so steady state decoding does not allocate per frame.
- Has no game framework or UI dependency, and the assembly is AOT compatible.
- libvpx and libyuv are licensed under BSD style terms. No LGPL or GPL code is involved.

## Requirements

- .NET 10 SDK.
- Native `vpx` and `libyuv` binaries. The repository ships the Windows x64 and Windows ARM64 builds that the script in this repository produces.
- To rebuild the native binaries: Visual Studio with the C++ toolchain, Git, and network access (the build script fetches vcpkg).

### Platform support

| Platform | Architectures | Native binaries |
|---|---|---|
| Windows | x64, ARM64 | Ship with the repository (MSVC builds through vcpkg) |
| Linux | x64, ARM64 | Built by `.github/workflows/native-libraries.yml` and packed into the NuGet package |
| macOS | x64, ARM64 | Built by `.github/workflows/native-libraries.yml` and packed into the NuGet package |
| Android | ARM64, x64 | Built by `.github/workflows/native-libraries.yml` (shared libraries for jniLibs) |
| iOS, Mac Catalyst | ARM64 | Not automated yet. iOS needs static linking, see below |

The assembly resolves `vpx` and `libyuv` from `runtimes/<rid>/native/`, where `<rid>` is the runtime identifier of the running process (`win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `android-arm64`, `android-x64`). Add a native library pair to a `native/<rid>/` folder in the repository and the build copies it, or extend the workflow with the platform.

iOS does not allow shipping dylibs, so libvpx and libyuv have to be linked statically into the app. The resolver then falls back to the main program handle. libyuv exports its color matrices as data symbols, which dlsym may not find in a statically linked binary, so a small C shim that returns those constants is the planned follow-up for iOS and Mac Catalyst.

The Windows native build script is Windows only (Visual Studio, vcpkg, PowerShell). Other platforms build through the workflow; the same steps can be run by hand with a `git clone` of libvpx and libyuv plus `./configure --enable-shared` and CMake.

## Repository layout

```
LibVpxFrameDecoder.slnx
src/LibVpxFrameDecoder/      managed library (net10.0, unsafe, AOT compatible)
  Interop/                   libvpx P/Invoke, native structs, SafeHandle
  Webm/                      EBML/WebM demuxer with alpha pairing and cluster seeking
  Decoding/                  VP8/VP9 decoding (main + alpha) and I420 to RGBA/BGRA conversion
tools/FrameDump/             verification console tool
native/win-x64/              native libvpx and libyuv (x64)
native/win-arm64/            native libvpx and libyuv (ARM64)
native/libvpx-LICENSE.txt    libvpx license text
native/libvpx-PATENTS.txt    WebM patent grant
native/libyuv-LICENSE.txt    libyuv license text
native/libyuv-PATENTS.txt    libyuv patent grant
native/version.txt           native build provenance
ports/libvpx/                vcpkg overlay port that builds shared libvpx (vpx.dll) on MSVC
ports/libyuv/                vcpkg overlay port that builds shared libyuv (libyuv.dll) without the JPEG helpers
scripts/build-native-libs.ps1
```

## Build

### 1. Native libraries (Windows)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-native-libs.ps1 -VcpkgRoot C:\vcpkg
```

The script clones and bootstraps [vcpkg](https://github.com/microsoft/vcpkg) into the folder given by `-VcpkgRoot`, installs `libvpx` and `libyuv` for `x64-windows` and `arm64-windows`, then copies `vpx.dll` and `libyuv.dll` into `native\win-x64` and `native\win-arm64` together with the license files and `version.txt`.

Two overlay ports are used. The stock vcpkg port builds libvpx statically on MSVC, so `ports\libvpx` enables the shared (DLL) build. `ports\libyuv` keeps the shared build and disables libyuv's optional JPEG helpers, so the runtime only needs `libyuv.dll`. Both are passed through `--overlay-ports`. The same steps manually:

```powershell
git clone --depth 1 https://github.com/microsoft/vcpkg.git C:\vcpkg
C:\vcpkg\bootstrap-vcpkg.bat -disableMetrics
$env:VCPKG_BUILD_TYPE = "release"
C:\vcpkg\vcpkg.exe install libvpx:x64-windows libvpx:arm64-windows libyuv:x64-windows libyuv:arm64-windows --overlay-ports=C:\path\to\LibVpxFrameDecoder\ports
# copy installed\<triplet>\bin\vpx.dll and installed\<triplet>\bin\libyuv.dll into native\win-<arch>\
```

The pinned libvpx version and the `VPX_DECODER_ABI_VERSION` constant in `src/LibVpxFrameDecoder/Interop/VpxNative.cs` have to match. They are currently `1.16.0` and `12`. libyuv is pinned to the commit recorded in `ports\libyuv\portfile.cmake`.

libyuv compiles its AArch64 assembly kernels only with a GCC style assembler, so the Windows ARM64 DLL uses the portable code paths while the x64 DLL uses the AVX2 intrinsics kernels. Both are much faster than a managed conversion loop.

### 2. Managed build

```powershell
dotnet build LibVpxFrameDecoder.slnx
dotnet build LibVpxFrameDecoder.slnx -p:Platform=x64
dotnet build LibVpxFrameDecoder.slnx -p:Platform=ARM64
```

The library project copies every `native\<rid>\` folder of the repository into `runtimes\<rid>\native\` under the output folder. A resolver in the assembly loads the folder that matches the runtime identifier of the running process, so one build works natively and under x64 emulation. The files also flow into the output of any application that references the project.

`dotnet pack src\LibVpxFrameDecoder\LibVpxFrameDecoder.csproj` produces a NuGet package where every `native\<rid>\` folder becomes a `runtimes\<rid>\native\` asset, together with the license file.

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
| `ThreadCount` | half of the processors, capped at 8 | Decoder threads passed to libvpx. Eight threads per video keep four 1080p60 streams inside a 60 fps frame budget. |
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

- `info` prints codec, size, frame rate, duration and alpha mode for each file.
- `dump` writes PNG frames with alpha preserved for visual inspection.
- `compare` decodes the same frame with ffmpeg and compares the RGB channels. ffmpeg's native VP9 decoder does not output alpha, so the alpha channel is checked through the PNG dumps and the alpha statistics that `all` prints.
- `premultiply` decodes a frame twice, once without premultiplied alpha and once with it, and checks the premultiplied output against `round(channel * alpha / 255)`.
- `concurrent` decodes one frame of every input file per tick and reports whether several videos fit inside a 60 fps display frame, both back to back and on all cores.
- `stress` opens, decodes and disposes repeatedly, reports managed and private byte deltas, then checks that the file handle was released.
- `bench` reports milliseconds per frame and frames per second after a warmup.
- `all` runs a full pass over a directory and writes `report.txt` next to the PNG dumps.

The clips under `tests/assets/` are three synthetic 2 second WebM files (VP9 with alpha, VP8 with alpha, VP9 without alpha) generated by `python scripts/generate-test-assets.py` from a moving alpha pattern and the ffmpeg test image. They are the inputs of the verification runs (`FrameDump compare tests/assets --ffmpeg ffmpeg`).

Example:

```powershell
dotnet run --project tools/FrameDump -- all "C:\videos" --out artifacts --compare --ffmpeg ffmpeg
```

## Limitations

- WebM containers with VP8 or VP9 video only. H.264, AAC and MP4 are out of scope.
- I420 8 bit only (VP9 profile 0). Other profiles or formats throw `VpxException`.
- No audio decoding. Audio tracks are skipped.
- Laced blocks use the first frame only. Video blocks are rarely laced.
- Seeking works at cluster granularity. The demuxer seeks to the cluster and decodes forward while skipping frames until the requested time.
- Decoding runs on the CPU. There is no hardware decoder integration.

## License

LibVpxFrameDecoder is licensed under the [MIT License](LICENSE.txt).

## Third-party notices

The native libraries under `native/` are builds of [libvpx](https://github.com/webmproject/libvpx), licensed under `BSD-3-Clause AND ISC` and covered by the Google WebM patent grant for VP8 and VP9. The license and patent texts ship with the binaries (`native/libvpx-LICENSE.txt`, `native/libvpx-PATENTS.txt`).

`native/libyuv.dll` is a build of [libyuv](https://chromium.googlesource.com/libyuv/libyuv), licensed under a BSD style license with an additional patent grant. The texts ship with the binary (`native/libyuv-LICENSE.txt`, `native/libyuv-PATENTS.txt`).
