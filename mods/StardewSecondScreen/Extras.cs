using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.TokenizableStrings;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Locations;
using StardewValley.SpecialOrders;
using Object = StardewValley.Object;

namespace StardewSecondScreen;

/// <summary>
/// Stardual: more Overview rows. To do: bundles you can finish or nearly have, animal care,
/// bedtime, low energy, weekly gifts, seasonal berries, the Queen of Sauce and special orders,
/// and shops that are closed today. When you're bored: lucky days, legendary fish and
/// collection milestones. Also the short "tomorrow" preview under the weather.
/// </summary>
internal sealed partial class Overview
{
    private static bool IsSunday(int dayOfMonth) => dayOfMonth % 7 == 0;
    private static bool IsMonday(int dayOfMonth) => dayOfMonth % 7 == 1;
    private static bool IsWednesday(int dayOfMonth) => dayOfMonth % 7 == 3;

    // ---------------------------------------------------------------- tomorrow

    private List<string> TomorrowNotes(Farmer player)
    {
        var notes = new List<string>();
        int day = Game1.dayOfMonth + 1, season = Game1.seasonIndex;
        if (day > 28) { day = 1; season = (season + 1) % 4; }
        string seasonKey = Utility.getSeasonKey((Season)season);

        foreach (NPC npc in Utility.getAllVillagers())
            if (npc.Birthday_Day == day && string.Equals(npc.Birthday_Season, seasonKey, StringComparison.OrdinalIgnoreCase) && player.friendshipData.ContainsKey(npc.Name))
                notes.Add($"{npc.displayName}'s birthday");
        try
        {
            var dates = Game1.temporaryContent.Load<Dictionary<string, string>>("Data\\Festivals\\FestivalDates");
            if (dates.TryGetValue(seasonKey + day, out string? name)) notes.Add(name);
        }
        catch { }
        if (day % 7 % 5 == 0) notes.Add("Traveling Cart");
        if (IsSunday(day) && NewRecipe(player, Game1.stats.DaysPlayed + 1) is not null) notes.Add("New recipe on TV");
        if (Game1.weatherForTomorrow is "Rain" or "Storm" or "GreenRain") notes.Add("No watering");
        return notes;
    }

    // ---------------------------------------------------------------- shops

    private static string ShopHours(int closes, string who)
    {
        int now = Game1.timeOfDay;
        if (now >= closes) return $"{who} is closed now. Go tomorrow";
        if (now >= closes - 200) return $"{who} closes at {Clock(closes)}";
        return "";
    }

    private static string Clock(int time)
    {
        int h = time / 100 % 24;
        return $"{(h % 12 == 0 ? 12 : h % 12)} {(h < 12 ? "am" : "pm")}";
    }

    private void ClosedShops(Farmer player, List<Row> todo)
    {
        var closed = new List<string>();
        int d = Game1.dayOfMonth;
        if (IsWednesday(d) && !Game1.MasterPlayer.hasCompletedCommunityCenter()) closed.Add("Pierre's");
        if (d % 7 == 1 || d % 7 == 2) closed.Add("Marnie's Ranch");
        if (closed.Count == 0) return;
        todo.Add(new Row { Id = "closed", Icon = "", Title = "Closed today: " + Names(closed), Detail = closed.Contains("Pierre's") ? "JojaMart sells seeds if you need them" : "Buy animal supplies another day" });
    }

    // ---------------------------------------------------------------- bundles

    private void BundlesNearlyDone(Farmer player, List<Row> todo)
    {
        if (Game1.getLocationFromName("CommunityCenter") is not CommunityCenter cc) return;
        if (player.mailReceived.Contains("JojaMember")) return;
        var finishNow = new List<Row>();
        var oneLeft = new List<Row>();
        foreach (var pair in Game1.netWorldState.Value.BundleData)
        {
            string[] key = pair.Key.Split('/');
            if (key.Length < 2 || !int.TryParse(key[^1], out int index) || !cc.bundles.ContainsKey(index)) continue;
            if (!StardewSecondScreen.Bundles.RoomOpen(cc, Game1.player, key[0])) continue;
            bool[] done = cc.bundles[index];
            string[] parts = pair.Value.Split('/');
            if (parts.Length < 3) continue;
            string name = parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6]) ? parts[6] : parts[0];
            string[] list = parts[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int total = 0, have = 0;
            var missing = new List<(string id, string name, bool carried)>();
            for (int i = 0; i + 2 < list.Length; i += 3)
            {
                total++;
                if (i / 3 < done.Length && done[i / 3]) { have++; continue; }
                int.TryParse(list[i + 1], out int count);
                count = Math.Max(1, count);
                int.TryParse(list[i + 2], out int quality);
                if (list[i] == "-1") { missing.Add(("", count.ToString("N0") + "g", player.Money >= count)); continue; }
                string qid = ItemRegistry.QualifyItemId(list[i]) ?? "(O)" + list[i];
                int inBag = player.Items.Where(it => it != null && it.QualifiedItemId == qid && it.Quality >= quality).Sum(it => it.Stack);
                missing.Add((qid, ItemRegistry.GetData(qid)?.DisplayName ?? list[i], inBag >= count));
            }
            int required = total;
            if (parts.Length > 4 && int.TryParse(parts[4], out int pick) && pick > 0 && pick < total) required = pick;
            int left = required - have;
            if (left <= 0 || missing.Count == 0) continue;
            var carried = missing.Where(m => m.carried).ToList();
            string icon = ItemIcon(carried.FirstOrDefault().id ?? missing[0].id ?? "");
            if (carried.Count >= left)
            {
                finishNow.Add(new Row
                {
                    Id = "bundle-finish:" + index, Icon = icon, Title = $"Finish the {BundleName(name)}",
                    Detail = missing.All(m => m.id == "")
                        ? $"You have the {missing[0].name}. Pay it at the Community Center vault"
                        : $"You're carrying {Names(carried.Take(left).Select(m => m.name).ToList())}. Take it to the Community Center",
                    Tone = "warn", Page = "BUNDLES"
                });
            }
            else if (left == 1)
            {
                oneLeft.Add(new Row
                {
                    Id = "bundle-one:" + index, Icon = icon,
                    Title = missing.Count == 1 ? $"{BundleName(name)}: just {missing[0].name} left" : $"{BundleName(name)}: 1 more item",
                    Detail = missing.Count == 1 ? "Then the bundle is done" : "Any of " + Names(missing.Select(m => m.name).ToList()),
                    Page = "BUNDLES"
                });
            }
        }
        todo.AddRange(finishNow.Take(3));
        todo.AddRange(oneLeft.Take(Math.Max(0, 3 - finishNow.Count)));
    }

    private bool _hayAlertToday;

    /// <summary>Hay in every silo you own, and how much they can hold. The game keeps hay per location.</summary>
    public static (int hay, int capacity) Hay()
    {
        int hay = 0, capacity = 0;
        Utility.ForEachLocation(location =>
        {
            int cap = 0;
            try { cap = location.GetHayCapacity(); } catch { }
            if (cap > 0 || location.piecesOfHay.Value > 0)
            {
                hay += location.piecesOfHay.Value;
                capacity += cap;
            }
            return true;
        }, includeInteriors: false);
        return (hay, capacity);
    }

    private static string BundleName(string name) =>
        name.EndsWith("Bundle", StringComparison.OrdinalIgnoreCase) ? name : name + " Bundle";

    // ---------------------------------------------------------------- animals

    private void AnimalCare(Farmer player, List<Row> todo)
    {
        Farm farm = Game1.getFarm();
        int animals = 0;
        foreach (Building building in farm.buildings)
            if (building.GetIndoors() is AnimalHouse house) animals += house.animalsThatLiveHere.Count;
        if (animals > 0)
        {
            var (hay, capacity) = Hay();
            int days = hay / animals;
            // Outside winter they graze, so hay only matters on wet days and in winter.
            bool needsHay = Game1.season == Season.Winter || Game1.IsRainingHere(farm) || Game1.IsSnowingHere(farm);
            bool low = days <= 3 && hay < capacity;
            if (low) _hayAlertToday = true;
            if (low)
            {
                todo.Add(new Row
                {
                    Id = "hay", Icon = ItemIcon("(O)178"),
                    Title = hay == 0 ? "Your silos are out of hay" : "Hay is running low",
                    Detail = hay == 0 ? (needsHay ? $"{Plural(animals, "animal needs", "animals need")} feeding today. Buy hay at Marnie's or cut grass with the scythe"
                        : "Your animals graze outside today, but they'll need hay on rainy days and in winter")
                        : $"{hay} of {capacity} hay for {Plural(animals, "animal", "animals")}: about {Plural(days, "day", "days")}",
                    Tone = days <= 1 && needsHay ? "warn" : ""
                });
            }
            else if (_hayAlertToday)
            {
                // It was low earlier today and you've filled it: tick it off.
                todo.Add(new Row
                {
                    Id = "hay", Icon = ItemIcon("(O)178"), Title = "Silos stocked with hay",
                    Detail = $"{hay} of {capacity} hay: about {Plural(days, "day", "days")} for {Plural(animals, "animal", "animals")}",
                    Done = true
                });
            }
        }
        var outside = farm.animals.Values.ToList();
        bool late = Game1.timeOfDay >= 1800, wet = Game1.IsRainingHere(farm) || Game1.IsSnowingHere(farm) || Game1.season == Season.Winter;
        if (outside.Count > 0 && (late || wet))
        {
            todo.Add(new Row
            {
                Id = "animals-out", Icon = Cursor("heart", 211, 428, 7, 6),
                Title = $"Bring {Plural(outside.Count, "animal", "animals")} inside",
                Detail = (late ? "It's getting late" : "They don't like the weather") + ": " + Names(outside.Select(a => a.displayName).ToList()),
                Tone = "warn"
            });
        }
        int truffles = farm.objects.Values.Count(o => o.QualifiedItemId == "(O)430");
        if (truffles > 0)
            todo.Add(new Row { Id = "truffles", Icon = ItemIcon("(O)430"), Title = $"Collect {Plural(truffles, "truffle", "truffles")}", Detail = "Your pigs dug them up on the farm" });
    }

    // ---------------------------------------------------------------- you

    private void Bedtime(Farmer player, List<Row> todo)
    {
        int now = Game1.timeOfDay;
        if (now < 2300) return;
        todo.Add(new Row
        {
            Id = "bed", Icon = "",
            Title = now >= 2400 ? "Get to bed now" : "Head to bed soon",
            Detail = "You pass out at 2 am and lose some money", Tone = now >= 2400 ? "warn" : ""
        });
    }

    private void LowEnergy(Farmer player, List<Row> todo)
    {
        if (player.MaxStamina <= 0 || player.Stamina > player.MaxStamina * 0.2f) return;
        Object? food = player.Items.OfType<Object>()
            .Where(o => o.Edibility > 0 && o.Edibility != -300)
            .OrderByDescending(o => o.staminaRecoveredOnConsumption()).FirstOrDefault();
        todo.Add(new Row
        {
            Id = "energy", Icon = food != null ? ItemIcon(food.QualifiedItemId) : "",
            Title = food != null ? $"Low energy: eat {food.DisplayName}" : "Low energy",
            Detail = food != null ? $"+{food.staminaRecoveredOnConsumption()} energy. You have {food.Stack}" : $"{(int)player.Stamina} of {player.MaxStamina} left, and nothing to eat in your backpack",
            Tone = "warn"
        });
    }

    private void WeeklyGifts(Farmer player, List<Row> todo)
    {
        if (!IsSunday(Game1.dayOfMonth)) return;
        var open = new List<string>();
        foreach (var pair in player.friendshipData.Pairs)
        {
            Friendship f = pair.Value;
            if (f.GiftsThisWeek >= 2 || f.IsMarried() || f.Points >= 2500) continue;
            NPC? npc = Game1.getCharacterFromName(pair.Key);
            if (npc == null || !npc.CanReceiveGifts()) continue;
            open.Add(npc.displayName);
        }
        if (open.Count == 0) return;
        todo.Add(new Row
        {
            Id = "weekly-gifts", Icon = "",
            Title = "Weekly gifts reset tomorrow", Detail = $"{Plural(open.Count, "villager", "villagers")} can still get a gift this week: {Names(open)}",
            Page = "GIFT_GUIDE"
        });
    }

    // ---------------------------------------------------------------- the world

    private void Berries(Farmer player, List<Row> todo)
    {
        void Window(int season, int from, int to, string id, string name)
        {
            if (Game1.seasonIndex != season || Game1.dayOfMonth < from - 1 || Game1.dayOfMonth > to) return;
            bool before = Game1.dayOfMonth == from - 1;
            todo.Add(new Row
            {
                Id = "berries:" + name, Icon = ItemIcon(id),
                Title = before ? $"{name} season starts tomorrow" : $"{name} season",
                Detail = before ? $"Bushes all over the valley, {SeasonNames[season]} {from} to {to}" : $"Pick them from bushes through {SeasonNames[season]} {to}"
            });
        }
        Window(0, 15, 18, "(O)296", "Salmonberry");
        Window(2, 8, 11, "(O)410", "Blackberry");
    }

    /// <summary>The Queen of Sauce's new recipe on a given day, if you don't know it yet.</summary>
    private static string? NewRecipe(Farmer player, uint daysPlayed)
    {
        try
        {
            int week = (int)(daysPlayed % 224 / 7);
            if (daysPlayed % 224 == 0) week = 32;
            var channel = DataLoader.Tv_CookingChannel(Game1.temporaryContent);
            if (!channel.TryGetValue(week.ToString(), out string? entry)) return null;
            string recipe = entry.Split('/')[0];
            return player.cookingRecipes.ContainsKey(recipe) ? null : recipe;
        }
        catch { return null; }
    }

    private void QueenOfSauce(Farmer player, List<Row> todo)
    {
        if (IsSunday(Game1.dayOfMonth) && NewRecipe(player, Game1.stats.DaysPlayed) is string recipe)
            todo.Add(new Row { Id = "tv", Icon = "", Title = "Watch the Queen of Sauce", Detail = $"Learn to cook {recipe}. On the TV at home today" });
        else if (IsWednesday(Game1.dayOfMonth))
            todo.Add(new Row { Id = "tv", Icon = "", Title = "Queen of Sauce rerun today", Detail = "A chance to learn a recipe you missed. On the TV at home" });
    }

    private void SpecialOrdersBoard(Farmer player, List<Row> todo)
    {
        if (!IsMonday(Game1.dayOfMonth) || !SpecialOrder.IsSpecialOrdersBoardUnlocked()) return;
        todo.Add(new Row { Id = "orders-board", Icon = Cursor("order", 403, 496, 5, 14), Title = "New special orders", Detail = "The board outside Pierre's has new requests this week", Page = "JOURNAL" });
    }

    // ---------------------------------------------------------------- when you're bored

    private void LuckyDay(Farmer player, List<Row> bored)
    {
        double luck = player.DailyLuck;
        if (luck <= 0.02) return;
        if (_minedToday) return; // you've already been down today
        if (!player.mailReceived.Contains("landslideDone") && Game1.year == 1 && Game1.seasonIndex == 0 && Game1.dayOfMonth < 5) return;
        bool skull = player.hasSkullKey;
        bored.Add(new Row
        {
            Id = "luck", Icon = ItemIcon("(O)72"),
            Title = luck > 0.07 ? "Great luck today" : "Good luck today",
            Detail = skull ? "A good day for the Skull Cavern: more ladders and better finds" : "A good day for the mines: more ladders and better finds",
            Tag = "Luck"
        });
    }

    private sealed record Legend(string Id, int Season, string Where, int Level, bool Rain = false, string Mail = "");

    private static readonly Legend[] Legends =
    {
        new("(O)163", 0, "the Mountain lake by the log, while it rains", 10, Rain: true),
        new("(O)159", 1, "the end of the Beach's east pier", 5),
        new("(O)160", 2, "the wooden bridge north of JojaMart", 3),
        new("(O)775", 3, "the south tip of Arrowhead Island in the Forest", 6),
        new("(O)682", -1, "the Sewers", 0, Mail: "rusty"),
    };

    private void LegendaryFish(Farmer player, List<Row> bored)
    {
        foreach (Legend fish in Legends)
        {
            if (player.fishCaught.ContainsKey(fish.Id)) continue;
            if (fish.Season >= 0 && fish.Season != Game1.seasonIndex) continue;
            if (fish.Rain && !Game1.isRaining) continue;
            if (fish.Mail == "rusty" && !player.hasRustyKey) continue;
            if (player.FishingLevel < fish.Level) continue;
            string name = ItemRegistry.GetData(fish.Id)?.DisplayName ?? "a legendary fish";
            bored.Add(new Row { Id = "legend:" + fish.Id, Icon = ItemIcon(fish.Id), Title = $"Try for the {name}", Detail = $"A legendary fish. Cast at {fish.Where}", Tag = "Legendary" });
            return;
        }
    }

    private static readonly int[] MuseumMilestones = { 5, 10, 15, 20, 25, 30, 35, 40, 50, 60, 70, 80, 90, 95 };

    private void Collections(Farmer player, List<Row> bored)
    {
        if (Game1.getLocationFromName("ArchaeologyHouse") is LibraryMuseum museum)
        {
            int donated = museum.museumPieces.Count();
            int next = MuseumMilestones.FirstOrDefault(m => m > donated);
            if (next > 0 && next - donated <= 5)
                bored.Add(new Row { Id = "museum-goal", Icon = ItemIcon("(O)96"), Title = $"{Plural(next - donated, "more donation", "more donations")} to the museum", Detail = $"Gunther has a reward at {next}. You've given {donated}", Tag = "Museum" });
        }
        try
        {
            var fish = new List<string>();
            foreach (var pair in Game1.objectData)
            {
                var data = pair.Value;
                if (data.Category != Object.FishCategory || data.ExcludeFromFishingCollection) continue;
                string qid = "(O)" + pair.Key;
                if (!player.fishCaught.ContainsKey(qid)) fish.Add(TokenParser.ParseText(data.DisplayName) ?? pair.Key);
            }
            if (fish.Count > 0 && fish.Count <= 5)
                bored.Add(new Row { Id = "angler", Icon = ItemIcon("(T)FiberglassRod"), Title = $"{Plural(fish.Count, "fish", "fish")} left for Master Angler", Detail = Names(fish), Tag = "Fishing" });
        }
        catch { }
    }
}
