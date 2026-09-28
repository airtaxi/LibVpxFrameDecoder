<#
.SYNOPSIS
    Builds libvpx and libyuv with vcpkg and copies vpx.dll and libyuv.dll into native/win-x64 and native/win-arm64.

.DESCRIPTION
    Requires Visual Studio with the C++ toolchain and network access. vcpkg is cloned into
    -VcpkgRoot when it is missing, and bootstrapped when vcpkg.exe is missing. The script is
    idempotent: existing checkouts and installed packages are reused. Both libraries are built
    through the overlay ports in ports/ so the DLL builds are produced.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\build-native-libs.ps1
#>
param(
    [string]$VcpkgRoot = "C:\Data\Utils\vcpkg",
    [string]$LibVpxVersion = "1.16.0",
    [switch]$SkipInstall
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$nativeRoot = Join-Path $repositoryRoot "native"

if (-not (Test-Path (Join-Path $VcpkgRoot ".git"))) {
    Write-Host "Cloning vcpkg into $VcpkgRoot"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $VcpkgRoot) | Out-Null
    git clone --depth 1 https://github.com/microsoft/vcpkg.git $VcpkgRoot
    if ($LASTEXITCODE -ne 0) { throw "git clone failed with exit code $LASTEXITCODE" }
}

$vcpkgExe = Join-Path $VcpkgRoot "vcpkg.exe"
if (-not (Test-Path $vcpkgExe)) {
    Write-Host "Bootstrapping vcpkg"
    Push-Location $VcpkgRoot
    try {
        & cmd /c "bootstrap-vcpkg.bat -disableMetrics"
        if ($LASTEXITCODE -ne 0) { throw "vcpkg bootstrap failed with exit code $LASTEXITCODE" }
    }
    finally { Pop-Location }
}

if (-not $SkipInstall) {
    Write-Host "Installing libvpx and libyuv (x64-windows, arm64-windows) with the overlay ports"
    # The stock libvpx port builds statically on MSVC, so ports\libvpx turns on the shared build and
    # produces vpx.dll. ports\libyuv keeps the shared build and disables libyuv's optional JPEG
    # helpers so the runtime only needs libyuv.dll. A release build is enough for a runtime DLL.
    $env:VCPKG_BUILD_TYPE = "release"
    & $vcpkgExe install "libvpx:x64-windows" "libvpx:arm64-windows" "libyuv:x64-windows" "libyuv:arm64-windows" "--overlay-ports=$repositoryRoot\ports"
    if ($LASTEXITCODE -ne 0) { throw "vcpkg install failed with exit code $LASTEXITCODE" }
}

$tripletToRuntimeIdentifier = [ordered]@{
    "x64-windows"   = "win-x64"
    "arm64-windows" = "win-arm64"
}

foreach ($triplet in $tripletToRuntimeIdentifier.Keys) {
    $targetDirectory = Join-Path $nativeRoot $tripletToRuntimeIdentifier[$triplet]
    New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
    foreach ($library in @("vpx.dll", "libyuv.dll")) {
        $source = Join-Path $VcpkgRoot "installed\$triplet\bin\$library"
        if (-not (Test-Path $source)) { throw "$library was not found at $source. Make sure the overlay ports under ports\ were used." }
        Copy-Item -Path $source -Destination (Join-Path $targetDirectory $library) -Force
        Write-Host "Copied $source -> $targetDirectory\$library"
    }
}

foreach ($port in @("libvpx", "libyuv")) {
    $sourceRoot = Get-ChildItem -Path (Join-Path $VcpkgRoot "buildtrees\$port\src") -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $sourceRoot) { Write-Warning "$port source tree was not found under buildtrees; its license files were not refreshed."; continue }
    foreach ($licenseName in @("LICENSE", "PATENTS")) {
        $source = Join-Path $sourceRoot.FullName $licenseName
        if (-not (Test-Path $source)) { continue }
        $target = Join-Path $nativeRoot "$port-$licenseName.txt"
        Copy-Item -Path $source -Destination $target -Force
        Write-Host "Copied $source -> $target"
    }
}

$installedVersion = "1.16.0"
$versionHeader = Join-Path $VcpkgRoot "installed\x64-windows\include\vpx\vpx_version.h"
if (Test-Path $versionHeader) {
    $versionLine = Select-String -Path $versionHeader -Pattern "VERSION_STRING" | Select-Object -First 1
    if ($versionLine) { $installedVersion = ($versionLine.Line -replace '.*"(.*)".*', '$1') }
}

$libYuvVersion = "1916"
$libYuvVersionHeader = Join-Path $VcpkgRoot "installed\x64-windows\include\libyuv\version.h"
if (Test-Path $libYuvVersionHeader) {
    $versionLine = Select-String -Path $libYuvVersionHeader -Pattern "define\s+LIBYUV_VERSION\s+" | Select-Object -First 1
    if ($versionLine) { $libYuvVersion = ($versionLine.Line -replace '.*LIBYUV_VERSION\s+(\S+).*', '$1') }
}

$libYuvCommit = "unknown"
$libYuvPortFile = Join-Path $repositoryRoot "ports\libyuv\portfile.cmake"
if (Test-Path $libYuvPortFile) {
    $referenceLine = Select-String -Path $libYuvPortFile -Pattern "^\s*REF\s+(\S+)" | Select-Object -First 1
    if ($referenceLine) { $libYuvCommit = $referenceLine.Matches[0].Groups[1].Value }
}

$versionInfo = @(
    "libvpx $installedVersion (vcpkg triplet x64-windows / arm64-windows)"
    "libyuv $libYuvVersion (vcpkg triplet x64-windows / arm64-windows, pinned $libYuvCommit)"
    "pinned request: libvpx $LibVpxVersion"
    "built: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    "source: https://github.com/webmproject/libvpx"
    "source: https://chromium.googlesource.com/libyuv/libyuv"
    "license: libvpx BSD-3-Clause AND ISC (see libvpx-LICENSE.txt and libvpx-PATENTS.txt)"
    "license: libyuv BSD-3-Clause (see libyuv-LICENSE.txt and libyuv-PATENTS.txt)"
)
Set-Content -Path (Join-Path $nativeRoot "version.txt") -Value $versionInfo

Write-Host "Done. native libraries are ready under $nativeRoot"
