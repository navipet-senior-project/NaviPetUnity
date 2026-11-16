#!/usr/bin/env bash
# Builds the native sign-pose solver (tools/sign-pnp/IndoorSignPnp.cpp) against the official OpenCV 4.14.0
# release (Apache-2.0, github.com/opencv/opencv). iOS and Android builds of the project fail without its output.
#
# Usage: tools/build-sign-pnp.sh [ios|android]   (default: both; iOS needs Xcode)
#
# The release zips are checked against the sha256 GitHub publishes for them and cached in .cache/opencv
# (OPENCV_CACHE overrides), so only the first run needs the network. Every output is gitignored:
#   Assets/Plugins/iOS/libindoorsignpnp.a                     arm64 device, shim plus only the OpenCV code it uses
#   Assets/Plugins/Android/libs/arm64-v8a/libindoorsignpnp.so
set -euo pipefail

VERSION=4.14.0
IOS_ZIP=opencv-$VERSION-ios-framework.zip
IOS_SHA=f4f428fa9270adf95b3ed4705eb18ba3028ed2c2777a4f1a002739b869986a6a
ANDROID_ZIP=opencv-$VERSION-android-sdk.zip
ANDROID_SHA=e8cfaf2e51f2e2127a6ede91718d1ef7587f8b6e62db922816e7c33a1f1116a7
URL=https://github.com/opencv/opencv/releases/download/$VERSION

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CACHE="${OPENCV_CACHE:-$ROOT/.cache/opencv}"
SHIM="$ROOT/tools/sign-pnp/IndoorSignPnp.cpp"
EDITOR_DIR="${UNITY_EDITOR_DIR:-/Applications/Unity/Hub/Editor/6000.3.26f1}"

verified() { [[ -f "$1" ]] && echo "$2  $1" | shasum -a 256 -c - >/dev/null 2>&1; }

# Downloads $1 into the cache unless a copy with sha256 $2 is already there; never keeps a file that fails the check.
fetch() {
  local file="$CACHE/$1"
  verified "$file" "$2" && return
  mkdir -p "$CACHE"
  echo "downloading $URL/$1"
  if ! curl -fL --retry 3 -o "$file.part" "$URL/$1"; then
    rm -f "$file.part"
    echo "error: could not download $URL/$1. Offline? Copy that file into $CACHE and rerun." >&2
    exit 1
  fi
  if ! verified "$file.part" "$2"; then
    rm -f "$file.part"
    echo "error: $1 does not match the pinned sha256 $2; refusing to use it." >&2
    exit 1
  fi
  mv "$file.part" "$file"
}

# Unzips the listed members of cached zip $1 into $2 once; a half-finished unzip is never reused.
extract() {
  local zip="$1" dir="$2"
  shift 2
  [[ -d "$dir" ]] && return
  rm -rf "$dir.tmp"
  unzip -q "$CACHE/$zip" "$@" -d "$dir.tmp"
  mv "$dir.tmp" "$dir"
}

ios() {
  fetch "$IOS_ZIP" "$IOS_SHA"
  local sdk="$CACHE/opencv-$VERSION-ios" work="$CACHE/ios-build" out="$ROOT/Assets/Plugins/iOS/libindoorsignpnp.a"
  extract "$IOS_ZIP" "$sdk" 'opencv2.framework/Versions/A/*'
  local fw="$sdk/opencv2.framework/Versions/A"
  rm -rf "$work"
  # The headers include each other as <opencv2/...>.
  mkdir -p "$work/include" && ln -s "$fw/Headers" "$work/include/opencv2"
  # minimum iOS matches ProjectSettings.
  xcrun -sdk iphoneos clang++ -arch arm64 -miphoneos-version-min=15.0 -std=c++17 -O2 \
    -I"$work/include" -c "$SHIM" -o "$work/shim.o"
  lipo -thin arm64 "$fw/opencv2" -output "$work/opencv2.a"
  # Pull in only the OpenCV objects the shim needs and hide their symbols. Linking the whole framework would also
  # force-load its Objective-C and Swift wrappers under -ObjC, which the ML Kit pods add to UnityFramework.
  xcrun -sdk iphoneos ld -r -arch arm64 "$work/shim.o" "$work/opencv2.a" -exported_symbol _IndoorSignSolveIppe \
    -o "$work/IndoorSignPnp.o"
  rm -f "$out"
  xcrun -sdk iphoneos ar rcs "$out" "$work/IndoorSignPnp.o"
  cp "$ROOT/tools/sign-pnp/LICENSE-OpenCV.txt" "$(dirname "$out")/LICENSE-OpenCV.txt"
  cp "$ROOT/tools/sign-pnp/NOTICE-OpenCV.txt" "$(dirname "$out")/NOTICE-OpenCV.txt"
  rm -rf "$work"
  echo "iOS: $out"
}

android() {
  fetch "$ANDROID_ZIP" "$ANDROID_SHA"
  local sdk="$CACHE/opencv-$VERSION-android"
  extract "$ANDROID_ZIP" "$sdk" 'OpenCV-android-sdk/sdk/native/jni/include/*' \
    'OpenCV-android-sdk/sdk/native/staticlibs/arm64-v8a/*' 'OpenCV-android-sdk/sdk/native/3rdparty/libs/arm64-v8a/*' \
    'OpenCV-android-sdk/sdk/etc/licenses/*'
  local native="$sdk/OpenCV-android-sdk/sdk/native"
  local clang="$EDITOR_DIR/PlaybackEngines/AndroidPlayer/NDK/toolchains/llvm/prebuilt/darwin-x86_64/bin/clang++"
  if [[ ! -x "$clang" ]]; then
    echo "error: no NDK clang at $clang. Install Android Build Support for the editor or set UNITY_EDITOR_DIR." >&2
    exit 1
  fi
  local out="$ROOT/Assets/Plugins/Android/libs/arm64-v8a"
  local thirdparty=()
  for lib in "$native"/3rdparty/libs/arm64-v8a/lib*.a; do
    lib="${lib##*/lib}"
    thirdparty+=("-l${lib%.a}")
  done
  mkdir -p "$out"
  # minSdk 26 matches ProjectSettings; 16 KB pages for Android 15+ devices.
  "$clang" --target=aarch64-linux-android26 -std=c++17 -O2 -fPIC -shared \
    -I"$native/jni/include" "$SHIM" \
    -L"$native/staticlibs/arm64-v8a" -L"$native/3rdparty/libs/arm64-v8a" \
    -Wl,--start-group -lopencv_calib3d -lopencv_features2d -lopencv_flann -lopencv_imgproc -lopencv_core \
    "${thirdparty[@]}" -Wl,--end-group -static-libstdc++ -llog -lz -ldl -lm \
    -Wl,--no-undefined -Wl,--gc-sections -Wl,--exclude-libs,ALL -Wl,-z,max-page-size=16384 -s \
    -o "$out/libindoorsignpnp.so"
  cp "$ROOT/tools/sign-pnp/LICENSE-OpenCV.txt" "$out/LICENSE-OpenCV.txt"
  cp "$ROOT/tools/sign-pnp/NOTICE-OpenCV.txt" "$out/NOTICE-OpenCV.txt"
  rm -rf "$out/third-party-licenses"
  cp -R "$sdk/OpenCV-android-sdk/sdk/etc/licenses" "$out/third-party-licenses"
  echo "Android: $out/libindoorsignpnp.so"
}

case "${1:-all}" in
  ios) ios ;;
  android) android ;;
  all) ios; android ;;
  *) echo "usage: $0 [ios|android]" >&2; exit 2 ;;
esac
