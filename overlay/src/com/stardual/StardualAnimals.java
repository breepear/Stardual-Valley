package com.stardual;

import androidx.compose.runtime.Composer;

import org.json.JSONArray;
import org.json.JSONObject;

/**
 * The Animals page: your farm animals (not Pokémon), who still needs petting, who has milk, wool
 * or eggs for you, your coops and barns, hay in the silos and your pet.
 *
 * The mod sends it as one "sdv_animals_page" message; the page is assets/stardual/animals.html.
 * tools/smali_patches.py adds the tab (after Pokémon) and sends it to {@link #page}.
 */
public final class StardualAnimals {

    private static final StardualWebPage PAGE =
            new StardualWebPage("StardualAnimals", "sdv_animals_page", "animals.html", null);

    private static volatile String status = "";

    private StardualAnimals() {}

    static void offer(String raw) {
        JSONObject json = PAGE.offer(raw);
        if (json == null) return;
        JSONArray list = json.optJSONArray("animals");
        int left = 0;
        if (list != null) {
            for (int i = 0; i < list.length(); i++) {
                JSONObject a = list.optJSONObject(i);
                if (a != null && !"1".equals(a.optString("petted"))) left++;
            }
        }
        status = list == null || list.length() == 0 ? "" : left == 0 ? "All petted" : left + " to pet";
    }

    static String status() {
        return status;
    }

    public static void page(Composer composer) {
        PAGE.page(composer);
    }
}
