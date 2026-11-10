package com.stardual;

import com.wemu.session.secondscreen.SecondScreen;

import kotlin.Unit;
import kotlin.jvm.functions.Function0;

/**
 * The tap action on a Relationships row. It asks the Stardew mod to open Lookup Anything's
 * info panel for that villager on the game screen, the same panel its controller button shows.
 *
 * tools/smali_patches.py makes each row clickable with one of these.
 */
public final class StardualLookup implements Function0<Unit> {

    private final String npc;

    public StardualLookup(String npc) {
        this.npc = npc == null ? "" : npc;
    }

    @Override
    public Unit invoke() {
        if (!npc.isEmpty()) {
            try {
                SecondScreen.INSTANCE.send("{\"type\":\"lookup_npc\",\"key\":\"" + escape(npc) + "\"}");
            } catch (RuntimeException ignored) {
                // Not connected to the game; the tap does nothing.
            }
        }
        return Unit.INSTANCE;
    }

    private static String escape(String text) {
        return text.replace("\\", "\\\\").replace("\"", "\\\"");
    }
}
