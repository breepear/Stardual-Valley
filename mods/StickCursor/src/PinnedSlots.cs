#if PC
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace StickCursor;

/// <summary>
/// Keeps So Many Toolbars' pinned items physically in their slots.
///
/// So Many Toolbars draws the toolbar as a view of the backpack: a pinned slot shows the
/// pinned item wherever it really is, and the other slots show the rest of the backpack in
/// order, skipping it. So whenever a pinned item wasn't really in its slot, everything after
/// it looked shifted and the toolbar no longer matched the backpack. Moving the pinned item
/// into its real slot makes the view and the backpack the same again. A pinned tool that has
/// been upgraded (Copper Axe to Steel Axe) is re-pinned as the new tool.
/// </summary>
internal static class PinnedSlots
{
    private const string ChannelType = "SoManyToolbars.Framework.Runtime.InventoryChannel";
    private static readonly List<WeakReference> Channels = new();
    private static FieldInfo? _pinned;
    private static PropertyInfo? _itemId;
    private static IMonitor? _monitor;
    private static PropertyInfo? _showTooltips;
    public static bool HideTooltips = true;
    private static readonly Dictionary<string, Type?> ToolTypes = new();

    public static void Setup(IModHelper helper, IMonitor monitor, Harmony harmony)
    {
        _monitor = monitor;
        if (!helper.ModRegistry.IsLoaded("ekyso.SoManyToolbars")) return;
        Type? channel = AccessTools.TypeByName(ChannelType);
        _pinned = channel == null ? null : AccessTools.Field(channel, "Pinned");
        _itemId = AccessTools.TypeByName("SoManyToolbars.Framework.Model.ItemRefModel") is Type pin
            ? AccessTools.Property(pin, "ItemId")
            : null;
        if (channel == null || _pinned == null || _itemId == null)
        {
            monitor.Log("So Many Toolbars changed; not keeping its pinned items in place.", LogLevel.Warn);
            return;
        }
        // Its item tooltip is meant to sit by the toolbar, but the patch that places it
        // doesn't load under Cinderbox, so it shows in the top-left corner instead.
        _showTooltips = AccessTools.TypeByName("SoManyToolbars.Framework.Runtime.GlobalSettings") is Type settings
            ? AccessTools.Property(settings, "ShowTooltips")
            : null;
        foreach (ConstructorInfo ctor in channel.GetConstructors())
        {
            harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(PinnedSlots), nameof(AfterChannelCreated)));
        }
        monitor.Log("Keeping So Many Toolbars' pinned items in their slots.", LogLevel.Debug);
    }

    private static void AfterChannelCreated(object __instance)
    {
        lock (Channels)
        {
            Channels.RemoveAll(w => !w.IsAlive);
            Channels.Add(new WeakReference(__instance));
        }
    }

    /// <summary>Moves each pinned item into its slot. Safe to call often.</summary>
    public static void Enforce()
    {
        if (HideTooltips && _showTooltips != null && _showTooltips.GetValue(null) is true)
            _showTooltips.SetValue(null, false);
        if (_pinned == null || _itemId == null || !Context.IsPlayerFree) return;
        Farmer player = Game1.player;
        if (player.UsingTool || player.CursorSlotItem != null) return;

        List<object> channels = new();
        lock (Channels)
        {
            foreach (WeakReference w in Channels)
                if (w.Target is object c) channels.Add(c);
        }

        foreach (object channel in channels)
        {
            if (_pinned.GetValue(channel) is not IDictionary pins) continue;
            foreach (DictionaryEntry entry in pins)
            {
                if (entry.Key is not int slot || entry.Value == null) continue;
                // Only the first toolbar row; later "pages" of the toolbar are left alone.
                if (slot < 0 || slot >= 12 || slot >= player.Items.Count) continue;
                if (_itemId.GetValue(entry.Value) is not string id) continue;

                int at = Find(player, id);
                if (at < 0)
                {
                    at = FindUpgrade(player, id);
                    if (at < 0) continue;   // not in the backpack (at Clint's, in a chest...)
                    _itemId.SetValue(entry.Value, player.Items[at].QualifiedItemId);
                    _monitor?.Log($"Re-pinned slot {slot + 1} from {id} to {player.Items[at].QualifiedItemId}.", LogLevel.Debug);
                }
                if (at == slot) continue;
                Swap(player, at, slot);
            }
        }
    }

    private static int Find(Farmer player, string id)
    {
        for (int i = 0; i < player.Items.Count; i++)
            if (player.Items[i]?.QualifiedItemId == id) return i;
        return -1;
    }

    /// <summary>A tool of the same kind as a pinned tool that's no longer in the backpack.</summary>
    private static int FindUpgrade(Farmer player, string id)
    {
        if (!id.StartsWith("(T)")) return -1;
        if (!ToolTypes.TryGetValue(id, out Type? type))
        {
            try { type = ItemRegistry.Create(id)?.GetType(); } catch { type = null; }
            ToolTypes[id] = type;
        }
        if (type == null) return -1;
        for (int i = 0; i < player.Items.Count; i++)
            if (player.Items[i] is Tool tool && tool.GetType() == type) return i;
        return -1;
    }

    private static void Swap(Farmer player, int from, int to)
    {
        int selected = player.CurrentToolIndex;
        Item? a = player.Items[from];
        Item? b = player.Items[to];
        player.Items[to] = a;
        player.Items[from] = b;
        // Keep holding the same item.
        if (selected == from) player.CurrentToolIndex = to;
        else if (selected == to) player.CurrentToolIndex = from;
        _monitor?.Log($"Moved pinned {a?.DisplayName} from slot {from + 1} back to slot {to + 1}.", LogLevel.Trace);
    }
}
#endif
