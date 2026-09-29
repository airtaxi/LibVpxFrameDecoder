cmake_minimum_required(VERSION 3.16)

set(VCPKG_TARGET_ARCHITECTURE arm)
# A static CRT keeps libc++ inside the shared libraries, so no libc++_shared.so has to ship with the app.
set(VCPKG_CRT_LINKAGE static)
set(VCPKG_LIBRARY_LINKAGE dynamic)
set(VCPKG_CMAKE_SYSTEM_NAME Android)
set(VCPKG_CMAKE_SYSTEM_VERSION 24)
# armeabi-v7a is the 32 bit ARM ABI. Neon stays on, because current Android NDKs reject ANDROID_ARM_NEON=OFF
# with "Disabling Neon is no longer supported", and every device that runs API 24 supports it. The libvpx
# overlay port passes --disable-neon to its own configure step. The ABI and the 16 KB page size support are
# set explicitly.
set(VCPKG_CMAKE_CONFIGURE_OPTIONS -DANDROID_ABI=armeabi-v7a -DANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON)
