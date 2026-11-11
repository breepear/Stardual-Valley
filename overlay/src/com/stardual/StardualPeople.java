package com.stardual;

import android.content.Context;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.widget.LinearLayout;
import android.widget.TextView;

import androidx.compose.foundation.layout.SizeKt;
import androidx.compose.runtime.Composer;
import androidx.compose.runtime.MutableState;
import androidx.compose.runtime.SnapshotStateKt;
import androidx.compose.ui.Modifier;
import androidx.compose.ui.viewinterop.AndroidView_androidKt;

import com.wemu.session.secondscreen.StardewVillager;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

import kotlin.jvm.functions.Function1;

/**
 * The People page's sort bar: Name, Hearts (the app's own order, most hearts first) or
 * Single (villagers who are single first). tools/smali_patches.py draws {@link #bar} where the
 * page's header used to be and lists the villagers in the order it hands back.
 */
public final class StardualPeople {

    private static final String[] MODES = {"name", "hearts", "single"};
    private static final String[] LABELS = {"Name", "Hearts", "Single"};
    private static final String PREFS = "stardual", KEY = "people_sort";

    private static final int INK = Color.parseColor("#2f1c11");
    private static final int SUB = Color.parseColor("#6b4d2e");
    private static final int ROW = Color.parseColor("#f9da95");
    private static final int RULE = Color.parseColor("#d6aa67");
    private static final int CHIP = Color.parseColor("#e59436");
    private static final int CHIP_EDGE = Color.parseColor("#b06a2a");

    /** Compose state, so changing the sort redraws the list. */
    @SuppressWarnings("unchecked")
    private static final MutableState<Object> MODE =
            (MutableState<Object>) SnapshotStateKt.mutableStateOf$default(saved(), null, 2, null);

    private static List<?> lastIn;
    private static Object lastMode;
    private static List<Object> lastOut;

    private StardualPeople() {}

    /** Draws the sort bar and returns the villagers in the chosen order. */
    @SuppressWarnings("unchecked")
    public static List<?> bar(List<?> villagers, Composer composer) {
        Modifier width = SizeKt.fillMaxWidth(Modifier.Companion, 1f);
        AndroidView_androidKt.AndroidView(new Factory(), width, null, composer, 0, 4);
        Object mode = MODE.getValue();
        if (villagers == null) return null;
        if (villagers == lastIn && mode.equals(lastMode)) return lastOut;
        List<Object> out = new ArrayList<>((List<Object>) villagers);
        try {
            if ("name".equals(mode)) {
                Collections.sort(out, Comparator.comparing(v -> name(v).toLowerCase()));
            } else if ("single".equals(mode)) {
                // Stable sort: single villagers first, each group in the app's own order.
                Collections.sort(out, Comparator.comparingInt(v -> isSingle(v) ? 0 : 1));
            }
        } catch (Throwable ignored) {
        }
        lastIn = villagers;
        lastMode = mode;
        lastOut = out;
        return out;
    }

    private static String name(Object v) {
        String n = v instanceof StardewVillager ? ((StardewVillager) v).getName() : null;
        return n == null ? "" : n;
    }

    private static boolean isSingle(Object v) {
        if (!(v instanceof StardewVillager)) return false;
        return "Single".equals(StardualStatus.get(((StardewVillager) v).getNpc()));
    }

    private static String saved() {
        try {
            Context context = StardualWebPage.appContext;
            if (context != null) {
                String m = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString(KEY, "hearts");
                for (String s : MODES) if (s.equals(m)) return s;
            }
        } catch (Throwable ignored) {
        }
        return "hearts";
    }

    private static void choose(String mode) {
        MODE.setValue(mode);
        try {
            Context context = StardualWebPage.appContext;
            if (context != null) {
                SharedPreferences.Editor e = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit();
                e.putString(KEY, mode).apply();
            }
        } catch (Throwable ignored) {
        }
    }

    private static final class Factory implements Function1<Object, Object> {
        @Override
        public Object invoke(Object contextObject) {
            Context context = (Context) contextObject;
            LinearLayout outer = new LinearLayout(context);
            outer.setOrientation(LinearLayout.VERTICAL);
            LinearLayout bar = new LinearLayout(context);
            bar.setOrientation(LinearLayout.HORIZONTAL);
            bar.setGravity(Gravity.CENTER_VERTICAL);
            bar.setBackgroundColor(ROW);
            int h = dp(context, 12), v = dp(context, 5);
            bar.setPadding(h, v, h, v);
            TextView label = text(context, "SORT", SUB, true, 11);
            label.setLetterSpacing(0.06f);
            label.setPadding(0, 0, dp(context, 10), 0);
            bar.addView(label);
            List<TextView> chips = new ArrayList<>();
            for (int i = 0; i < MODES.length; i++) {
                String mode = MODES[i];
                TextView chip = text(context, LABELS[i], INK, true, 12.5f);
                chip.setGravity(Gravity.CENTER);
                chip.setPadding(dp(context, 14), dp(context, 4), dp(context, 14), dp(context, 4));
                LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                        LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT);
                lp.setMarginEnd(dp(context, 6));
                chip.setTag(mode);
                chip.setOnClickListener(view -> {
                    choose(mode);
                    paint(chips);
                });
                chips.add(chip);
                bar.addView(chip, lp);
            }
            paint(chips);
            outer.addView(bar);
            View rule = new View(context);
            rule.setBackgroundColor(RULE);
            outer.addView(rule, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(context, 1)));
            return outer;
        }
    }

    private static void paint(List<TextView> chips) {
        Object mode = MODE.getValue();
        for (TextView chip : chips) {
            boolean on = chip.getTag().equals(mode);
            GradientDrawable bg = new GradientDrawable();
            bg.setColor(on ? CHIP : Color.TRANSPARENT);
            bg.setStroke(dp(chip.getContext(), 1), on ? CHIP_EDGE : RULE);
            chip.setBackground(bg);
            chip.setTextColor(on ? INK : SUB);
        }
    }

    private static TextView text(Context context, String s, int color, boolean bold, float sp) {
        TextView t = new TextView(context);
        t.setText(s);
        t.setTextColor(color);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, sp);
        t.setTypeface(bold ? Typeface.DEFAULT_BOLD : Typeface.DEFAULT);
        t.setIncludeFontPadding(false);
        return t;
    }

    private static int dp(Context context, float dp) {
        return Math.round(TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, dp, context.getResources().getDisplayMetrics()));
    }
}
