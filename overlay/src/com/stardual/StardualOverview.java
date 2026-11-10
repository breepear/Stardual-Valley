package com.stardual;

import android.annotation.SuppressLint;
import android.content.Context;
import android.graphics.Color;
import android.os.Handler;
import android.os.Looper;
import android.view.View;
import android.util.Log;
import android.webkit.ConsoleMessage;
import android.webkit.JavascriptInterface;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebView;
import android.webkit.WebViewClient;

import androidx.compose.foundation.layout.SizeKt;
import androidx.compose.runtime.Composer;
import androidx.compose.ui.Modifier;
import androidx.compose.ui.viewinterop.AndroidView_androidKt;

import com.wemu.session.secondscreen.SecondScreen;

import org.json.JSONArray;
import org.json.JSONObject;

import java.lang.ref.WeakReference;
import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;

import kotlin.jvm.functions.Function1;

/**
 * The Overview page: things to do today and ideas for when you're bored.
 *
 * The Stardew mod works the lists out and sends them as one "sdv_overview" message.
 * tools/smali_patches.py hands every message to {@link #onMessage} before the app parses it,
 * and shows {@link #page} as the first page. The page itself is a small web page
 * (assets/stardual/overview.html) that draws the lists and remembers what was ticked off.
 */
public final class StardualOverview {

    private static final String TAG = "StardualOverview";
    private static final String PAGE = "file:///android_asset/stardual/overview.html";
    private static final Handler MAIN = new Handler(Looper.getMainLooper());
    private static final List<WeakReference<WebView>> VIEWS = new ArrayList<>();

    private static volatile String latest;
    private static volatile String status = "";

    private StardualOverview() {}

    /** Called with every message from the mod. Only the overview message is kept. */
    public static void onMessage(String raw) {
        try {
            if (raw == null || !raw.contains("\"sdv_overview\"")) return;
            JSONObject json = new JSONObject(raw);
            if (!"sdv_overview".equals(json.optString("type"))) return;
            latest = raw;
            status = countLeft(json);
            MAIN.post(StardualOverview::pushAll);
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
        Modifier fill = SizeKt.fillMaxSize(Modifier.Companion, 1f);
        // Mask 4: "update" is left out, so the app's default (do nothing) is used.
        AndroidView_androidKt.AndroidView(new Factory(), fill, null, composer, 0, 4);
    }

    private static void pushAll() {
        Iterator<WeakReference<WebView>> it = VIEWS.iterator();
        while (it.hasNext()) {
            WebView view = it.next().get();
            if (view == null) {
                it.remove();
            } else {
                push(view);
            }
        }
    }

    private static void push(WebView view) {
        String raw = latest;
        if (raw == null) return;
        view.evaluateJavascript("window.stardualRender && window.stardualRender(" + JSONObject.quote(raw) + ")", null);
    }

    private static final class Factory implements Function1<Object, Object> {
        @SuppressLint("SetJavaScriptEnabled")
        @Override
        public Object invoke(Object context) {
            WebView view = new WebView((Context) context);
            view.setBackgroundColor(Color.TRANSPARENT);
            view.setOverScrollMode(View.OVER_SCROLL_NEVER);
            view.setVerticalScrollBarEnabled(false);
            view.getSettings().setJavaScriptEnabled(true);
            view.getSettings().setDomStorageEnabled(true);
            view.getSettings().setAllowFileAccess(true);
            view.addJavascriptInterface(new Bridge(), "Stardual");
            view.setWebViewClient(new WebViewClient() {
                @Override
                public void onPageFinished(WebView page, String url) {
                    Log.i(TAG, "page loaded: " + url + " data=" + (latest != null));
                    push(page);
                }

                @Override
                public void onReceivedError(WebView page, WebResourceRequest request, WebResourceError error) {
                    Log.w(TAG, "page error: " + request.getUrl() + " " + error.getDescription());
                }
            });
            view.setWebChromeClient(new WebChromeClient() {
                @Override
                public boolean onConsoleMessage(ConsoleMessage message) {
                    Log.i(TAG, "page: " + message.message() + " (" + message.lineNumber() + ")");
                    return true;
                }
            });
            view.loadUrl(PAGE);
            VIEWS.add(new WeakReference<>(view));
            return view;
        }
    }

    /** What the web page can ask for. */
    private static final class Bridge {
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
