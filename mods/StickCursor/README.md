# Stick Cursor

Console-style controls for Stardew Valley on the AYN Thor. Optional, but it
pairs well with Stardual Valley.

| | Android (SMAPI Launcher) | PC (Cinderbox) |
| --- | --- | --- |
| Right stick | Shows and moves the pointer | Shows and moves the pointer during play, fades after 4 seconds |
| A | | Acts on the tile you're facing, or on the tile under the pointer while it shows. Never clicks the clock box or toolbar by accident. |
| Holding an item | | Shows where it will go (the green / red square) |
| L2 / R2 | Step through the toolbar and wrap around | |
| L1 / R1 | Jump to the first slot / 12 slots at a time | |

## Building

The same `src/ModEntry.cs` builds both versions. The PC-only code sits inside
`#if PC`.

```bash
# Android
dotnet build -c Release

# PC (Cinderbox)
dotnet build -c Release -p:DefineConstants=PC -o bin/pc
```

Copy the output `StickCursor.dll` into a `StickCursor` folder in the game's
`Mods` folder, with `manifest.json` (Android) or `manifest.pc.json` renamed to
`manifest.json` (PC).
