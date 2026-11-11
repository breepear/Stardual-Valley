using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Locations;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Locations;

namespace StardewSecondScreen;

/// <summary>
/// Stardual: builds the Bundles page. For every Community Center bundle that isn't finished,
/// it lists the items still missing, with a picture, how many are in the backpack, and the
/// seasons each item can be found in (worked out from the game's crop, fruit tree, fish and
/// forage data; anything else counts as any season). The app splits the list into "this
/// season" and "upcoming". Sent as one "sdv_bundles_page" message.
/// </summary>
internal sealed class Bundles
{
    private static readonly string[] SeasonOrder = { "spring", "summer", "fall", "winter" };

    private readonly Sprites _sprites;
    private readonly IMonitor _monitor;
    private readonly Dictionary<string, string> _icons = new();
    private Dictionary<string, Source>? _sources;

    /// <summary>Where an item comes from and when. No seasons means any season.</summary>
    private sealed class Source
    {
        public readonly HashSet<string> Seasons = new();
        public bool AnySeason;
        public string Kind = "";
        public string Hint = "";
    }

    public Bundles(Sprites sprites, IMonitor monitor)
    {
        _sprites = sprites;
        _monitor = monitor;
    }

    public void Reset()
    {
        _icons.Clear();
        _sources = null;
    }

    public string Build()
    {
        _sources ??= LoadSources();
        Farmer player = Game1.player;
        var bundles = new List<string>();
        if (Game1.getLocationFromName("CommunityCenter") is CommunityCenter cc)
        {
            foreach (var pair in Game1.netWorldState.Value.BundleData)
            {
                try
                {
                    string? json = Bundle(cc, player, pair.Key, pair.Value);
                    if (json != null) bundles.Add(json);
                }
                catch (Exception ex)
                {
                    _monitor.LogOnce($"Bundles: could not read {pair.Key}: {ex.Message}", LogLevel.Trace);
                }
            }
        }

        string season = Game1.currentSeason ?? "spring";
        return Json.Message("sdv_bundles_page",
            Json.Str("day", $"{Constants.SaveFolderName}-{Game1.Date.TotalDays}"),
            Json.Str("season", season),
            Json.Num("dayOfMonth", Game1.dayOfMonth),
            Json.Flag("joja", player.mailReceived.Contains("JojaMember")),
            Json.Flag("ccDone", player.mailReceived.Contains("ccIsComplete") || cc_complete()),
            Json.Str("starSilver", Region("ui:star_silver", new Rectangle(338, 400, 8, 8))),
            Json.Str("starGold", Region("ui:star_gold", new Rectangle(346, 400, 8, 8))),
            Json.Str("starIridium", Region("ui:star_iridium", new Rectangle(346, 392, 8, 8))),
            Json.Array("bundles", bundles));

        static bool cc_complete() => Game1.MasterPlayer?.hasCompletedCommunityCenter() == true;
    }

    private string? Bundle(CommunityCenter cc, Farmer player, string key, string value)
    {
        string[] keyParts = key.Split('/');
        if (keyParts.Length < 2 || !int.TryParse(keyParts[^1], out int index) || !cc.bundles.ContainsKey(index)) return null;
        bool[] done = cc.bundles[index];
        string[] parts = value.Split('/');
        if (parts.Length < 3) return null;
        string name = parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6]) ? parts[6] : parts[0];
        string[] list = parts[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var items = new List<string>();
        int have = 0, total = 0;
        for (int i = 0; i + 2 < list.Length; i += 3)
        {
            int slot = i / 3;
            total++;
            if (slot < done.Length && done[slot]) { have++; continue; }
            int.TryParse(list[i + 1], out int count);
            count = Math.Max(1, count);
            int.TryParse(list[i + 2], out int quality);

            if (list[i] == "-1")
            {
                items.Add(Json.Object(
                    Json.Str("key", $"{index}:{slot}"), Json.Str("id", "gold"), Json.Str("name", count.ToString("N0") + "g"),
                    Json.Str("icon", ""), Json.Num("count", count), Json.Num("quality", 0),
                    Json.Num("inBag", Math.Min(player.Money, count)), Json.Flag("carried", player.Money >= count),
                    Json.Str("seasons", ""), Json.Str("kind", "Gold"), Json.Str("hint", "")));
                continue;
            }

            string qid = ItemRegistry.QualifyItemId(list[i]) ?? "(O)" + list[i];
            ParsedItemData? data = ItemRegistry.GetData(qid);
            int inBag = player.Items.Where(it => it != null && it.QualifiedItemId == qid && it.Quality >= quality).Sum(it => it.Stack);
            Source? source = _sources!.TryGetValue(qid, out var s) ? s : null;
            string seasons = source == null || source.AnySeason || source.Seasons.Count == 0
                ? ""
                : string.Join(" ", SeasonOrder.Where(source.Seasons.Contains));
            items.Add(Json.Object(
                Json.Str("key", $"{index}:{slot}"), Json.Str("id", qid), Json.Str("name", data?.DisplayName ?? list[i]),
                Json.Str("icon", ItemIcon(qid, data)), Json.Num("count", count), Json.Num("quality", quality),
                Json.Num("inBag", inBag), Json.Flag("carried", inBag >= count),
                Json.Str("seasons", seasons), Json.Str("kind", source?.Kind ?? ""), Json.Str("hint", source?.Hint ?? "")));
        }

        int required = total;
        if (parts.Length > 4 && int.TryParse(parts[4], out int pick) && pick > 0 && pick < total) required = pick;
        if (have >= required || items.Count == 0) return null;
        return Json.Object(
            Json.Str("id", index.ToString()), Json.Str("room", keyParts[0]), Json.Str("name", name),
            Json.Num("have", have), Json.Num("required", required), Json.Num("total", total),
            Json.Array("items", items));
    }

    // ---------------------------------------------------------------- seasons

    private Dictionary<string, Source> LoadSources()
    {
        var map = new Dictionary<string, Source>();
        Source Get(string id, string kind)
        {
            string qid = ItemRegistry.QualifyItemId(id) ?? "(O)" + id;
            if (!map.TryGetValue(qid, out var s)) map[qid] = s = new Source { Kind = kind };
            return s;
        }

        try
        {
            foreach (var crop in Game1.cropData.Values)
            {
                if (string.IsNullOrEmpty(crop.HarvestItemId)) continue;
                var s = Get(crop.HarvestItemId, "Crop");
                foreach (var season in crop.Seasons) s.Seasons.Add(season.ToString().ToLowerInvariant());
            }
        }
        catch (Exception ex) { _monitor.Log($"Bundles: crop seasons: {ex.Message}", LogLevel.Trace); }

        try
        {
            foreach (var tree in Game1.fruitTreeData.Values)
            {
                foreach (var fruit in tree.Fruit)
                {
                    if (string.IsNullOrEmpty(fruit.ItemId)) continue;
                    var s = Get(fruit.ItemId, "Fruit tree");
                    if (fruit.Season.HasValue) s.Seasons.Add(fruit.Season.Value.ToString().ToLowerInvariant());
                    else foreach (var season in tree.Seasons) s.Seasons.Add(season.ToString().ToLowerInvariant());
                }
            }
        }
        catch (Exception ex) { _monitor.Log($"Bundles: fruit tree seasons: {ex.Message}", LogLevel.Trace); }

        try
        {
            foreach (var pair in DataLoader.Fish(Game1.content))
            {
                string[] f = pair.Value.Split('/');
                if (f.Length < 2) continue;
                var s = Get(pair.Key, "Fish");
                if (f[1] == "trap")
                {
                    s.AnySeason = true;
                    s.Hint = "Crab pot";
                    continue;
                }
                if (f.Length > 7)
                {
                    foreach (string season in f[6].Split(' ', StringSplitOptions.RemoveEmptyEntries)) s.Seasons.Add(season.ToLowerInvariant());
                    s.Hint = FishHint(f[5], f[7]);
                }
            }
        }
        catch (Exception ex) { _monitor.Log($"Bundles: fish seasons: {ex.Message}", LogLevel.Trace); }

        try
        {
            var seasonCondition = new Regex(@"SEASON(?:\s+Here)?\s+([a-zA-Z ]+)", RegexOptions.IgnoreCase);
            foreach (var pair in DataLoader.Locations(Game1.content))
            {
                // Ginger Island forage is there all year; don't let it hide the mainland seasons.
                if (pair.Key.StartsWith("Island", StringComparison.OrdinalIgnoreCase)) continue;
                var spawns = new List<SpawnForageData>();
                if (pair.Value.Forage != null) spawns.AddRange(pair.Value.Forage);
                foreach (var spawn in spawns)
                {
                    if (string.IsNullOrEmpty(spawn.ItemId) || spawn.ItemId.Contains(' ')) continue;
                    var s = Get(spawn.ItemId, "Forage");
                    if (s.Kind == "") s.Kind = "Forage";
                    if (spawn.Season.HasValue) { s.Seasons.Add(spawn.Season.Value.ToString().ToLowerInvariant()); continue; }
                    Match m = seasonCondition.Match(spawn.Condition ?? "");
                    if (m.Success)
                    {
                        foreach (string season in m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            if (SeasonOrder.Contains(season.ToLowerInvariant())) s.Seasons.Add(season.ToLowerInvariant());
                    }
                    else if (s.Kind == "Forage")
                    {
                        s.AnySeason = true;
                    }
                }
            }
        }
        catch (Exception ex) { _monitor.Log($"Bundles: forage seasons: {ex.Message}", LogLevel.Trace); }

        // Winter Root and Snow Yam come from digging in winter, not from the forage lists.
        Get("412", "Forage").Seasons.Add("winter");
        Get("416", "Forage").Seasons.Add("winter");
        return map;
    }

    private static string FishHint(string times, string weather)
    {
        string when = "";
        string[] t = times.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (t.Length >= 2 && int.TryParse(t[0], out int from) && int.TryParse(t[1], out int to) && !(from <= 600 && to >= 2600))
            when = $"{Clock(from)}–{Clock(to)}";
        string rain = weather switch { "rainy" => "Rain", "sunny" => "Sun", _ => "" };
        return string.Join(", ", new[] { rain, when }.Where(x => x != ""));

        static string Clock(int time)
        {
            int h = time / 100 % 24;
            string ampm = h < 12 ? "am" : "pm";
            int h12 = h % 12 == 0 ? 12 : h % 12;
            return h12 + ampm;
        }
    }

    // ---------------------------------------------------------------- pictures

    private string ItemIcon(string qid, ParsedItemData? data)
    {
        if (_icons.TryGetValue(qid, out string? cached)) return cached;
        string icon = "";
        try
        {
            if (data != null)
            {
                byte[]? png = _sprites.EncodeRegion(data.GetTexture(), data.GetSourceRect());
                if (png != null) icon = Convert.ToBase64String(png);
            }
        }
        catch { }
        _icons[qid] = icon;
        return icon;
    }

    private string Region(string key, Rectangle source)
    {
        if (_icons.TryGetValue(key, out string? cached)) return cached;
        byte[]? png = _sprites.EncodeRegion(Game1.mouseCursors, source);
        string icon = png == null ? "" : Convert.ToBase64String(png);
        _icons[key] = icon;
        return icon;
    }
}
