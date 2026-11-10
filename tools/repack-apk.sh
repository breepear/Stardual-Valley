#!/usr/bin/env bash
# Builds the Stardual Valley app from the upstream DualScreen Mods release.
#
# This repo does not contain everything needed to build the app from source:
# DualScreenActivity.kt imports ThorTheme and the Stardew pages (ModsPage,
# SecondScreen) from shared libraries that are not published here. So instead of
# compiling, this script takes the upstream release APK, swaps in this repo's
# app name, icon and start screen, and signs the result with your own key.
#
# The start screen (overlay/src/com/stardual/StardualActivity.java) is compiled
# here and added to the APK as an extra dex file. It becomes the launcher
# activity and shows src/main/res/drawable-nodpi/stardual_home.png full screen
# until a Stardew save is loaded.
#
# Requirements:
#   - Java 17+ (javac)
#   - apktool jar   (https://github.com/iBotPeaches/Apktool/releases)
#   - Android SDK build-tools (zipalign, apksigner, d8) and a platform android.jar
#   - gh (GitHub CLI), signed in, to download the upstream release
#
# Usage:
#   APKTOOL_JAR=/path/to/apktool.jar \
#   BUILD_TOOLS=~/Library/Android/sdk/build-tools/37.0.0 \
#   ANDROID_JAR=~/Library/Android/sdk/platforms/android-36/android.jar \
#   KEYSTORE=~/stardual.keystore KEYSTORE_PASS=yourpass \
#   tools/repack-apk.sh
#
# If KEYSTORE does not exist it is created. Keep it: Android only lets you update
# the installed app in place if every new build is signed with the same key.
# Output: .repack/stardual-valley.apk

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UPSTREAM_REPO="${UPSTREAM_REPO:-JoeCorrell/AYN-Thor-Dualscreen-Mods}"
UPSTREAM_TAG="${UPSTREAM_TAG:-v2.0}"
UPSTREAM_ASSET="${UPSTREAM_ASSET:-DualScreen.Mods.apk}"

: "${APKTOOL_JAR:?Set APKTOOL_JAR to the apktool jar}"
: "${BUILD_TOOLS:?Set BUILD_TOOLS to an Android SDK build-tools folder}"
: "${ANDROID_JAR:?Set ANDROID_JAR to an Android SDK platform android.jar}"
: "${KEYSTORE:?Set KEYSTORE to your signing keystore path}"
: "${KEYSTORE_PASS:?Set KEYSTORE_PASS to the keystore password}"
KEY_ALIAS="${KEY_ALIAS:-stardual}"

WORK="$REPO_ROOT/.repack"
rm -rf "$WORK"
mkdir -p "$WORK"

echo "Downloading $UPSTREAM_ASSET from $UPSTREAM_REPO $UPSTREAM_TAG"
gh release download "$UPSTREAM_TAG" -R "$UPSTREAM_REPO" -p "$UPSTREAM_ASSET" -D "$WORK"

echo "Unpacking"
java -jar "$APKTOOL_JAR" d -o "$WORK/app" "$WORK/$UPSTREAM_ASSET"

echo "Applying Stardual's changes to the app's code"
# Fixes the bundles page crash and tightens the Gift Guide layout. See the file for details.
python3 -I "$REPO_ROOT/tools/smali_patches.py" "$WORK/app"

echo "Applying name and icon"
APP_NAME="$(sed -n 's|.*<string name="app_name">\(.*\)</string>.*|\1|p' "$REPO_ROOT/src/main/res/values/strings.xml")"
sed -i.bak "s|<string name=\"app_name\">[^<]*</string>|<string name=\"app_name\">$APP_NAME</string>|" \
  "$WORK/app/res/values/strings.xml"
# Launcher icon: legacy PNGs plus the adaptive icon (background + foreground layers)
# that Android 8+ launchers mask to a circle or other shape.
for dir in "$REPO_ROOT"/src/main/res/mipmap-*; do
  name="$(basename "$dir")"
  mkdir -p "$WORK/app/res/$name"
  cp "$dir"/* "$WORK/app/res/$name/"
done
if ! grep -q 'android:icon=' "$WORK/app/AndroidManifest.xml"; then
  sed -i.bak 's|android:label="@string/app_name"|android:icon="@mipmap/ic_launcher" android:label="@string/app_name" android:roundIcon="@mipmap/ic_launcher"|' \
    "$WORK/app/AndroidManifest.xml"
fi

echo "Adding the Overview page"
# overlay/assets holds the web page the Overview tab shows (see StardualOverview.java).
mkdir -p "$WORK/app/assets"
cp -R "$REPO_ROOT/overlay/assets/." "$WORK/app/assets/"

echo "Adding the Stardual start screen"
mkdir -p "$WORK/app/res/drawable-nodpi"
cp "$REPO_ROOT/src/main/res/drawable-nodpi/stardual_home.png" "$WORK/app/res/drawable-nodpi/"
mkdir -p "$WORK/overlay/stubs" "$WORK/overlay/classes" "$WORK/overlay/dex"
# Paths may contain spaces, so collect file lists into arrays.
stubs=(); while IFS= read -r -d '' f; do stubs+=("$f"); done < <(find "$REPO_ROOT/overlay/stubs" -name '*.java' -print0)
srcs=(); while IFS= read -r -d '' f; do srcs+=("$f"); done < <(find "$REPO_ROOT/overlay/src" -name '*.java' -print0)
javac -nowarn -source 11 -target 11 -cp "$ANDROID_JAR" -d "$WORK/overlay/stubs" "${stubs[@]}"
javac -nowarn -source 11 -target 11 -cp "$ANDROID_JAR:$WORK/overlay/stubs" -d "$WORK/overlay/classes" "${srcs[@]}"
classes=(); while IFS= read -r -d '' f; do classes+=("$f"); done < <(find "$WORK/overlay/classes" -name '*.class' -print0)
"$BUILD_TOOLS/d8" --release --min-api 26 --lib "$ANDROID_JAR" --classpath "$WORK/overlay/stubs" \
  --output "$WORK/overlay/dex" "${classes[@]}"
# Add it after the upstream dex files. apktool unpacks those as smali, smali_classes2 ...
# smali_classesN, which it rebuilds into classes.dex ... classesN.dex.
n=1; while [ -e "$WORK/app/classes$((n + 1)).dex" ] || [ -d "$WORK/app/smali_classes$((n + 1))" ]; do n=$((n + 1)); done
cp "$WORK/overlay/dex/classes.dex" "$WORK/app/classes$((n + 1)).dex"
# Launch StardualActivity instead of the upstream DualScreenActivity.
sed -i.bak 's|android:name="com.wemu.dualscreen.DualScreenActivity"|android:name="com.stardual.StardualActivity"|' \
  "$WORK/app/AndroidManifest.xml"
grep -q 'com.stardual.StardualActivity' "$WORK/app/AndroidManifest.xml"
find "$WORK/app" -name '*.bak' -delete

echo "Rebuilding"
java -jar "$APKTOOL_JAR" b "$WORK/app" -o "$WORK/unsigned.apk"
"$BUILD_TOOLS/zipalign" -f -p 4 "$WORK/unsigned.apk" "$WORK/aligned.apk"

if [ ! -f "$KEYSTORE" ]; then
  echo "Creating keystore $KEYSTORE"
  keytool -genkeypair -keystore "$KEYSTORE" -alias "$KEY_ALIAS" -keyalg RSA -keysize 2048 \
    -validity 10000 -storepass "$KEYSTORE_PASS" -keypass "$KEYSTORE_PASS" -dname "CN=Stardual Valley"
fi

echo "Signing"
"$BUILD_TOOLS/apksigner" sign --ks "$KEYSTORE" --ks-key-alias "$KEY_ALIAS" \
  --ks-pass "pass:$KEYSTORE_PASS" --key-pass "pass:$KEYSTORE_PASS" \
  --out "$WORK/stardual-valley.apk" "$WORK/aligned.apk"

echo "Done: $WORK/stardual-valley.apk"
"$BUILD_TOOLS/aapt2" dump badging "$WORK/stardual-valley.apk" | grep -E '^application:'
