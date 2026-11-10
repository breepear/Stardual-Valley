# Mods

The game side of Stardual Valley. These run inside Stardew Valley through SMAPI.
The same builds work on the Android game (SMAPI Launcher) and the PC game
(Cinderbox); only Stick Cursor has a separate PC build.

| Folder | What it does | Needed? |
| --- | --- | --- |
| `StardewSecondScreen` | Reads the game and feeds the Stardual Valley app over a local socket | Yes |
| `StickCursor` | Console-style pointer and toolbar controls | Optional |

The Dual Screen mod and the app are two halves of one feature: the mod sends
messages and the app's pages draw them, and the format is only written down in
the code on each side, so they are changed and released together.

These are .NET projects, not built by Gradle:

```bash
cd StardewSecondScreen
dotnet build -c Release
```

`Pathoschild.Stardew.ModBuildConfig` needs a PC copy of Stardew Valley with
SMAPI to build against; pass `-p:GamePath=/path/to/Stardew Valley` if it can't
find one. Copy the output from `bin/Release/net6.0` into the game's `Mods`
folder on the Thor by hand.
