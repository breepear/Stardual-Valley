using System.Text.RegularExpressions;
using StardewValley;
using StardewValley.TokenizableStrings;

namespace StardewSecondScreen;

/// <summary>
/// Stardual: readable names for places and things. Places inside farm buildings use the
/// building's own name ("Pokemon Coop", "Big Barn") instead of the inside's internal name
/// ("Coop0de2e1ed-3aab-..."), and names without a display name get spaces ("Farm Cave").
/// </summary>
internal static class Names
{
    public static string Place(GameLocation? location)
    {
        if (location == null) return "";
        try
        {
            var building = location.ParentBuilding;
            if (building != null)
            {
                string? name = building.GetData()?.Name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    string parsed = TokenParser.ParseText(name);
                    if (!string.IsNullOrWhiteSpace(parsed) && !parsed.StartsWith("(no translation")) return parsed;
                }
                return Spaced(building.buildingType.Value ?? "Building");
            }
        }
        catch
        {
        }
        try
        {
            string display = location.DisplayName;
            if (!string.IsNullOrWhiteSpace(display) && display != location.Name) return display;
        }
        catch
        {
        }
        return Spaced(location.Name ?? "");
    }

    /// <summary>"FarmCave" becomes "Farm Cave"; "Custom_Claire_Room" becomes "Claire Room".</summary>
    public static string Spaced(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        if (name.StartsWith("Custom_")) name = name.Substring(7);
        name = name.Replace('_', ' ');
        name = Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
        return name.Trim();
    }

    /// <summary>"Mushroom Box" becomes "Mushroom Boxes", "Keg" becomes "Kegs".</summary>
    public static string Plural(string noun)
    {
        if (string.IsNullOrEmpty(noun)) return noun;
        if (Regex.IsMatch(noun, "(s|x|z|ch|sh)$")) return noun + "es";
        if (Regex.IsMatch(noun, "[^aeiou]y$")) return noun.Substring(0, noun.Length - 1) + "ies";
        return noun + "s";
    }
}
