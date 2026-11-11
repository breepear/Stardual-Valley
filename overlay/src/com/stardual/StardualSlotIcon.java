package com.stardual;

import androidx.compose.runtime.Composer;

import com.wemu.session.secondscreen.StardewReferenceSocialKt;
import com.wemu.session.secondscreen.StardewState;

import kotlin.Unit;
import kotlin.jvm.functions.Function3;

/**
 * The small picture above a checkbox on the People page: a speech bubble over "talked today"
 * and a wrapped gift over each of the week's two gifts. The mod sends both pictures.
 */
public final class StardualSlotIcon implements Function3<Object, Object, Object, Object> {

    private final StardewState state;
    private final String label;

    public StardualSlotIcon(StardewState state, String label) {
        this.state = state;
        this.label = label;
    }

    @Override
    public Object invoke(Object scope, Object composerObject, Object changed) {
        Composer composer = (Composer) composerObject;
        // Its own group, so a picture that hasn't arrived yet doesn't upset the layout.
        composer.startReplaceGroup(0x5d1a1);
        try {
            String id = label != null && label.startsWith("Talk") ? "ui:slot_talk" : "ui:slot_gift";
            java.util.Map sprites = state.getSprites();
            Object sprite = sprites == null ? null : sprites.get(id);
            if (sprite != null) StardewReferenceSocialKt.stardualSlotSprite(sprite, composer);
        } finally {
            composer.endReplaceGroup();
        }
        return Unit.INSTANCE;
    }
}
