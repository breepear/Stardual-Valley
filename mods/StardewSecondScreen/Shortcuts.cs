using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.SpecialOrders;
using StardewValley.Tools;
using Object = StardewValley.Object;

namespace StardewSecondScreen;

/// <summary>
/// Stardual: the Shortcuts page. Big buttons for things that take several presses on a
/// controller (eat your best food, warp home, open a menu, pause time), plus everything in
/// your backpack and every villager you've met, so the page's search box can find them.
/// The page sends back "stardual_do" commands, which <see cref="Run"/> carries out.
/// </summary>
internal sealed class Shortcuts
{
    private readonly Sprites _sprites;
    private readonly IMonitor _monitor;
    private readonly Dictionary<string, string> _icons = new();

    /// <summary>Time is held still while this is on (see <see cref="Tick"/>).</summary>
    public static bool TimePaused;

    public Shortcuts(Sprites sprites, IMonitor monitor)
    {
        _sprites = sprites;
        _monitor = monitor;
    }

    private sealed record Totem(string Id, string Where);

    private static readonly Totem[] Totems =
    {
        new("(O)688", "the Farm"), new("(O)689", "the Mountains"), new("(O)690", "the Beach"),
        new("(O)261", "the Desert"), new("(O)886", "Ginger Island"),
    };

    public string Build()
    {
        Farmer player = Game1.player;
        var actions = new List<string>();
        void Add(string id, string group, string title, string detail, string icon, bool enabled = true) =>
            actions.Add(Json.Object(Json.Str("id", id), Json.Str("group", group), Json.Str("title", title), Json.Str("detail", detail), Json.Str("icon", icon), Json.Flag("enabled", enabled)));

        // Quick: things to do, in the order you'd reach for them.
        int best = BestFood(player);
        Object? food = best >= 0 ? player.Items[best] as Object : null;
        Add("eat:best", "Quick", food != null ? $"Eat {food.DisplayName}" : "Eat something",
            food != null ? $"+{food.staminaRecoveredOnConsumption()} energy · {food.Stack} left" : "Nothing to eat in your backpack",
            food != null ? ItemIcon(food.QualifiedItemId) : ItemIcon("(O)196"), food != null);
        bool posted = Game1.CanAcceptDailyQuest();
        Add("menu:help", "Quick", "Help Wanted board", posted ? "A new request is posted today" : "No new request today",
            ItemIcon("(O)789"));
        if (SpecialOrder.IsSpecialOrdersBoardUnlocked())
            Add("menu:orders", "Quick", "Special Orders board", "This week's town requests", ItemIcon("(O)872"));
        if (Game1.mailbox.Count > 0)
            Add("mail", "Quick", "Read your mail", Game1.mailbox.Count == 1 ? "1 letter waiting" : $"{Game1.mailbox.Count} letters waiting", ItemIcon("(O)PrizeTicket"));
        Add("tv:fortune", "Quick", "Today's fortune", "What the spirits say about your luck", ItemIcon("(O)74"));
        Add("tv:weather", "Quick", "Weather report", "Tomorrow's forecast", ItemIcon("(O)681"));
        Add("tv:fishing", "Quick", "Fishing report", "What's biting this season", ItemIcon("(O)145"));
        int dow = Game1.dayOfMonth % 7;
        if (dow == 0 || dow == 3)
            Add("tv:recipe", "Quick", "The Queen of Sauce", dow == 0 ? "This week's new recipe" : "Wednesday rerun", ItemIcon("(O)211"));
        if (dow == 1 || dow == 4)
            Add("tv:tip", "Quick", "Livin' Off the Land", "Today's farming tip", ItemIcon("(O)16"));
        Add("time:toggle", "Quick", TimePaused ? "Resume time" : "Pause time",
            TimePaused ? "The clock is stopped" : "Stop the clock while you plan", ItemIcon("(BC)9"));
        Add("run:toggle", "Quick", Game1.options.autoRun ? "Walk" : "Run",
            Game1.options.autoRun ? "Switch to walking" : "Switch to running", ItemIcon("(O)395"));

        // Travel
        foreach (Totem t in Totems)
        {
            int at = Find(player, t.Id);
            if (at < 0) continue;
            Add("use:" + at, "Travel", ItemRegistry.GetData(t.Id)?.DisplayName ?? "Warp Totem", $"Warp to {t.Where} · {player.Items[at].Stack} left", ItemIcon(t.Id));
        }
        if (Find(player, "(T)ReturnScepter") is int scepter && scepter >= 0)
            Add("use:" + scepter, "Travel", "Return Scepter", "Warp home, as often as you like", ItemIcon("(T)ReturnScepter"));
        if (Find(player, "(O)911") is int flute && flute >= 0)
            Add("use:" + flute, "Travel", "Horse Flute", "Call your horse to you", ItemIcon("(O)911"));

        // Everything in the backpack, for search.
        var items = new List<string>();
        for (int i = 0; i < player.Items.Count; i++)
        {
            Item? item = player.Items[i];
            if (item == null) continue;
            bool edible = item is Object o && o.Edibility > 0 && o.Edibility != -300;
            bool usable = Totems.Any(t => t.Id == item.QualifiedItemId) || item.QualifiedItemId is "(O)911" or "(T)ReturnScepter";
            items.Add(Json.Object(Json.Num("slot", i), Json.Str("name", item.DisplayName), Json.Str("icon", ItemIcon(item.QualifiedItemId)),
                Json.Num("stack", item.Stack), Json.Flag("edible", edible), Json.Flag("usable", usable), Json.Flag("held", i == player.CurrentToolIndex),
                Json.Str("type", item.getCategoryName() ?? "")));
        }

        // Villagers you've met, for search.
        var people = new List<string>();
        foreach (NPC npc in Utility.getAllVillagers().Where(n => player.friendshipData.ContainsKey(n.Name)).OrderBy(n => n.displayName))
            people.Add(Json.Object(Json.Str("key", npc.Name), Json.Str("name", npc.displayName), Json.Str("icon", NpcIcon(npc))));

        return Json.Message("sdv_shortcuts", Json.Array("actions", actions), Json.Array("items", items), Json.Array("people", people));
    }

    /// <summary>Carries out a shortcut the page asked for. Returns whether anything changed.</summary>
    public bool Run(string key, Action<string> lookup)
    {
        if (!Context.IsWorldReady || string.IsNullOrEmpty(key)) return false;
        Farmer player = Game1.player;
        int colon = key.IndexOf(':');
        string verb = colon < 0 ? key : key[..colon];
        string arg = colon < 0 ? "" : key[(colon + 1)..];
        try
        {
            switch (verb)
            {
                case "time":
                    TimePaused = !TimePaused;
                    Game1.addHUDMessage(new HUDMessage(TimePaused ? "Time paused" : "Time resumed", HUDMessage.newQuest_type));
                    return true;
                case "lookup":
                    lookup(arg);
                    return false;
                case "run":
                    Game1.options.autoRun = !Game1.options.autoRun;
                    Game1.addHUDMessage(new HUDMessage(Game1.options.autoRun ? "Running" : "Walking", HUDMessage.newQuest_type));
                    return true;
                case "mail":
                    if (!Context.IsPlayerFree || Game1.mailbox.Count == 0) return false;
                    Game1.currentLocation.mailbox();
                    return true;
                case "tv":
                    if (!Context.IsPlayerFree) return false;
                    Television(arg);
                    return true;
                case "menu":
                    if (!Context.IsPlayerFree) return false;
                    Game1.activeClickableMenu = arg switch
                    {
                        "inventory" => new GameMenu(GameMenu.inventoryTab),
                        "crafting" => new GameMenu(GameMenu.craftingTab),
                        "map" => new GameMenu(GameMenu.mapTab),
                        "skills" => new GameMenu(GameMenu.skillsTab),
                        "social" => new GameMenu(GameMenu.socialTab),
                        "collections" => new GameMenu(GameMenu.collectionsTab),
                        "quests" => NewMenu(typeof(QuestLog)) ?? Game1.activeClickableMenu,
                        "calendar" => new Billboard(false),
                        "help" => new Billboard(true),
                        "orders" => new SpecialOrdersBoard(),
                        _ => Game1.activeClickableMenu
                    };
                    return false;
            }
            if (!Context.IsPlayerFree) return false;
            int slot = arg == "best" ? BestFood(player) : int.TryParse(arg, out int n) ? n : -1;
            if (slot < 0 || slot >= player.Items.Count || player.Items[slot] is not Item item) return false;
            switch (verb)
            {
                case "eat":
                    if (item is not Object food || food.Edibility <= 0 || food.Edibility == -300) return false;
                    player.eatObject((Object)food.getOne());
                    player.Items[slot] = food.ConsumeStack(1);
                    return true;
                case "hold":
                    Hold(player, slot);
                    return true;
                case "use":
                    Hold(player, slot);
                    if (item is Wand wand)
                    {
                        wand.DoFunction(Game1.currentLocation, (int)player.Position.X, (int)player.Position.Y, 1, player);
                        return true;
                    }
                    if (item is Object obj && obj.performUseAction(Game1.currentLocation))
                    {
                        int at = player.Items.IndexOf(obj);
                        if (at >= 0) player.Items[at] = obj.ConsumeStack(1);
                    }
                    return true;
            }
        }
        catch (Exception ex)
        {
            _monitor.Log($"Shortcut '{key}' failed: {ex.Message}", LogLevel.Warn);
        }
        return false;
    }

    /// <summary>
    /// Opens a menu whose constructor differs between the Android and PC games (QuestLog takes
    /// an optional quest index on Android and nothing on PC), using each optional default.
    /// </summary>
    private static IClickableMenu? NewMenu(Type type)
    {
        foreach (var ctor in type.GetConstructors().OrderBy(c => c.GetParameters().Length))
        {
            var ps = ctor.GetParameters();
            if (ps.All(p => p.IsOptional))
                return (IClickableMenu)ctor.Invoke(ps.Select(p => p.DefaultValue).ToArray());
        }
        return null;
    }

    /// <summary>Shows one of the TV channels as a dialogue, as if you'd turned on the TV at home.</summary>
    private static void Television(string channel)
    {
        if (ItemRegistry.Create("(F)1466") is not TV tv) return;
        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        object? Call(string name, Type[] types, params object[] args) =>
            typeof(TV).GetMethod(name, any, null, types, null)?.Invoke(tv, args);
        object? text = channel switch
        {
            "fortune" => Call("getFortuneForecast", new[] { typeof(Farmer) }, Game1.player),
            "weather" => Call("getWeatherForecast", Type.EmptyTypes),
            "fishing" => Call("getFishingInfo", Type.EmptyTypes),
            "recipe" => Call("getWeeklyRecipe", Type.EmptyTypes),
            "tip" => Call("getTodaysTip", Type.EmptyTypes),
            _ => null
        };
        if (text is string[] pages && pages.Length > 0) Game1.multipleDialogues(pages);
        else if (text is string line && line.Length > 0) Game1.drawObjectDialogue(line);
    }

    /// <summary>Keeps the clock still while time is paused. Called every tick.</summary>
    public static void Tick()
    {
        if (TimePaused && Context.IsWorldReady) Game1.gameTimeInterval = 0;
    }

    /// <summary>Selects a backpack slot. The PC toolbar shows one row, so it pages to that row first.</summary>
    private static void Hold(Farmer player, int slot)
    {
        if (Constants.TargetPlatform != GamePlatform.Android)
        {
            int row = slot / 12;
            for (int i = 0; i < row; i++) player.shiftToolbar(right: true);
            slot %= 12;
        }
        player.CurrentToolIndex = slot;
        Game1.playSound("toolSwap");
    }

    /// <summary>
    /// The food that gives the most energy, leaving out food with buffs (Oil of Garlic, Coffee,
    /// Spicy Eel...) unless there's nothing else, since those are usually saved for a reason.
    /// </summary>
    private static int BestFood(Farmer player)
    {
        int best = -1, most = 0, bestBuff = -1, mostBuff = 0;
        for (int i = 0; i < player.Items.Count; i++)
        {
            if (player.Items[i] is not Object o || o.Edibility <= 0 || o.Edibility == -300) continue;
            int gain = o.staminaRecoveredOnConsumption();
            bool buff;
            try { buff = o.GetFoodOrDrinkBuffs().Any(); } catch { buff = false; }
            if (buff) { if (gain > mostBuff) { mostBuff = gain; bestBuff = i; } }
            else if (gain > most) { most = gain; best = i; }
        }
        return best >= 0 ? best : bestBuff;
    }

    private static int Find(Farmer player, string qid)
    {
        for (int i = 0; i < player.Items.Count; i++)
            if (player.Items[i]?.QualifiedItemId == qid) return i;
        return -1;
    }

    private string ItemIcon(string qid)
    {
        if (_icons.TryGetValue(qid, out string? cached)) return cached;
        string icon = "";
        try
        {
            ParsedItemData? data = ItemRegistry.GetData(qid);
            if (data != null && _sprites.EncodeRegion(data.GetTexture(), data.GetSourceRect()) is byte[] png) icon = Convert.ToBase64String(png);
        }
        catch { }
        _icons[qid] = icon;
        return icon;
    }

    private string NpcIcon(NPC npc)
    {
        string key = "npc:" + npc.Name;
        if (_icons.TryGetValue(key, out string? cached)) return cached;
        string icon = "";
        try
        {
            if (npc.Portrait != null && _sprites.EncodeRegion(npc.Portrait, new Microsoft.Xna.Framework.Rectangle(0, 0, 64, 64)) is byte[] png) icon = Convert.ToBase64String(png);
        }
        catch { }
        _icons[key] = icon;
        return icon;
    }
}
