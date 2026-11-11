package com.stardual;

import android.webkit.JavascriptInterface;

import androidx.compose.runtime.Composer;

import com.wemu.session.secondscreen.SecondScreen;

import org.json.JSONObject;

/**
 * The Shortcuts page: big buttons for things that take several presses on a controller
 * (eat your best food, warp home, open a menu, pause time), and a search box that finds
 * shortcuts, backpack items, villagers and app pages.
 *
 * The mod sends the list as one "sdv_shortcuts" message; each button sends back a
 * "stardual_do" command, which the mod carries out in the game. The page is
 * assets/stardual/shortcuts.html. tools/smali_patches.py adds the tab.
 */
public final class StardualShortcuts {

    private static final StardualWebPage PAGE =
            new StardualWebPage("StardualShortcuts", "sdv_shortcuts", "shortcuts.html", new Bridge());

    private StardualShortcuts() {}

    static void offer(String raw) {
        PAGE.offer(raw);
    }

    static void page(Composer composer) {
        PAGE.page(composer);
    }

    /** What the web page can ask for. */
    private static final class Bridge {
        /** Run a shortcut in the game, like "eat:best", "use:12" or "menu:map". */
        @JavascriptInterface
        public void run(String action) {
            if (action == null || action.isEmpty()) return;
            try {
                SecondScreen.INSTANCE.send("{\"type\":\"stardual_do\",\"key\":" + JSONObject.quote(action) + "}");
            } catch (Throwable ignored) {
            }
        }

        /** Open a page of the app. */
        @JavascriptInterface
        public void openPage(String page) {
            StardualOverview.openPage(page);
        }

        /** Open Lookup Anything's panel for a villager on the top screen. */
        @JavascriptInterface
        public void lookup(String npc) {
            if (npc == null || npc.isEmpty()) return;
            try {
                SecondScreen.INSTANCE.send("{\"type\":\"lookup_npc\",\"key\":" + JSONObject.quote(npc) + "}");
            } catch (Throwable ignored) {
            }
        }
    }
}
