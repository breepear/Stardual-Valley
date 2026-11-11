using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.WorldMaps;

namespace StardewSecondScreen;

/// <summary>
/// Stardual: every villager's schedule for today, for the Gifts page. Tapping a villager
/// there shows where they are now and each place they go today, so you can find them to
/// hand over a gift. Places use the game's own world map names ("Pierre's General Store",
/// "Cindersap Forest"...). Sent as one "sdv_schedules" message.
/// </summary>
internal static class Schedules
{
    public static string Build()
    {
        var people = new List<string>();
        foreach (string key in Game1.player.friendshipData.Keys.ToList())
        {
            try
            {
                NPC npc = Game1.getCharacterFromName(key, true, false);
                if (npc != null) people.Add(Person(npc));
            }
            catch
            {
            }
        }
        return Json.Message("sdv_schedules", Json.Num("time", Game1.timeOfDay), Json.Array("people", people));
    }

    private static string Person(NPC npc)
    {
        var stops = new List<string>();
        string note = "";
        var schedule = npc.Schedule;
        if (Game1.isFestival())
        {
            note = "At today's festival.";
        }
        else if (schedule == null || schedule.Count == 0)
        {
            note = npc.getSpouse() == Game1.player ? "On your farm today." : "No outings today: they stay put.";
        }
        else
        {
            // Where they wake up, then each stop. A stop's time is when they set off.
            // Rooms in one building share its name, so a stop at the same place doing the
            // same thing as the one before is left out.
            string lastWhere = null, lastDoing = null;
            void Add(int time, string where, string doing)
            {
                if (where == lastWhere && doing == lastDoing) return;
                stops.Add(Stop(time, where, doing));
                lastWhere = where;
                lastDoing = doing;
            }
            string home = HomeName(npc);
            if (!string.IsNullOrEmpty(home) && schedule.Keys.Min() > 600)
            {
                Add(600, home, "");
            }
            foreach (var pair in schedule.OrderBy(p => p.Key))
            {
                var step = pair.Value;
                if (step == null) continue;
                Add(pair.Key, Place(step.targetLocationName, step.targetTile), Doing(step.endOfRouteBehavior));
            }
        }

        string now = "";
        GameLocation here = npc.currentLocation;
        if (here != null)
        {
            now = Place(here.Name, npc.TilePoint);
            if (npc.controller != null) now = "Walking through " + now;
        }
        return Json.Object(
            Json.Str("name", npc.displayName ?? npc.Name ?? ""),
            Json.Str("npc", npc.Name ?? ""),
            Json.Str("now", now),
            Json.Str("note", note),
            Json.Array("stops", stops));
    }

    private static string Stop(int time, string where, string doing)
    {
        return Json.Object(Json.Num("t", time), Json.Str("time", Clock(time)), Json.Str("where", where), Json.Str("doing", doing));
    }

    private static string HomeName(NPC npc)
    {
        try
        {
            string map = npc.DefaultMap;
            if (string.IsNullOrEmpty(map)) return "";
            var tile = new Point((int)(npc.DefaultPosition.X / 64f), (int)(npc.DefaultPosition.Y / 64f));
            return Place(map, tile);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>The world map's name for a tile ("Pierre's General Store"), or the location's own name.</summary>
    public static string Place(string locationName, Point tile)
    {
        if (string.IsNullOrEmpty(locationName)) return "";
        // Mods park villagers in hidden rooms ("Custom_Claire_WarpRoom") while they're away.
        if (locationName.Contains("Warp", StringComparison.OrdinalIgnoreCase)) return "Away from town";
        GameLocation location = null;
        try
        {
            location = Game1.getLocationFromName(locationName);
        }
        catch
        {
        }
        if (location != null)
        {
            try
            {
                var data = WorldMapManager.GetPositionData(location, tile);
                if (data.HasValue)
                {
                    string text = data.Value.GetScrollText();
                    if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
                }
            }
            catch
            {
            }
            try
            {
                string display = location.DisplayName;
                if (!string.IsNullOrWhiteSpace(display)) return display;
            }
            catch
            {
            }
        }
        return Spaced(locationName.StartsWith("Custom_") ? locationName.Substring(7) : locationName);
    }

    /// <summary>A few things the schedule says they do when they arrive.</summary>
    private static string Doing(string behavior)
    {
        if (string.IsNullOrEmpty(behavior)) return "";
        string b = behavior.ToLowerInvariant();
        if (b.Contains("fish")) return "fishing";
        if (b.Contains("read") || b.Contains("book")) return "reading";
        if (b.Contains("paint")) return "painting";
        if (b.Contains("sleep")) return "sleeping";
        if (b.Contains("jog") || b.Contains("run")) return "jogging";
        if (b.Contains("skate")) return "skateboarding";
        if (b.Contains("guitar") || b.Contains("flute") || b.Contains("violin") || b.Contains("music")) return "playing music";
        if (b.Contains("work") || b.Contains("counter") || b.Contains("shop")) return "working";
        if (b.Contains("cook")) return "cooking";
        if (b.Contains("game")) return "playing games";
        if (b.Contains("drink") || b.Contains("beer")) return "having a drink";
        if (b.Contains("photo") || b.Contains("camera")) return "taking photos";
        if (b.Contains("lift") || b.Contains("exercise") || b.Contains("gym")) return "working out";
        if (b.Contains("pray") || b.Contains("grave")) return "at the graveyard";
        return "";
    }

    private static string Clock(int time)
    {
        int h = time / 100 % 24, m = time % 100;
        string ampm = h < 12 ? "am" : "pm";
        int h12 = h % 12 == 0 ? 12 : h % 12;
        return $"{h12}:{m:00} {ampm}";
    }

    private static string Spaced(string name)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c == '_') { sb.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]) && name[i - 1] != '_') sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
