using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.GameData.FarmAnimals;
using StardewValley.TokenizableStrings;

namespace StardewSecondScreen;

/// <summary>
/// Stardual: the Animals page. Your farm animals (not Pokemon, which have their own page):
/// who's been petted today, who has milk, wool or eggs for you, hearts and mood, plus your
/// coops and barns, hay in the silos, and your pet. Sent as one "sdv_animals_page" message.
/// </summary>
internal sealed class AnimalsPage
{
    private readonly Sprites _sprites;
    private readonly IMonitor _monitor;
    private readonly Dictionary<string, string> _icons = new();

    public AnimalsPage(Sprites sprites, IMonitor monitor)
    {
        _sprites = sprites;
        _monitor = monitor;
    }

    public string Build()
    {
        var animals = new List<string>();
        var houses = new List<string>();
        string pet = "";
        int hay = 0, capacity = 0;
        try
        {
            Farm farm = Game1.getFarm();
            foreach (FarmAnimal a in farm.getAllFarmAnimals().Where(a => !Pokemon.IsPokemon(a)).OrderBy(a => a.displayName))
            {
                FarmAnimalData? data = a.GetAnimalData();
                bool produce = a.currentProduce.Value != null;
                string collect = "";
                if (produce)
                {
                    var harvest = a.GetHarvestType();
                    string tool = data?.HarvestTool ?? "";
                    if (harvest == FarmAnimalHarvestType.HarvestWithTool)
                        collect = tool.Contains("Shears", StringComparison.OrdinalIgnoreCase) ? "Ready to shear" : "Ready to milk";
                    else if (harvest == FarmAnimalHarvestType.DigUp)
                        collect = "Found something to dig up";
                    else
                        collect = "Left something in the " + (Names.Place(a.home?.GetIndoors()) is { Length: > 0 } h ? h : "barn");
                }
                animals.Add(Json.Object(
                    Json.Str("name", a.displayName ?? a.Name ?? ""),
                    Json.Str("species", Species(a, data)),
                    Json.Str("icon", Icon(a)),
                    Json.Num("hearts", a.friendshipTowardFarmer.Value / 200),
                    Json.Num("happiness", a.happiness.Value),
                    Json.Flag("petted", a.wasPet.Value),
                    Json.Flag("baby", a.isBaby()),
                    Json.Str("collect", collect),
                    Json.Str("home", Names.Place(a.home?.GetIndoors()) is { Length: > 0 } home ? home : "Outside")));
            }

            foreach (Building b in farm.buildings)
            {
                if (b.GetIndoors() is not AnimalHouse inside || Pokemon.IsPokeHouse(b.buildingType.Value)) continue;
                int days = b.daysOfConstructionLeft.Value + b.daysUntilUpgrade.Value;
                int count = inside.animalsThatLiveHere.Count;
                int max = b.GetData()?.MaxOccupants ?? inside.animalLimit.Value;
                houses.Add(Json.Object(
                    Json.Str("name", Names.Place(inside)),
                    Json.Num("animals", count),
                    Json.Num("capacity", max),
                    Json.Num("buildingDays", days)));
            }

            (hay, capacity) = Overview.Hay();

            Pet? p = Game1.player.getPet();
            if (p != null)
            {
                bool petted = p.lastPetDay.TryGetValue(Game1.player.UniqueMultiplayerID, out int day) && day == Game1.Date.TotalDays;
                bool bowl = farm.buildings.OfType<PetBowl>().Any(b => b.watered.Value);
                pet = Json.Object(
                    Json.Str("name", p.displayName ?? p.Name ?? ""),
                    Json.Str("icon", Icon(p)),
                    Json.Flag("petted", petted),
                    Json.Flag("bowl", bowl),
                    Json.Num("hearts", p.friendshipTowardFarmer.Value / 200));
            }
        }
        catch (Exception ex)
        {
            _monitor.Log("Animals page: " + ex.Message, LogLevel.Trace);
        }
        int eaters = 0;
        try
        {
            eaters = Game1.getFarm().getAllFarmAnimals().Count(a => !Pokemon.IsPokemon(a));
        }
        catch
        {
        }
        return Json.Message("sdv_animals_page",
            Json.Array("animals", animals),
            Json.Array("houses", houses),
            Json.Num("hay", hay),
            Json.Num("hayCapacity", capacity),
            Json.Num("eaters", eaters),
            pet.Length > 0 ? "\"pet\":" + pet : "\"pet\":null");
    }

    private static string Species(FarmAnimal a, FarmAnimalData? data)
    {
        string? name = data?.DisplayName;
        if (!string.IsNullOrWhiteSpace(name))
        {
            string parsed = TokenParser.ParseText(name);
            if (!string.IsNullOrWhiteSpace(parsed) && !parsed.StartsWith("(no translation")) return parsed;
        }
        return a.displayType ?? Names.Spaced(a.type.Value ?? "");
    }

    /// <summary>The first frame of the animal's own sprite (its skin, baby or adult), as base64.</summary>
    private string Icon(Character c)
    {
        try
        {
            AnimatedSprite sprite = c.Sprite;
            if (sprite?.Texture == null) return "";
            string key = sprite.textureName.Value ?? sprite.Texture.Name ?? c.Name;
            if (_icons.TryGetValue(key, out string? cached)) return cached;
            byte[]? png = _sprites.EncodeRegion(sprite.Texture, new Rectangle(0, 0, sprite.SpriteWidth, sprite.SpriteHeight));
            string icon = png == null ? "" : Convert.ToBase64String(png);
            _icons[key] = icon;
            return icon;
        }
        catch
        {
            return "";
        }
    }
}
