using System;
using Object = StardewValley.Object;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Locations;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.Quests;
using StardewValley.SpecialOrders;
using StardewValley.TerrainFeatures;

namespace StardewSecondScreen;

/// <summary>
/// Stardual: builds the Overview page, the app's first page. It has two lists: things to do
/// today (birthdays, watering, harvests, machines, animals, deadlines...) and ideas for when
/// you're bored (new fish, villagers to talk to, museum and bundle items you're carrying...).
/// Everything is worked out here, where the whole game is visible, and sent as one
/// "sdv_overview" message with small pictures included, so the app only has to draw it.
/// </summary>
internal sealed partial class Overview
{
    private readonly Sprites _sprites;
    private readonly IMonitor _monitor;
    private readonly Dictionary<string, string> _icons = new();

    public Overview(Sprites sprites, IMonitor monitor)
    {
        _sprites = sprites;
        _monitor = monitor;
    }

    public void Reset() => _icons.Clear();

    private sealed class Row
    {
        public string Id = "";
        public string Icon = "";
        public string Title = "";
        public string Detail = "";
        public string Tone = "";
        public string Tag = "";
        public string Npc = "";
        public bool Done;
        public string Page = "";

        public string ToJson() => Json.Object(
            Json.Str("id", Id), Json.Str("icon", Icon), Json.Str("title", Title), Json.Str("detail", Detail),
            Json.Str("tone", Tone), Json.Str("tag", Tag), Json.Str("npc", Npc), Json.Flag("done", Done),
            Json.Str("page", Page));
    }

    public string Build()
    {
        Farmer player = Game1.player;
        _cookedAtStart ??= player.recipesCooked.Pairs.ToDictionary(p => p.Key, p => p.Value);
        var todo = new List<Row>();
        var bored = new List<Row>();
        void Try(string what, Action step)
        {
            try { step(); }
            catch (Exception ex) { _monitor.LogOnce($"Overview: could not work out {what}: {ex.Message}", LogLevel.Trace); }
        }

        Try("birthdays", () => Birthdays(player, todo));
        Try("crops", () => Crops(todo));
        Try("machines", () => Machines(todo));
        Try("animals", () => Animals(player, todo));
        Try("quests", () => Quests(player, todo));
        Try("plant", () => PlantDeadlines(player, todo));
        Try("festivals", () => Festivals(player, todo));
        Try("town", () => Town(player, todo));
        Try("closed shops", () => ClosedShops(player, todo));
        Try("bundles nearly done", () => BundlesNearlyDone(player, todo));
        Try("animal care", () => AnimalCare(player, todo));
        Try("bedtime", () => Bedtime(player, todo));
        Try("energy", () => LowEnergy(player, todo));
        Try("weekly gifts", () => WeeklyGifts(player, todo));
        Try("berries", () => Berries(player, todo));
        Try("tv", () => QueenOfSauce(player, todo));
        Try("special orders board", () => SpecialOrdersBoard(player, todo));

        Try("luck", () => LuckyDay(player, bored));
        Try("legendary fish", () => LegendaryFish(player, bored));
        Try("fish", () => Fish(player, bored));
        Try("gifts", () => GiftIdeas(player, bored));
        Try("villagers", () => Talk(player, bored));
        Try("museum", () => Museum(player, bored));
        Try("bundles", () => Bundles(player, bored));
        Try("cooking", () => Cooking(player, bored));
        Try("shipping", () => Shipping(player, bored));
        Try("mines", () => Mines(player, bored));
        Try("collections", () => Collections(player, bored));
        var tomorrowNotes = new List<string>();
        Try("tomorrow", () => tomorrowNotes = TomorrowNotes(player));

        string luck = "Neutral", luckTone = "";
        double daily = player.DailyLuck;
        if (daily > 0.07) { luck = "Great"; luckTone = "good"; }
        else if (daily > 0.02) { luck = "Good"; luckTone = "good"; }
        else if (daily < -0.07) { luck = "Very bad"; luckTone = "bad"; }
        else if (daily < -0.02) { luck = "Bad"; luckTone = "bad"; }

        string date = $"{Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)} {Game1.dayOfMonth} {Utility.getSeasonNameFromNumber(Game1.seasonIndex)}";
        return Json.Message("sdv_overview",
            Json.Str("day", $"{Constants.SaveFolderName}-{Game1.Date.TotalDays}"),
            Json.Str("date", date),
            Json.Num("year", Game1.year),
            Json.Str("tomorrow", Tomorrow()),
            Json.Str("tomorrowIcon", TomorrowIcon()),
            Json.Array("tomorrowNotes", tomorrowNotes.Select(n => "\"" + Json.Escape(n) + "\"")),
            Json.Str("checkOff", Cursor("check_off", 227, 425, 9, 9)),
            Json.Str("checkOn", Cursor("check_on", 236, 425, 9, 9)),
            Json.Str("luck", luck),
            Json.Str("luckTone", luckTone),
            Json.Str("time", Game1.getTimeOfDayString(Game1.timeOfDay)),
            Json.Array("todo", todo.Select(r => r.ToJson())),
            Json.Array("bored", bored.Select(r => r.ToJson())));
    }

    // ---------------------------------------------------------------- pictures

    private string ItemIcon(string qualifiedId)
    {
        if (string.IsNullOrEmpty(qualifiedId)) return "";
        if (_icons.TryGetValue(qualifiedId, out string? cached)) return cached;
        string icon = "";
        try
        {
            ParsedItemData data = ItemRegistry.GetData(qualifiedId);
            if (data != null)
            {
                byte[]? png = _sprites.EncodeRegion(data.GetTexture(), data.GetSourceRect());
                if (png != null) icon = Convert.ToBase64String(png);
            }
        }
        catch { }
        _icons[qualifiedId] = icon;
        return icon;
    }

    private string Region(string key, Texture2D? sheet, Rectangle source)
    {
        if (_icons.TryGetValue(key, out string? cached)) return cached;
        byte[]? png = _sprites.EncodeRegion(sheet, source);
        string icon = png == null ? "" : Convert.ToBase64String(png);
        _icons[key] = icon;
        return icon;
    }

    private string NpcIcon(NPC? npc)
    {
        if (npc?.Portrait == null) return "";
        return Region("npc:" + npc.Name, npc.Portrait, new Rectangle(0, 0, 64, 64));
    }

    private string Cursor(string key, int x, int y, int w, int h) => Region("ui:" + key, Game1.mouseCursors, new Rectangle(x, y, w, h));

    private static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    private static string Places(IEnumerable<string> names)
    {
        List<string> list = names.Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
        if (list.Count <= 2) return string.Join(" and ", list);
        return $"{list[0]}, {list[1]} and {list.Count - 2} more";
    }

    private static string Names(List<string> names)
    {
        if (names.Count == 0) return "";
        if (names.Count == 1) return names[0];
        if (names.Count <= 3) return string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
        return $"{names[0]}, {names[1]}, {names[2]} and {names.Count - 3} more";
    }

    private static string PlaceName(GameLocation location)
    {
        string name = location.DisplayName;
        if (location is Farm) return "Farm";
        if (location.Name == "FarmHouse") return "House";
        // Inside a building, the building's name; "IslandWest" becomes "Island West".
        return StardewSecondScreen.Names.Place(location);
    }

    private static string Article(string noun) =>
        noun.Length > 0 && "AEIOUaeiou".IndexOf(noun[0]) >= 0 ? "an " + noun : "a " + noun;

    // ---------------------------------------------------------------- to do

    private void Birthdays(Farmer player, List<Row> todo)
    {
        // Only villagers you've met: Sandy's birthday shouldn't show before the desert opens.
        NPC? npc = Utility.getTodaysBirthdayNPC();
        if (npc != null && player.friendshipData.ContainsKey(npc.Name))
        {
            player.friendshipData.TryGetValue(npc.Name, out Friendship? f);
            bool given = f != null && f.GiftsToday > 0;
            Item? best = player.Items.Where(i => i != null && i.canBeGivenAsGift() && npc.getGiftTasteForThisItem(i) == NPC.gift_taste_love).FirstOrDefault()
                ?? player.Items.Where(i => i != null && i.canBeGivenAsGift() && npc.getGiftTasteForThisItem(i) == NPC.gift_taste_like).FirstOrDefault();
            string detail = given ? "Gift given"
                : best != null ? $"{(npc.getGiftTasteForThisItem(best) == NPC.gift_taste_love ? "Loves" : "Likes")} {best.DisplayName}. You have {best.Stack}"
                : "You aren't carrying anything they love";
            todo.Add(new Row { Id = "bday:" + npc.Name, Icon = NpcIcon(npc), Title = $"{npc.displayName}'s birthday", Detail = detail, Tone = given ? "" : "warn", Npc = npc.Name, Done = given, Page = "GIFT_GUIDE" });
        }
        // Tomorrow's birthday, so there's time to find a gift.
        int day = Game1.dayOfMonth + 1;
        int season = Game1.seasonIndex;
        if (day > 28) { day = 1; season = (season + 1) % 4; }
        string seasonKey = Utility.getSeasonKey((Season)season);
        foreach (NPC other in Utility.getAllVillagers())
        {
            if (other.Birthday_Day == day && string.Equals(other.Birthday_Season, seasonKey, StringComparison.OrdinalIgnoreCase)
                && player.friendshipData.ContainsKey(other.Name))
            {
                todo.Add(new Row { Id = "bday-next:" + other.Name, Icon = NpcIcon(other), Title = $"{other.displayName}'s birthday is tomorrow", Detail = "Find a gift today", Npc = other.Name, Page = "GIFT_GUIDE" });
                break;
            }
        }
    }

    private void Crops(List<Row> todo)
    {
        int dry = 0, ready = 0;
        var dryPlaces = new List<string>();
        var readyPlaces = new List<string>();
        string readyId = "";
        var dryBy = new Dictionary<string, int>();
        var skippedBy = new Dictionary<string, int>();
        bool islandFarm = Game1.MasterPlayer.hasOrWillReceiveMail("Island_UpgradeHouse");
        Utility.ForEachLocation(location =>
        {
            // Only places you farm: the farm, the greenhouse, buildings and the farmhouse on
            // it, and the island farm once it's yours. Wild crops elsewhere (spring onions in
            // the forest, crops added by mods in town) aren't yours to water.
            bool yours = location.IsFarm || location.IsGreenhouse || location is FarmHouse
                || location.ParentBuilding != null || (location.Name == "IslandWest" && islandFarm);
            if (!yours)
            {
                int skipped = location.terrainFeatures.Values.OfType<HoeDirt>().Count(d => d.crop != null && !d.crop.dead.Value && !d.readyForHarvest() && d.needsWatering() && !d.isWatered());
                if (skipped > 0) skippedBy[location.NameOrUniqueName] = skipped;
                return true;
            }
            void Check(HoeDirt dirt)
            {
                Crop? crop = dirt.crop;
                if (crop == null || crop.dead.Value || crop.forageCrop.Value) return;
                if (dirt.readyForHarvest())
                {
                    ready++;
                    readyPlaces.Add(PlaceName(location));
                    if (readyId == "" && !string.IsNullOrEmpty(crop.indexOfHarvest.Value)) readyId = ItemRegistry.QualifyItemId(crop.indexOfHarvest.Value) ?? "";
                }
                else if (dirt.needsWatering() && !dirt.isWatered())
                {
                    dry++;
                    dryPlaces.Add(PlaceName(location));
                    dryBy[location.NameOrUniqueName] = dryBy.GetValueOrDefault(location.NameOrUniqueName) + 1;
                }
            }
            foreach (TerrainFeature feature in location.terrainFeatures.Values)
            {
                if (feature is HoeDirt dirt) Check(dirt);
            }
            foreach (Object obj in location.objects.Values)
            {
                if (obj is IndoorPot pot && pot.hoeDirt.Value != null) Check(pot.hoeDirt.Value);
            }
            return true;
        });
        string summary = string.Join(", ", dryBy.Select(p => $"{p.Key} {p.Value}"))
            + " | skipped: " + string.Join(", ", skippedBy.Select(p => $"{p.Key} {p.Value}"));
        if (summary != _lastDrySummary)
        {
            _lastDrySummary = summary;
            _monitor.Log($"Overview: dry crops by place: {summary}", LogLevel.Debug);
        }
        bool hadCrops = dry + ready > 0 || _hadCropsToday;
        if (dry > 0 || hadCrops)
        {
            _hadCropsToday = true;
            todo.Add(new Row
            {
                Id = "water", Icon = ItemIcon("(T)WateringCan"),
                Title = dry > 0 ? $"Water {Plural(dry, "crop", "crops")}" : "Crops watered",
                Detail = dry > 0 ? Places(dryPlaces) : Game1.isRaining ? "The rain did it for you" : "All done for today",
                Done = dry == 0, Page = "FIELDS"
            });
        }
        if (ready > 0)
        {
            todo.Add(new Row { Id = "harvest", Icon = ItemIcon(readyId), Title = $"Harvest {Plural(ready, "crop", "crops")}", Detail = "Ready in " + Places(readyPlaces), Page = "FIELDS" });
        }
    }

    private bool _hadCropsToday;
    private string? _lastDrySummary;

    /// <summary>Forget yesterday's state at the start of each day.</summary>
    public void NewDay()
    {
        _hadCropsToday = false;
        _cookedAtStart = null;
        _minedToday = false;
        _hayAlertToday = false;
    }

    // What you've done today, so "when you're bored" ideas go away once you've done them.
    private Dictionary<string, int>? _cookedAtStart;
    private bool _minedToday;

    /// <summary>Called when you arrive somewhere. Going into the mines clears the lucky-day idea.</summary>
    public void Arrived(GameLocation location)
    {
        if (location is StardewValley.Locations.MineShaft || location.Name is "Mine" or "SkullCave") _minedToday = true;
    }

    private bool CookedToday(Farmer player, string? id = null)
    {
        if (_cookedAtStart == null) return false;
        foreach (var pair in player.recipesCooked.Pairs)
        {
            if (id != null && pair.Key != id) continue;
            if (!_cookedAtStart.TryGetValue(pair.Key, out int before) || pair.Value > before) return true;
        }
        return false;
    }

    private void Machines(List<Row> todo)
    {
        var byName = new Dictionary<string, (int count, string id, List<string> places)>();
        Utility.ForEachLocation(location =>
        {
            foreach (Object obj in location.objects.Values)
            {
                if (!obj.bigCraftable.Value || !obj.readyForHarvest.Value || obj.heldObject.Value == null || obj is Chest) continue;
                string name = obj.DisplayName;
                if (!byName.TryGetValue(name, out var entry)) entry = (0, obj.QualifiedItemId, new List<string>());
                entry.count++;
                entry.places.Add(PlaceName(location));
                byName[name] = entry;
            }
            return true;
        });
        foreach (var pair in byName.OrderByDescending(p => p.Value.count).Take(4))
        {
            int n = pair.Value.count;
            todo.Add(new Row
            {
                Id = "machine:" + pair.Key, Icon = ItemIcon(pair.Value.id),
                Title = n == 1 ? $"{pair.Key} is ready" : $"{n} {StardewSecondScreen.Names.Plural(pair.Key)} are ready",
                Detail = Places(pair.Value.places), Page = "MACHINES"
            });
        }
    }

    private void Animals(Farmer player, List<Row> todo)
    {
        Farm farm = Game1.getFarm();
        List<FarmAnimal> everyone = farm.getAllFarmAnimals().ToList();
        // Pokemon (Pokemon Ranch) get their own row: you train them instead of petting them.
        List<FarmAnimal> pokemon = everyone.Where(Pokemon.IsPokemon).ToList();
        List<FarmAnimal> animals = everyone.Where(a => !Pokemon.IsPokemon(a)).ToList();
        if (pokemon.Count > 0)
        {
            List<FarmAnimal> untrained = pokemon.Where(a => !a.wasPet.Value).ToList();
            todo.Add(new Row
            {
                Id = "train-pokemon", Icon = ItemIcon("(O)bees.pkr_rarecandy"),
                Title = untrained.Count > 0 ? $"Train {untrained.Count} Pokémon" : "Pokémon trained",
                Detail = untrained.Count > 0 ? Names(untrained.Select(a => a.displayName).ToList()) : "Your team is ready",
                Done = untrained.Count == 0, Page = "POKEMON"
            });
        }
        if (animals.Count > 0)
        {
            List<FarmAnimal> unpetted = animals.Where(a => !a.wasPet.Value).ToList();
            todo.Add(new Row
            {
                Id = "pet-animals", Icon = Cursor("heart_big", 211, 428, 7, 6),
                Title = unpetted.Count > 0 ? $"Pet {Plural(unpetted.Count, "animal", "animals")}" : "Animals petted",
                Detail = unpetted.Count > 0 ? Names(unpetted.Select(a => a.displayName).ToList()) : "Everyone's happy",
                Done = unpetted.Count == 0, Page = "ANIMALS"
            });
            int produce = everyone.Count(a => a.currentProduce.Value != null && a.GetHarvestType() == StardewValley.GameData.FarmAnimals.FarmAnimalHarvestType.HarvestWithTool);
            if (produce > 0)
            {
                todo.Add(new Row { Id = "produce", Icon = ItemIcon("(T)MilkPail"), Title = $"Milk or shear {Plural(produce, "animal", "animals")}", Detail = "They have something for you", Page = "ANIMALS" });
            }
        }
        if (player.hasPet())
        {
            PetBowl? bowl = farm.buildings.OfType<PetBowl>().FirstOrDefault();
            if (bowl != null)
            {
                bool full = bowl.watered.Value;
                todo.Add(new Row { Id = "pet-bowl", Title = full ? "Pet bowl filled" : "Fill your pet's water bowl", Detail = full ? (Game1.isRaining ? "The rain filled it" : "Done") : "It's by the farmhouse", Done = full });
            }
        }
    }

    private void Quests(Farmer player, List<Row> todo)
    {
        // Everything in the journal, with what it asks you to do next.
        foreach (Quest quest in player.questLog)
        {
            if (quest.completed.Value || quest.destroy.Value) continue;
            bool timed = quest.IsTimedQuest();
            int left = timed ? quest.GetDaysLeft() : -1;
            if (timed && left <= 0) continue;
            string objective = quest.currentObjective ?? "";
            string due = !timed ? "" : left == 1 ? "Last day" : $"{left} days left";
            todo.Add(new Row
            {
                Id = "quest:" + quest.id.Value, Icon = Cursor("quest", 403, 496, 5, 14),
                Title = quest.GetName(),
                Detail = string.Join(" · ", new[] { objective, due }.Where(x => x != "")),
                Tone = timed && left <= 2 ? "warn" : "", Page = "JOURNAL"
            });
        }
        foreach (SpecialOrder order in player.team.specialOrders)
        {
            if (order.questState.Value != SpecialOrderStatus.InProgress) continue;
            int left = order.GetDaysLeft();
            todo.Add(new Row
            {
                Id = "order:" + order.questKey.Value, Icon = Cursor("order", 403, 496, 5, 14),
                Title = order.GetName(), Detail = left <= 0 ? "Ends today" : left == 1 ? "Last day" : $"{left} days left",
                Tone = left <= 2 ? "warn" : "", Page = "JOURNAL"
            });
        }
    }

    private void Town(Farmer player, List<Row> todo)
    {
        if (Game1.mailbox.Count > 0)
        {
            todo.Add(new Row { Id = "mail", Icon = Cursor("mail", 189, 423, 15, 13), Title = "Check your mailbox", Detail = Plural(Game1.mailbox.Count, "letter", "letters") });
        }
        if (player.toolBeingUpgraded.Value != null && player.daysLeftForToolUpgrade.Value <= 0)
        {
            Item tool = player.toolBeingUpgraded.Value;
            string hours = ShopHours(1600, "Clint");
            todo.Add(new Row { Id = "tool", Icon = ItemIcon(tool.QualifiedItemId), Title = $"Pick up your {tool.DisplayName}", Detail = hours == "" ? "Clint has finished it" : "Clint has finished it. " + hours, Tone = hours == "" ? "" : "warn" });
        }
        if (Game1.getLocationFromName("Forest") is Forest forest && (forest.travelingMerchantDay || forest.ShouldTravelingMerchantVisitToday()) && Game1.timeOfDay < 2000)
        {
            List<string> forBundles = new();
            try { forBundles = CartBundleItems(); } catch { }
            todo.Add(new Row
            {
                Id = "cart", Icon = Cursor("coin", 280, 412, 20, 16), Title = "Visit the Traveling Cart",
                Detail = (ShopHours(2000, "The cart") is string closing && closing != "" ? "Cindersap Forest. " + closing : "Cindersap Forest, 6 am to 8 pm")
                    + (forBundles.Count > 0 ? ". Has " + Names(forBundles) + " for your bundles" : ""),
                Tone = forBundles.Count > 0 || Game1.timeOfDay >= 1800 ? "warn" : ""
            });
        }
    }

    // ---------------------------------------------------------------- when you're bored

    private static readonly HashSet<string> FishingSpots = new() { "Town", "Mountain", "Forest", "Beach", "Woods", "Desert", "Railroad" };

    private void Fish(Farmer player, List<Row> bored)
    {
        Dictionary<string, string> fishData = DataLoader.Fish(Game1.content);
        var where = new Dictionary<string, HashSet<string>>();
        foreach (var pair in DataLoader.Locations(Game1.content))
        {
            if (!FishingSpots.Contains(pair.Key) || pair.Value.Fish == null) continue;
            if (pair.Key == "Desert" && !Game1.player.mailReceived.Contains("ccVault")) continue;
            GameLocation? location = Game1.getLocationFromName(pair.Key);
            string place = location?.DisplayName ?? pair.Key;
            foreach (SpawnFishData spawn in pair.Value.Fish)
            {
                if (spawn.IsBossFish || string.IsNullOrEmpty(spawn.ItemId)) continue;
                if (spawn.Season.HasValue && spawn.Season.Value != Game1.season) continue;
                string id = ItemRegistry.QualifyItemId(spawn.ItemId) ?? spawn.ItemId;
                if (!id.StartsWith("(O)")) continue;
                if (!where.TryGetValue(id, out var set)) where[id] = set = new HashSet<string>();
                set.Add(place);
            }
        }
        var picks = new List<(Row row, bool now)>();
        foreach (var pair in fishData)
        {
            string[] f = pair.Value.Split('/');
            if (f.Length < 8 || f[1] == "trap") continue;
            string id = "(O)" + pair.Key;
            if (player.fishCaught.ContainsKey(id) || !where.TryGetValue(id, out var places)) continue;
            if (!f[6].Split(' ').Contains(Game1.currentSeason)) continue;
            string weather = f[7];
            if (weather == "rainy" && !Game1.isRaining || weather == "sunny" && Game1.isRaining) continue;
            string[] t = f[5].Split(' ');
            bool later = false, now = false;
            string window = "";
            for (int i = 0; i + 1 < t.Length; i += 2)
            {
                if (!int.TryParse(t[i], out int start) || !int.TryParse(t[i + 1], out int end)) continue;
                if (end > Game1.timeOfDay) later = true;
                if (start <= Game1.timeOfDay && Game1.timeOfDay < end) now = true;
                if (window == "") window = start <= 600 && end >= 2600 ? "Any time" : $"{Game1.getTimeOfDayString(start)} to {Game1.getTimeOfDayString(end)}";
            }
            if (!later) continue;
            string weatherText = weather == "rainy" ? "Rain" : weather == "sunny" ? "Sun" : "";
            string detail = string.Join(" · ", new[] { Places(places), weatherText, window }.Where(s => s != ""));
            picks.Add((new Row { Id = "fish:" + pair.Key, Icon = ItemIcon(id), Title = "Catch " + Article(ItemRegistry.GetData(id)?.DisplayName ?? f[0]), Detail = detail, Tag = "New fish" }, now));
        }
        foreach (var pick in picks.OrderByDescending(p => p.now).Take(2)) bored.Add(pick.row);
    }

    private void GiftIdeas(Farmer player, List<Row> bored)
    {
        var picks = new List<(Row row, int points)>();
        foreach (var pair in player.friendshipData.Pairs)
        {
            NPC? npc = Game1.getCharacterFromName(pair.Key);
            Friendship f = pair.Value;
            if (npc == null || !npc.CanSocialize || f.GiftsToday > 0 || f.GiftsThisWeek >= 2 || npc.isBirthday()) continue;
            if (f.Points >= 2500) continue;
            Item? loved = player.Items.FirstOrDefault(i => i != null && i.canBeGivenAsGift() && npc.getGiftTasteForThisItem(i) == NPC.gift_taste_love);
            if (loved == null) continue;
            picks.Add((new Row { Id = "gift:" + npc.Name, Icon = NpcIcon(npc), Title = $"Give {npc.displayName} {loved.DisplayName}", Detail = "A gift they love, from your backpack", Tag = "Friends", Npc = npc.Name, Page = "GIFT_GUIDE" }, f.Points));
        }
        foreach (var pick in picks.OrderBy(p => p.points).Take(2)) bored.Add(pick.row);
    }

    private void Talk(Farmer player, List<Row> bored)
    {
        var names = new List<(string name, int points)>();
        foreach (var pair in player.friendshipData.Pairs)
        {
            NPC? npc = Game1.getCharacterFromName(pair.Key);
            if (npc == null || !npc.CanSocialize || pair.Value.TalkedToToday) continue;
            names.Add((npc.displayName, pair.Value.Points));
        }
        if (names.Count == 0) return;
        List<string> ordered = names.OrderBy(n => n.points).Select(n => n.name).ToList();
        bored.Add(new Row { Id = "talk", Icon = Cursor("talk", 66, 4, 14, 12), Title = $"Say hi to {Plural(ordered.Count, "villager", "villagers")}", Detail = Names(ordered), Tag = "Friends", Page = "RELATIONSHIPS" });
    }

    private void Museum(Farmer player, List<Row> bored)
    {
        if (Game1.getLocationFromName("ArchaeologyHouse") is not LibraryMuseum museum) return;
        List<Item> items = player.Items.Where(i => i != null && museum.isItemSuitableForDonation(i)).ToList();
        if (items.Count == 0) return;
        bored.Add(new Row { Id = "museum", Icon = ItemIcon(items[0].QualifiedItemId), Title = $"Donate {Plural(items.Count, "item", "items")} to the museum", Detail = Names(items.Select(i => i.DisplayName).Distinct().ToList()), Tag = "Museum" });
    }

    private void Bundles(Farmer player, List<Row> bored)
    {
        if (Game1.getLocationFromName("CommunityCenter") is not CommunityCenter cc) return;
        var carried = new List<string>();
        string firstId = "";
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
            {
                if (i / 3 < done.Length && done[i / 3] || list[i] == "-1") continue;
                string qid = ItemRegistry.QualifyItemId(list[i]) ?? ("(O)" + list[i]);
                int.TryParse(list[i + 1], out int count);
                int.TryParse(list[i + 2], out int quality);
                int have = player.Items.Where(it => it != null && it.QualifiedItemId == qid && it.Quality >= quality).Sum(it => it.Stack);
                if (have >= Math.Max(1, count))
                {
                    if (firstId == "") firstId = qid;
                    carried.Add(ItemRegistry.GetData(qid)?.DisplayName ?? list[i]);
                }
            }
        }
        carried = carried.Distinct().ToList();
        if (carried.Count == 0) return;
        bored.Add(new Row { Id = "bundles", Icon = ItemIcon(firstId), Title = $"Bring {Plural(carried.Count, "item", "items")} to the Community Center", Detail = Names(carried), Tag = "Bundles", Page = "BUNDLES" });
    }

    private void Cooking(Farmer player, List<Row> bored)
    {
        // Once you've cooked something today, the idea has been done.
        if (CookedToday(player)) return;
        IList<Item>? fridge = null;
        if (Utility.getHomeOfFarmer(player) is FarmHouse home && home.fridge.Value != null) fridge = home.fridge.Value.Items;
        foreach (string name in player.cookingRecipes.Keys)
        {
            CraftingRecipe recipe = new CraftingRecipe(name, isCookingRecipe: true);
            if (!recipe.doesFarmerHaveIngredientsInInventory(fridge)) continue;
            string id = recipe.itemToProduce.FirstOrDefault() ?? "";
            string qid = ItemRegistry.QualifyItemId(id) ?? id;
            bool first = !player.recipesCooked.ContainsKey(id);
            bored.Add(new Row { Id = "cook:" + name, Icon = ItemIcon(qid), Title = $"Cook {recipe.DisplayName}", Detail = first ? "You've never made it, and you have everything" : "You have every ingredient", Tag = "Cooking" });
            return;
        }
    }

    private void Shipping(Farmer player, List<Row> bored)
    {
        List<Item> fresh = player.Items.Where(i => i is Object o && o.countsForShippedCollection() && !player.basicShipped.ContainsKey(o.ItemId)).ToList();
        if (fresh.Count == 0) return;
        bored.Add(new Row { Id = "ship", Icon = ItemIcon(fresh[0].QualifiedItemId), Title = $"Ship {Plural(fresh.Count, "new item", "new items")}", Detail = Names(fresh.Select(i => i.DisplayName).Distinct().ToList()) + " for the collection", Tag = "Shipping" });
    }

    private void Mines(Farmer player, List<Row> bored)
    {
        int deepest = player.deepestMineLevel;
        if (deepest >= 120)
        {
            if (player.hasSkullKey) bored.Add(new Row { Id = "skull", Icon = ItemIcon("(O)72"), Title = "Dive into the Skull Cavern", Detail = "Deepest level so far: " + Math.Max(0, deepest - 120), Tag = "Mines" });
            return;
        }
        if (!player.mailReceived.Contains("landslideDone") && Game1.year == 1 && Game1.seasonIndex == 0 && Game1.dayOfMonth < 5) return;
        int next = (deepest / 5 + 1) * 5;
        string extra = deepest < 40 && next >= 40 ? "The frozen floors start there" : deepest < 80 && next >= 80 ? "The lava floors start there" : next == 120 ? "The bottom of the mines" : "Unlocks the next elevator stop";
        bored.Add(new Row { Id = "mines", Icon = ItemIcon("(T)Pickaxe"), Title = $"Reach mine level {next}", Detail = deepest <= 0 ? "You haven't been down yet" : $"You've reached level {deepest}. {extra}", Tag = "Mines" });
    }

    /// <summary>The game's own weather picture for tomorrow, from the clock's weather slot.</summary>
    private string TomorrowIcon()
    {
        int slot = Game1.weatherForTomorrow switch
        {
            "Rain" => 4, "GreenRain" => 4, "Storm" => 5, "Snow" => 7, "Festival" => 1, "Wedding" => 0,
            "Wind" => Game1.seasonIndex == 3 ? 8 : Game1.seasonIndex == 2 ? 6 : 3, _ => 2
        };
        return Cursor("weather" + slot, 317 + 12 * slot, 421, 12, 8);
    }

    private static string Tomorrow() => Game1.weatherForTomorrow switch
    {
        "Rain" => "Rain", "Storm" => "Storm", "Snow" => "Snow", "Wind" => "Windy", "Festival" => "Festival", "GreenRain" => "Green rain", "Wedding" => "Wedding", _ => "Sunny"
    };
}
