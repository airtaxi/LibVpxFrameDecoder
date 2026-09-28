#!/usr/bin/env bash
#
# Builds the static libvpx, libyuv and lvpxshim libraries for iOS and Mac Catalyst into native/apple/.
#
# Usage:
#   bash scripts/build-native-apple-static.sh ios          # ios-device and ios-simulator (arm64)
#   bash scripts/build-native-apple-static.sh maccatalyst  # maccatalyst-arm64
#
# The script runs on macOS with Xcode (xcode-select must point at a full Xcode) and needs network access to
# clone libvpx and libyuv. Every target produces the static libraries that
# buildTransitive/LibVpxFrameDecoder.targets links into an app through NativeReference. The version pins match
# the other platforms: libvpx v1.16.0 and libyuv d98915a654d3564e4802a0004add46221c4e4348.
set -euo pipefail

LIBVPX_VERSION="${LIBVPX_VERSION:-v1.16.0}"
LIBYUV_COMMIT="${LIBYUV_COMMIT:-d98915a654d3564e4802a0004add46221c4e4348}"
DEPLOYMENT_TARGET="${DEPLOYMENT_TARGET:-15.0}"

REPOSITORY_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SOURCE_ROOT="$REPOSITORY_ROOT/build/apple"
OUTPUT_ROOT="$REPOSITORY_ROOT/native/apple"
PROCESSOR_COUNT="$(sysctl -n hw.ncpu)"

info() { printf '%s\n' "$*"; }

fetch_sources() {
    mkdir -p "$SOURCE_ROOT"
    if [ ! -d "$SOURCE_ROOT/libvpx/.git" ]; then
        info "Cloning libvpx $LIBVPX_VERSION"
        git clone --depth 1 --branch "$LIBVPX_VERSION" https://github.com/webmproject/libvpx.git "$SOURCE_ROOT/libvpx"
    fi
    if [ ! -d "$SOURCE_ROOT/libyuv/.git" ]; then
        info "Cloning libyuv"
        git clone https://chromium.googlesource.com/libyuv/libyuv "$SOURCE_ROOT/libyuv"
    fi
    info "Checking out libyuv $LIBYUV_COMMIT"
    git -C "$SOURCE_ROOT/libyuv" fetch --depth 1 origin "$LIBYUV_COMMIT" || true
    git -C "$SOURCE_ROOT/libyuv" checkout --detach "$LIBYUV_COMMIT"
    patch_libvpx_configure
}

# The arm64-darwin-gcc target of libvpx adds the iphoneos sysroot and -miphoneos-version-min on its own, which
# conflicts with the target triple of the simulator and of Mac Catalyst. The block that adds them is patched to
# read the sysroot and the target triple from the environment instead, so the configure checks, the build and
# the resulting archive all agree on the platform. The patch fails loudly when the upstream block changes.
patch_libvpx_configure() {
    python3 - "$SOURCE_ROOT/libvpx/build/make/configure.sh" <<'PY'
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
text = path.read_text(encoding="utf-8")
marker = "    arm*-darwin-*)\n"
start = text.find(marker)
if start == -1:
    raise SystemExit("libvpx configure.sh does not contain the arm*-darwin case")
end = text.find("      ;;\n", start)
if end == -1:
    raise SystemExit("libvpx configure.sh does not terminate the arm*-darwin case")
end += len("      ;;\n")
block = text[start:end]
if "VPX_APPLE_SYSROOT" in block:
    print("the libvpx configure.sh is already patched")
elif "IOS_VERSION_MIN" in block or "build script passes the sysroot" in block:
    replacement = (
        "    arm*-darwin-*)\n"
        "      # The build script passes the sysroot and the target triple through the environment, because the\n"
        "      # same libvpx target also builds for the simulator and for Mac Catalyst.\n"
        "      if [ -n \"${VPX_APPLE_SYSROOT}\" ]; then\n"
        "        add_cflags  \"-isysroot ${VPX_APPLE_SYSROOT}\"\n"
        "        add_ldflags \"-isysroot ${VPX_APPLE_SYSROOT}\"\n"
        "      fi\n"
        "      if [ -n \"${VPX_APPLE_TARGET}\" ]; then\n"
        "        add_cflags  \"-target ${VPX_APPLE_TARGET}\"\n"
        "        add_ldflags \"-target ${VPX_APPLE_TARGET}\"\n"
        "      fi\n"
        "      ;;\n"
    )
    path.write_text(text[:start] + replacement + text[end:], encoding="utf-8")
    print("patched the libvpx configure.sh")
else:
    raise SystemExit(f"unexpected arm*-darwin block in libvpx configure.sh: {block!r}")
PY
}

# build_libvpx <name> <sysroot> <target triple>
build_libvpx() {
    local name="$1"
    local sysroot="$2"
    local target_triple="$3"
    local build_directory="$SOURCE_ROOT/build/libvpx-$name"
    local prefix_directory="$SOURCE_ROOT/install/libvpx-$name"

    info "Building libvpx for $name (sysroot $sysroot, target $target_triple)"
    rm -rf "$build_directory"
    mkdir -p "$build_directory" "$OUTPUT_ROOT/$name"
    (
        cd "$build_directory"
        # The patched configure.sh reads the sysroot and the target triple from the environment and adds them to
        # both the compile and the link flags. libvpx configure has no --extra-ldflags option, and its default
        # linker is the bare 'ld', which rejects driver flags, so the compiler driver runs the configure links.
        export VPX_APPLE_SYSROOT="$sysroot"
        export VPX_APPLE_TARGET="$target_triple"
        export LD="$(xcrun --find clang)"
        if ! "$SOURCE_ROOT/libvpx/configure" \
            --target=arm64-darwin-gcc \
            --disable-shared --enable-static \
            --disable-examples --disable-tools --disable-docs --disable-unit-tests \
            --enable-pic \
            --prefix="$prefix_directory"
        then
            echo "libvpx configure failed for $name; the tail of config.log:" >&2
            tail -n 40 config.log >&2 || true
            exit 1
        fi
        make -j"$PROCESSOR_COUNT"
        make install
    )
    cp "$prefix_directory/lib/libvpx.a" "$OUTPUT_ROOT/$name/libvpx.a"
}

# build_libyuv <name> <sysroot>
build_libyuv() {
    local name="$1"
    local sysroot="$2"
    local build_directory="$SOURCE_ROOT/build/libyuv-$name"

    info "Building libyuv for $name (sysroot $sysroot)"
    rm -rf "$build_directory"
    mkdir -p "$OUTPUT_ROOT/$name"
    # CMAKE_SYSTEM_NAME=iOS plus the iphonesimulator sysroot produces an arm64 simulator library, and the macOS
    # sysroot plus CMAKE_OSX_DEPLOYMENT_TARGET produces a Mac Catalyst (macabi) library. CMAKE_SYSTEM_PROCESSOR
    # is set explicitly because libyuv selects its aarch64 kernels from it, and only the static 'yuv' target is
    # built because libyuv always declares the shared target as well and the package only needs the archive.
    cmake -S "$SOURCE_ROOT/libyuv" -B "$build_directory" \
        -DCMAKE_SYSTEM_NAME=iOS \
        -DCMAKE_OSX_SYSROOT="$sysroot" \
        -DCMAKE_OSX_ARCHITECTURES=arm64 \
        -DCMAKE_OSX_DEPLOYMENT_TARGET="$DEPLOYMENT_TARGET" \
        -DCMAKE_SYSTEM_PROCESSOR=arm64 \
        -DBUILD_SHARED_LIBS=OFF \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_DISABLE_FIND_PACKAGE_JPEG=ON
    cmake --build "$build_directory" --config Release --target yuv -j "$PROCESSOR_COUNT"
    cp "$build_directory/libyuv.a" "$OUTPUT_ROOT/$name/libyuv.a"
}

# build_shim <name> <sysroot> <target triple>
build_shim() {
    local name="$1"
    local sysroot="$2"
    local target_triple="$3"
    local object_path="$SOURCE_ROOT/build/lvpx_shim-$name.o"

    info "Building the lvpxshim constants shim for $name (target $target_triple)"
    mkdir -p "$SOURCE_ROOT/build" "$OUTPUT_ROOT/$name"
    clang -c "$REPOSITORY_ROOT/native/shim/lvpx_shim.c" \
        -o "$object_path" \
        -isysroot "$sysroot" \
        -target "$target_triple" \
        -O2
    ar rcs "$OUTPUT_ROOT/$name/liblvpxshim.a" "$object_path"
}

case "${1:-}" in
    ios)
        fetch_sources
        device_sysroot="$(xcrun --sdk iphoneos --show-sdk-path)"
        simulator_sysroot="$(xcrun --sdk iphonesimulator --show-sdk-path)"
        device_target="arm64-apple-ios$DEPLOYMENT_TARGET"
        simulator_target="arm64-apple-ios$DEPLOYMENT_TARGET-simulator"
        build_libvpx ios-device "$device_sysroot" "$device_target"
        build_libvpx ios-simulator "$simulator_sysroot" "$simulator_target"
        build_libyuv ios-device iphoneos
        build_libyuv ios-simulator iphonesimulator
        build_shim ios-device "$device_sysroot" "$device_target"
        build_shim ios-simulator "$simulator_sysroot" "$simulator_target"
        ;;
    maccatalyst)
        fetch_sources
        macos_sysroot="$(xcrun --sdk macosx --show-sdk-path)"
        catalyst_target="arm64-apple-ios$DEPLOYMENT_TARGET-macabi"
        build_libvpx maccatalyst-arm64 "$macos_sysroot" "$catalyst_target"
        build_libyuv maccatalyst-arm64 macosx
        build_shim maccatalyst-arm64 "$macos_sysroot" "$catalyst_target"
        ;;
    *)
        printf 'Usage: %s ios|maccatalyst\n' "$0" >&2
        exit 1
        ;;
esac

info "Done. Static libraries are ready under $OUTPUT_ROOT"
find "$OUTPUT_ROOT" -name '*.a' -print | sort
