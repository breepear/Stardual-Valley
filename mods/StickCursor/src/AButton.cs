#if PC
using System;
using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace StickCursor;

/// <summary>
/// Keeps A working on the PC game.
///
/// A sometimes stopped doing anything until a menu was opened and closed. Two things reset
/// when a menu opens, and either can leave A dead until then:
/// - The game only reads A while its gamepad mode is on, and it turns that off whenever it
///   thinks the mouse was used (our pointer and CinderTap's taps both move the mouse).
/// - CinderTap (tap to move) has a "simulated press" and a "driving movement" state that it
///   only clears when a menu opens; while stuck, it can swallow the action.
/// When the controller's A is pressed, both are put right before the game reads the press.
/// Each press is logged so a remaining case can be traced.
/// </summary>
internal static class AButton
{
    private static IMonitor? _monitor;
    private static bool _ready;
    private static FieldInfo? _instance;
    private static FieldInfo? _session, _runtime;
    private static Type? _sessionType;
    private static MethodInfo? _cancelDrive, _clearDeferred;
    private static readonly string[] StuckFlags = { "SimPressActive", "ShortToolAimPending", "Charging", "DrivingMovement" };

    public static void Setup(IModHelper helper, IMonitor monitor)
    {
        _monitor = monitor;
        if (!helper.ModRegistry.IsLoaded("Eky.CinderTap") && AccessTools.TypeByName("CinderTap.ModEntry") == null) return;
        Type? entry = AccessTools.TypeByName("CinderTap.ModEntry");
        _instance = entry == null ? null : AccessTools.Field(entry, "Instance");
        _session = entry == null ? null : AccessTools.Field(entry, "_session");
        _runtime = entry == null ? null : AccessTools.Field(entry, "_runtime");
        _sessionType = AccessTools.TypeByName("CinderTap.GestureSession");
        _cancelDrive = _runtime == null ? null : AccessTools.Method(_runtime.FieldType, "CancelActiveDrive");
        _clearDeferred = _sessionType == null ? null : AccessTools.Method(_sessionType, "ClearDeferredToolFollowUp");
        _ready = _instance != null && _session != null && _sessionType != null;
        monitor.Log(_ready ? "Watching CinderTap so A keeps working." : "CinderTap changed; only fixing gamepad mode for A.", LogLevel.Debug);
    }

    /// <summary>Called when the controller's A is pressed, before the game reads it.</summary>
    public static void OnAPressed()
    {
        string notes = "";
        if (!Game1.options.gamepadControls)
        {
            Game1.options.gamepadControls = true;
            notes += " gamepad-mode-was-off";
        }
        try
        {
            object? mod = _ready ? _instance!.GetValue(null) : null;
            object? session = mod == null ? null : _session!.GetValue(mod);
            if (session != null)
            {
                string stuck = "";
                foreach (string name in StuckFlags)
                {
                    FieldInfo? f = AccessTools.Field(_sessionType, name);
                    if (f != null && f.GetValue(session) is true) stuck += " " + name;
                }
                if (stuck != "")
                {
                    // The same reset CinderTap does when a menu opens.
                    _clearDeferred?.Invoke(session, null);
                    Set(session, "SimPressActive", false);
                    Set(session, "SimPressForceOneTick", false);
                    Set(session, "SimPressShortUse", false);
                    Set(session, "ShortToolAimPending", false);
                    Set(session, "SimPressButton", (SButton)1000);
                    Set(session, "Charging", false);
                    Set(session, "QueueChargeStartTick", -1L);
                    Set(session, "QueueChargeFinishing", false);
                    Set(session, "CinderMarkerTarget", null);
                    if (AccessTools.Field(_sessionType, "DrivingMovement")?.GetValue(session) is true && _runtime?.GetValue(mod) is object runtime)
                        _cancelDrive?.Invoke(runtime, null);
                    notes += " cindertap-reset:" + stuck;
                }
            }
        }
        catch (Exception ex)
        {
            notes += " (could not check CinderTap: " + ex.Message + ")";
        }
        Farmer p = Game1.player;
        _monitor?.Log($"A pressed: free {Context.IsPlayerFree}, canMove {p.CanMove}, tool {p.UsingTool}, menu {Game1.activeClickableMenu?.GetType().Name ?? "none"}, "
            + $"mouseLast {Game1.lastCursorMotionWasMouse}{(notes == "" ? "" : " | fixed:" + notes)}", notes == "" ? LogLevel.Trace : LogLevel.Debug);
    }

    private static void Set(object target, string name, object? value)
    {
        FieldInfo? f = AccessTools.Field(target.GetType(), name);
        if (f != null) f.SetValue(target, value);
    }
}
#endif
