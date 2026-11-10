package com.stardual;

import androidx.compose.runtime.Composer;

import com.wemu.session.secondscreen.StardewReferenceSocialKt;
import com.wemu.session.secondscreen.StardewState;

import kotlin.Unit;
import kotlin.jvm.functions.Function3;

/**
 * Content for one row of hearts on the Relationships page. The page draws its hearts
 * as two of these (first half and second half of the hearts), one above the other.
 */
public final class StardualHeartRow implements Function3<Object, Object, Object, Object> {

    private final StardewState state;
    private final int filled;
    private final int maximum;

    public StardualHeartRow(StardewState state, int filled, int maximum) {
        this.state = state;
        this.filled = filled;
        this.maximum = maximum;
    }

    @Override
    public Object invoke(Object scope, Object composer, Object changed) {
        StardewReferenceSocialKt.stardualHeartRow(state, filled, maximum, (Composer) composer);
        return Unit.INSTANCE;
    }
}
