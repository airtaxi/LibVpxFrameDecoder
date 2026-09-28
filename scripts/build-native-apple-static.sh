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
}

# build_libvpx <name> <sysroot> <extra flags>
build_libvpx() {
    local name="$1"
    local sysroot="$2"
    local extra_flags="$3"
    local build_directory="$SOURCE_ROOT/build/libvpx-$name"
    local prefix_directory="$SOURCE_ROOT/install/libvpx-$name"

    info "Building libvpx for $name (sysroot $sysroot)"
    rm -rf "$build_directory"
    mkdir -p "$build_directory" "$OUTPUT_ROOT/$name"
    (
        cd "$build_directory"
        # The arm64-darwin-gcc target is the iOS target of libvpx: it adds -miphoneos-version-min and the
        # iphoneos sysroot on its own. The extra flags are appended last, so they override the sysroot and the
        # deployment target for the simulator and for Mac Catalyst.
        # libvpx configure has no --extra-ldflags option, so the link flags go through the environment, which
        # is also how the vcpkg port passes them.
        export LDFLAGS="-isysroot $sysroot $extra_flags"
        "$SOURCE_ROOT/libvpx/configure" \
            --target=arm64-darwin-gcc \
            --disable-shared --enable-static \
            --disable-examples --disable-tools --disable-docs --disable-unit-tests \
            --enable-pic \
            --prefix="$prefix_directory" \
            --extra-cflags="-isysroot $sysroot $extra_flags"
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
    # CMAKE_SYSTEM_NAME=iOS plus the iphonesimulator sysroot produces an arm64 simulator library, and the
    # macOS sysroot plus CMAKE_OSX_DEPLOYMENT_TARGET produces a Mac Catalyst (macabi) library.
    cmake -S "$SOURCE_ROOT/libyuv" -B "$build_directory" \
        -DCMAKE_SYSTEM_NAME=iOS \
        -DCMAKE_OSX_SYSROOT="$sysroot" \
        -DCMAKE_OSX_ARCHITECTURES=arm64 \
        -DCMAKE_OSX_DEPLOYMENT_TARGET="$DEPLOYMENT_TARGET" \
        -DBUILD_SHARED_LIBS=OFF \
        -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_DISABLE_FIND_PACKAGE_JPEG=ON
    cmake --build "$build_directory" --config Release -j "$PROCESSOR_COUNT"
    cp "$build_directory/libyuv.a" "$OUTPUT_ROOT/$name/libyuv.a"
}

# build_shim <name> <sysroot> <extra flags>
build_shim() {
    local name="$1"
    local sysroot="$2"
    local extra_flags="$3"
    local object_path="$SOURCE_ROOT/build/lvpx_shim-$name.o"

    info "Building the lvpxshim constants shim for $name"
    mkdir -p "$SOURCE_ROOT/build" "$OUTPUT_ROOT/$name"
    clang -c "$REPOSITORY_ROOT/native/shim/lvpx_shim.c" \
        -o "$object_path" \
        -isysroot "$sysroot" \
        -arch arm64 \
        -O2 \
        $extra_flags
    ar rcs "$OUTPUT_ROOT/$name/liblvpxshim.a" "$object_path"
}

case "${1:-}" in
    ios)
        fetch_sources
        device_sysroot="$(xcrun --sdk iphoneos --show-sdk-path)"
        simulator_sysroot="$(xcrun --sdk iphonesimulator --show-sdk-path)"
        build_libvpx ios-device "$device_sysroot" "-miphoneos-version-min=$DEPLOYMENT_TARGET"
        build_libvpx ios-simulator "$simulator_sysroot" "-mios-simulator-version-min=$DEPLOYMENT_TARGET"
        build_libyuv ios-device iphoneos
        build_libyuv ios-simulator iphonesimulator
        build_shim ios-device "$device_sysroot" "-miphoneos-version-min=$DEPLOYMENT_TARGET"
        build_shim ios-simulator "$simulator_sysroot" "-mios-simulator-version-min=$DEPLOYMENT_TARGET"
        ;;
    maccatalyst)
        fetch_sources
        macos_sysroot="$(xcrun --sdk macosx --show-sdk-path)"
        catalyst_flags="-target arm64-apple-ios$DEPLOYMENT_TARGET-macabi"
        build_libvpx maccatalyst-arm64 "$macos_sysroot" "$catalyst_flags"
        build_libyuv maccatalyst-arm64 macosx
        build_shim maccatalyst-arm64 "$macos_sysroot" "$catalyst_flags"
        ;;
    *)
        printf 'Usage: %s ios|maccatalyst\n' "$0" >&2
        exit 1
        ;;
esac

info "Done. Static libraries are ready under $OUTPUT_ROOT"
find "$OUTPUT_ROOT" -name '*.a' -print | sort
