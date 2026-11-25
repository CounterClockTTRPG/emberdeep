#!/usr/bin/env bash
# Export a debug APK (build/android/Emberdeep.apk), then install it with adb if a phone is connected.
#   bash scripts/export-android.sh
# Needs: the Godot 4.7.2 .NET Android export templates (~/.local/share/godot/export_templates/4.7.2.stable.mono),
# an Android SDK (ANDROID_SDK, default ~/.local/opt/android-sdk), a JDK 17+ (JAVA_HOME or java on PATH), a debug
# keystore (made here if missing, ANDROID_DEBUG_KEYSTORE).
set -uo pipefail
source "$(dirname "$0")/common.sh"
require_godot
require_dotnet

ANDROID_SDK="${ANDROID_SDK:-$HOME/.local/opt/android-sdk}"
KEYSTORE="${ANDROID_DEBUG_KEYSTORE:-$HOME/.local/share/emberdeep-android/debug.keystore}"
if [ ! -f "$KEYSTORE" ]; then
    mkdir -p "$(dirname "$KEYSTORE")"
    "${JAVA_HOME:+$JAVA_HOME/bin/}keytool" -genkeypair -keystore "$KEYSTORE" -alias androiddebugkey -keyalg RSA \
        -keysize 2048 -validity 10000 -storepass android -keypass android -dname "CN=Android Debug,O=Android,C=US" || exit 1
fi
export PATH="$(dirname "$DOTNET"):$PATH"
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export ANDROID_HOME="$ANDROID_SDK" ANDROID_SDK_ROOT="$ANDROID_SDK"
export GODOT_ANDROID_KEYSTORE_DEBUG_PATH="$KEYSTORE" GODOT_ANDROID_KEYSTORE_DEBUG_USER=androiddebugkey \
       GODOT_ANDROID_KEYSTORE_DEBUG_PASSWORD=android

# assets/ is read as raw bytes at run time (models, icons, fonts, manifests) and normally hidden from Godot's importer
# by assets/.gdignore. On a phone there is no folder beside the app, so for the export the .gdignore steps aside and
# every file is marked "keep": packed into the APK exactly as it is. It comes back afterwards, whatever happens.
ASSETS="$PROJECT_DIR/assets"
[ -f "$ASSETS/.gdignore" ] && mv "$ASSETS/.gdignore" "$ASSETS/.gdignore.export"
trap '[ -f "$ASSETS/.gdignore.export" ] && mv "$ASSETS/.gdignore.export" "$ASSETS/.gdignore"' EXIT
while IFS= read -r -d '' f; do
    [ -e "$f.import" ] || printf '[remap]\n\nimporter="keep"\n' > "$f.import"
done < <(find "$ASSETS" -type f \( -name '*.glb' -o -name '*.png' -o -name '*.ttf' \) -print0)

APK="$PROJECT_DIR/build/android/Emberdeep.apk"
mkdir -p "$PROJECT_DIR/build/android"
rm -f "$APK"
"$GODOT" --headless --path "$PROJECT_DIR" --export-debug "Android" "$APK" 2>&1 | tr -d '\r' | grep -v -E "Storing|^ADDING:|reimport"
[ -f "$APK" ] || { echo "export failed: no $APK" >&2; exit 1; }
echo "exported $APK ($(du -h "$APK" | cut -f1))"

ADB="$ANDROID_SDK/platform-tools/adb"
if [ -x "$ADB" ] && [ -n "$("$ADB" devices | sed 1d | grep -w device)" ]; then
    "$ADB" install -r "$APK"
else
    echo "no device connected (install with: $ADB install -r $APK)"
fi
