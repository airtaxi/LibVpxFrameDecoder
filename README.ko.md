# LibVpxFrameDecoder

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

🌐 [English](README.md) | 한국어

LibVpxFrameDecoder는 .NET에서 WebM(VP8/VP9) 영상을 디코딩하는 라이브러리입니다. 네이티브 libvpx API를 직접 호출하고, 디코딩한 프레임을 재사용 버퍼에 쓰며, 플랫폼 디코더가 놓치는 WebM 알파 채널까지 처리합니다.

## 주요 기능

- WebM(Matroska) 파일의 VP8, VP9 영상을 디코딩합니다.
- `BlockAdditional`(`BlockAddID = 1`)에 들어 있는 알파 프레임을 읽어 알파 채널이 있는 RGBA/BGRA 프레임을 만듭니다.
- I420에서 RGBA/BGRA 변환은 libyuv가 처리합니다. 알파 병합, 선택적인 프리멀티플라이드 알파, BT.601/BT.709 스튜디오/풀 레인지 행렬을 지원합니다.
- 프레임을 호출자가 가진 버퍼에 쓰기 때문에 디코딩이 안정된 상태에서는 프레임마다 메모리를 새로 할당하지 않습니다.
- 게임 프레임워크나 UI 라이브러리에 의존하지 않으며 AOT 환경에서도 동작합니다.
- libvpx와 libyuv는 BSD 계열 라이선스입니다. LGPL이나 GPL 코드는 포함되지 않습니다.

## 요구 사항

- .NET 10 SDK
- 네이티브 `vpx`와 `libyuv` 바이너리. 이 저장소는 스크립트로 만든 Windows x64, Windows ARM64 빌드를 함께 제공합니다.
- 네이티브 바이너리를 다시 빌드하려면 Visual Studio의 C++ 도구 모음, Git, 네트워크 접속이 필요합니다. 빌드 스크립트가 vcpkg를 내려받습니다.

### 지원 플랫폼

Windows x64와 Windows ARM64만 지원합니다. 저장소에는 해당 네이티브 바이너리만 들어 있고, 어셈블리에 포함된 리졸버는 `runtimes/win-x64/native/`, `runtimes/win-arm64/native/` 폴더만 탐색합니다. Linux, macOS, 32비트 Windows는 기본 상태로 동작하지 않으며, 네이티브 빌드 스크립트도 Windows 전용(Visual Studio, vcpkg, PowerShell)입니다.

관리 코드는 플랫폼 중립적이므로 다른 플랫폼을 추가할 수 있습니다. 대상 플랫폼용 libvpx와 libyuv를 빌드해 어셈블리 옆 `runtimes/<rid>/native/` 폴더에 넣고, `Interop/NativeLibraryResolver.cs`에 해당 폴더 이름을 추가하면 됩니다.

## 저장소 구조

```
LibVpxFrameDecoder.slnx
src/LibVpxFrameDecoder/      관리 라이브러리 (net10.0, unsafe, AOT 호환)
  Interop/                   libvpx P/Invoke, 네이티브 구조체, SafeHandle
  Webm/                      알파 페어링과 클러스터 시크를 처리하는 EBML/WebM 디먹서
  Decoding/                  VP8/VP9 디코딩(메인 + 알파), I420에서 RGBA/BGRA 변환
tools/FrameDump/             검증용 콘솔 도구
native/win-x64/              네이티브 libvpx와 libyuv (x64)
native/win-arm64/            네이티브 libvpx와 libyuv (ARM64)
native/libvpx-LICENSE.txt    libvpx 라이선스 전문
native/libvpx-PATENTS.txt    WebM 특허 그랜트
native/libyuv-LICENSE.txt    libyuv 라이선스 전문
native/libyuv-PATENTS.txt    libyuv 특허 그랜트
native/version.txt           네이티브 빌드 출처 기록
ports/libvpx/                MSVC에서 공유 라이브러리(vpx.dll)를 만들기 위한 vcpkg 오버레이 포트
ports/libyuv/                JPEG 헬퍼를 빼고 공유 libyuv(libyuv.dll)를 만드는 vcpkg 오버레이 포트
scripts/build-native-libs.ps1
```

## 빌드

### 1. 네이티브 라이브러리 (Windows)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-native-libs.ps1 -VcpkgRoot C:\vcpkg
```

스크립트는 `-VcpkgRoot`로 지정한 폴더에 [vcpkg](https://github.com/microsoft/vcpkg)를 복제하고 부트스트랩한 뒤 `libvpx`와 `libyuv`를 `x64-windows`, `arm64-windows`용으로 설치합니다. 그다음 `vpx.dll`과 `libyuv.dll`을 라이선스 파일, `version.txt`와 함께 `native\win-x64`와 `native\win-arm64`에 복사합니다.

오버레이 포트를 두 개 사용합니다. vcpkg의 기본 포트는 MSVC에서 libvpx를 정적으로만 빌드하므로 `ports\libvpx`가 공유 라이브러리 빌드를 켭니다. `ports\libyuv`는 공유 빌드를 유지하면서 선택 기능인 JPEG 헬퍼를 꺼서 실행 시 `libyuv.dll`만 필요하게 만듭니다. 두 포트 모두 `--overlay-ports`로 전달됩니다. 같은 과정을 직접 실행하려면 다음과 같이 합니다.

```powershell
git clone --depth 1 https://github.com/microsoft/vcpkg.git C:\vcpkg
C:\vcpkg\bootstrap-vcpkg.bat -disableMetrics
$env:VCPKG_BUILD_TYPE = "release"
C:\vcpkg\vcpkg.exe install libvpx:x64-windows libvpx:arm64-windows libyuv:x64-windows libyuv:arm64-windows --overlay-ports=C:\path\to\LibVpxFrameDecoder\ports
# installed\<triplet>\bin\vpx.dll 과 installed\<triplet>\bin\libyuv.dll 을 native\win-<arch>\ 로 복사
```

고정한 libvpx 버전과 `src/LibVpxFrameDecoder/Interop/VpxNative.cs`의 `VPX_DECODER_ABI_VERSION` 값은 서로 맞아야 합니다. 현재 값은 각각 `1.16.0`, `12`입니다. libyuv는 `ports\libyuv\portfile.cmake`에 기록된 커밋으로 고정합니다.

libyuv의 AArch64 어셈블리 커널은 GCC 계열 어셈블러로만 컴파일되므로 Windows ARM64 DLL은 이식성 있는 코드 경로를, x64 DLL은 AVX2 intrinsics 커널을 사용합니다. 두 경로 모두 관리 코드 변환 루프보다 훨씬 빠릅니다.

### 2. 관리 코드 빌드

```powershell
dotnet build LibVpxFrameDecoder.slnx
dotnet build LibVpxFrameDecoder.slnx -p:Platform=x64
dotnet build LibVpxFrameDecoder.slnx -p:Platform=ARM64
```

라이브러리 프로젝트는 두 아키텍처를 출력 폴더 아래 `runtimes\win-x64\native\`, `runtimes\win-arm64\native\`에 복사합니다. 어셈블리 안의 리졸버가 실행 중인 프로세스에 맞는 폴더를 로드하므로, 하나의 빌드로 네이티브 실행과 x64 에뮬레이션 실행이 모두 됩니다. 이 파일들은 프로젝트를 참조하는 애플리케이션의 출력 폴더에도 함께 복사됩니다.

## 사용법

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
| `ThreadCount` | 프로세서 수의 절반, 최대 8 | libvpx에 전달하는 디코더 스레드 수입니다. 파일당 8개면 1080p60 4개 동시 재생이 60fps 예산 안에 들어옵니다. |
| `ColorConversion` | `Auto` | YUV에서 RGB로 바꿀 때 쓸 행렬과 레인지입니다. `Auto`는 비트스트림의 컬러 스페이스를 따릅니다. |

### API 요약

- `WebmVideo`: 파일을 열고 프레임을 읽고 시크합니다. 디코더와 픽셀 버퍼를 함께 소유합니다.
- `WebmVideoInfo`: 코덱 ID, 크기, 재생 시간, 프레임 레이트, 알파 모드.
- `VpxFrameInfo`: 디코딩한 프레임의 타임스탬프와 크기.
- `VpxFrameDecoderOptions`: 디코딩과 변환 옵션.
- `VpxRuntime.LibraryVersionString`: 로드된 네이티브 라이브러리 버전입니다. 로드 확인용으로 쓸 수 있습니다.

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

- `info`는 파일마다 코덱, 크기, 프레임 레이트, 재생 시간, 알파 모드를 출력합니다.
- `dump`는 알파를 유지한 PNG 프레임을 만들어 눈으로 확인할 수 있게 합니다.
- `compare`는 같은 프레임을 ffmpeg로 디코딩해 RGB 채널을 비교합니다. ffmpeg의 네이티브 VP9 디코더는 알파를 출력하지 않으므로, 알파는 PNG 덤프와 `all`이 출력하는 알파 통계로 확인합니다.
- `premultiply`는 같은 프레임을 프리멀티플라이드 알파 없이 한 번, 적용해서 한 번 디코딩하고, 프리멀티플라이드 결과가 `round(channel * alpha / 255)`와 일치하는지 확인합니다.
- `concurrent`는 입력 파일마다 1틱에 1프레임씩 디코딩하면서 여러 영상이 60fps 디스플레이 프레임 예산 안에 들어오는지 순차 방식과 병렬 방식으로 각각 보고합니다.
- `stress`는 파일을 열고 디코딩하고 해제하는 과정을 반복한 뒤 관리 메모리와 프라이빗 바이트 변화량을 보고하고, 파일 핸들이 반환됐는지 확인합니다.
- `bench`는 워밍업 후 프레임당 밀리초와 초당 프레임 수를 보고합니다.
- `all`은 디렉터리 전체를 검사하고 PNG 덤프 옆에 `report.txt`를 남깁니다.

예시:

```powershell
dotnet run --project tools/FrameDump -- all "C:\videos" --out artifacts --compare --ffmpeg ffmpeg
```

## 제한 사항

- WebM 컨테이너의 VP8, VP9 영상만 다룹니다. H.264, AAC, MP4는 지원 대상이 아닙니다.
- I420 8비트(VP9 프로파일 0)만 지원합니다. 다른 프로파일이나 포맷은 `VpxException`을 던집니다.
- 오디오는 디코딩하지 않습니다. 오디오 트랙은 건너뜁니다.
- 레이싱된 블록은 첫 프레임만 사용합니다. 영상 블록은 레이싱되는 경우가 거의 없습니다.
- 시크는 클러스터 단위로 동작합니다. 디먹서가 해당 클러스터로 이동한 뒤 요청한 시간에 도달할 때까지 프레임을 건너뛰며 디코딩합니다.
- 디코딩은 CPU로만 수행합니다. 하드웨어 디코더 연동은 없습니다.

## 라이선스

LibVpxFrameDecoder는 [MIT License](LICENSE.txt)로 배포됩니다.

## 서드파티 고지

`native/` 아래 네이티브 라이브러리는 [libvpx](https://github.com/webmproject/libvpx) 빌드 결과물입니다. `BSD-3-Clause AND ISC` 라이선스가 적용되고 VP8, VP9에 대한 Google WebM 특허 그랜트가 함께 적용됩니다. 라이선스와 특허 전문은 바이너리와 함께 배포됩니다(`native/libvpx-LICENSE.txt`, `native/libvpx-PATENTS.txt`).

`native/libyuv.dll`은 [libyuv](https://chromium.googlesource.com/libyuv/libyuv) 빌드 결과물입니다. BSD 계열 라이선스에 추가 특허 그랜트가 적용됩니다. 전문은 바이너리와 함께 배포됩니다(`native/libyuv-LICENSE.txt`, `native/libyuv-PATENTS.txt`).
