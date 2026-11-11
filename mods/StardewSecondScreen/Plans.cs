using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;
using StardewValley.GameData.Crops;
using StardewValley.Internal;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.TokenizableStrings;
using Object = StardewValley.Object;

namespace StardewSecondScreen;

/// <summary>
/// Stardual: Overview to-do rows that look ahead: the last day to plant seeds you own before
/// the season ends, what the Traveling Cart has for your bundles, and festival prep.
/// </summary>
internal sealed partial class Overview
{
    private static readonly string[] SeasonNames = { "Spring", "Summer", "Fall", "Winter" };

    // ---------------------------------------------------------------- planting deadlines

    private void PlantDeadlines(Farmer player, List<Row> todo)
    {
        int season = Game1.seasonIndex;
        var owned = new Dictionary<string, (int bag, int stored, Item sample)>();
        void Count(Item? item, bool inBag)
        {
            if (item is not Object obj || obj.Category != Object.SeedsCategory || !Game1.cropData.ContainsKey(obj.ItemId)) return;
            owned.TryGetValue(obj.ItemId, out var c);
            owned[obj.ItemId] = inBag ? (c.bag + obj.Stack, c.stored, c.sample ?? obj) : (c.bag, c.stored + obj.Stack, c.sample ?? obj);
        }
        foreach (Item? item in player.Items) Count(item, true);
        Utility.ForEachLocation(location =>
        {
            if (!(location.IsFarm || location is FarmHouse || location.ParentBuilding != null)) return true;
            foreach (Object obj in location.objects.Values)
                if (obj is Chest chest && chest.playerChest.Value)
                    foreach (Item? item in chest.Items) Count(item, false);
            if (location is FarmHouse house && house.fridge.Value != null)
                foreach (Item? item in house.fridge.Value.Items) Count(item, false);
            return true;
        });

        bool agriculturist = player.professions.Contains(Farmer.agriculturist);
        var rows = new List<(int last, Row row)>();
        foreach (var pair in owned)
        {
            CropData crop = Game1.cropData[pair.Key];
            var seasons = crop.Seasons.Select(s => (int)s).ToHashSet();
            // Only crops that grow now and die when the season turns.
            if (!seasons.Contains(season) || seasons.Contains((season + 1) % 4)) continue;
            int days = crop.DaysInPhase.Sum();
            if (days <= 0) continue;
            if (agriculturist) days -= (int)Math.Ceiling(days * 0.1);
            int last = 28 - days;
            int left = last - Game1.dayOfMonth;
            if (left < 0 || left > 7) continue;
            var (bag, stored, sample) = pair.Value;
            string have = bag > 0 && stored > 0 ? $"{bag} in backpack, {stored} stored"
                : bag > 0 ? $"{bag} in backpack" : $"{stored} stored";
            string name = sample.DisplayName;
            rows.Add((last, new Row
            {
                Id = "plant:" + pair.Key, Icon = ItemIcon(sample.QualifiedItemId),
                Title = left == 0 ? $"Last day to plant {name}" : $"Plant {name} by {SeasonNames[season]} {last}",
                Detail = $"Takes {days} days to grow · {have}" + (left == 1 ? " · 1 day left" : left > 1 ? $" · {left} days left" : ""),
                Tone = left <= 1 ? "warn" : "", Page = "FIELDS"
            }));
        }
        foreach (var r in rows.OrderBy(r => r.last)) todo.Add(r.row);
    }

    // ---------------------------------------------------------------- traveling cart

    /// <summary>Bundle items the Traveling Cart sells today, by name.</summary>
    private List<string> CartBundleItems()
    {
        int today = Game1.Date.TotalDays;
        if (_cartDay == today && _cartItems != null) return _cartItems;
        _cartDay = today;
        _cartItems = FindCartBundleItems();
        return _cartItems;
    }

    private int _cartDay = -1;
    private List<string>? _cartItems;

    private List<string> FindCartBundleItems()
    {
        var wanted = new HashSet<string>();
        if (Game1.getLocationFromName("CommunityCenter") is CommunityCenter cc)
        {
            foreach (var pair in Game1.netWorldState.Value.BundleData)
            {
                string[] key = pair.Key.Split('/');
                if (key.Length < 2 || !int.TryParse(key[^1], out int index) || !cc.bundles.ContainsKey(index)) continue;
                if (!StardewSecondScreen.Bundles.RoomOpen(cc, Game1.player, key[0])) continue;
                bool[] done = cc.bundles[index];
                string[] parts = pair.Value.Split('/');
                if (parts.Length < 3) continue;
                string[] list = parts[2].Split(' ');
                for (int i = 0; i + 2 < list.Length; i += 3)
                    if (!(i / 3 < done.Length && done[i / 3]) && list[i] != "-1")
                        wanted.Add(ItemRegistry.QualifyItemId(list[i]) ?? "(O)" + list[i]);
            }
        }
        var found = new List<string>();
        if (wanted.Count == 0) return found;
        foreach (ISalable sale in ShopBuilder.GetShopStock("Traveler").Keys)
            if (sale is Item item && wanted.Contains(item.QualifiedItemId)) found.Add(item.DisplayName);
        return found.Distinct().ToList();
    }

    // ---------------------------------------------------------------- festivals

    private sealed record Prep(string Id, string Icon, string Title, string Detail, bool Done = false);

    private void Festivals(Farmer player, List<Row> todo)
    {
        Dictionary<string, string> dates;
        try { dates = Game1.temporaryContent.Load<Dictionary<string, string>>("Data\\Festivals\\FestivalDates"); }
        catch { dates = new(); }
        var passive = DataLoader.PassiveFestivals(Game1.content);

        for (int ahead = 0; ahead <= 7; ahead++)
        {
            int day = Game1.dayOfMonth + ahead, season = Game1.seasonIndex;
            if (day > 28) { day -= 28; season = (season + 1) % 4; }
            string key = Utility.getSeasonKey((Season)season) + day;
            if (dates.TryGetValue(key, out string? name))
                AddFestival(player, todo, key, name, ahead);
            foreach (var pair in passive)
            {
                var f = pair.Value;
                if ((int)f.Season != season || f.StartDay != day) continue;
                if (pair.Key == "DesertFestival" && !player.mailReceived.Contains("ccVault")) continue;
                string shown = TokenParser.ParseText(f.DisplayName);
                if (string.IsNullOrWhiteSpace(shown)) shown = System.Text.RegularExpressions.Regex.Replace(pair.Key, "(?<=[a-z])(?=[A-Z])", " ");
                AddFestival(player, todo, pair.Key, shown, ahead);
            }
        }
    }

    private void AddFestival(Farmer player, List<Row> todo, string key, string name, int ahead)
    {
        var (where, preps) = FestivalPrep(player, key);
        string when = ahead == 0 ? "today" : ahead == 1 ? "tomorrow" : $"in {ahead} days";
        todo.Add(new Row
        {
            Id = "festival:" + key, Icon = Cursor("festival", 346, 392, 8, 8),
            Title = $"{name} {(ahead == 0 ? "is today" : when)}",
            Detail = ahead == 0 ? (where == "" ? "Most shops are closed" : where + ". Most shops are closed") : where,
            Tone = ahead <= 1 ? "warn" : "", Page = "CALENDAR"
        });
        foreach (Prep p in preps)
        {
            todo.Add(new Row
            {
                Id = $"prep:{key}:{p.Id}", Icon = p.Icon, Title = p.Title,
                Detail = $"For the {name} {when}" + (p.Detail == "" ? "" : ". " + p.Detail),
                Tone = !p.Done && ahead <= 1 ? "warn" : "", Done = p.Done
            });
        }
    }

    private (string where, List<Prep> preps) FestivalPrep(Farmer player, string key)
    {
        var preps = new List<Prep>();
        bool hasRod = player.Items.Any(i => i is StardewValley.Tools.FishingRod);
        Prep Rod(string why) => new("rod", ItemIcon("(T)BambooPole"), "Bring a fishing rod and bait", why, hasRod);
        switch (key)
        {
            case "spring13":
                preps.Add(new("seeds", ItemIcon("(O)745"), "Save up for Strawberry Seeds",
                    "Pierre sells them at the festival for 100g each. Plant them right away for two harvests", player.Money >= 1000));
                return ("Town, 9 am to 2 pm", preps);
            case "spring24":
            {
                NPC? partner = Utility.getAllVillagers()
                    .Where(n => n.datable.Value && player.friendshipData.TryGetValue(n.Name, out var f) && f.Points >= 1000)
                    .OrderByDescending(n => player.friendshipData[n.Name].Points).FirstOrDefault();
                preps.Add(new("partner", Cursor("heart", 211, 428, 7, 6), "Get someone to 4 hearts to dance with",
                    partner != null ? $"{partner.displayName} will dance with you" : "Nobody you can date is at 4 hearts yet", partner != null));
                return ("Cindersap Forest, 9 am to 2 pm", preps);
            }
            case "summer11":
            {
                Item? best = player.Items
                    .Where(i => i is Object o && !o.bigCraftable.Value && o.Category < 0 && o.Category != Object.SeedsCategory && o.Category != Object.fertilizerCategory && o.Category != Object.litterCategory)
                    .OrderByDescending(i => i.Quality).ThenByDescending(i => i.sellToStorePrice()).FirstOrDefault();
                bool good = best != null && best.Quality >= Object.highQuality;
                preps.Add(new("soup", best != null ? ItemIcon(best.QualifiedItemId) : ItemIcon("(O)24"), "Pick something for the potluck soup",
                    best == null ? "A gold or iridium quality crop, fish or artisan good works best"
                    : good ? $"Your best is {best.DisplayName}" : $"Your best is {best.DisplayName}. Something gold quality would be better", good));
                return ("The Beach, 9 am to 2 pm", preps);
            }
            case "summer28":
                return ("The Beach, 10 pm to midnight", preps);
            case "fall16":
            {
                int kinds = player.Items.Where(i => i is Object o && !o.bigCraftable.Value && o.Category < 0 && o.sellToStorePrice() > 0 && o.Category != Object.SeedsCategory && o.Category != Object.litterCategory)
                    .Select(i => i.QualifiedItemId).Distinct().Count();
                preps.Add(new("grange", ItemIcon("(O)276"), "Gather 9 items for your grange display",
                    $"High quality and a mix of crops, fish, artisan goods and forage score best. You're carrying {kinds} kinds", kinds >= 9));
                preps.Add(new("tokens", ItemIcon("(O)434"), "Bring money for Star Tokens", "Win them at the games and spend them on prizes"));
                return ("Town, 9 am to 3 pm", preps);
            }
            case "fall27":
                return ("Town, 10 pm to midnight", preps);
            case "winter8":
                preps.Add(Rod("There's an ice fishing contest"));
                return ("Cindersap Forest, 9 am to 2 pm", preps);
            case "winter25":
                preps.Add(new("gift", ItemIcon("(O)72"), "Bring a gift for your secret friend",
                    "You'll find out who at the feast. Something most villagers love is safe"));
                return ("Town, 9 am to 2 pm", preps);
            case "TroutDerby":
                preps.Add(Rod("Catch rainbow trout for Golden Tags"));
                return ("Cindersap Forest river", preps);
            case "SquidFest":
                preps.Add(Rod("Catch squid at the beach for prizes"));
                return ("The Beach", preps);
            case "NightMarket":
                return ("The Beach, 5 pm to 2 am", preps);
            case "DesertFestival":
                return ("The Desert, take the bus", preps);
        }
        return ("", preps);
    }
}
