#!/usr/bin/env bash
# Exports the Indoor AR scene as a Unity library, builds it with Unity's own
# Gradle, and copies the AARs into the Flutter app's gitignored unity-ar folder.
#
# Usage: tools/build-indoor-ar-aar.sh /path/to/NaviPetFlutter/android/unity-ar
#
# From build step 4 on, the AARs contain the app's MultiSet credentials:
# share them privately, never commit them or attach them to a public release.
set -euo pipefail

OUT="${1:?usage: $0 <NaviPetFlutter>/android/unity-ar}"
if [[ "$(basename "$OUT")" != "unity-ar" ]]; then
  echo "refusing: the output folder must be named unity-ar (it is wiped)" >&2
  exit 1
fi
OUT_PARENT="$(dirname "$OUT")"
if [[ ! -f "$OUT_PARENT/gradlew" && ! -f "$OUT_PARENT/settings.gradle" && ! -f "$OUT_PARENT/settings.gradle.kts" ]]; then
  echo "refusing: $OUT_PARENT is not an Android Gradle project (no gradlew or settings.gradle)" >&2
  exit 1
fi

EDITOR_DIR="${UNITY_EDITOR_DIR:-/Applications/Unity/Hub/Editor/6000.3.26f1}"
UNITY="$EDITOR_DIR/Unity.app/Contents/MacOS/Unity"
ANDROID_PLAYER="$EDITOR_DIR/PlaybackEngines/AndroidPlayer"
# Unity ships Gradle as bare jars (no bin/gradle launcher): run GradleMain directly.
GRADLE_LAUNCHER="$(find "$ANDROID_PLAYER/Tools/gradle/lib" -maxdepth 1 -name 'gradle-launcher-*.jar' 2>/dev/null | head -1)"
JDK="$ANDROID_PLAYER/OpenJDK"
[[ -x "$JDK/bin/java" ]] || JDK="$JDK/Contents/Home"
for tool in "$UNITY" "$GRADLE_LAUNCHER" "$JDK/bin/java"; do
  if [[ ! -e "$tool" ]]; then
    echo "missing $tool: install Unity 6000.3.26f1 with Android Build Support" >&2
    exit 1
  fi
done

PROJECT="$(cd "$(dirname "$0")/.." && pwd)"
EXPORT="$PROJECT/Builds/IndoorArAndroid"
mkdir -p "$PROJECT/Logs"

rm -rf "$EXPORT"
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" -buildTarget Android \
  -executeMethod IndoorAr.Editor.IndoorArExport.ExportAndroid \
  -logFile "$PROJECT/Logs/indoor-ar-export.log"

# Every Unity Gradle module except the launcher app becomes an AAR.
ANDROID_HOME="$ANDROID_PLAYER/SDK" JAVA_HOME="$JDK" "$JDK/bin/java" -Dorg.gradle.appname=gradle \
  -classpath "$GRADLE_LAUNCHER" org.gradle.launcher.GradleMain \
  -p "$EXPORT" --no-daemon assembleRelease -x :launcher:assembleRelease

rm -rf "$OUT"
mkdir -p "$OUT"
find "$EXPORT" -path '*/build/outputs/aar/*-release.aar' -not -path "$EXPORT/launcher/*" \
  -exec cp {} "$OUT/" \;
# Local AARs (ARCore client, permissions) are not bundled inside unityLibrary's AAR.
if [[ -d "$EXPORT/unityLibrary/libs" ]]; then
  find "$EXPORT/unityLibrary/libs" -maxdepth 1 -name '*.aar' -exec cp {} "$OUT/" \;
fi
# Remote Maven dependencies the Unity modules declare; the app's Gradle adds them.
{ grep -hoE "implementation[ (]+['\"][A-Za-z0-9_.-]+:[A-Za-z0-9_.-]+:[A-Za-z0-9_.+-]+['\"]" \
    "$EXPORT"/unityLibrary/build.gradle "$EXPORT"/unityLibrary/*.androidlib/build.gradle 2>/dev/null \
    || true; } | sed -E "s/.*['\"](.+)['\"]/\1/" | sort -u > "$OUT/maven-deps.txt"

if [[ ! -f "$OUT/unityLibrary-release.aar" ]]; then
  echo "unityLibrary-release.aar was not produced" >&2
  exit 1
fi
ls -l "$OUT"
echo "maven-deps.txt:"
cat "$OUT/maven-deps.txt"
