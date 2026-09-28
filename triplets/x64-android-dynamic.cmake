cmake_minimum_required(VERSION 3.16)

set(VCPKG_TARGET_ARCHITECTURE x64)
# A static CRT keeps libc++ inside the shared libraries, so no libc++_shared.so has to ship with the app.
set(VCPKG_CRT_LINKAGE static)
set(VCPKG_LIBRARY_LINKAGE dynamic)
set(VCPKG_CMAKE_SYSTEM_NAME Android)
set(VCPKG_CMAKE_SYSTEM_VERSION 24)
# The Android NDK toolchain falls back to armeabi-v7a when no ABI is given, which silently produces
# 32 bit x86 shared libraries. The ABI and the 16 KB page size support are set explicitly.
set(VCPKG_CMAKE_CONFIGURE_OPTIONS -DANDROID_ABI=x86_64 -DANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON)
