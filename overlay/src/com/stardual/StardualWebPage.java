package com.stardual;

import android.annotation.SuppressLint;
import android.content.Context;
import android.graphics.Color;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import android.view.View;
import android.webkit.ConsoleMessage;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebView;
import android.webkit.WebViewClient;

import androidx.compose.foundation.layout.SizeKt;
import androidx.compose.runtime.Composer;
import androidx.compose.ui.Modifier;
import androidx.compose.ui.viewinterop.AndroidView_androidKt;

import org.json.JSONObject;

import java.lang.ref.WeakReference;
import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;

import kotlin.jvm.functions.Function1;

/**
 * A page drawn by a small web page in assets/stardual, fed by one message type from the mod.
 * The newest message is kept and handed to the page's window.stardualRender(raw) whenever it
 * changes or the page (re)loads. Used by the Overview and Bundles pages.
 */
final class StardualWebPage {

    private static final Handler MAIN = new Handler(Looper.getMainLooper());

    private final String tag;
    private final String type;
    private final String url;
    private final Object bridge;
    private final List<WeakReference<WebView>> views = new ArrayList<>();
    private volatile String latest;

    /** The app, for pages that ask about the device (the Overview's battery box). */
    static volatile Context appContext;

    StardualWebPage(String tag, String type, String asset, Object bridge) {
        this.tag = tag;
        this.type = type;
        this.url = "file:///android_asset/stardual/" + asset;
        this.bridge = bridge;
    }

    /** Keeps the message if it is this page's type. Returns the parsed message, or null. */
    JSONObject offer(String raw) {
        try {
            if (raw == null || !raw.contains("\"" + type + "\"")) return null;
            JSONObject json = new JSONObject(raw);
            if (!type.equals(json.optString("type"))) return null;
            latest = raw;
            MAIN.post(this::pushAll);
            return json;
        } catch (Throwable ignored) {
            return null;
        }
    }

    /** Draws the page. Called by the app's page switch, inside its own replace group. */
    void page(Composer composer) {
        Modifier fill = SizeKt.fillMaxSize(Modifier.Companion, 1f);
        // Mask 4: "update" is left out, so the app's default (do nothing) is used.
        AndroidView_androidKt.AndroidView(new Factory(), fill, null, composer, 0, 4);
    }

    private void pushAll() {
        Iterator<WeakReference<WebView>> it = views.iterator();
        while (it.hasNext()) {
            WebView view = it.next().get();
            if (view == null) it.remove();
            else push(view);
        }
    }

    private void push(WebView view) {
        String raw = latest;
        if (raw == null) return;
        view.evaluateJavascript("window.stardualRender && window.stardualRender(" + JSONObject.quote(raw) + ")", null);
    }

    private final class Factory implements Function1<Object, Object> {
        @SuppressLint({"SetJavaScriptEnabled", "JavascriptInterface"})
        @Override
        public Object invoke(Object context) {
            appContext = ((Context) context).getApplicationContext();
            WebView view = new WebView((Context) context);
            view.setBackgroundColor(Color.TRANSPARENT);
            view.setOverScrollMode(View.OVER_SCROLL_NEVER);
            view.setVerticalScrollBarEnabled(false);
            // So a text box on the page (the Shortcuts search) can take the keyboard.
            view.setFocusable(true);
            view.setFocusableInTouchMode(true);
            view.getSettings().setJavaScriptEnabled(true);
            view.getSettings().setDomStorageEnabled(true);
            view.getSettings().setAllowFileAccess(true);
            if (bridge != null) view.addJavascriptInterface(bridge, "Stardual");
            view.setWebViewClient(new WebViewClient() {
                @Override
                public void onPageFinished(WebView page, String loaded) {
                    Log.i(tag, "page loaded: " + loaded + " data=" + (latest != null));
                    push(page);
                }

                @Override
                public void onReceivedError(WebView page, WebResourceRequest request, WebResourceError error) {
                    Log.w(tag, "page error: " + request.getUrl() + " " + error.getDescription());
                }
            });
            view.setWebChromeClient(new WebChromeClient() {
                @Override
                public boolean onConsoleMessage(ConsoleMessage message) {
                    Log.i(tag, "page: " + message.message() + " (" + message.lineNumber() + ")");
                    return true;
                }
            });
            view.loadUrl(url);
            views.add(new WeakReference<>(view));
            return view;
        }
    }
}
