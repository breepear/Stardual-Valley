package com.stardual;

import android.os.Handler;
import android.os.Looper;

import androidx.compose.runtime.Composer;
import androidx.compose.runtime.MutableState;

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
    private static final Handler MAIN = new Handler(Looper.getMainLooper());
    private static volatile java.lang.ref.WeakReference<MutableState<Object>> chosenPage;

    /**
     * The app's "which page is showing" state, handed over by tools/smali_patches.py when the
     * screen is built, so a row on the Overview can open its page.
     */
    @SuppressWarnings("unchecked")
    public static void rememberPage(MutableState<?> state) {
        if (state != null) chosenPage = new java.lang.ref.WeakReference<>((MutableState<Object>) state);
    }

    /** Switches the lower screen to a page by its name (FIELDS, GIFT_GUIDE, BUNDLES...). */
    static void openPage(String name) {
        java.lang.ref.WeakReference<MutableState<Object>> ref = chosenPage;
        MutableState<Object> state = ref == null ? null : ref.get();
        if (state == null || name == null || name.isEmpty()) return;
        MAIN.post(() -> {
            try {
                state.setValue(name);
            } catch (Throwable ignored) {
            }
        });
    }

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
            StardualShortcuts.offer(raw);
            StardualGifts.offer(raw);
            StardualPokemon.offer(raw);
            StardualAnimals.offer(raw);
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

    /**
     * The app's page switch has no branch for Stardual's own pages; tools/smali_patches.py
     * sends them all here with the page being drawn.
     */
    public static void page(Object page, Composer composer) {
        // A group of its own per page, so Compose builds each page's own web view instead of
        // reusing the Overview's when you switch tabs.
        String n = name(page);
        composer.startReplaceGroup("SHORTCUTS".equals(n) ? 0x53484f52 : "POKEMON".equals(n) ? 0x504f4b45
                : "ANIMALS".equals(n) ? 0x414e494d : 0x4f564552);
        if ("SHORTCUTS".equals(n)) {
            StardualShortcuts.page(composer);
        } else if ("POKEMON".equals(n)) {
            StardualPokemon.page(composer);
        } else if ("ANIMALS".equals(n)) {
            StardualAnimals.page(composer);
        } else {
            PAGE.page(composer);
        }
        composer.endReplaceGroup();
    }

    private static String name(Object page) {
        return page instanceof Enum ? ((Enum<?>) page).name() : String.valueOf(page);
    }

    /** The tabs' order, which Settings' list of pages to show or hide follows too. */
    private static final java.util.List<String> TAB_ORDER = java.util.Arrays.asList(
            "OVERVIEW", "SHORTCUTS", "MAP", "RELATIONSHIPS", "GIFT_GUIDE", "POKEMON", "ANIMALS", "FIELDS",
            "CRAFTING", "JOURNAL", "MACHINES", "BUNDLES", "CALENDAR", "PROGRESS");

    /** Sorts Settings' list of pages to show or hide into the tabs' order. */
    public static void settingsOrder(java.util.List<Object> pages) {
        try {
            java.util.Collections.sort(pages, (a, b) -> Integer.compare(rank(a), rank(b)));
        } catch (Throwable ignored) {
        }
    }

    private static int rank(Object page) {
        int i = TAB_ORDER.indexOf(name(page));
        return i < 0 ? TAB_ORDER.size() : i;
    }

    /** Every tab's picture: an unframed game-style icon the mod sends as "ui:page_<PAGE>". */
    public static String pageIcon(Object page) {
        return "ui:page_" + name(page);
    }

    /** Overview, Shortcuts and Settings are icon-only tabs, so they take half a tab's width. */
    public static float tabWidth(Object page, float width) {
        String n = name(page);
        return "OVERVIEW".equals(n) || "SHORTCUTS".equals(n) || "SETTINGS".equals(n) ? width * 0.5f : width;
    }

    /** The line under a Stardual tab's name. */
    public static String status(Object page) {
        if ("SHORTCUTS".equals(name(page))) return "";
        if ("POKEMON".equals(name(page))) return StardualPokemon.status();
        if ("ANIMALS".equals(name(page))) return StardualAnimals.status();
        return status();
    }

    /** What the web page can ask for. */
    private static final class Bridge {
        /** Open another page of the app, like the Fields page for "Water 12 crops". */
        @android.webkit.JavascriptInterface
        public void openPage(String page) {
            StardualOverview.openPage(page);
        }

        /** The Thor's battery: "percent|charging" ("82|1"), or "" if it can't be read. */
        @android.webkit.JavascriptInterface
        public String battery() {
            try {
                android.content.Context context = StardualWebPage.appContext;
                if (context == null) return "";
                android.content.Intent intent = context.registerReceiver(null,
                        new android.content.IntentFilter(android.content.Intent.ACTION_BATTERY_CHANGED));
                if (intent == null) return "";
                int level = intent.getIntExtra(android.os.BatteryManager.EXTRA_LEVEL, -1);
                int scale = intent.getIntExtra(android.os.BatteryManager.EXTRA_SCALE, 100);
                int status = intent.getIntExtra(android.os.BatteryManager.EXTRA_STATUS, -1);
                if (level < 0 || scale <= 0) return "";
                boolean charging = status == android.os.BatteryManager.BATTERY_STATUS_CHARGING
                        || status == android.os.BatteryManager.BATTERY_STATUS_FULL;
                return Math.round(level * 100f / scale) + "|" + (charging ? "1" : "0");
            } catch (Throwable e) {
                return "";
            }
        }

        /** Pick a villager on the Gifts page, before opening it (a birthday on the to-do list). */
        @android.webkit.JavascriptInterface
        public void showVillager(String npc) {
            StardualGifts.showVillager(npc);
        }

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
