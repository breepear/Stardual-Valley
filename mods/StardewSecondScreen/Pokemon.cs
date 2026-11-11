using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.FarmAnimals;
using StardewValley.TokenizableStrings;
using Object = StardewValley.Object;

namespace StardewSecondScreen;

/// <summary>
/// Stardual: the Pokemon page, for Pokemon Ranch's Pokemon. Pokemon are farm animals whose
/// home is a Pokemon Coop or Pokemon Barn ("PokeCoop" / "PokeBarn"), so this works for any
/// pack that follows that naming. Sends one "sdv_pokemon" message: your Pokemon (trained
/// today, hearts, something to collect), your Pokemon buildings and their incubators, and
/// every Pokemon you can get, with where it comes from.
/// </summary>
internal sealed class Pokemon
{
    private readonly Sprites _sprites;
    private readonly IMonitor _monitor;
    private readonly Dictionary<string, string> _icons = new();

    public Pokemon(Sprites sprites, IMonitor monitor)
    {
        _sprites = sprites;
        _monitor = monitor;
    }

    /// <summary>True for a Pokemon (an animal that lives in a Pokemon Coop or Barn).</summary>
    public static bool IsPokemon(FarmAnimal animal)
    {
        try
        {
            return IsPokeHouse(animal.GetAnimalData()?.House);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsPokeHouse(string? house) => house != null && house.StartsWith("Poke", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this pack is installed at all (any Pokemon in the animal data).</summary>
    public static bool Installed()
    {
        try
        {
            return Game1.farmAnimalData != null && Game1.farmAnimalData.Values.Any(d => IsPokeHouse(d.House));
        }
        catch
        {
            return false;
        }
    }

    public string Build()
    {
        var owned = new List<string>();
        var houses = new List<string>();
        var dex = new List<string>();
        var ownedTypes = new Dictionary<string, int>();
        try
        {
            Farm farm = Game1.getFarm();
            foreach (FarmAnimal a in farm.getAllFarmAnimals().Where(IsPokemon).OrderBy(a => a.displayName))
            {
                string type = a.type.Value ?? "";
                ownedTypes[type] = ownedTypes.TryGetValue(type, out int c) ? c + 1 : 1;
                FarmAnimalData? data = a.GetAnimalData();
                bool produce = a.currentProduce.Value != null;
                owned.Add(Json.Object(
                    Json.Str("name", a.displayName ?? a.Name ?? ""),
                    Json.Str("species", Species(type, data)),
                    Json.Str("icon", AnimalIcon(type, data, a.isBaby())),
                    Json.Num("hearts", a.friendshipTowardFarmer.Value / 200),
                    Json.Num("happiness", a.happiness.Value),
                    Json.Flag("trained", a.wasPet.Value),
                    Json.Flag("baby", a.isBaby()),
                    Json.Flag("produce", produce),
                    Json.Str("home", Names.Place(a.home?.GetIndoors()) is { Length: > 0 } h ? h : "Outside")));
            }

            foreach (Building b in farm.buildings.Where(b => IsPokeHouse(b.buildingType.Value)))
            {
                string name = BuildingName(b);
                int days = b.daysOfConstructionLeft.Value + b.daysUntilUpgrade.Value;
                var animalHouse = b.GetIndoors() as AnimalHouse;
                int count = animalHouse?.animalsThatLiveHere.Count ?? 0;
                int capacity = b.GetData()?.MaxOccupants ?? animalHouse?.animalLimit.Value ?? 0;
                var incubators = new List<string>();
                GameLocation? inside = b.GetIndoors();
                if (inside != null)
                {
                    foreach (Object o in inside.objects.Values)
                    {
                        if (o.bigCraftable.Value && (o.Name ?? "").Contains("Incubator", StringComparison.OrdinalIgnoreCase))
                        {
                            Object? egg = o.heldObject.Value;
                            incubators.Add(Json.Object(
                                Json.Str("egg", egg?.DisplayName ?? ""),
                                Json.Num("minutes", egg == null ? 0 : Math.Max(0, o.MinutesUntilReady))));
                        }
                    }
                }
                houses.Add(Json.Object(
                    Json.Str("name", name),
                    Json.Num("animals", count),
                    Json.Num("capacity", capacity),
                    Json.Num("buildingDays", days),
                    Json.Array("incubators", incubators)));
            }

            // Every Pokemon, and where it comes from: bought at Marnie's, or a surprise when
            // buying another one (Pokemon Ranch's evolutions, rare forms and Ditto).
            var all = Game1.farmAnimalData.Where(p => IsPokeHouse(p.Value.House)).ToList();
            var surprise = new Dictionary<string, List<string>>();
            foreach (var pair in all)
            {
                if (pair.Value.PurchasePrice <= 0 || pair.Value.AlternatePurchaseTypes == null) continue;
                foreach (var alt in pair.Value.AlternatePurchaseTypes)
                {
                    foreach (string id in alt.AnimalIds ?? new List<string>())
                    {
                        if (id == pair.Key) continue;
                        if (!surprise.TryGetValue(id, out var from)) surprise[id] = from = new List<string>();
                        string buy = Species(pair.Key, pair.Value);
                        if (!from.Contains(buy)) from.Add(buy);
                    }
                }
            }
            var seen = new HashSet<string>();
            foreach (var pair in all.OrderBy(p => p.Value.PurchasePrice <= 0 ? 1 : 0).ThenBy(p => Species(p.Key, p.Value)))
            {
                string species = Species(pair.Key, pair.Value);
                // Male and female forms share a name: list the species once.
                if (!seen.Add(species + "|" + pair.Value.House)) continue;
                int have = all.Where(p => Species(p.Key, p.Value) == species).Sum(p => ownedTypes.TryGetValue(p.Key, out int n) ? n : 0);
                string how;
                if (pair.Value.PurchasePrice > 0) how = $"Marnie's · {pair.Value.PurchasePrice:N0}g";
                else if (surprise.TryGetValue(pair.Key, out var from)) how = from.Count > 2 ? "Surprise when buying any Pokemon" : "Surprise when buying " + string.Join(" or ", from);
                else how = "Hatches from an egg";
                dex.Add(Json.Object(
                    Json.Str("species", species),
                    Json.Str("icon", AnimalIcon(pair.Key, pair.Value, false)),
                    Json.Str("house", BuildingTypeName(pair.Value.House)),
                    Json.Str("how", how),
                    Json.Flag("buy", pair.Value.PurchasePrice > 0),
                    Json.Num("have", have)));
            }
        }
        catch (Exception ex)
        {
            _monitor.Log("Pokemon page: " + ex.Message, LogLevel.Trace);
        }
        bool hasHouse = houses.Count > 0;
        return Json.Message("sdv_pokemon",
            Json.Flag("installed", Installed()),
            Json.Flag("hasHouse", hasHouse),
            Json.Array("owned", owned),
            Json.Array("houses", houses),
            Json.Array("dex", dex));
    }

    private static string Species(string type, FarmAnimalData? data)
    {
        string? name = data?.DisplayName;
        if (!string.IsNullOrWhiteSpace(name))
        {
            string parsed = TokenParser.ParseText(name);
            if (!string.IsNullOrWhiteSpace(parsed) && !parsed.StartsWith("(no translation")) return parsed;
        }
        return Names.Spaced(type);
    }

    private static string BuildingName(Building b)
    {
        string? name = b.GetData()?.Name;
        if (!string.IsNullOrWhiteSpace(name))
        {
            string parsed = TokenParser.ParseText(name);
            if (!string.IsNullOrWhiteSpace(parsed) && !parsed.StartsWith("(no translation")) return parsed;
        }
        return Names.Spaced(b.buildingType.Value ?? "");
    }

    private static string BuildingTypeName(string? type)
    {
        if (string.IsNullOrEmpty(type)) return "";
        try
        {
            if (Game1.buildingData.TryGetValue(type, out var data) && !string.IsNullOrWhiteSpace(data.Name))
            {
                string parsed = TokenParser.ParseText(data.Name);
                if (!parsed.StartsWith("(no translation")) return parsed;
            }
        }
        catch
        {
        }
        return type.StartsWith("PokeBarn") ? "Pokemon Barn" : type.StartsWith("PokeCoop") ? "Pokemon Coop" : Names.Spaced(type);
    }

    /// <summary>The first frame of the animal's sprite (the shop picture if it has one), as base64.</summary>
    private string AnimalIcon(string type, FarmAnimalData? data, bool baby)
    {
        string key = type + (baby ? ":baby" : "");
        if (_icons.TryGetValue(key, out string? cached)) return cached;
        string icon = "";
        try
        {
            if (data != null)
            {
                string? path = baby && !string.IsNullOrEmpty(data.BabyTexture) ? data.BabyTexture : data.Texture;
                if (!string.IsNullOrEmpty(path))
                {
                    Texture2D texture = Game1.content.Load<Texture2D>(path);
                    byte[]? png = _sprites.EncodeRegion(texture, new Rectangle(0, 0, data.SpriteWidth, data.SpriteHeight));
                    if (png != null) icon = Convert.ToBase64String(png);
                }
            }
        }
        catch
        {
        }
        _icons[key] = icon;
        return icon;
    }
}
