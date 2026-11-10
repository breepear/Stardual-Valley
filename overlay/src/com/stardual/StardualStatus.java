package com.stardual;

import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

import kotlinx.serialization.json.JsonPrimitive;

/**
 * Each villager's relationship line ("Single", "Married to Pierre", "Dating you"...).
 *
 * The Stardew mod sends it as a "relationship" field on every villager. The upstream app's
 * villager model has no field for it, so tools/smali_patches.py has the app's villager
 * parser hand each villager's JSON here, and the Relationships page reads it back by NPC.
 */
public final class StardualStatus {

    private static final Map<String, String> BY_NPC = new ConcurrentHashMap<>();

    private StardualStatus() {}

    /** Called with each villager's kotlinx JsonObject (a Map of String to JsonElement). */
    @SuppressWarnings("rawtypes")
    public static void remember(Object villagerJson) {
        try {
            Map json = (Map) villagerJson;
            String npc = text(json.get("npc"));
            if (npc.isEmpty()) return;
            BY_NPC.put(npc, text(json.get("relationship")));
        } catch (RuntimeException ignored) {
            // Never let this break the upstream parser.
        }
    }

    public static String get(String npc) {
        if (npc == null) return "";
        String line = BY_NPC.get(npc);
        return line == null ? "" : line;
    }

    private static String text(Object element) {
        return element instanceof JsonPrimitive ? ((JsonPrimitive) element).getContent() : "";
    }
}
