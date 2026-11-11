# Stick Cursor

Console-style controls for Stardew Valley on the AYN Thor. Optional, but it
pairs well with Stardual Valley.

| | Android (SMAPI Launcher) | PC (Cinderbox) |
| --- | --- | --- |
| Right stick | Shows and moves the pointer | Shows and moves the pointer during play, fades after 4 seconds |
| A | | Acts on the tile you're facing, or on the tile under the pointer while it shows. Never clicks the clock box or toolbar by accident, and never goes dead (it puts the game back in gamepad mode and clears stuck CinderTap presses). |
| Holding an item | | Shows where it will go (the green / red square) |
| L2 / R2 | Step through the toolbar and wrap around | |
| L1 / R1 | Jump to the first slot / 12 slots at a time | The game's own: page through your backpack rows |
| So Many Toolbars | | If installed: keeps pinned items really in their slots, and hides its item tooltip (it shows in the top-left corner under Cinderbox) |

## Building

The same `src` folder builds both versions. The PC-only code sits inside
`#if PC` (all of `AButton.cs` and `PinnedSlots.cs`).

```bash
# Android
dotnet build -c Release

# PC (Cinderbox)
dotnet build -c Release -p:DefineConstants=PC -o bin/pc
```

Copy the output `StickCursor.dll` into a `StickCursor` folder in the game's
`Mods` folder, with `manifest.json` (Android) or `manifest.pc.json` renamed to
`manifest.json` (PC).
