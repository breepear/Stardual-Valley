package com.stardual;

import androidx.compose.runtime.Composer;

import org.json.JSONArray;
import org.json.JSONObject;

/**
 * The Pokémon page (for the Pokemon Ranch mod): your Pokémon and whether they're trained today,
 * your Pokémon homes and incubators, and a Pokédex of where to get each one.
 *
 * The mod sends it as one "sdv_pokemon" message; the page is assets/stardual/pokemon.html.
 * tools/smali_patches.py adds the tab (after Gifts) and sends it to {@link #page}.
 */
public final class StardualPokemon {

    private static final StardualWebPage PAGE =
            new StardualWebPage("StardualPokemon", "sdv_pokemon", "pokemon.html", null);

    private static volatile String status = "";

    private StardualPokemon() {}

    static void offer(String raw) {
        JSONObject json = PAGE.offer(raw);
        if (json == null) return;
        JSONArray owned = json.optJSONArray("owned");
        int left = 0;
        if (owned != null) {
            for (int i = 0; i < owned.length(); i++) {
                JSONObject m = owned.optJSONObject(i);
                if (m != null && !"1".equals(m.optString("trained"))) left++;
            }
        }
        status = owned == null || owned.length() == 0 ? "" : left == 0 ? "All trained" : left + " to train";
    }

    /** The line under the tab's name. */
    static String status() {
        return status;
    }

    /** Draws the page. Called by StardualOverview.page, inside its own replace group. */
    public static void page(Composer composer) {
        PAGE.page(composer);
    }
}
