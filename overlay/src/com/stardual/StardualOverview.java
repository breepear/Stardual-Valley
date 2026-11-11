package com.stardual;

import androidx.compose.runtime.Composer;

import com.wemu.session.secondscreen.SecondScreen;

import org.json.JSONArray;
import org.json.JSONObject;

/**
 * The Overview page: things to do today and ideas for when you're bored.
 *
 * The Stardew mod works the lists out and sends them as one "sdv_overview" message.
 * tools/smali_patches.py hands every message to {@link #onMessage} before the app parses it,
 * and shows {@link #page} as the first page. The page itself is a small web page
 * (assets/stardual/overview.html) that draws the lists and remembers what was ticked off.
 * The same hook feeds the Bundles page ({@link StardualBundles}).
 */
public final class StardualOverview {

    private static final StardualWebPage PAGE =
            new StardualWebPage("StardualOverview", "sdv_overview", "overview.html", new Bridge());

    private static volatile String status = "";

    private StardualOverview() {}

    /** Called with every message from the mod. Only the pages' own messages are kept. */
    public static void onMessage(String raw) {
        try {
            JSONObject json = PAGE.offer(raw);
            if (json != null) {
                status = countLeft(json);
                return;
            }
            StardualBundles.offer(raw);
        } catch (Throwable ignored) {
            // Never let this break the app's own message handling.
        }
    }

    /** The line under the tab name: how many things are left to do. */
    public static String status() {
        String line = status;
        return line == null ? "" : line;
    }

    private static String countLeft(JSONObject json) {
        JSONArray todo = json.optJSONArray("todo");
        if (todo == null) return "";
        int left = 0;
        for (int i = 0; i < todo.length(); i++) {
            JSONObject row = todo.optJSONObject(i);
            if (row != null && !"1".equals(row.optString("done"))) left++;
        }
        return left == 0 ? "All done" : left + " to do";
    }

    /** Draws the page. Called by the app's page switch, inside its own replace group. */
    public static void page(Composer composer) {
        PAGE.page(composer);
    }

    /** What the web page can ask for. */
    private static final class Bridge {
        /** Open Lookup Anything's panel for a villager on the top screen. */
        @android.webkit.JavascriptInterface
        public void lookup(String npc) {
            if (npc == null || npc.isEmpty()) return;
            try {
                SecondScreen.INSTANCE.send("{\"type\":\"lookup_npc\",\"key\":" + JSONObject.quote(npc) + "}");
            } catch (Throwable ignored) {
            }
        }
    }
}
