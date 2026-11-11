package com.stardual;

import androidx.compose.runtime.Composer;

/**
 * The Bundles page: the Community Center bundles you can work on this season first, with
 * what's already in your backpack picked out, and the rest under "Upcoming".
 *
 * The mod sends the list as one "sdv_bundles_page" message (it still sends "sdv_bundles"
 * too, which the app uses for the line under the tab name). tools/smali_patches.py draws
 * {@link #page} in place of the app's own bundles page. The page is assets/stardual/bundles.html.
 */
public final class StardualBundles {

    private static final StardualWebPage PAGE =
            new StardualWebPage("StardualBundles", "sdv_bundles_page", "bundles.html", null);

    private StardualBundles() {}

    static void offer(String raw) {
        PAGE.offer(raw);
    }

    /** Draws the page. Called by the app's page switch, inside its own replace group. */
    public static void page(Composer composer) {
        PAGE.page(composer);
    }
}
