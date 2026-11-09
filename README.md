# Stardual Valley

<img src="assets/stardual-valley-logo.png" width="320" alt="Stardual Valley logo" />

A lower-screen companion for **Stardew Valley on Android**, running on the AYN
Thor. The top screen plays the game; the bottom screen shows your day, map,
farm, journal, bundles, calendar and backpack, live.

**Modded for Android by [breepear](https://github.com/breepear).**

## Credits

Stardual Valley is built on
[AYN Thor DualScreen Mods](https://github.com/JoeCorrell/AYN-Thor-Dualscreen-Mods)
by **[JoeCorrell](https://github.com/JoeCorrell)**, with the Stardew Valley mod
by **Profmags**. They made the companion app and the mod; all of that work is
theirs. This fork adapts it for the Android version of Stardew Valley (instead
of the PC version running through WEMU) and gives the app its own name and logo.

The upstream project has no license, so its authors keep all rights to their
code. If you are JoeCorrell or Profmags and want anything changed or taken down,
please open an issue.

## Download

Get the latest **Stardual Valley APK** from the
[Releases page](../../releases/latest). You also need the Stardew mod from the
[upstream v2.0 release](https://github.com/JoeCorrell/AYN-Thor-Dualscreen-Mods/releases/tag/v2.0)
(`StardewDualScreenMod.zip`). See [Running on Android](#running-on-android-ayn-thor)
for the full setup.

## What changed in this fork

- **App name:** "DualScreen Mods" is now "Stardual Valley"
  (`src/main/res/values/strings.xml`).
- **App icon:** the upstream app had none. The Stardual Valley logo
  (`assets/stardual-valley-logo.png`) is the launcher icon, in
  `src/main/res/mipmap-*` and wired up in `src/main/AndroidManifest.xml`.
- **`tools/repack-apk.sh`:** builds the renamed app from the upstream release
  APK. See [Building the app](#building-the-app) for why.
- **Android setup docs:** below.

No code changes were needed to run on Android. The upstream v2.0 release mod
and app work as they are.

## Running on Android (AYN Thor)

Tested on an AYN Thor (Android 13) with:

| Piece | Version |
| --- | --- |
| Stardew Valley (Play Store) | 1.6.15.3 |
| [SMAPI Launcher](https://github.com/NRTnarathip/SMAPILoader/releases) | 1.1.7 |
| [SMAPI for Android](https://www.nexusmods.com/stardewvalley/mods/44436) | 4.3.2.5 |
| Stardew Valley Dual Screen Mod (upstream release) | 2.0.0 |

The upstream README says the mod needs SMAPI 4.5. Its `manifest.json` actually
asks for 4.0.0, and Android SMAPI 4.3.2.5 loads it. SMAPI rewrites a few calls
for the Android game automatically, and no errors are logged.

1. **Install SMAPI for Android.** Install the SMAPI Launcher APK, then use
   **Install SMAPI From Zip** with the SMAPI-Android zip from Nexus. The launcher
   only accepts a zip whose file name contains `SMAPI-Android`.
2. **Install the mod.** Download `StardewDualScreenMod.zip` from the
   [upstream v2.0 release](https://github.com/JoeCorrell/AYN-Thor-Dualscreen-Mods/releases/tag/v2.0),
   extract it, and copy the `StardewDualScreenMod` folder into:
   ```
   Android/data/abc.smapi.gameloader/files/Mods/
   ```
   (Android SMAPI uses this folder, not `StardewValley/Mods`.)
3. **Install the app.** Download `Stardual-Valley.apk` from the
   [Releases page](../../releases/latest) and install it. If the upstream
   DualScreen Mods app is installed, uninstall it first: both use the same
   package name but are signed with different keys.
4. **Play.** Start the game from **SMAPI Launcher**, then open **Stardual
   Valley** on the lower screen. It connects to `ws://127.0.0.1:7786` and fills
   in once a save is loaded.

To open the app on the lower screen over adb (display 4 on the Thor):

```bash
adb shell am start --display 4 -n com.wemu.dualscreenmods/com.wemu.dualscreen.DualScreenActivity
```

Notes:

- The mod hides the game's HUD on the upper screen, because that information
  moves to the lower screen. The app's Settings page has the mod's options.
- The SMAPI log (`Android/data/abc.smapi.gameloader/files/ErrorLogs/SMAPI-latest.txt`)
  shows `Listening on ws://127.0.0.1:7786` when the mod starts and
  `wemu connected` when the app connects.

## Building the app

The app can't be compiled from this repo alone: `DualScreenActivity.kt` imports
`ThorTheme`, `ModsPage` and `SecondScreen` from shared libraries that aren't
published here. Instead, `tools/repack-apk.sh` downloads the upstream release
APK, applies this repo's name and icon, and signs it with your own key:

```bash
APKTOOL_JAR=/path/to/apktool.jar \
BUILD_TOOLS=~/Library/Android/sdk/build-tools/37.0.0 \
KEYSTORE=~/stardual.keystore KEYSTORE_PASS=yourpass \
tools/repack-apk.sh
```

The result is `.repack/stardual-valley.apk`. Because it is signed with a
different key than the upstream app, uninstall the upstream app before
installing it the first time. Keep your keystore safe and out of git: every
later build must use the same key to update in place.

To change the icon, replace `assets/stardual-valley-logo.png` and regenerate
the launcher sizes (needs Pillow):

```bash
python3 - <<'PY'
from PIL import Image
im = Image.open('assets/stardual-valley-logo.png').convert('RGB')
for d, s in {'mdpi': 48, 'hdpi': 72, 'xhdpi': 96, 'xxhdpi': 144, 'xxxhdpi': 192}.items():
    im.resize((s, s), Image.LANCZOS).save(f'src/main/res/mipmap-{d}/ic_launcher.png')
PY
```

---

## Upstream README

The rest of this file is the original project's README.

Native lower-screen companions for games running through WEMU on the AYN Thor.
The Android companion keeps useful information and touch controls on the lower
display while the game remains playable on the upper display.

The current release includes the DualScreen Android app and the Stardew Valley
SMAPI integration.

## Downloads

Open the [latest release](https://github.com/JoeCorrell/AYN-Thor-Dualscreen-Mods/releases/latest) and download:

- `DualScreen Mods.apk` for the AYN Thor lower screen.
- `WemuSecondScreen.zip` for a complete Stardew Valley mod installation.
- `StardewDualScreen.dll` when updating an existing mod folder manually.

## Stardew Valley pages

| Page | Contents |
| --- | --- |
| Today | Weekday, date, time, weather, luck, gold, energy, health, shipping, skills, hotbar, and all unlocked backpack rows |
| Map | Stardew's valley map, the current location, and the farmer marker |
| Farm | Tall, independently scrollable machine, animal, and fruit-tree lists with compact farm summaries |
| Journal | Active quests, objectives, deadlines, rewards, and cancellation where Stardew permits it |
| Bundles | Community Center rooms, bundle progress, and missing item icons |
| Calendar | A fixed four-week season calendar with weekday headers, birthday portraits and labels, festivals, and travelling-cart days |
| Settings | Appearance, visible pages, connection details, and live mod options |

## Inventory controls and item details

- The Today page places the backpack directly below the hotbar.
- L/R rotates every unlocked backpack row through the active hotbar, so all
  inventory rows remain usable without tiny transfer controls.
- Tap selects or moves supported inventory items; hold uses or eats supported
  items.
- Watering cans show a compact blue capacity bar beneath the item.
- Weapons show their special-action cooldown.
- Skills show their current level without the removed XP-to-next-level line.

Farm lists use Stardew's real machine, animal, produce, and fruit-tree data.
Entries are arranged vertically and can be scrolled. Animals with no produce do
not receive a placeholder item icon.

Weather uses full wording such as `Sunny`, and the Today page includes the day
of the week alongside the date, season, and time.

## Installation

### Android companion

Install `DualScreen Mods.apk` on the AYN Thor. Android may ask you to allow
installation from the app that opened the APK.

### Stardew Valley mod

The mod requires Stardew Valley 1.6 and SMAPI 4.5 or later.

1. Extract `WemuSecondScreen.zip`.
2. Copy the extracted `WemuSecondScreen` folder into Stardew Valley's `Mods`
   directory inside the Steam container.
3. Start Stardew Valley through SMAPI.

For a manual update, replace the old DLL with `StardewDualScreen.dll` and ensure
the mod's `manifest.json` contains:

```json
"EntryDll": "StardewDualScreen.dll"
```

The default socket is `ws://127.0.0.1:7786`. Local connections work without
configuration. Set `AllowRemote` to `true` in the mod's `config.json` only when
the companion must connect over the network.

## Building

Build the SMAPI integration from `mods/StardewSecondScreen`:

```powershell
dotnet build -c Release
```

The output is `bin/Release/net6.0/StardewDualScreen.dll`.
