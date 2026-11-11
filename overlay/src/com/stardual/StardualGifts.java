package com.stardual;

import android.content.Context;
import android.graphics.Color;
import android.graphics.Typeface;
import android.os.Handler;
import android.os.Looper;
import android.text.TextUtils;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.widget.LinearLayout;
import android.widget.TextView;

import androidx.compose.foundation.layout.SizeKt;
import androidx.compose.runtime.Composer;
import androidx.compose.ui.Modifier;
import androidx.compose.ui.viewinterop.AndroidView_androidKt;

import org.json.JSONArray;
import org.json.JSONObject;

import java.lang.ref.WeakReference;
import java.lang.reflect.Method;
import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;

import kotlin.Unit;
import kotlin.jvm.functions.Function1;

/**
 * The Gifts page's "where to find them" panel: under the selected villager's name, where
 * they are right now and each place they go today. The mod sends every villager's schedule
 * as one "sdv_schedules" message (see Schedules.cs); tools/smali_patches.py draws
 * {@link #schedule} in the villager's details, above their loved gifts.
 */
public final class StardualGifts {

    private static final Handler MAIN = new Handler(Looper.getMainLooper());
    private static final List<WeakReference<LinearLayout>> VIEWS = new ArrayList<>();
    private static volatile JSONObject latest;

    private static final int INK = Color.parseColor("#2f1c11");
    private static final int SUB = Color.parseColor("#6b4d2e");
    private static final int NOW = Color.parseColor("#2f7a2a");
    private static final int RULE = Color.parseColor("#d6aa67");

    private StardualGifts() {}

    // Where loved gifts are: item ids in the backpack, and stored ones with where they are.
    private static volatile java.util.Set<String> bag = new java.util.HashSet<>();
    private static volatile java.util.Map<String, String> stored = new java.util.HashMap<>();

    /** Keeps the newest schedules and gift-places messages. */
    static void offer(String raw) {
        try {
            if (raw != null && raw.contains("\"sdv_gift_places\"")) {
                JSONObject json = new JSONObject(raw);
                if ("sdv_gift_places".equals(json.optString("type"))) {
                    java.util.Set<String> b = new java.util.HashSet<>();
                    JSONArray ids = json.optJSONArray("backpack");
                    if (ids != null) for (int i = 0; i < ids.length(); i++) b.add(ids.optString(i));
                    java.util.Map<String, String> st = new java.util.HashMap<>();
                    JSONArray list = json.optJSONArray("stored");
                    if (list != null) for (int i = 0; i < list.length(); i++) {
                        JSONObject o = list.optJSONObject(i);
                        if (o != null) st.put(o.optString("id"), o.optString("where"));
                    }
                    bag = b;
                    stored = st;
                }
                return;
            }
            if (raw == null || !raw.contains("\"sdv_schedules\"")) return;
            JSONObject json = new JSONObject(raw);
            if (!"sdv_schedules".equals(json.optString("type"))) return;
            latest = json;
            MAIN.post(StardualGifts::redrawAll);
        } catch (Throwable ignored) {
        }
    }

    /** Draws the panel for the villager in a Gifts page entry. Called from the page's smali. */
    public static void schedule(Object entry, Composer composer) {
        String name = nameOf(entry);
        Modifier width = SizeKt.fillMaxWidth(Modifier.Companion, 1f);
        AndroidView_androidKt.AndroidView(new Factory(), width, new Update(name), composer, 0, 0);
    }

    private static volatile String pending;

    /** The Gifts page opens on this villager next time it's drawn (internal or display name). */
    static void showVillager(String npc) {
        if (npc == null || npc.isEmpty()) return;
        JSONObject p = person(npc);
        pending = p != null ? p.optString("name", npc) : npc;
    }

    /**
     * The Gifts page's selected villager. tools/smali_patches.py routes the page's reads of its
     * "selected name" state through here, so a villager picked with {@link #showVillager} wins.
     */
    @SuppressWarnings("unchecked")
    public static String selected(androidx.compose.runtime.MutableState<?> state) {
        String p = pending;
        if (p != null) {
            pending = null;
            MAIN.post(() -> {
                try {
                    ((androidx.compose.runtime.MutableState<Object>) state).setValue(p);
                } catch (Throwable ignored) {
                }
            });
            return p;
        }
        Object v = state.getValue();
        return v == null ? null : v.toString();
    }

    /** The tag on a loved gift the player has: "BACKPACK" or "CHEST" (the fridge counts as a chest). */
    public static String tag(String id) {
        if (id != null && !bag.contains(id) && stored.containsKey(id)) {
            return "Fridge".equals(stored.get(id)) ? "FRIDGE" : "CHEST";
        }
        return "BACKPACK";
    }

    /** " in backpack", " in storage" or " in bag & chest", for a villager's loved gifts you have. */
    public static String where(Object entry) {
        boolean inBag = false, inStore = false;
        try {
            Object gifts = entry.getClass().getMethod("getGifts").invoke(entry);
            for (Object item : (java.util.List<?>) gifts) {
                Object carried = item.getClass().getMethod("getCarried").invoke(item);
                if (!Boolean.TRUE.equals(carried)) continue;
                String id = String.valueOf(item.getClass().getMethod("getId").invoke(item));
                if (bag.contains(id) || !stored.containsKey(id)) inBag = true;
                else inStore = true;
            }
        } catch (Throwable ignored) {
        }
        if (inBag && inStore) return " in bag & chest";
        return inStore ? " in storage" : " in backpack";
    }

    private static String nameOf(Object entry) {
        if (entry == null) return "";
        try {
            Method m = entry.getClass().getMethod("getName");
            Object v = m.invoke(entry);
            return v == null ? "" : v.toString();
        } catch (Throwable e) {
            return "";
        }
    }

    private static final class Factory implements Function1<Object, Object> {
        @Override
        public Object invoke(Object context) {
            LinearLayout outer = new LinearLayout((Context) context);
            outer.setOrientation(LinearLayout.VERTICAL);
            LinearLayout box = new LinearLayout((Context) context);
            box.setOrientation(LinearLayout.VERTICAL);
            int h = dp(outer, 14), v = dp(outer, 7);
            box.setPadding(h, v, h, v);
            outer.addView(box);
            // A line along the bottom, like the page's other dividers.
            View rule = new View((Context) context);
            rule.setBackgroundColor(RULE);
            outer.addView(rule, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(outer, 1)));
            synchronized (VIEWS) {
                VIEWS.add(new WeakReference<>(box));
            }
            return outer;
        }
    }

    private static final class Update implements Function1<Object, Object> {
        private final String name;

        Update(String name) {
            this.name = name;
        }

        @Override
        public Object invoke(Object view) {
            LinearLayout box = (LinearLayout) ((LinearLayout) view).getChildAt(0);
            box.setTag(name);
            draw(box);
            return Unit.INSTANCE;
        }
    }

    private static void redrawAll() {
        synchronized (VIEWS) {
            Iterator<WeakReference<LinearLayout>> it = VIEWS.iterator();
            while (it.hasNext()) {
                LinearLayout box = it.next().get();
                if (box == null) it.remove();
                else draw(box);
            }
        }
    }

    private static JSONObject person(String name) {
        JSONObject json = latest;
        if (json == null || name == null) return null;
        JSONArray people = json.optJSONArray("people");
        if (people == null) return null;
        for (int i = 0; i < people.length(); i++) {
            JSONObject p = people.optJSONObject(i);
            if (p != null && (name.equals(p.optString("name")) || name.equals(p.optString("npc")))) return p;
        }
        return null;
    }

    private static void draw(LinearLayout box) {
        Object tag = box.getTag();
        String name = tag == null ? "" : tag.toString();
        String key = name + "|" + (latest == null ? 0 : latest.hashCode());
        if (key.equals(box.getContentDescription())) return;
        box.setContentDescription(key);
        box.removeAllViews();

        JSONObject p = person(name);
        if (p == null) {
            box.addView(line(box, "Today's schedule isn't here yet.", SUB, false, 12));
            return;
        }

        String now = p.optString("now");
        LinearLayout head = new LinearLayout(box.getContext());
        head.setOrientation(LinearLayout.HORIZONTAL);
        head.setGravity(Gravity.CENTER_VERTICAL);
        TextView label = line(box, "NOW", SUB, true, 11);
        label.setLetterSpacing(0.06f);
        label.setMinWidth(dp(box, 66));
        head.addView(label);
        TextView where = line(box, now.isEmpty() ? "Not around" : now, NOW, true, 14);
        where.setSingleLine(true);
        where.setEllipsize(TextUtils.TruncateAt.END);
        head.addView(where, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f));
        box.addView(head);

        String note = p.optString("note");
        JSONArray stops = p.optJSONArray("stops");
        if (!note.isEmpty() || stops == null || stops.length() == 0) {
            box.addView(line(box, note.isEmpty() ? "No outings today." : note, SUB, false, 12.5f));
            return;
        }

        int time = latest.optInt("time", 600);
        int current = -1;
        for (int i = 0; i < stops.length(); i++) {
            JSONObject s = stops.optJSONObject(i);
            if (s != null && s.optInt("t") <= time) current = i;
        }
        for (int i = 0; i < stops.length(); i++) {
            JSONObject s = stops.optJSONObject(i);
            if (s == null) continue;
            LinearLayout row = new LinearLayout(box.getContext());
            row.setOrientation(LinearLayout.HORIZONTAL);
            row.setPadding(0, dp(box, 2), 0, 0);
            boolean here = i == current;
            TextView t = line(box, s.optString("time"), here ? INK : SUB, here, 12.5f);
            t.setMinWidth(dp(box, 66));
            row.addView(t);
            String text = s.optString("where");
            String doing = s.optString("doing");
            if (!doing.isEmpty()) text += " · " + doing;
            TextView w = line(box, text, INK, here, 12.5f);
            w.setSingleLine(true);
            w.setEllipsize(TextUtils.TruncateAt.END);
            row.addView(w, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f));
            if (i < current) row.setAlpha(0.5f);
            box.addView(row);
        }
    }

    private static TextView line(View parent, String text, int color, boolean bold, float sp) {
        TextView t = new TextView(parent.getContext());
        t.setText(text);
        t.setTextColor(color);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, sp);
        t.setTypeface(bold ? Typeface.DEFAULT_BOLD : Typeface.DEFAULT);
        t.setIncludeFontPadding(false);
        return t;
    }

    private static int dp(View v, float dp) {
        return Math.round(TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, dp, v.getResources().getDisplayMetrics()));
    }
}
