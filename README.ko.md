# LibVpxFrameDecoder

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)
[![NuGet](https://img.shields.io/nuget/v/LibVpxFrameDecoder.svg)](https://www.nuget.org/packages/LibVpxFrameDecoder)

🌐 [English](README.md) | 한국어

LibVpxFrameDecoder는 .NET에서 WebM(VP8/VP9) 영상을 디코딩하는 라이브러리입니다. 네이티브 libvpx API를 직접 호출하고, 플랫폼 디코더가 흔히 놓치는 WebM 알파 채널까지 챙깁니다.

## 기능

- WebM(Matroska) 파일의 VP8, VP9 영상을 디코딩하며, `BlockAdditional`(`BlockAddID = 1`)에 저장된 알파 프레임도 읽어서 알파가 살아 있는 RGBA/BGRA 프레임을 만듭니다.
- I420 프레임은 libyuv가 RGBA/BGRA로 변환합니다. 알파 병합, 프리멀티플라이드 알파(선택), BT.601/BT.709 스튜디오/풀 레인지 행렬을 지원합니다.
- 프레임을 호출자가 준비한 버퍼에 쓰므로, 디코딩이 안정된 뒤에는 프레임마다 메모리를 새로 할당하지 않습니다.
- UI나 게임 프레임워크에 의존하지 않고 AOT 환경에서도 동작합니다.
- libvpx와 libyuv는 BSD 계열 라이선스입니다. LGPL이나 GPL 코드는 들어 있지 않습니다.

## 요구 사항

- .NET 10 SDK
- 네이티브 `vpx`와 `libyuv` 바이너리. 지원 플랫폼용 바이너리는 NuGet 패키지에 모두 들어 있습니다. 직접 빌드하려면 Windows에서는 C++ 도구 모음이 있는 Visual Studio, Git, 네트워크 연결이 필요하고, macOS에서는 Xcode가 필요합니다.

## 지원 플랫폼

| 플랫폼 | 아키텍처 | 네이티브 바이너리 |
|---|---|---|
| Windows | x64, ARM64 | 공유 라이브러리 |
| Linux | x64, ARM64 | 공유 라이브러리 |
| macOS (Apple silicon) | ARM64 | 공유 라이브러리 |
| Android | ARM32, ARM64, x64 | 공유 라이브러리, libc++ 정적 링크 |
| iOS | ARM64 | 정적 라이브러리, 기기 및 Apple silicon 시뮬레이터 |
| Mac Catalyst | ARM64 | 정적 라이브러리 |

바이너리는 모두 `.github/workflows/native-libraries.yml`에서 빌드해 NuGet 패키지에 담습니다. 실행할 때 어셈블리는 프로세스의 런타임 식별자에 맞는 `runtimes/<rid>/native/` 폴더에서 `vpx`와 `libyuv`를 찾습니다(`win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-arm64`, `android-arm`, `android-arm64`, `android-x64`). 저장소의 `native/<rid>/`에 라이브러리 쌍을 넣으면 빌드가 함께 복사합니다. Intel macOS는 지원하지 않습니다.

Android 공유 라이브러리는 `libc++_shared.so` 없이 동작합니다. C++ 런타임을 라이브러리 안에 정적으로 링크했고, LOAD 세그먼트는 최신 기기의 16KB 페이지에 맞게 정렬했습니다.

Apple 플랫폼에서는 dylib을 배포할 수 없어서 iOS와 Mac Catalyst는 정적 라이브러리를 앱에 직접 링크합니다. 이 링크는 `buildTransitive/LibVpxFrameDecoder.targets`가 처리하며, arm64 슬라이스만 제공합니다(iOS 15.0, Mac Catalyst 15.0 이상). libyuv는 색 변환 행렬을 데이터 심볼로 내보내는데, 정적 링크에서는 `dlsym`으로 찾을 수 없어서 패키지에 들어 있는 `native/shim/lvpx_shim.c`가 이 상수를 함수로 돌려줍니다.

## 빌드

관리 라이브러리는 .NET SDK로 빌드합니다.

```powershell
dotnet build LibVpxFrameDecoder.slnx
```

빌드하면 저장소의 `native\<rid>\` 폴더가 출력 폴더 아래 `runtimes\<rid>\native\`로 복사되므로, 하나의 빌드로 네이티브 실행과 x64 에뮬레이션 실행을 모두 처리할 수 있습니다. `dotnet pack src\LibVpxFrameDecoder\LibVpxFrameDecoder.csproj`는 이 폴더들을 NuGet 패키지의 `runtimes\<rid>\native\` 자산으로 만들고, Apple 정적 라이브러리는 라이선스와 특허 전문과 함께 `static/` 아래에 넣습니다.

Windows용 네이티브 바이너리(C++ 도구 모음이 있는 Visual Studio와 PowerShell 필요):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-native-libs.ps1 -VcpkgRoot C:\vcpkg
```

스크립트는 [vcpkg](https://github.com/microsoft/vcpkg)를 준비하고 `ports\`의 오버레이 포트로 libvpx와 libyuv를 설치합니다. 오버레이 포트는 MSVC에서 libvpx를 DLL로 빌드하고, libyuv는 선택 기능인 JPEG 헬퍼를 빼고 빌드합니다. 고정한 libvpx 버전과 `src/LibVpxFrameDecoder/Interop/VpxNative.cs`의 `VPX_DECODER_ABI_VERSION` 값은 서로 맞아야 합니다. 현재 값은 각각 `1.16.0`, `12`입니다.

iOS와 Mac Catalyst용 정적 라이브러리(macOS와 Xcode 필요):

```bash
bash scripts/build-native-apple-static.sh ios
bash scripts/build-native-apple-static.sh maccatalyst
```

결과물은 `native/apple/`에 만들어지며 저장소에는 커밋하지 않습니다. `.github/workflows/native-libraries.yml`이 모든 플랫폼의 바이너리를 빌드해 NuGet 패키지에 넣고, `main`에 push할 때는 라이브러리 프로젝트의 `<Version>`이 올라간 경우에만 실행됩니다. `tests/probes/`의 최소 앱으로 패키지를 끝까지 검증하는 일도 이 워크플로가 맡습니다.

## 사용법

NuGet에서 패키지를 설치한 뒤 사용합니다(`dotnet add package LibVpxFrameDecoder`).

```csharp
using LibVpxFrameDecoder;

using var video = WebmVideo.Open("clip.webm");

Console.WriteLine(video.Info.CodecId);   // V_VP9
Console.WriteLine(video.Info.HasAlpha);  // 알파가 있는 파일이면 true

while (video.TryReadFrame(out var frame))
{
    // video.Pixels에 RGBA 바이트가 들어 있습니다(기본값은 프리멀티플라이드).
    // frame.Timestamp, frame.Width, frame.Height, frame.HasAlpha
}

video.Seek(TimeSpan.FromSeconds(2.5));
video.Rewind();
```

`Pixels`와 `PixelBuffer`는 다음 프레임을 읽거나 객체를 해제할 때까지 유효합니다.

### 옵션

| 옵션 | 기본값 | 설명 |
| --- | --- | --- |
| `PixelFormat` | `Rgba` | 출력 바이트 순서입니다. `Rgba` 또는 `Bgra`. |
| `PremultiplyAlpha` | `true` | 프리멀티플라이드 알파 블렌딩을 위해 RGB에 알파를 곱합니다. |
| `ThreadCount` | 프로세서 수의 절반, 최대 8 | libvpx에 전달하는 디코더 스레드 수입니다. |
| `ColorConversion` | `Auto` | YUV를 RGB로 바꿀 때 쓰는 행렬과 레인지입니다. `Auto`는 비트스트림의 컬러 스페이스를 따릅니다. |

### API 요약

- `WebmVideo`: 파일을 열고 프레임을 읽고 시크합니다. 디코더와 픽셀 버퍼를 함께 소유합니다.
- `WebmVideoInfo`: 코덱 ID, 크기, 재생 시간, 프레임 레이트, 알파 모드.
- `VpxFrameInfo`: 디코딩한 프레임의 타임스탬프와 크기.
- `VpxFrameDecoderOptions`: 디코딩과 변환 옵션.
- `VpxRuntime.LibraryVersionString`: 로드된 네이티브 라이브러리의 버전입니다. 로드 여부를 확인할 때 쓸 수 있습니다.

## 검증 도구

`tools/FrameDump`는 파일을 디코딩해서 리포트와 PNG 덤프를 만듭니다.

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

`info`는 파일마다 코덱, 크기, 프레임 레이트, 재생 시간, 알파 모드를 출력하고, `dump`는 알파를 살린 PNG를 만들고, `bench`는 워밍업 뒤의 프레임당 밀리초와 초당 프레임 수를 보고합니다. `compare`는 같은 프레임을 ffmpeg로 디코딩해 RGB 채널을 비교합니다. ffmpeg는 알파를 출력하지 않으므로 알파는 PNG 덤프와 `all`이 출력하는 통계로 확인합니다. `premultiply`는 프리멀티플라이드 결과가 `round(channel * alpha / 255)`와 맞는지 검사합니다. `concurrent`는 입력 파일 여러 개를 함께 디코딩하면서 60fps 프레임 예산 안에 들어오는지 확인합니다. `stress`는 열기, 디코딩, 해제를 반복하며 메모리 변화량과 파일 핸들 반환 여부를 보고합니다. `all`은 디렉터리 전체를 검사하고 PNG 덤프 옆에 `report.txt`를 남깁니다.

`tests/assets/`의 클립은 `python scripts/generate-test-assets.py`로 만든 합성 WebM 파일(알파 있는 VP9, 알파 있는 VP8, 알파 없는 VP9, 각 2초)입니다.

예:

```powershell
dotnet run --project tools/FrameDump -- all "C:\videos" --out artifacts --compare --ffmpeg ffmpeg
```

## 제한 사항

- WebM 컨테이너의 VP8, VP9 영상만 다룹니다. H.264, AAC, MP4는 지원하지 않습니다.
- I420 8비트(VP9 프로파일 0)만 지원합니다. 다른 프로파일이나 포맷은 `VpxException`을 던집니다.
- 오디오는 디코딩하지 않고 오디오 트랙은 건너뜁니다.
- 레이싱된 블록은 첫 프레임만 사용합니다. 영상 블록은 레이싱되는 경우가 거의 없습니다.
- 시크는 클러스터 단위로 동작합니다. 디먹서가 해당 클러스터로 이동한 뒤 요청한 시간에 도달할 때까지 프레임을 건너뛰며 디코딩합니다.
- 디코딩은 CPU로만 수행하며 하드웨어 디코더 연동은 없습니다.

## 라이선스

LibVpxFrameDecoder는 [MIT License](LICENSE.txt)로 배포됩니다.

## 서드파티 고지

패키지에 들어 있는 libvpx 바이너리(`runtimes/<rid>/native/`와 iOS, Mac Catalyst용 `static/` 정적 라이브러리)는 [libvpx](https://github.com/webmproject/libvpx) 빌드 결과물입니다. `BSD-3-Clause AND ISC` 라이선스가 적용되고 VP8, VP9에 대한 Google WebM 특허 그랜트가 함께 적용됩니다. 라이선스와 특허 전문은 패키지의 `native/libvpx-LICENSE.txt`, `native/libvpx-PATENTS.txt`에 들어 있습니다.

libyuv 바이너리도 패키지에 함께 들어 있으며 [libyuv](https://chromium.googlesource.com/libyuv/libyuv) 빌드 결과물입니다. BSD 계열 라이선스에 추가 특허 그랜트가 적용됩니다. 전문은 `native/libyuv-LICENSE.txt`, `native/libyuv-PATENTS.txt`에 들어 있습니다.

Android 공유 라이브러리는 LLVM libc++ 런타임을 정적으로 포함하므로, 앱에 `libc++_shared.so`가 필요하지 않습니다. libc++는 Apache License v2.0 with LLVM Exceptions로 배포되며, 전문은 패키지의 `native/libcxx-LICENSE.txt`에 들어 있습니다.
