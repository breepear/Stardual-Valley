package com.stardual;

import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.TextView;

import androidx.activity.ComponentActivity;
import androidx.activity.EdgeToEdge;
import androidx.activity.compose.ComponentActivityKt;

import com.wemu.dualscreen.ComposableSingletons$DualScreenActivityKt;
import com.wemu.session.secondscreen.SecondScreen;

/**
 * Stardual Valley's launcher activity.
 *
 * Shows the upstream companion UI exactly as DualScreenActivity does, then lays a
 * full-screen title image over it while no Stardew save is loaded. That covers the
 * upstream "Second screen" multi-game list, so only Stardew is ever shown. The image hides
 * itself as soon as the Stardew mod reports a loaded save, revealing the Stardew pages.
 */
public class StardualActivity extends ComponentActivity {

    private static final long POLL_MS = 400;

    private final Handler handler = new Handler(Looper.getMainLooper());
    private View cover;
    private TextView hint;
    private boolean running;

    private final Runnable poll = new Runnable() {
        @Override
        public void run() {
            refresh();
            if (running) handler.postDelayed(this, POLL_MS);
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        // Same as upstream DualScreenActivity.onCreate.
        EdgeToEdge.enable$default(this, null, null, 3, null);
        ComponentActivityKt.setContent$default(this, null,
                ComposableSingletons$DualScreenActivityKt.INSTANCE.getLambda$125444135$dualscreen_debug(), 1, null);
        addCover();
        refresh();
    }

    @Override
    protected void onResume() {
        super.onResume();
        running = true;
        handler.post(poll);
    }

    @Override
    protected void onPause() {
        running = false;
        handler.removeCallbacks(poll);
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        // Same as upstream DualScreenActivity.onDestroy.
        if (isFinishing()) SecondScreen.INSTANCE.shutdown();
        super.onDestroy();
    }

    private void addCover() {
        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(Color.BLACK);
        // Swallow touches so the list underneath can't be tapped while covered.
        root.setClickable(true);

        ImageView image = new ImageView(this);
        image.setScaleType(ImageView.ScaleType.CENTER_CROP);
        int imageId = getResources().getIdentifier("stardual_home", "drawable", getPackageName());
        if (imageId != 0) image.setImageResource(imageId);
        root.addView(image, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        hint = new TextView(this);
        hint.setTextColor(Color.WHITE);
        hint.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        hint.setTypeface(Typeface.DEFAULT_BOLD);
        hint.setGravity(Gravity.CENTER);
        hint.setLetterSpacing(0.02f);
        hint.setShadowLayer(4f, 0f, 2f, Color.argb(200, 0, 0, 0));
        int padH = dp(18), padV = dp(9);
        hint.setPadding(padH, padV, padH, padV);
        GradientDrawable pill = new GradientDrawable();
        pill.setColor(Color.argb(150, 12, 20, 40));
        pill.setCornerRadius(dp(22));
        hint.setBackground(pill);
        FrameLayout.LayoutParams hintLp = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT,
                Gravity.BOTTOM | Gravity.CENTER_HORIZONTAL);
        hintLp.bottomMargin = dp(28);
        root.addView(hint, hintLp);

        cover = root;
        addContentView(root, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
    }

    private void refresh() {
        if (cover == null) return;
        Object gameId = SecondScreen.INSTANCE.getGameId().getValue();
        boolean saveLoaded = "stardew".equals(gameId);
        cover.setVisibility(saveLoaded ? View.GONE : View.VISIBLE);
        if (!saveLoaded) {
            Object state = SecondScreen.INSTANCE.getConnectionState().getValue();
            boolean connected = state != null && state.getClass().getSimpleName().equals("Connected");
            hint.setText(connected
                    ? "Connected! Load a save in Stardew Valley to begin."
                    : "Launch Stardew Valley through SMAPI to connect Stardual.");
        }
    }

    private int dp(int v) {
        return Math.round(TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v,
                getResources().getDisplayMetrics()));
    }
}
