# Stardual Valley

<img src="assets/stardual-valley-banner.png" width="100%" alt="Stardual Valley title art: the Stardual Valley wordmark over a starry sky and green hills" />

A lower-screen companion for **Stardew Valley on the AYN Thor**. The top screen
plays the game; the bottom screen shows an overview of your day, the map, your
relationships, gift ideas, fields, crafting, journal, machines, bundles and
calendar, live.

**Works with both versions of Stardew Valley on the Thor:**

- **Android** (the Google Play game), running through **SMAPI Launcher**
- **PC** (the Steam game), running through **Cinderbox**

**Adapted by [breepear](https://github.com/breepear)** from JoeCorrell's
AYN Thor DualScreen Mods (see [Credits](#credits)).

## Credits

Stardual Valley is built on
[AYN Thor DualScreen Mods](https://github.com/JoeCorrell/AYN-Thor-Dualscreen-Mods)
by **[JoeCorrell](https://github.com/JoeCorrell)**, with the Stardew Valley mod
by **Profmags**. They made the companion app and the mod; this project builds
on their work. Stardual Valley adds its own pages, layout changes and fixes on
top, gives the app its own name and logo, and makes it work with Stardew on
Android and in Cinderbox.

The upstream project has no license, so its authors keep all rights to their
code. If you are JoeCorrell or Profmags and want anything changed or taken down,
please open an issue.

## Download

Get these from the latest [Releases page](../../releases/latest):

| File | What it is | Needed? |
| --- | --- | --- |
| `Stardual-Valley.apk` | The lower-screen app | Yes |
| `StardewDualScreenMod.zip` | The Stardew mod that feeds the app. Same file for Android and PC. | Yes |
| `StickCursor-Android.zip` | Right-stick pointer and toolbar controls for the Android game | Optional |
| `StickCursor-PC.zip` | Right-stick pointer, A-button fix and placement preview for the PC game in Cinderbox | Optional |

Then follow [Setup](#setup).

## What Stardual Valley adds

On top of the upstream app and mod:

- **Overview page** (the first page). A dashboard of your day:
  - **Today, tomorrow and luck** across the top, with tomorrow's weather.
  - **To do:** birthdays (with a gift you own that they love or like),
    crops to water and harvest, machines that are ready, animals to pet, milk or
    shear, the pet bowl, timed quests and special orders, mail, a finished tool
    upgrade, the Traveling Cart and festivals. Tap a row to tick it off; ticks
    are kept for the day. Things the game knows you've done tick themselves.
  - **When you're bored:** fish you haven't caught that are biting now, gift
    ideas, villagers you haven't talked to, museum and bundle items you're
    carrying, something to cook, new items to ship and your next mines goal.
    Tap a villager to open their Lookup Anything panel on the top screen.
- **Backpack page removed.** The Overview replaces it, and the game's own
  toolbar covers the rest.
- **Relationships page:** hearts in two rows, each villager's status ("Single",
  "Married to Pierre", "Dating you") under their birthday, no header or footer,
  cleaner gift and talk checkboxes. Tap a villager to open their Lookup
  Anything panel on the top screen.
- **Gift Guide fits more villagers:** about five at once instead of two.
- **Map:** the villager row runs the full width with names under bigger
  portraits; the farm summary and date row are gone.
- **Crafting and Fields:** the bottom strips are gone so the lists fill the
  screen.
- **Tab order:** Overview, Map, Relationships, Gift Guide, Fields, Crafting,
  Journal, Machines, Bundles, Calendar, Settings.
- **Start screen:** the Stardual Valley title art while you wait for the game,
  with a hint, then "Connected! Load a save in Stardew Valley to begin." It
  replaces the upstream multi-game list, so the app only shows Stardew.
- **Fixes:** the Bundles page no longer crashes on the Construction Bundle, and
  reopening the app while the game is running loads all the pictures again.
- **App name and icon:** "Stardual Valley" with its own two-star logo.
- **Stick Cursor** (optional mod, in `mods/StickCursor`). On PC it also fixes A:
  it always acts on the tile you're facing (or the pointer, when it shows) and
  never opens the menu by catching the clock box.

The app's code changes are in `tools/smali_patches.py` and `overlay/`; the mod
changes are in `mods/StardewSecondScreen`. See [Building the app](#building-the-app).

## Requirements

- **An AYN Thor** (or another dual-screen Android device; only the Thor is
  tested).
- **Stardew Valley**, either:
  - **Android:** from Google Play (or the Galaxy Store), 1.6.15.3 or later,
    with **SMAPI Launcher** and **SMAPI for Android**; or
  - **PC:** the Steam version running in **Cinderbox**, which comes with SMAPI.
- **The Stardew Valley Dual Screen Mod** from this repo's releases.
- **The Stardual Valley app.**

Tested on an AYN Thor (Android 13) with:

| Piece | Version |
| --- | --- |
| Stardew Valley for Android (Google Play) | 1.6.15.3 |
| [SMAPI Launcher](https://github.com/NRTnarathip/SMAPILoader/releases) | 1.1.7 |
| [SMAPI for Android](https://www.nexusmods.com/stardewvalley/mods/44436) | 4.3.2.5 |
| Stardew Valley PC (Steam), in Cinderbox | 1.6.15 |
| Cinderbox | 0.8.1 (SMAPI 4.5.2.5) |
| Stardew Valley Dual Screen Mod | 2.1.0, from this repo's [releases](../../releases/latest) |
| Stardual Valley | latest [release](../../releases/latest) |

## Setup

Do Step 1 for the version of Stardew you play (or both), then Steps 2 to 4.

### Step 1a: Android game: set up SMAPI Launcher

SMAPI Launcher runs the Android version of Stardew Valley with mods.

1. **Install Stardew Valley** from Google Play and open it once to make sure
   it runs.
2. **Remove other mod loaders for the Android game.** Delete any leftover
   `StardewValley/smapi-internal` folder in your device storage from an older
   loader. Leftover loader files can make the game stick on a white screen.
   (Cinderbox's own `StardewValley/desktop` folder is fine to keep.)
3. **Install SMAPI Launcher.** Download `SMAPI-Launcher-v1.1.7.apk` from the
   [SMAPI Launcher releases](https://github.com/NRTnarathip/SMAPILoader/releases)
   and install it. Android may ask you to allow installs from your browser or
   file manager.
4. **Download SMAPI for Android 4.3.2.5** from
   [Nexus Mods](https://www.nexusmods.com/stardewvalley/mods/44436?tab=files)
   (you need a free Nexus account; choose **Manual download**). Use this Nexus
   version: the older 4.3.2.1 build on GitHub shows a white screen with
   Stardew 1.6.15.3.
5. **Install SMAPI.** Open SMAPI Launcher, tap **Install SMAPI From Zip**, and
   pick the zip you downloaded. The launcher only accepts a zip whose file
   name contains `SMAPI-Android` (the Nexus download already does; rename it
   if you've changed the name). Wait for **"Successfully Install SMAPI"**.
6. **Check it works.** Tap **Start Game**. The title screen should show
   `SMAPI v4.3.2.5` in the bottom-left corner.

> [!NOTE]
> SMAPI Launcher keeps its own mods and saves, separate from the regular game:
>
> - Mods go in `Android/data/abc.smapi.gameloader/files/Mods/`
> - Saves are in `Android/data/abc.smapi.gameloader/files/Saves/`
>
> Your existing farm won't appear in SMAPI until you copy its save folder from
> `Android/data/com.chucklefish.stardewvalley/files/Saves/` into SMAPI's
> `Saves` folder. Always start the game from **SMAPI Launcher**, not the
> regular Stardew Valley icon, or mods won't load.

### Step 1b: PC game: set up Cinderbox

Cinderbox runs the PC (Steam) version of Stardew Valley on Android, with SMAPI
built in. Install it and set up your game by following Cinderbox's own
instructions, then start the game once to check SMAPI loads.

> [!NOTE]
> Cinderbox keeps everything in `StardewValley/desktop/` in your device storage:
>
> - Mods go in `StardewValley/desktop/Mods/`
> - Saves are in `StardewValley/desktop/Saves/`
> - The SMAPI log is `StardewValley/desktop/ErrorLogs/SMAPI-latest.txt`
>
> PC and Android saves use the same format, so you can copy a save folder from
> SMAPI Launcher's `Saves` folder into Cinderbox's (or back) to carry a farm
> between them.

### Step 2: Install the Dual Screen mod

1. Download `StardewDualScreenMod.zip` from the
   [Releases page](../../releases/latest).
2. Extract it and copy the `StardewDualScreenMod` folder into your game's
   `Mods` folder:
   - Android: `Android/data/abc.smapi.gameloader/files/Mods/`
   - PC (Cinderbox): `StardewValley/desktop/Mods/`

   Replace any older `StardewDualScreenMod` folder, including Joe's 2.0 mod:
   the app's new pages need this version.
3. **Optional:** install Stick Cursor the same way, from
   `StickCursor-Android.zip` or `StickCursor-PC.zip` to match your game.

### Step 3: Install Stardual Valley

Download `Stardual-Valley.apk` from the [Releases page](../../releases/latest)
and install it. If Joe's original DualScreen Mods app is installed, uninstall it
first: both use the same package name but are signed with different keys.

### Step 4: Play

1. Open **Stardual Valley** on the lower screen. It shows the title art and
   says to launch Stardew Valley.
2. Start the game on the top screen, from **SMAPI Launcher** (Android) or
   **Cinderbox** (PC). When the mod connects, the lower screen says to load a
   save.
3. Load your farm. The lower screen opens on the Overview.

To open the app on the lower screen over adb (display 4 on the Thor):

```bash
adb shell am start --display 4 -n com.wemu.dualscreenmods/com.stardual.StardualActivity
```

### Troubleshooting

- **The lower screen never says "Connected".** Make sure the
  `StardewDualScreenMod` folder is in the right `Mods` folder for your game
  (see Step 2), and on Android that you started the game from SMAPI Launcher,
  not the regular Stardew icon. Check the SMAPI log
  (`Android/data/abc.smapi.gameloader/files/ErrorLogs/SMAPI-latest.txt` on
  Android, `StardewValley/desktop/ErrorLogs/SMAPI-latest.txt` in Cinderbox):
  it should show `Listening on ws://127.0.0.1:7786` when the mod starts and
  `wemu connected` when the app connects.
- **The Overview says "Waiting for the game" or a page is empty.** You're
  probably running Joe's 2.0 mod. Install `StardewDualScreenMod.zip` from this
  repo's releases (Step 2) and restart the game.
- **The Android game sticks on a white screen.** Use SMAPI for Android 4.3.2.5
  from Nexus, and remove leftover files from other mod loaders (see Step 1a).
- **The game's HUD is missing on the top screen.** That's intended: the mod
  moves that information to the lower screen. The app's Settings page has the
  mod's options, including turning the HUD back on.

## Building the app

The app can't be compiled from this repo alone: `DualScreenActivity.kt` imports
`ThorTheme`, `ModsPage` and `SecondScreen` from shared libraries that aren't
published here. Instead, `tools/repack-apk.sh` downloads Joe's upstream v2.0
release APK as an untouched base and applies everything Stardual Valley changes:

- **`tools/smali_patches.py`** edits the upstream app's unpacked code: the new
  Overview page, the removed Backpack page, the Relationships, Gift Guide, Map,
  Crafting and Fields layouts, the tab order and the bundles fix. Each patch
  stops the build if the code it expects has moved, so a new upstream release
  can't be patched in the wrong place.
- **`overlay/src`** holds Stardual Valley's own Java classes (the start screen,
  the Overview page, relationship lines and the Lookup Anything taps). They're
  compiled against stand-in declarations of the upstream classes they call
  (`overlay/stubs`, not packaged) and added to the APK as an extra dex file.
- **`overlay/assets`** holds the Overview page's layout
  (`stardual/overview.html`).
- The app name, icon and start-screen art come from `src/main/res`.

```bash
APKTOOL_JAR=/path/to/apktool.jar \
BUILD_TOOLS=~/Library/Android/sdk/build-tools/37.0.0 \
ANDROID_JAR=~/Library/Android/sdk/platforms/android-36/android.jar \
KEYSTORE=~/stardual.keystore KEYSTORE_PASS=yourpass \
tools/repack-apk.sh
```

The result is `.repack/stardual-valley.apk`. Because it is signed with a
different key than the upstream app, uninstall the upstream app before
installing it the first time. Keep your keystore safe and out of git: every
later build must use the same key to update in place.

The mods build separately with `dotnet`; see [mods/README.md](mods/README.md).

The launcher icon is an adaptive icon
(`src/main/res/mipmap-anydpi-v26/ic_launcher.xml`). The logo is the background
layer (`ic_launcher_background.png`), scaled so the whole image fills the middle
72dp that a round launcher shows, with its edges mirrored out to the full 108dp
layer. The foreground layer (`ic_launcher_foreground.png`) is empty. The
`ic_launcher.png` files are the plain logo for older Android versions. To change
the icon, regenerate those files in every `mipmap-<density>` folder from a new
`assets/stardual-valley-logo.png` (layers: 108, 162, 216, 324 and 432 px;
plain icons: 48, 72, 96, 144 and 192 px).
