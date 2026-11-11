using System;
using Object = StardewValley.Object;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.Inventories;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;
using StardewValley.Quests;
using StardewValley.SpecialOrders;
using StardewValley.SpecialOrders.Objectives;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using StardewValley.WorldMaps;
using StardewValley.GameData.Characters;

namespace StardewSecondScreen;

public sealed class ModEntry : Mod
{
	private readonly struct GroundCandidate(int x, int y, int red, int green, int blue, int spread)
	{
		public int X { get; } = x;

		public int Y { get; } = y;

		public int Red { get; } = red;

		public int Green { get; } = green;

		public int Blue { get; } = blue;

		public int Spread { get; } = spread;

		public int Luminance => Red + Green + Blue;
	}

	private const int Port = 7786;

	private Bridge? _bridge;

	private Beacon? _beacon;

	private int _lastGold = -1;

	private int _lastInventoryHash;

	private int _lastSelected = -1;

	private int _lastWeaponCooldownBucket = -1;

	private int _lastMapX = -1;

	private int _lastMapY = -1;

	private bool _farmRefreshPending;

	private string _lastMapOutcome = "";

	private int _mapWidth;

	private int _mapHeight;

	private bool _regionMeasured;

	private float _regionWidth;

	private float _regionHeight;

	private ModConfig _config = new ModConfig();

	private readonly Sprites _sprites = new Sprites();

	private Overview? _overview;

	private Bundles? _bundles;

	private Shortcuts? _shortcuts;

	private Pokemon? _pokemon;

	private AnimalsPage? _animalsPage;

	private int _lastHay = -1;

	private readonly CommandQueue _commands = new CommandQueue();

	private bool _hudHidden;

	private bool _portraitWanted;

	private bool _farmerWanted;

	private bool _artSent;

	private string _groundSeason = "";

	private bool _portraitFailed;

	private bool _farmerFailed;

	private int _farmerTries;

	private int _farmerDelay;

	private const int Thousandths = 1000;

	private const float AssumedMapZoom = 4f;

	private const int ScanWidth = 512;

	private const int ScanHeight = 512;

	private const int MaximumTexture = 20000;

	private const int MaximumChoicesPerSheet = 8;

	private const int MaximumChoices = 40;

	private const int MidLuminance = 384;

	private const int TextureDistanceScale = 128;

	private const int Tile = 16;

	private const int MinimumTexture = 256;

	private static readonly int[] LevelThresholds = new int[10] { 100, 380, 770, 1300, 2150, 3300, 4800, 6900, 10000, 15000 };

	private const int MaximumGifts = 8;

	private const int MaximumRecipes = 24;

	private const int DaysInSeason = 28;

	private const int InedibleThreshold = -300;

	private const int MaximumShipped = 12;

	private const int MineFloors = 120;

	private const int MuseumPieces = 95;

	private int _lastDonated = -1;

	[MemberNotNullWhen(true, "_bridge")]
	private bool Live
	{
		[MemberNotNullWhen(true, "_bridge")]
		get
		{
			if (Context.IsWorldReady)
			{
				return _bridge?.HasClients ?? false;
			}
			return false;
		}
	}

	public override void Entry(IModHelper helper)
	{
		_config = helper.ReadConfig<ModConfig>();
		_bridge = new Bridge(7786, (string message) =>
		{
			Monitor.Log(message, LogLevel.Info);
		}, _config.AllowRemote);
		_bridge.MessageReceived += _commands.Accept;
		_bridge.Start();
		if (_config.AllowRemote)
		{
			_beacon = new Beacon(7786, Describe, (string message) =>
			{
				Monitor.Log(message, LogLevel.Info);
			});
			_beacon.Start();
		}
		helper.Events.GameLoop.UpdateTicked += OnTick;
		helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
		helper.Events.GameLoop.DayStarted += OnDayStarted;
		helper.Events.GameLoop.TimeChanged += OnTimeChanged;
		helper.Events.GameLoop.OneSecondUpdateTicked += OnSecond;
		helper.Events.Player.InventoryChanged += (object? _, InventoryChangedEventArgs _) =>
		{
			Attempt("inventory", SendInventory);
			Attempt("bundles", SendBundles);
			Attempt("crafting", SendCrafting);
			Attempt("cooking", SendCooking);
			Attempt("shipping", SendShipping);
			Attempt("collections", SendCollections);
			Attempt("gifts", SendGifts);
			Attempt("trees", SendTrees);
			Attempt("pokemon", SendPokemon);
		Attempt("animals page", SendAnimalsPage);
			_farmRefreshPending = true;
		};
		helper.Events.Player.Warped += (object? _, WarpedEventArgs e) =>
		{
			// Stardual: "when you're bored" ideas drop off as soon as you've done them.
			if (e.IsLocalPlayer && e.NewLocation != null) _overview?.Arrived(e.NewLocation);
			Attempt("overview", SendOverview);
			Attempt("day", SendDay);
			Attempt("villagers", SendVillagers);
		};
		helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
		// Stardual: refresh the Overview when a menu or conversation closes (after talking to
		// someone, giving a gift, cooking or shipping), so finished ideas go away right away.
		helper.Events.Display.MenuChanged += (object? _, MenuChangedEventArgs e) =>
		{
			if (e.NewMenu == null && Context.IsWorldReady) Attempt("overview", SendOverview);
		};
		helper.Events.Display.Rendered += OnRendered;
	}

	public override object? GetApi()
	{
		return null;
	}

	private string Describe()
	{
		return Json.Message("wemu_beacon", Json.Str("game", "stardew"), Json.Num("port", 7786), Json.Str("save", (!Context.IsWorldReady) ? "" : (((NetFieldBase<string, NetString>)(object)Game1.player?.farmName)?.Value ?? "")));
	}

	protected override void Dispose(bool disposing)
	{
		_beacon?.Dispose();
		_bridge?.Dispose();
		base.Dispose(disposing);
	}

	private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
	{
		Announce();
		SendEverything();
	}

	private void OnDayStarted(object? sender, DayStartedEventArgs e)
	{
		_overview?.NewDay();
		SendEverything();
	}

	private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
	{
		Attempt("day", SendDay);
		Attempt("villagers", SendVillagers);
		Attempt("machines", SendMachines);
		Attempt("animals", SendAnimals);
		Attempt("pet", SendPets);
		Attempt("mines", SendMines);
		Attempt("crops", SendCrops);
		Attempt("trees", SendTrees);
		Attempt("quests", SendQuests);
		Attempt("orders", SendOrders);
		Attempt("overview", SendOverview);
		Attempt("shortcuts", SendShortcuts);
		Attempt("schedules", SendSchedules);
		Attempt("pokemon", SendPokemon);
		Attempt("animals page", SendAnimalsPage);
	}

	/// <summary>Stardual: the Animals page (farm animals that aren't Pokemon, coops and barns, hay, your pet).</summary>
	private void SendAnimalsPage()
	{
		if (!Live)
		{
			return;
		}
		_animalsPage ??= new AnimalsPage(_sprites, Monitor);
		_bridge.Broadcast("sdv_animals_page", _animalsPage.Build());
	}

	/// <summary>Stardual: the Pokemon page (Pokemon Ranch's Pokemon, their buildings and where to get more).</summary>
	private void SendPokemon()
	{
		if (!Live)
		{
			return;
		}
		_pokemon ??= new Pokemon(_sprites, Monitor);
		_bridge.Broadcast("sdv_pokemon", _pokemon.Build());
	}

	/// <summary>Stardual: today's schedule for every villager you've met (the Gifts page).</summary>
	private void SendSchedules()
	{
		if (!Live)
		{
			return;
		}
		_bridge.Broadcast("sdv_schedules", Schedules.Build());
	}

	/// <summary>Stardual: the Shortcuts page (big buttons, plus the backpack and villagers to search).</summary>
	private void SendShortcuts()
	{
		if (!Live)
		{
			return;
		}
		_shortcuts ??= new Shortcuts(_sprites, Monitor);
		_bridge.Broadcast("sdv_shortcuts", _shortcuts.Build());
	}

	/// <summary>Stardual: the Overview page (to do today, and ideas for when you're bored).</summary>
	private void SendOverview()
	{
		if (!Live)
		{
			return;
		}
		_overview ??= new Overview(_sprites, Monitor);
		_bridge.Broadcast("sdv_overview", _overview.Build());
	}

	private void OnSecond(object? sender, OneSecondUpdateTickedEventArgs e)
	{
		if (!Context.IsWorldReady)
		{
			return;
		}
		Bridge bridge = _bridge;
		if (bridge == null || !bridge.HasClients)
		{
			return;
		}
		if (Game1.player.Money != _lastGold)
		{
			_lastGold = Game1.player.Money;
			SendDay();
		}
		if (_config.SendMap)
		{
			(int, int) tuple = PlayerMapPixel();
			if (tuple.Item1 != _lastMapX || tuple.Item2 != _lastMapY)
			{
				SendDay();
			}
		}
		// Stardual: hay goes into silos without touching the backpack, so watch it too.
		int hay = Overview.Hay().hay;
		if (hay != _lastHay)
		{
			_lastHay = hay;
			Attempt("overview", SendOverview);
		}
		int num = InventoryHash();
		if (num != _lastInventoryHash)
		{
			_lastInventoryHash = num;
			SendInventory();
			Attempt("overview", SendOverview);
		Attempt("shortcuts", SendShortcuts);
		}
	}

	private void OnTick(object? sender, UpdateTickedEventArgs e)
	{
		Shortcuts.Tick();
		if (!Context.IsWorldReady)
		{
			return;
		}
		bool flag = _bridge?.HasClients ?? false;
		bool flag2 = flag && _config.HideGameHud;
		if (flag2 != _hudHidden)
		{
			_hudHidden = flag2;
			Game1.displayHUD = !flag2;
			Monitor.Log(flag2 ? "The game's HUD is off the top screen" : "The game's HUD is back", LogLevel.Info);
			if (flag)
			{
				Resync();
			}
		}
		// Stardual: the app connected again (it restarted, or was closed and reopened) while
		// the HUD setting stayed the same. A fresh app has no pictures or art, so start over.
		if (flag && _bridge.Joins != _lastJoins)
		{
			Resync();
		}
		if (Game1.player.CurrentToolIndex != _lastSelected)
		{
			_lastSelected = Game1.player.CurrentToolIndex;
			SendInventory();
		}
		if (flag && e.IsMultipleOf(15u))
		{
			int num = WeaponCooldownBucket();
			if (num != _lastWeaponCooldownBucket)
			{
				_lastWeaponCooldownBucket = num;
				SendInventory();
			}
			if (_farmRefreshPending)
			{
				_farmRefreshPending = false;
				Attempt("machines", SendMachines);
				Attempt("animals", SendAnimals);
				Attempt("pet", SendPets);
				Attempt("crops", SendCrops);
				Attempt("quests", SendQuests);
				Attempt("orders", SendOrders);
			}
		}
		_commands.Drain(Apply);
	}

	private void Apply(Command command)
	{
		Farmer player = Game1.player;
		string type = command.Type;
		if (type == null)
		{
			return;
		}
		// Stardual: tapping a villager on the Relationships page opens Lookup Anything's panel for them.
		if (type == "stardual_do")
		{
			_shortcuts ??= new Shortcuts(_sprites, Monitor);
			if (_shortcuts.Run(command.Key, LookupVillager))
			{
				Attempt("inventory", SendInventory);
				Attempt("overview", SendOverview);
			}
			Attempt("shortcuts", SendShortcuts);
			return;
		}
		if (type == "lookup_npc")
		{
			LookupVillager(command.Key);
			return;
		}
		switch (type.Length)
		{
		case 12:
			switch (type[1])
			{
			case 'a':
			{
				if (!(type == "cancel_quest") || !_config.AllowQuestCancel)
				{
					break;
				}
				int a = command.A;
				if (a >= 0 && a < ((NetList<Quest, NetRef<Quest>>)(object)player.questLog).Count)
				{
					Quest val = ((NetList<Quest, NetRef<Quest>>)(object)player.questLog)[a];
					if (val != null && ((NetFieldBase<bool, NetBool>)(object)val.canBeCancelled).Value)
					{
						((NetList<Quest, NetRef<Quest>>)(object)player.questLog).RemoveAt(a);
						SendQuests();
					}
				}
				break;
			}
			case 'r':
				if (type == "craft_recipe" && _config.AllowInventoryEdits && _config.SendCrafting)
				{
					CraftRecipe(command.Key);
				}
				break;
			}
			break;
		case 8:
			switch (type[0])
			{
			case 'e':
			{
				if (!(type == "eat_slot") || !_config.AllowInventoryEdits)
				{
					break;
				}
				int a3 = command.A;
				if (a3 < 0 || a3 >= player.Items.Count)
				{
					break;
				}
				Item val4 = player.Items[a3];
				Object val5 = (Object)(object)((val4 is Object) ? val4 : null);
				if (val5 != null && val5.Edibility > -300)
				{
					Game1.player.eatObject(val5, false);
					int num2 = ((Item)val5).Stack;
					((Item)val5).Stack = num2 - 1;
					if (((Item)val5).Stack <= 0)
					{
						player.Items[a3] = null;
					}
					SendInventory();
				}
				break;
			}
			case 's':
				if (type == "sort_bag" && _config.AllowInventoryEdits)
				{
					ItemGrabMenu.organizeItemsInList((IList<Item>)player.Items);
					SendInventory();
				}
				break;
			}
			break;
		case 11:
			if (type == "select_slot" && _config.AllowInventoryEdits && command.A >= 0 && command.A < 12)
			{
				player.CurrentToolIndex = command.A;
			}
			break;
		case 9:
			if (type == "move_item" && _config.AllowInventoryEdits)
			{
				int a2 = command.A;
				int b = command.B;
				if (a2 >= 0 && b >= 0 && a2 < player.Items.Count && b < player.Items.Count)
				{
					Inventory items = player.Items;
					int num = a2;
					Inventory items2 = player.Items;
					int num2 = b;
					Item val2 = player.Items[b];
					Item val3 = player.Items[a2];
					items[num] = val2;
					items2[num2] = val3;
					SendInventory();
				}
			}
			break;
		case 13:
			if (type == "shift_toolbar" && _config.AllowInventoryEdits)
			{
				player.shiftToolbar(command.A > 0);
				_lastInventoryHash = InventoryHash();
				SendInventory();
			}
			break;
		case 10:
			if (type == "set_option" && ApplyOption(command.Key, command.A != 0))
			{
				Helper.WriteConfig(_config);
				SendEverything();
			}
			break;
		case 7:
			if (type == "refresh")
			{
				_artSent = false;
				_sprites.Reset();
				SendEverything();
			}
			break;
		}
	}

	// Stardual: opens Lookup Anything's info panel for a villager on the game screen, the same
	// panel its controller button shows. Lookup Anything has no public API for this, so it is
	// reached by reflection; if it is missing or changes, the tap does nothing.
	private void LookupVillager(string name)
	{
		if (string.IsNullOrEmpty(name) || !Context.IsWorldReady)
		{
			return;
		}
		NPC? npc = Game1.getCharacterFromName(name, true, false);
		if (npc == null)
		{
			Monitor.Log("Lookup: no villager named " + name, LogLevel.Trace);
			return;
		}
		IModInfo? info = Helper.ModRegistry.Get("Pathoschild.LookupAnything");
		object? lookupMod = info?.GetType().GetProperty("Mod")?.GetValue(info);
		if (lookupMod == null)
		{
			Monitor.Log("Lookup: Lookup Anything is not installed", LogLevel.Trace);
			return;
		}
		try
		{
			object? factory = Helper.Reflection.GetField<object>(lookupMod, "TargetFactory").GetValue();
			if (factory == null)
			{
				return;
			}
			object? subject = factory.GetType().GetMethod("GetByEntity")?.Invoke(factory, new object?[] { npc, npc.currentLocation });
			if (subject == null)
			{
				Monitor.Log("Lookup: nothing to show for " + name, LogLevel.Trace);
				return;
			}
			Helper.Reflection.GetMethod(lookupMod, "ShowLookupFor").Invoke(subject);
		}
		catch (Exception ex)
		{
			Monitor.Log("Lookup failed for " + name + ": " + ex.Message, LogLevel.Warn);
		}
	}

	private bool ApplyOption(string key, bool value)
	{
		switch (key)
		{
		case "hideGameHud":
			_config.HideGameHud = value;
			return true;
		case "allowInventoryEdits":
			_config.AllowInventoryEdits = value;
			return true;
		case "allowQuestCancel":
			_config.AllowQuestCancel = value;
			return true;
		case "farmerMarker":
			_config.FarmerMarker = value;
			return true;
		case "sendCrops":
			_config.SendCrops = value;
			return true;
		case "sendMachines":
			_config.SendMachines = value;
			return true;
		case "sendAnimals":
			_config.SendAnimals = value;
			return true;
		case "sendBundles":
			_config.SendBundles = value;
			return true;
		case "sendVillagers":
			_config.SendVillagers = value;
			return true;
		case "sendMap":
			_config.SendMap = value;
			return true;
		case "sendCrafting":
			_config.SendCrafting = value;
			return true;
		default:
			Monitor.Log("The console asked for an unknown setting \"" + key + "\".");
			return false;
		}
	}

	private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
	{
		_lastGold = -1;
		_lastInventoryHash = 0;
		_lastSelected = -1;
		_lastMapX = -1;
		_lastMapY = -1;
		_farmRefreshPending = false;
		_lastMapOutcome = "";
		_regionMeasured = false;
		_portraitWanted = false;
		_farmerWanted = false;
		_portraitFailed = false;
		_farmerFailed = false;
		_artSent = false;
		_groundSeason = "";
		_sprites.Reset();
		if (_hudHidden)
		{
			_hudHidden = false;
			Game1.displayHUD = true;
		}
		_bridge?.Broadcast("game_info", Json.Message("game_info", Json.Str("gameId", "stardew"), Json.Str("saveName", "")));
	}

	private int _lastJoins;

	/// <summary>Stardual: sends the app everything from scratch, pictures and art included.</summary>
	private void Resync()
	{
		_lastJoins = _bridge?.Joins ?? 0;
		_artSent = false;
		Announce();
		_sprites.Reset();
		SendEverything();
	}

	/// <summary>Stardual: one line on who this villager is with, for the Relationships page.
	/// "Married to you", "Dating you", "Married to Pierre", "Single" and so on.</summary>
	/// <summary>
	/// Stardual: how far the next heart is, for the People page ("4 chats or 1 loved gift").
	/// A heart is 250 friendship points; talking gives 20 a day, a loved gift 80.
	/// </summary>
	private static string NextHeartLine(NPC npc, Friendship friendship)
	{
		try
		{
			if (friendship == null) return "";
			int points = friendship.Points;
			int cap = friendship.IsMarried() ? 14 : 10;
			bool bouquetNeeded = npc.datable.Value && !friendship.IsDating() && !friendship.IsMarried() && !friendship.IsEngaged();
			if (bouquetNeeded) cap = 8;
			if (points >= cap * 250 - 1)
			{
				return bouquetNeeded ? "Give a bouquet for more hearts" : "Max hearts";
			}
			int need = 250 - points % 250;
			int chats = (need + 19) / 20;
			int loved = (need + 79) / 80;
			string c = chats == 1 ? "1 chat" : chats + " chats";
			string g = loved == 1 ? "1 loved gift" : loved + " loved gifts";
			return c + " or " + g;
		}
		catch
		{
			return "";
		}
	}

	private static string RelationshipLine(NPC npc, Friendship friendship)
	{
		try
		{
			if (friendship != null)
			{
				if (friendship.IsMarried())
				{
					return friendship.IsRoommate() ? "Your roommate" : "Married to you";
				}
				if (friendship.IsEngaged())
				{
					return "Engaged to you";
				}
				if (friendship.IsDivorced())
				{
					return "Divorced from you";
				}
				if (friendship.IsDating())
				{
					return "Dating you";
				}
			}
			// Their own partner, from the game's character data ("Pierre": "...Relative_Husband]").
			CharacterData data = npc.GetData();
			if (data?.FriendsAndFamily != null)
			{
				foreach (KeyValuePair<string, string> relative in data.FriendsAndFamily)
				{
					string kind = relative.Value ?? "";
					string who = Game1.getCharacterFromName(relative.Key, true, false)?.displayName ?? relative.Key;
					if (kind.Contains("Husband", StringComparison.OrdinalIgnoreCase) || kind.Contains("Wife", StringComparison.OrdinalIgnoreCase))
					{
						return "Married to " + who;
					}
					if (kind.Contains("Boyfriend", StringComparison.OrdinalIgnoreCase) || kind.Contains("Girlfriend", StringComparison.OrdinalIgnoreCase) || kind.Contains("Partner", StringComparison.OrdinalIgnoreCase))
					{
						return "Seeing " + who;
					}
				}
			}
		}
		catch
		{
		}
		return "Single";
	}

	private void Announce()
	{
		_bridge?.Broadcast("game_info", Json.Message("game_info", Json.Str("gameId", "stardew"), Json.Str("saveName", ((NetFieldBase<string, NetString>)(object)Game1.player?.farmName)?.Value ?? "")));
	}

	private void SendEverything()
	{
		Attempt("config", SendConfig);
		Attempt("interface art", SendUi);
		Attempt("day", SendDay);
		Attempt("inventory", SendInventory);
		Attempt("quests", SendQuests);
		Attempt("villagers", SendVillagers);
		Attempt("crops", SendCrops);
		Attempt("skills", SendSkills);
		Attempt("machines", SendMachines);
		Attempt("bundles", SendBundles);
		Attempt("animals", SendAnimals);
		Attempt("gifts", SendGifts);
		Attempt("crafting", SendCrafting);
		Attempt("calendar", SendCalendar);
		Attempt("cart", SendCart);
		Attempt("orders", SendOrders);
		Attempt("cooking", SendCooking);
		Attempt("shipping", SendShipping);
		Attempt("trees", SendTrees);
		Attempt("pet", SendPets);
		Attempt("mines", SendMines);
		Attempt("collections", SendCollections);
		Attempt("overview", SendOverview);
		Attempt("shortcuts", SendShortcuts);
		Attempt("schedules", SendSchedules);
		Attempt("pokemon", SendPokemon);
		Attempt("animals page", SendAnimalsPage);
	}

	private void Attempt(string what, Action send)
	{
		try
		{
			send();
		}
		catch (Exception value)
		{
			Monitor.Log($"Could not send the {what}: {value}", LogLevel.Error);
		}
	}

	private void SendSprite(string qualifiedId)
	{
		if (_bridge != null && !string.IsNullOrEmpty(qualifiedId) && !_sprites.AlreadySent(qualifiedId))
		{
			byte[] array = _sprites.EncodeId(qualifiedId);
			if (array != null)
			{
				_bridge.Send(Json.Message("sdv_sprite", Json.Str("id", qualifiedId), Json.Str("png", Convert.ToBase64String(array))));
			}
		}
	}

	private void SendMachines()
	{
		if (!Live)
		{
			return;
		}
		if (!_config.SendMachines)
		{
			Clear("SendMachines");
			return;
		}
		List<string> machines = new List<string>();
		Utility.ForEachLocation((Func<GameLocation, bool>)((GameLocation location) =>
		{
			foreach (Object item in location.objects.Values.ToList())
			{
				Object val = ((NetFieldBase<Object, NetRef<Object>>)(object)item?.heldObject).Value;
				if (item != null && val != null && !(item is Chest) && !(((Item)item).QualifiedItemId == "(BC)165"))
				{
					SendSprite(((Item)item).QualifiedItemId);
					SendSprite(((Item)val).QualifiedItemId);
					machines.Add(Json.Object(Json.Str("id", ((Item)item).QualifiedItemId), Json.Str("name", ((Item)item).DisplayName ?? ((Item)item).Name ?? ""), Json.Str("outputId", ((Item)val).QualifiedItemId), Json.Str("output", ((Item)val).DisplayName ?? ((Item)val).Name ?? ""), Json.Num("count", ((Item)val).Stack), Json.Str("location", LocationName(location)), Json.Num("minutes", Math.Max(0, item.MinutesUntilReady)), Json.Flag("ready", ((NetFieldBase<bool, NetBool>)(object)item.readyForHarvest).Value)));
				}
			}
			return true;
		}), true, false);
		_bridge.Broadcast("sdv_machines", Json.Message("sdv_machines", Json.Array("machines", machines)));
	}

	private void SendBundles()
	{
		if (!Live)
		{
			return;
		}
		if (!_config.SendBundles)
		{
			Clear("SendBundles");
			return;
		}
		List<string> list = new List<string>();
		try
		{
			GameLocation locationFromName = Game1.getLocationFromName("CommunityCenter");
			CommunityCenter val = (CommunityCenter)(object)((locationFromName is CommunityCenter) ? locationFromName : null);
			if (val != null)
			{
				foreach (KeyValuePair<string, string> bundleDatum in ((NetFieldBase<NetWorldState, NetRef<NetWorldState>>)(object)Game1.netWorldState).Value.BundleData)
				{
					try
					{
						string[] array = bundleDatum.Key.Split('/');
						if (array.Length < 2 || !int.TryParse(array[^1], out var result) || !((NetDictionary<int, bool[], NetArray<bool, NetBool>, SerializableDictionary<int, bool[]>, NetBundles>)(object)val.bundles).ContainsKey(result))
						{
							continue;
						}
						bool[] array2 = ((NetDictionary<int, bool[], NetArray<bool, NetBool>, SerializableDictionary<int, bool[]>, NetBundles>)(object)val.bundles)[result];
						string[] array3 = bundleDatum.Value.Split('/');
						if (array3.Length < 3)
						{
							continue;
						}
						string value = ((array3.Length > 6 && !string.IsNullOrWhiteSpace(array3[6])) ? array3[6] : array3[0]);
						string[] array4 = array3[2].Split(' ');
						List<string> list2 = new List<string>();
						int num = 0;
						int num2 = 0;
						for (int i = 0; i + 2 < array4.Length; i += 3)
						{
							int num3 = i / 3;
							num2++;
							if (num3 < array2.Length && array2[num3])
							{
								num++;
								continue;
							}
							if (!int.TryParse(array4[i + 1], out var result2))
							{
								result2 = 1;
							}
							int.TryParse(array4[i + 2], out var quality);
							if (array4[i] == "-1")
							{
								list2.Add(Json.Object(Json.Str("id", ""), Json.Str("name", result2.ToString("N0") + "g"), Json.Num("count", 1), Json.Num("quality", 0), Json.Flag("carried", Game1.player.Money >= result2)));
								continue;
							}
							string qid = ItemRegistry.QualifyItemId(array4[i]) ?? ("(O)" + array4[i]);
							ParsedItemData data = ItemRegistry.GetData(qid);
							if (data != null)
							{
								SendSprite(qid);
							}
							int num4 = ((IEnumerable<Item>)Game1.player.Items).Where((Item item) => item != null && item.QualifiedItemId == qid && item.Quality >= quality).Sum((Item item) => (item != null) ? item.Stack : 0);
							list2.Add(Json.Object(Json.Str("id", qid), Json.Str("name", data?.DisplayName ?? array4[i]), Json.Num("count", result2), Json.Num("quality", quality), Json.Flag("carried", num4 >= result2)));
						}
						int num5 = num2;
						if (array3.Length > 4 && int.TryParse(array3[4], out var result3) && result3 > 0 && result3 < num2)
						{
							num5 = result3;
						}
						if (num < num5 && list2.Count != 0)
						{
							list.Add(Json.Object(Json.Str("room", RoomName(array[0])), Json.Str("name", value), Json.Num("have", num), Json.Num("required", num5), Json.Array("missing", list2)));
						}
					}
					catch
					{
					}
				}
			}
		}
		catch
		{
		}
		_bridge.Broadcast("sdv_bundles", Json.Message("sdv_bundles", Json.Array("bundles", list)));
		// Stardual: the Bundles page (this season first, with backpack counts).
		_bundles ??= new Bundles(_sprites, Monitor);
		_bridge.Broadcast("sdv_bundles_page", _bundles.Build());
	}

	private void SendAnimals()
	{
		if (!Live)
		{
			return;
		}
		if (!_config.SendAnimals)
		{
			Clear("SendAnimals");
			return;
		}
		List<string> list = new List<string>();
		try
		{
			foreach (FarmAnimal allFarmAnimal in ((GameLocation)Game1.getFarm()).getAllFarmAnimals())
			{
				if (allFarmAnimal == null)
				{
					continue;
				}
				string text = "animal:" + (((NetFieldBase<string, NetString>)(object)allFarmAnimal.type).Value ?? allFarmAnimal.displayType ?? "farm");
				SendAnimalSprite(allFarmAnimal, text);
				string text2 = ((NetFieldBase<string, NetString>)(object)allFarmAnimal.currentProduce).Value?.Trim() ?? "";
				string text3 = "";
				string value = "";
				if (text2 != "" && text2 != "-1" && text2 != "0" && text2 != "(O)-1" && text2 != "(O)0")
				{
					text3 = ItemRegistry.QualifyItemId(text2) ?? ("(O)" + text2);
					ParsedItemData data = ItemRegistry.GetData(text3);
					if (data != null)
					{
						value = data.DisplayName;
						SendSprite(text3);
					}
					else
					{
						text3 = "";
					}
				}
				list.Add(Json.Object(Json.Str("name", ((Character)allFarmAnimal).displayName ?? ((Character)allFarmAnimal).Name ?? ""), Json.Str("type", allFarmAnimal.displayType ?? ((NetFieldBase<string, NetString>)(object)allFarmAnimal.type).Value ?? ""), Json.Str("spriteId", text), Json.Num("hearts", ((NetFieldBase<int, NetInt>)(object)allFarmAnimal.friendshipTowardFarmer).Value / 200), Json.Flag("petted", ((NetFieldBase<bool, NetBool>)(object)allFarmAnimal.wasPet).Value), Json.Flag("baby", allFarmAnimal.isBaby()), Json.Str("produceId", text3), Json.Str("produce", value)));
			}
		}
		catch
		{
		}
		_bridge.Broadcast("sdv_animals", Json.Message("sdv_animals", Json.Array("animals", list)));
	}

	private void SendAnimalSprite(FarmAnimal animal, string id)
	{
		if (_bridge == null || _sprites.AlreadySent(id))
		{
			return;
		}
		try
		{
			AnimatedSprite orLoadTexture = animal.GetOrLoadTexture();
			Rectangle source = new Rectangle(0, 0, orLoadTexture.SpriteWidth, orLoadTexture.SpriteHeight);
			byte[] array = _sprites.EncodeRegion(orLoadTexture.Texture, source);
			if (array != null)
			{
				_sprites.Remember(id);
				_bridge.Send(Json.Message("sdv_sprite", Json.Str("id", id), Json.Num("inset", 0), Json.Str("png", Convert.ToBase64String(array))));
			}
		}
		catch
		{
		}
	}

	private void SendDay()
	{
		if (Live)
		{
			(int, int) tuple = (_config.SendMap ? PlayerMapPixel() : (-1, -1));
			(_lastMapX, _lastMapY) = tuple;
			_bridge.Broadcast("sdv_day", Json.Message("sdv_day", Json.Str("season", Capitalise(Game1.currentSeason)), Json.Num("day", Game1.dayOfMonth), Json.Num("year", Game1.year), Json.Str("weekday", WorldDate.GetDayOfWeekFor(Game1.dayOfMonth).ToString()), Json.Num("timeOfDay", Game1.timeOfDay), Json.Str("weatherToday", WeatherToday()), Json.Str("weatherTomorrow", WeatherTomorrow()), Json.Num("gold", Game1.player.Money), Json.Num("energy", (int)Game1.player.Stamina), Json.Num("maxEnergy", Game1.player.MaxStamina), Json.Num("health", Game1.player.health), Json.Num("maxHealth", Game1.player.maxHealth), Json.Str("playerName", ((Character)Game1.player).Name ?? ""), Json.Str("farmName", ((NetFieldBase<string, NetString>)(object)Game1.player.farmName)?.Value ?? ""), Json.Num("playerLevel", Game1.player.Level), Json.Num("totalEarnings", Convert.ToInt64(Game1.player.totalMoneyEarned)), Json.Str("location", PlaceName()), Json.Num("luck", (int)Math.Round(((NetFieldBase<double, NetDouble>)(object)Game1.player.team.sharedDailyLuck).Value * 1000.0)), Json.Num("mapX", tuple.Item1), Json.Num("mapY", tuple.Item2)));
		}
	}

	private static string PlaceName()
	{
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			var positionData = WorldMapManager.GetPositionData(Game1.currentLocation, ((Character)Game1.player).TilePoint);
			object obj;
			if (!positionData.HasValue)
			{
				obj = null;
			}
			else
			{
				MapAreaPositionWithContext valueOrDefault = positionData.GetValueOrDefault();
				obj = valueOrDefault.GetScrollText();
			}
			string text = (string)obj;
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
		}
		catch
		{
		}
		GameLocation currentLocation = Game1.currentLocation;
		return ((currentLocation != null) ? currentLocation.Name : null) ?? "";
	}

	private (int x, int y) PlayerMapPixel()
	{
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		GameLocation currentLocation = Game1.currentLocation;
		string text = ((currentLocation != null) ? currentLocation.Name : null) ?? "nowhere";
		try
		{
			var positionData = WorldMapManager.GetPositionData(Game1.currentLocation, ((Character)Game1.player).TilePoint);
			if (!positionData.HasValue)
			{
				Explain($"no map area covers \"{text}\" at tile {((Character)Game1.player).TilePoint.X},{((Character)Game1.player).TilePoint.Y}");
				return (x: -1, y: -1);
			}
			MapAreaPositionWithContext value = positionData.Value;
			Vector2 mapPixelPosition = value.GetMapPixelPosition();
			if (!ValleyMapSize(positionData.Value, out var width, out var height))
			{
				Explain("\"" + text + "\" is outside the valley map region");
				return (x: -1, y: -1);
			}
			int num = (int)Math.Round(mapPixelPosition.X / width * 1000f);
			int num2 = (int)Math.Round(mapPixelPosition.Y / height * 1000f);
			Explain($"on the map at {(int)mapPixelPosition.X},{(int)mapPixelPosition.Y} of {(int)width}x{(int)height} from \"{text}\" ({num}/1000, {num2}/1000)");
			return (x: num, y: num2);
		}
		catch (Exception ex)
		{
			Explain("the map lookup threw in \"" + text + "\": " + ex.Message);
			return (x: -1, y: -1);
		}
	}

	private (int x, int y, string location) VillagerMapPixel(NPC npc)
	{
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		GameLocation currentLocation = ((Character)npc).currentLocation;
		string text = ((currentLocation != null) ? currentLocation.Name : null) ?? "";
		if (currentLocation == null)
		{
			return (x: -1, y: -1, location: text);
		}
		try
		{
			var positionData = WorldMapManager.GetPositionData(currentLocation, ((Character)npc).TilePoint);
			if (!positionData.HasValue)
			{
				return (x: -1, y: -1, location: text);
			}
			MapAreaPositionWithContext value = positionData.Value;
			Vector2 mapPixelPosition = value.GetMapPixelPosition();
			value = positionData.Value;
			string scrollText = value.GetScrollText();
			if (!ValleyMapSize(positionData.Value, out var width, out var height))
			{
				return (x: -1, y: -1, location: string.IsNullOrWhiteSpace(scrollText) ? text : scrollText);
			}
			return (x: (int)Math.Round(mapPixelPosition.X / width * 1000f), y: (int)Math.Round(mapPixelPosition.Y / height * 1000f), location: string.IsNullOrWhiteSpace(scrollText) ? text : scrollText);
		}
		catch
		{
			return (x: -1, y: -1, location: text);
		}
	}

	private bool ValleyMapSize(MapAreaPositionWithContext position, out float width, out float height)
	{
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		width = 0f;
		height = 0f;
		try
		{
			var positionData = WorldMapManager.GetPositionData((GameLocation)(object)Game1.getFarm(), Point.Zero);
			if (positionData.HasValue)
			{
				MapAreaPositionWithContext value = positionData.Value;
				MapRegion region = value.Data.Region;
				if (!string.Equals(position.Data.Region.Id, region.Id, StringComparison.Ordinal))
				{
					return false;
				}
				Rectangle mapPixelBounds = region.GetMapPixelBounds();
				width = mapPixelBounds.Width * 4;
				height = mapPixelBounds.Height * 4;
				if (width > 0f && height > 0f)
				{
					return true;
				}
			}
		}
		catch
		{
		}
		MeasureRegion(position);
		width = _regionWidth;
		height = _regionHeight;
		if (width > 0f)
		{
			return height > 0f;
		}
		return false;
	}

	private void MeasureRegion(MapAreaPositionWithContext position)
	{
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		if (_regionMeasured)
		{
			return;
		}
		_regionMeasured = true;
		_regionWidth = (float)Math.Max(1, _mapWidth) * 4f;
		_regionHeight = (float)Math.Max(1, _mapHeight) * 4f;
		string value = $"assumed {4f}x the picture";
		try
		{
			List<string> list = new List<string>();
			MemberInfo[] members = ((object)position).GetType().GetMembers(BindingFlags.Instance | BindingFlags.Public);
			foreach (MemberInfo memberInfo in members)
			{
				list.Add(memberInfo.Name);
			}
			Monitor.Log("Map position members: " + string.Join(", ", list));
			foreach (Rectangle item in RectanglesOn(position))
			{
				if (item.Width > 0 && item.Height > 0 && item.Width >= _mapWidth && item.Height >= _mapHeight)
				{
					_regionWidth = item.Width;
					_regionHeight = item.Height;
					value = $"read from the game as {item.Width}x{item.Height}";
					break;
				}
			}
		}
		catch
		{
		}
		Monitor.Log($"Map region is {(int)_regionWidth}x{(int)_regionHeight} ({value}); the picture is {_mapWidth}x{_mapHeight}.", LogLevel.Info);
	}

	private static IEnumerable<Rectangle> RectanglesOn(object root)
	{
		List<object> list = new List<object> { root };
		for (int depth = 0; depth < 2; depth++)
		{
			List<object> next = new List<object>();
			foreach (object node in list)
			{
				if (node == null)
				{
					continue;
				}
				PropertyInfo[] properties = node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
				foreach (PropertyInfo propertyInfo in properties)
				{
					if (propertyInfo.GetIndexParameters().Length == 0)
					{
						object value;
						try
						{
							value = propertyInfo.GetValue(node);
						}
						catch
						{
							continue;
						}
						if (value is Rectangle rectangle && (propertyInfo.Name.Contains("Bounds") || propertyInfo.Name.Contains("Pixel") || propertyInfo.Name.Contains("Area")))
						{
							yield return rectangle;
						}
						else if (value != null && (value.GetType().Namespace?.StartsWith("StardewValley") ?? false))
						{
							next.Add(value);
						}
					}
				}
				FieldInfo[] fields = node.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
				foreach (FieldInfo fieldInfo in fields)
				{
					object value2;
					try
					{
						value2 = fieldInfo.GetValue(node);
					}
					catch
					{
						continue;
					}
					if (value2 is Rectangle rectangle2 && (fieldInfo.Name.Contains("Bounds") || fieldInfo.Name.Contains("Pixel") || fieldInfo.Name.Contains("Area")))
					{
						yield return rectangle2;
					}
					else if (value2 != null && (value2.GetType().Namespace?.StartsWith("StardewValley") ?? false))
					{
						next.Add(value2);
					}
				}
			}
			list = next;
		}
	}

	private void Explain(string outcome)
	{
		string text = outcome.Split(' ')[0] + (outcome.Contains("on the map") ? "+" : "-");
		if (!(text == _lastMapOutcome))
		{
			_lastMapOutcome = text;
			Monitor.Log("Map position: " + outcome, LogLevel.Info);
		}
	}

	private void SendInventory()
	{
		if (!Live)
		{
			return;
		}
		Farmer player = Game1.player;
		int num = 0;
		List<string> list = new List<string>();
		for (int i = 0; i < player.Items.Count; i++)
		{
			Item val = player.Items[i];
			if (val != null && !_sprites.AlreadySent(val.QualifiedItemId))
			{
				byte[] array = _sprites.Encode(val);
				if (array == null)
				{
					list.Add(val.QualifiedItemId);
					continue;
				}
				num++;
				_bridge.Send(Json.Message("sdv_sprite", Json.Str("id", val.QualifiedItemId), Json.Str("png", Convert.ToBase64String(array))));
			}
		}
		if (num > 0 || list.Count > 0)
		{
			Monitor.Log($"Sprites: {num} sent" + ((list.Count > 0) ? $", {list.Count} could not be read: {string.Join(", ", list)}" : ""), (list.Count <= 0) ? LogLevel.Debug : LogLevel.Warn);
		}
		List<string> list2 = new List<string>();
		for (int j = 0; j < player.Items.Count; j++)
		{
			Item val2 = player.Items[j];
			if (val2 == null)
			{
				list2.Add(Json.Object(Json.Num("slot", j), Json.Str("id", "")));
				continue;
			}
			WateringCan val3 = (WateringCan)(object)((val2 is WateringCan) ? val2 : null);
			MeleeWeapon weapon = (MeleeWeapon)(object)((val2 is MeleeWeapon) ? val2 : null);
			(int, int) tuple = WeaponCooldown(weapon);
			list2.Add(Json.Object(Json.Num("slot", j), Json.Str("id", val2.QualifiedItemId), Json.Str("name", val2.DisplayName), Json.Num("count", val2.Stack), Json.Num("quality", val2.Quality), Json.Num("water", (val3 != null) ? val3.WaterLeft : (-1)), Json.Num("waterMax", val3?.waterCanMax ?? 0), Json.Flag("bottomless", val3 != null && val3.IsBottomless), Json.Num("cooldownMs", tuple.Item1), Json.Num("cooldownMax", tuple.Item2), Json.Str("category", val2.getCategoryName())));
		}
		_bridge.Broadcast("sdv_inventory", Json.Message("sdv_inventory", Json.Num("selected", player.CurrentToolIndex), Json.Num("capacity", player.MaxItems), Json.Array("items", list2)));
		_lastWeaponCooldownBucket = WeaponCooldownBucket();
	}

	private static (int remaining, int maximum) WeaponCooldown(MeleeWeapon? weapon)
	{
		if (weapon == null)
		{
			return (remaining: -1, maximum: 0);
		}
		return ((NetFieldBase<int, NetInt>)(object)weapon.type).Value switch
		{
			1 => (remaining: Math.Max(0, MeleeWeapon.daggerCooldown), maximum: 3000), 
			2 => (remaining: Math.Max(0, MeleeWeapon.clubCooldown), maximum: 6000), 
			0 => (remaining: Math.Max(0, MeleeWeapon.attackSwordCooldown), maximum: 1500), 
			_ => (remaining: Math.Max(0, MeleeWeapon.defenseCooldown), maximum: 1500), 
		};
	}

	private static int WeaponCooldownBucket()
	{
		int num = Math.Max(Math.Max(MeleeWeapon.defenseCooldown, MeleeWeapon.attackSwordCooldown), Math.Max(MeleeWeapon.daggerCooldown, MeleeWeapon.clubCooldown));
		if (num > 0)
		{
			return (num + 249) / 250;
		}
		return 0;
	}

	private void SendQuests()
	{
		if (!Live)
		{
			return;
		}
		List<string> list = new List<string>();
		for (int i = 0; i < ((NetList<Quest, NetRef<Quest>>)(object)Game1.player.questLog).Count; i++)
		{
			Quest val = ((NetList<Quest, NetRef<Quest>>)(object)Game1.player.questLog)[i];
			if (val != null)
			{
				string value = "";
				try
				{
					val.reloadObjective();
					value = StripMarkup(val.currentObjective);
				}
				catch
				{
				}
				list.Add(Json.Object(Json.Num("index", i), Json.Str("title", val.questTitle), Json.Str("detail", StripMarkup(val.questDescription)), Json.Str("objective", value), Json.Num("daysLeft", (((NetFieldBase<int, NetInt>)(object)val.daysLeft).Value > 0) ? ((NetFieldBase<int, NetInt>)(object)val.daysLeft).Value : (-1)), Json.Num("reward", ((NetFieldBase<int, NetInt>)(object)val.moneyReward).Value), Json.Flag("daily", ((NetFieldBase<bool, NetBool>)(object)val.dailyQuest).Value), Json.Flag("cancellable", ((NetFieldBase<bool, NetBool>)(object)val.canBeCancelled).Value), Json.Flag("complete", ((NetFieldBase<bool, NetBool>)(object)val.completed).Value)));
			}
		}
		_bridge.Broadcast("sdv_quests", Json.Message("sdv_quests", Json.Array("quests", list)));
	}

	private void SendVillagers()
	{
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		if (!Live)
		{
			return;
		}
		if (!_config.SendVillagers)
		{
			Clear("SendVillagers");
			return;
		}
		string[] array = new string[4] { "spring", "summer", "fall", "winter" };
		int num = Array.IndexOf(array, Game1.currentSeason);
		List<string> list = new List<string>();
		foreach (string item in ((IEnumerable<string>)(object)((NetDictionary<string, Friendship, NetRef<Friendship>, SerializableDictionary<string, Friendship>, NetStringDictionary<Friendship, NetRef<Friendship>>>)(object)Game1.player.friendshipData).Keys).ToList())
		{
			NPC characterFromName = Game1.getCharacterFromName(item, true, false);
			if (characterFromName == null)
			{
				continue;
			}
			Friendship val = ((NetDictionary<string, Friendship, NetRef<Friendship>, SerializableDictionary<string, Friendship>, NetStringDictionary<Friendship, NetRef<Friendship>>>)(object)Game1.player.friendshipData)[item];
			int num2 = -1;
			int num3 = Array.IndexOf(array, characterFromName.Birthday_Season ?? "");
			if (num3 >= 0 && characterFromName.Birthday_Day > 0 && num >= 0)
			{
				int num4 = num * 28 + Game1.dayOfMonth;
				int num5 = num3 * 28 + characterFromName.Birthday_Day;
				num2 = num5 - num4;
				if (num2 < 0)
				{
					num2 += 112;
				}
			}
			SendPortrait(characterFromName);
			(int x, int y, string location) valueTuple;
			if (!_config.SendMap)
			{
				GameLocation currentLocation = ((Character)characterFromName).currentLocation;
				valueTuple = (-1, -1, ((currentLocation != null) ? currentLocation.Name : null) ?? "");
			}
			else
			{
				valueTuple = VillagerMapPixel(characterFromName);
			}
			(int, int, string) tuple = valueTuple;
			list.Add(Json.Object(Json.Str("name", ((Character)characterFromName).displayName ?? item), Json.Str("npc", ((Character)characterFromName).Name ?? item), Json.Str("location", tuple.Item3), Json.Num("mapX", tuple.Item1), Json.Num("mapY", tuple.Item2), Json.Num("hearts", val.Points / 250), Json.Num("maxHearts", val.IsMarried() ? 14 : 10), Json.Str("birthdaySeason", Capitalise(characterFromName.Birthday_Season ?? "")), Json.Num("birthdayDay", characterFromName.Birthday_Day), Json.Num("birthdayIn", num2), Json.Num("giftsThisWeek", val.GiftsThisWeek), Json.Flag("talkedToday", val.TalkedToToday), Json.Str("relationship", RelationshipLine(characterFromName, val)), Json.Str("nextHeart", NextHeartLine(characterFromName, val))));
		}
		_bridge.Broadcast("sdv_villagers", Json.Message("sdv_villagers", Json.Array("villagers", list)));
	}

	private void SendCrops()
	{
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		if (!Live)
		{
			return;
		}
		if (!_config.SendCrops)
		{
			Clear("SendCrops");
			return;
		}
		// Stardual: every location including building interiors (sheds, greenhouse,
		// SVE buildings), crops in garden pots too, and no wild forage crops (the game's own
		// spring onions and Ginger Island ginger). Locations go by their display name.
		List<string> list = new List<string>();
		Utility.ForEachLocation(delegate(GameLocation location)
		{
			if (location == null)
			{
				return true;
			}
			string place = location.DisplayName;
			if (string.IsNullOrWhiteSpace(place))
			{
				place = location.Name;
			}
			foreach (TerrainFeature feature in location.terrainFeatures.Values.ToList())
			{
				if (feature is HoeDirt dirt)
				{
					AddCrop(list, dirt, place);
				}
			}
			foreach (StardewValley.Object obj in location.objects.Values.ToList())
			{
				if (obj is IndoorPot pot && pot.hoeDirt.Value != null)
				{
					AddCrop(list, pot.hoeDirt.Value, place);
				}
			}
			return true;
		}, true, false);
		_bridge.Broadcast("sdv_crops", Json.Message("sdv_crops", Json.Array("crops", list)));
	}

	private void AddCrop(List<string> list, HoeDirt dirt, string place)
	{
		Crop crop = dirt.crop;
		if (crop == null || crop.dead.Value || crop.forageCrop.Value)
		{
			return;
		}
		bool ready = crop.currentPhase.Value >= crop.phaseDays.Count - 1 && (!crop.fullyGrown.Value || crop.dayOfCurrentPhase.Value <= 0);
		int days = 0;
		for (int i = crop.currentPhase.Value; i < crop.phaseDays.Count - 1; i++)
		{
			days += crop.phaseDays[i];
		}
		days -= crop.dayOfCurrentPhase.Value;
		string harvest = crop.indexOfHarvest.Value;
		if (string.IsNullOrEmpty(harvest))
		{
			return;
		}
		string id = ItemRegistry.QualifyItemId(harvest) ?? ("(O)" + harvest);
		SendSprite(id);
		list.Add(Json.Object(Json.Str("id", id), Json.Str("name", CropName(crop)), Json.Str("location", place), Json.Num("daysLeft", Math.Max(0, days)), Json.Flag("needsWater", dirt.state.Value == 0 && !ready), Json.Flag("ready", ready)));
	}

	private void SendUi()
	{
		if (!Live)
		{
			return;
		}
		bool flag = _groundSeason != Game1.currentSeason;
		if (_artSent && !flag)
		{
			RequestPortrait();
			return;
		}
		Texture2D menuTexture = Game1.menuTexture;
		Texture2D mouseCursors = Game1.mouseCursors;
		List<string> missing = new List<string>();
		Piece("ui:panel", menuTexture, new Rectangle(0, 256, 60, 60), 20);
		Piece("ui:slot", menuTexture, Game1.getSourceRectForStandardTileSheet(menuTexture, 10, -1, -1));
		Piece("ui:slot_selected", menuTexture, Game1.getSourceRectForStandardTileSheet(menuTexture, 56, -1, -1));
		Piece("ui:heart", mouseCursors, new Rectangle(211, 428, 7, 6));
		Piece("ui:heart_empty", mouseCursors, new Rectangle(218, 428, 7, 6));
		Piece("ui:coin", mouseCursors, new Rectangle(280, 412, 20, 16));
		Piece("ui:arrow_left", mouseCursors, new Rectangle(352, 495, 12, 11));
		Piece("ui:arrow_right", mouseCursors, new Rectangle(365, 495, 12, 11));
		Piece("ui:scroll", mouseCursors, new Rectangle(325, 318, 25, 18), 12, 0);
		Piece("ui:check_off", mouseCursors, new Rectangle(227, 425, 9, 9));
		Piece("ui:check_on", mouseCursors, new Rectangle(236, 425, 9, 9));
		Piece("ui:button", mouseCursors, new Rectangle(432, 439, 9, 9), 3);
		Piece("ui:water_gauge", mouseCursors, new Rectangle(297, 420, 14, 5));
		Piece("ui:sort", mouseCursors, new Rectangle(162, 440, 16, 16));
		Piece("ui:season", mouseCursors, new Rectangle(406, 441 + Game1.seasonIndex * 8, 12, 8));
		if (Game1.weatherIcon == 999)
		{
			Piece("ui:time", Game1.mouseCursors_1_6, new Rectangle(243, 293, 12, 8));
		}
		else
		{
			Piece("ui:time", mouseCursors, new Rectangle(317 + 12 * Game1.weatherIcon, 421, 12, 8));
		}
		Piece("ui:tab_backpack", mouseCursors, new Rectangle(0, 368, 16, 16));
		Piece("ui:tab_skills", mouseCursors, new Rectangle(16, 368, 16, 16));
		Piece("ui:tab_relationships", mouseCursors, new Rectangle(32, 368, 16, 16));
		Piece("ui:tab_map", mouseCursors, new Rectangle(48, 368, 16, 16));
		Piece("ui:tab_crafting", mouseCursors, new Rectangle(64, 368, 16, 16));
		Piece("ui:tab_journal", mouseCursors, new Rectangle(80, 368, 16, 16));
		Piece("ui:tab_settings", mouseCursors, new Rectangle(96, 368, 16, 16));
		// Stardual: a parsnip for the Fields tab, so it doesn't share Calendar's season icon.
		ParsedItemData parsnip = ItemRegistry.GetDataOrErrorItem("(O)24");
		Piece("ui:tab_fields", parsnip.GetTexture(), parsnip.GetSourceRect());
		// Stardual: a stardrop for the Overview tab.
		ParsedItemData stardrop = ItemRegistry.GetDataOrErrorItem("(O)434");
		Piece("ui:tab_overview", stardrop.GetTexture(), stardrop.GetSourceRect());
		// Stardual: a warp totem for the Shortcuts tab.
		ParsedItemData totem = ItemRegistry.GetDataOrErrorItem("(O)688");
		Piece("ui:tab_shortcuts", totem.GetTexture(), totem.GetSourceRect());
		// Stardual: tab icons without the menu-tab frame, one per page ("ui:page_<PAGE>").
		// The Overview's is your own farmhouse.
		void ItemPiece(string page, params string[] ids)
		{
			foreach (string id in ids)
			{
				ParsedItemData? data = ItemRegistry.GetData(id);
				if (data == null) continue;
				Piece("ui:page_" + page, data.GetTexture(), data.GetSourceRect());
				return;
			}
		}
		try
		{
			Building? house = Game1.getFarm()?.GetMainFarmHouse();
			Texture2D? houseTexture = house?.texture?.Value;
			if (house != null && houseTexture != null)
				Piece("ui:page_OVERVIEW", houseTexture, house.getSourceRectForMenu() ?? house.getSourceRect());
			else
				ItemPiece("OVERVIEW", "(BC)Mini-Obelisk", "(O)434");
		}
		catch
		{
			ItemPiece("OVERVIEW", "(O)434");
		}
		ItemPiece("SHORTCUTS", "(O)688");
		ItemPiece("MAP", "(BC)37");
		// People is the farmer's face, like the game's Skills tab: see SendPlayerHead, which
		// sends it once the face is drawn (a bouquet until then).
		if (_peopleIcon != null) SendPeopleIcon();
		else ItemPiece("RELATIONSHIPS", "(O)458");
		ItemPiece("GIFT_GUIDE", "(O)221");
		// The pictures above the People page's checkboxes: a speech bubble for "talked today"
		// and a wrapped gift box (blue with a red ribbon, from LooseSprites/Giftbox) for the
		// week's two gifts.
		byte[]? talk = _sprites.EncodeRegion(Game1.mouseCursors, new Rectangle(66, 4, 14, 12));
		if (talk != null) _bridge.Send(Json.Message("sdv_sprite", Json.Str("id", "ui:slot_talk"), Json.Num("inset", 0), Json.Str("png", Convert.ToBase64String(talk))));
		try
		{
			Texture2D giftbox = Game1.content.Load<Texture2D>("LooseSprites\\Giftbox");
			byte[]? gift = _sprites.EncodeRegion(giftbox, new Rectangle(1, 80, 14, 16));
			if (gift != null) _bridge.Send(Json.Message("sdv_sprite", Json.Str("id", "ui:slot_gift"), Json.Num("inset", 0), Json.Str("png", Convert.ToBase64String(gift))));
		}
		catch
		{
		}
		ItemPiece("FIELDS", "(O)24");
		ItemPiece("CRAFTING", "(BC)208");
		ItemPiece("JOURNAL", "(O)102");
		ItemPiece("MACHINES", "(BC)12");
		ItemPiece("BUNDLES", "(O)373");
		// Animals: the White Cow's picture from Marnie's shop.
		try
		{
			if (Game1.farmAnimalData.TryGetValue("White Cow", out var cow) && !string.IsNullOrEmpty(cow.ShopTexture))
			{
				Texture2D shopCow = Game1.content.Load<Texture2D>(cow.ShopTexture);
				Piece("ui:page_ANIMALS", shopCow, cow.ShopSourceRect.IsEmpty ? shopCow.Bounds : cow.ShopSourceRect);
			}
			else
			{
				ItemPiece("ANIMALS", "(O)186");
			}
		}
		catch
		{
			ItemPiece("ANIMALS", "(O)186");
		}
		// Pokemon: Pikachu's picture from Marnie's shop (Pokemon Ranch), else an egg.
		try
		{
			if (Game1.farmAnimalData.TryGetValue("fpikachu", out var pika) && !string.IsNullOrEmpty(pika.ShopTexture))
			{
				Texture2D shop = Game1.content.Load<Texture2D>(pika.ShopTexture);
				Piece("ui:page_POKEMON", shop, pika.ShopSourceRect.IsEmpty ? shop.Bounds : pika.ShopSourceRect);
			}
			else
			{
				ItemPiece("POKEMON", "(O)176");
			}
		}
		catch
		{
			ItemPiece("POKEMON", "(O)176");
		}
		ItemPiece("CALENDAR", "(O)421");
		ItemPiece("PROGRESS", "(O)434");
		// Settings is the controller from the game's Options menu tab, cut out of the tab frame
		// (10 x 10 pixels, taken from LooseSprites/Cursors at 96,368).
		_bridge.Send(Json.Message("sdv_sprite", Json.Str("id", "ui:page_SETTINGS"), Json.Num("inset", 0), Json.Num("insetX", 0), Json.Num("insetY", 0), Json.Str("png", SettingsIcon)));
		if (!SendGround())
		{
			missing.Add("ui:ground");
		}
		SendGroundChoices();
		if (_config.SendMap && !SendMap())
		{
			missing.Add("ui:map");
		}
		RequestPortrait();
		_groundSeason = Game1.currentSeason;
		_artSent = true;
		if (missing.Count > 0)
		{
			Monitor.Log("Interface art this game version did not yield: " + string.Join(", ", missing) + " — the console draws those in its own theme.", LogLevel.Warn);
		}
		void Piece(string id, Texture2D? sheet, Rectangle rect, int inset = 0, int insetY = -1)
		{
			byte[] array = _sprites.EncodeRegion(sheet, rect);
			if (array == null)
			{
				missing.Add(id);
			}
			else
			{
				_bridge.Send(Json.Message("sdv_sprite", Json.Str("id", id), Json.Num("inset", inset), Json.Num("insetX", inset), Json.Num("insetY", (insetY < 0) ? inset : insetY), Json.Str("png", Convert.ToBase64String(array))));
			}
		}
	}

	private void RequestPortrait()
	{
		_portraitWanted = _config.SendMap && _config.FarmerMarker && !_portraitFailed;
		_farmerWanted = !_farmerFailed;
	}

	private bool SendGround()
	{
		if (_bridge == null)
		{
			return false;
		}
		string[] array = new string[2]
		{
			"Maps/" + Game1.currentSeason + "_outdoorsTileSheet",
			"Maps/spring_outdoorsTileSheet"
		};
		foreach (string text in array)
		{
			try
			{
				Texture2D texture2D = Game1.content.Load<Texture2D>(text);
				if (texture2D == null || texture2D.Width < 16 || texture2D.Height < 16)
				{
					continue;
				}
				int num = Math.Min(48, texture2D.Height);
				int width = texture2D.Width;
				Color[] array2 = new Color[width * num];
				texture2D.GetData(0, new Rectangle(0, 0, width, num), array2, 0, array2.Length);
				int num2 = -1;
				int num3 = 0;
				int num4 = int.MaxValue;
				for (int j = 0; j + 16 <= num; j += 16)
				{
					for (int k = 0; k + 16 <= width; k += 16)
					{
						if (Ground(array2, width, k, j, out var _, out var _, out var _, out var spread) && spread < num4)
						{
							num4 = spread;
							num2 = k;
							num3 = j;
						}
					}
				}
				if (num2 >= 0)
				{
					byte[] array3 = _sprites.EncodeRegion(texture2D, new Rectangle(num2, num3, 16, 16));
					if (array3 != null)
					{
						_bridge.Send(Json.Message("sdv_sprite", Json.Str("id", "ui:ground"), Json.Num("inset", 0), Json.Str("png", Convert.ToBase64String(array3))));
						Monitor.Log($"Backdrop is the tile at {num2},{num3} of \"{text}\".", LogLevel.Debug);
						return true;
					}
				}
			}
			catch
			{
			}
		}
		return false;
	}

	private void SendGroundChoices()
	{
		if (_bridge == null)
		{
			return;
		}
		List<(string, int, int)> list = new List<(string, int, int)>();
		string[] array = new string[5] { "Maps/walls_and_floors", "Maps/spring_outdoorsTileSheet", "Maps/summer_outdoorsTileSheet", "Maps/fall_outdoorsTileSheet", "Maps/winter_outdoorsTileSheet" };
		foreach (string text in array)
		{
			try
			{
				Texture2D texture2D = Game1.content.Load<Texture2D>(text);
				if (texture2D == null)
				{
					continue;
				}
				int num = Math.Min(512, texture2D.Height);
				int num2 = Math.Min(512, texture2D.Width);
				Color[] array2 = new Color[num2 * num];
				texture2D.GetData(0, new Rectangle(0, 0, num2, num), array2, 0, array2.Length);
				List<GroundCandidate> list2 = new List<GroundCandidate>();
				for (int j = 0; j + 16 <= num; j += 16)
				{
					for (int k = 0; k + 16 <= num2; k += 16)
					{
						if (Ground(array2, num2, k, j, out var red, out var green, out var blue, out var spread) && spread <= 20000)
						{
							list2.Add(new GroundCandidate(k, j, red, green, blue, spread));
						}
					}
				}
				foreach (GroundCandidate item in DiverseGroundTiles(list2, 8))
				{
					list.Add((text, item.X, item.Y));
				}
				goto IL_016e;
			}
			catch
			{
				goto IL_016e;
			}
			IL_016e:
			if (list.Count >= 40)
			{
				break;
			}
		}
		List<string> list3 = new List<string>();
		for (int l = 0; l < list.Count; l++)
		{
			(string, int, int) tuple = list[l];
			try
			{
				string text2 = GroundId(tuple.Item1, tuple.Item2, tuple.Item3);
				if (_sprites.AlreadySent(text2))
				{
					list3.Add(Json.Object(Json.Str("id", text2)));
					continue;
				}
				Texture2D sheet = Game1.content.Load<Texture2D>(tuple.Item1);
				byte[] array3 = _sprites.EncodeRegion(sheet, new Rectangle(tuple.Item2, tuple.Item3, 16, 16));
				if (array3 != null)
				{
					_bridge.Send(Json.Message("sdv_sprite", Json.Str("id", text2), Json.Num("inset", 0), Json.Str("png", Convert.ToBase64String(array3))));
					_sprites.Remember(text2);
					list3.Add(Json.Object(Json.Str("id", text2)));
				}
			}
			catch
			{
			}
		}
		_bridge.Broadcast("sdv_grounds", Json.Message("sdv_grounds", Json.Array("grounds", list3)));
		Monitor.Log($"Offered {list3.Count} backdrop tiles.", LogLevel.Debug);
	}

	private static List<GroundCandidate> DiverseGroundTiles(List<GroundCandidate> candidates, int limit)
	{
		if (candidates.Count <= limit)
		{
			return (from groundCandidate in candidates
				orderby groundCandidate.Luminance, groundCandidate.Y, groundCandidate.X
				select groundCandidate).ToList();
		}
		List<GroundCandidate> list = (from groundCandidate in candidates
			orderby groundCandidate.Y, groundCandidate.X
			select groundCandidate).ToList();
		List<GroundCandidate> list2 = new List<GroundCandidate>(limit);
		GroundCandidate item = (from groundCandidate in list
			orderby groundCandidate.Spread, Math.Abs(groundCandidate.Luminance - 384)
			select groundCandidate).First();
		list2.Add(item);
		list.Remove(item);
		while (list2.Count < limit && list.Count > 0)
		{
			GroundCandidate item2 = list[0];
			long num = -1L;
			foreach (GroundCandidate candidate in list)
			{
				long num2 = list2.Min((GroundCandidate already) => GroundDistance(candidate, already));
				if (num2 > num)
				{
					num = num2;
					item2 = candidate;
				}
			}
			list2.Add(item2);
			list.Remove(item2);
		}
		return (from groundCandidate in list2
			orderby groundCandidate.Luminance, groundCandidate.Spread, groundCandidate.Y, groundCandidate.X
			select groundCandidate).ToList();
	}

	private static long GroundDistance(GroundCandidate left, GroundCandidate right)
	{
		int num = left.Red - right.Red;
		int num2 = left.Green - right.Green;
		int num3 = left.Blue - right.Blue;
		int num4 = (left.Spread - right.Spread) / 128;
		return (long)num * (long)num + (long)num2 * (long)num2 + (long)num3 * (long)num3 + (long)num4 * (long)num4;
	}

	private static string GroundId(string sheet, int x, int y)
	{
		string value = sheet.Replace("Maps/", "").Replace("_outdoorsTileSheet", "");
		return $"ui:ground:{value}:{x}:{y}";
	}

	private static bool Ground(Color[] pixels, int stride, int tx, int ty, out int red, out int green, out int blue, out int spread)
	{
		red = 0;
		green = 0;
		blue = 0;
		spread = 0;
		for (int i = 0; i < 16; i++)
		{
			for (int j = 0; j < 16; j++)
			{
				Color color = pixels[(ty + i) * stride + tx + j];
				if (color.A < byte.MaxValue)
				{
					return false;
				}
				red += color.R;
				green += color.G;
				blue += color.B;
			}
		}
		red /= 256;
		green /= 256;
		blue /= 256;
		int num = red + green + blue;
		for (int k = 0; k < 16; k++)
		{
			for (int l = 0; l < 16; l++)
			{
				Color color2 = pixels[(ty + k) * stride + tx + l];
				spread += Math.Abs(color2.R + color2.G + color2.B - num);
			}
		}
		return spread >= 256;
	}

	private bool SendMap()
	{
		if (_bridge == null)
		{
			return false;
		}
		foreach (string item in MapAssetNames())
		{
			try
			{
				Texture2D texture2D = Game1.content.Load<Texture2D>(item);
				if (texture2D != null)
				{
					Rectangle source = new Rectangle(0, 0, Math.Min(300, texture2D.Width), Math.Min(180, texture2D.Height));
					byte[] array = _sprites.EncodeRegion(texture2D, source);
					if (array != null)
					{
						_bridge.Send(Json.Message("sdv_sprite", Json.Str("id", "ui:map"), Json.Num("inset", 0), Json.Str("png", Convert.ToBase64String(array))));
						_mapWidth = source.Width;
						_mapHeight = source.Height;
						Monitor.Log($"Map sent from \"{item}\", {source.Width}x{source.Height}, {array.Length / 1024}KB.", LogLevel.Info);
						return true;
					}
					Monitor.Log($"Map \"{item}\" loaded at {texture2D.Width}x{texture2D.Height} " + "but its pixels could not be read.", LogLevel.Warn);
				}
			}
			catch
			{
			}
		}
		return false;
	}

	private void OnRendered(object? sender, RenderedEventArgs e)
	{
		if ((!_portraitWanted && !_farmerWanted) || !Live)
		{
			return;
		}
		if (_portraitWanted)
		{
			_portraitWanted = false;
			if (!SendPlayerHead())
			{
				_portraitFailed = true;
			}
		}
		if (_farmerWanted)
		{
			// Stardual: a draw can come out empty (for example right after a save loads or
			// the app reconnects, before the farmer's textures are ready). Try again on later
			// frames instead of giving up for the rest of the session.
			if (_farmerDelay > 0)
			{
				_farmerDelay--;
			}
			else if (SendFarmer())
			{
				_farmerWanted = false;
				_farmerTries = 0;
			}
			else if (++_farmerTries >= 20)
			{
				_farmerWanted = false;
				_farmerFailed = true;
				_farmerTries = 0;
				Monitor.Log("Gave up drawing the farmer for the backpack page after 20 tries.", LogLevel.Warn);
			}
			else
			{
				_farmerDelay = 15;
			}
		}
	}

	private bool SendFarmer()
	{
		if (_bridge == null || Game1.player == null)
		{
			return false;
		}
		RenderTarget2D renderTarget2D = null;
		SpriteBatch spriteBatch = null;
		GraphicsDevice graphicsDevice = null;
		RenderTargetBinding[] array = null;
		try
		{
			graphicsDevice = Game1.graphics?.GraphicsDevice;
			if (graphicsDevice == null)
			{
				return false;
			}
			array = graphicsDevice.GetRenderTargets();
			renderTarget2D = new RenderTarget2D(graphicsDevice, 96, 144);
			graphicsDevice.SetRenderTarget(renderTarget2D);
			graphicsDevice.Clear(Color.Transparent);
			spriteBatch = new SpriteBatch(graphicsDevice);
			spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp);
			FarmerRenderer.isDrawingForUI = true;
			// Stardual: draw the farmer the way the game's inventory page does: front-facing
			// body frame, facing down, no current animation. The old call used the farmer's
			// current facing direction for hair, face and accessories while always drawing the
			// front body, so the picture came out mismatched whenever they faced another way.
			bool bathing = Game1.player.bathingClothes.Value;
			int frame = bathing ? 108 : 0;
			// Eyes open, even if the farmer happens to be mid-blink.
			int eyes = Game1.player.currentEyes;
			Game1.player.currentEyes = 0;
			try
			{
				Game1.player.FarmerRenderer.draw(spriteBatch, new FarmerSprite.AnimationFrame(frame, 0, false, false), frame, new Rectangle(0, bathing ? 576 : 0, 16, 32), new Vector2(16f, 8f), Vector2.Zero, 0.0001f, 2, Color.White, 0f, 1f, Game1.player);
			}
			finally
			{
				Game1.player.currentEyes = eyes;
			}
			FarmerRenderer.isDrawingForUI = false;
			spriteBatch.End();
			if (array.Length != 0)
			{
				graphicsDevice.SetRenderTargets(array);
			}
			else
			{
				graphicsDevice.SetRenderTarget(null);
			}
			Color[] array2 = new Color[13824];
			renderTarget2D.GetData(array2);
			int num = array2.Count((Color pixel) => pixel.A > 8);
			if (num < 128)
			{
				Monitor.Log($"Farmer for the backpack page drew {num} visible pixels; trying again.", LogLevel.Trace);
				return false;
			}
			using Texture2D texture2D = new Texture2D(graphicsDevice, 96, 144);
			texture2D.SetData(array2);
			using MemoryStream memoryStream = new MemoryStream();
			texture2D.SaveAsPng(memoryStream, 96, 144);
			Monitor.Log($"Farmer for the backpack page sent, {num} visible pixels.", LogLevel.Info);
			_bridge.Send(Json.Message("sdv_sprite", Json.Str("id", "ui:farmer"), Json.Num("inset", 0), Json.Str("png", Convert.ToBase64String(memoryStream.ToArray()))));
			return true;
		}
		catch (Exception ex)
		{
			Monitor.Log("Could not render the full farmer: " + ex.Message, LogLevel.Warn);
			return false;
		}
		finally
		{
			FarmerRenderer.isDrawingForUI = false;
			if (graphicsDevice != null)
			{
				try
				{
					if (array != null && array.Length > 0)
					{
						graphicsDevice.SetRenderTargets(array);
					}
					else
					{
						graphicsDevice.SetRenderTarget(null);
					}
				}
				catch
				{
				}
			}
			spriteBatch?.Dispose();
			renderTarget2D?.Dispose();
		}
	}

	private byte[]? _peopleIcon;

	private const string SettingsIcon = "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAKCAYAAACNMs+9AAAAWklEQVR4nGNgQAO2trb/0cWwAqIVElQMksSGMRRlZ2f/7500GQWDxOCKYYrQFYPYDB3/EYrRrYIpdDpiBlZ0l8EaoRCXif8/MKCaCOLAMMxkdDEMH2MLBRAfAOD9gbFjEnQRAAAAAElFTkSuQmCC";

	private void SendPeopleIcon()
	{
		_bridge.Send(Json.Message("sdv_sprite", Json.Str("id", "ui:page_RELATIONSHIPS"), Json.Num("inset", 0), Json.Num("insetX", 0), Json.Num("insetY", 0), Json.Str("png", Convert.ToBase64String(_peopleIcon!))));
	}

	private bool SendPlayerHead()
	{
		if (_bridge == null || Game1.player == null)
		{
			return false;
		}
		RenderTarget2D renderTarget2D = null;
		SpriteBatch spriteBatch = null;
		GraphicsDevice graphicsDevice = null;
		RenderTargetBinding[] array = null;
		try
		{
			graphicsDevice = Game1.graphics?.GraphicsDevice;
			if (graphicsDevice == null)
			{
				return false;
			}
			array = graphicsDevice.GetRenderTargets();
			renderTarget2D = new RenderTarget2D(graphicsDevice, 64, 64);
			graphicsDevice.SetRenderTarget(renderTarget2D);
			graphicsDevice.Clear(Color.Transparent);
			spriteBatch = new SpriteBatch(graphicsDevice);
			spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp);
			Game1.player.FarmerRenderer.drawMiniPortrat(spriteBatch, Vector2.Zero, 0.0001f, 4f, 2, Game1.player, 1f);
			spriteBatch.End();
			if (array.Length != 0)
			{
				graphicsDevice.SetRenderTargets(array);
			}
			else
			{
				graphicsDevice.SetRenderTarget(null);
			}
			Color[] array2 = new Color[4096];
			renderTarget2D.GetData(array2);
			int num = 0;
			Color[] array3 = array2;
			foreach (Color color in array3)
			{
				if (color.A > 8)
				{
					num++;
				}
			}
			if (num < 64)
			{
				Monitor.Log($"The farmer's portrait rendered {num} visible pixels, which is " + "not a face — the console will mark the map with its own dot instead.", LogLevel.Warn);
				return false;
			}
			using Texture2D texture2D = new Texture2D(graphicsDevice, 64, 64);
			texture2D.SetData(array2);
			using MemoryStream memoryStream = new MemoryStream();
			texture2D.SaveAsPng(memoryStream, 64, 64);
			byte[] array4 = memoryStream.ToArray();
			_bridge.Send(Json.Message("sdv_sprite", Json.Str("id", "ui:player"), Json.Num("inset", 0), Json.Str("png", Convert.ToBase64String(array4))));
			// Stardual: the People tab's icon is the same face, cropped to its edges.
			_peopleIcon = _sprites.EncodeCropped(graphicsDevice, array2, 64, 64);
			if (_peopleIcon != null) SendPeopleIcon();
			Monitor.Log($"Farmer portrait sent, {num} visible pixels, {array4.Length} bytes.", LogLevel.Info);
			return true;
		}
		catch (Exception ex)
		{
			Monitor.Log("Could not render the farmer's portrait: " + ex.Message, LogLevel.Warn);
			return false;
		}
		finally
		{
			if (graphicsDevice != null)
			{
				try
				{
					if (array != null && array.Length > 0)
					{
						graphicsDevice.SetRenderTargets(array);
					}
					else
					{
						graphicsDevice.SetRenderTarget(null);
					}
				}
				catch
				{
				}
			}
			spriteBatch?.Dispose();
			renderTarget2D?.Dispose();
		}
	}

	private static IEnumerable<string> MapAssetNames()
	{
		yield return "LooseSprites/map";
		yield return "LooseSprites\\map";
	}

	private void Clear(string sender)
	{
		string text = sender switch
		{
			"SendCrops" => "sdv_crops", 
			"SendMachines" => "sdv_machines", 
			"SendAnimals" => "sdv_animals", 
			"SendBundles" => "sdv_bundles", 
			"SendVillagers" => "sdv_villagers", 
			"SendGifts" => "sdv_gifts", 
			"SendCrafting" => "sdv_crafting", 
			"SendCooking" => "sdv_cooking", 
			"SendTrees" => "sdv_trees", 
			_ => "", 
		};
		string key = sender switch
		{
			"SendCrops" => "crops", 
			"SendMachines" => "machines", 
			"SendAnimals" => "animals", 
			"SendBundles" => "bundles", 
			"SendVillagers" => "villagers", 
			"SendGifts" => "villagers", 
			"SendCrafting" => "recipes", 
			"SendCooking" => "recipes", 
			"SendTrees" => "trees", 
			_ => "", 
		};
		if (text.Length != 0)
		{
			_bridge?.Broadcast(text, Json.Message(text, Json.Array(key, new List<string>())));
		}
	}

	private void SendConfig()
	{
		_bridge?.Broadcast("sdv_config", Json.Message("sdv_config", Json.Flag("hideGameHud", _config.HideGameHud), Json.Flag("allowInventoryEdits", _config.AllowInventoryEdits), Json.Flag("allowQuestCancel", _config.AllowQuestCancel), Json.Flag("farmerMarker", _config.FarmerMarker), Json.Flag("sendCrops", _config.SendCrops), Json.Flag("sendMachines", _config.SendMachines), Json.Flag("sendAnimals", _config.SendAnimals), Json.Flag("sendBundles", _config.SendBundles), Json.Flag("sendVillagers", _config.SendVillagers), Json.Flag("sendMap", _config.SendMap), Json.Flag("sendCrafting", _config.SendCrafting)));
	}

	private void SendSkills()
	{
		if (Live)
		{
			_bridge.Broadcast("sdv_skills", Json.Message("sdv_skills", Json.Num("farming", Game1.player.FarmingLevel), Json.Num("mining", Game1.player.MiningLevel), Json.Num("foraging", Game1.player.ForagingLevel), Json.Num("fishing", Game1.player.FishingLevel), Json.Num("combat", Game1.player.CombatLevel), Json.Num("farmingNext", ToNextLevel(0)), Json.Num("miningNext", ToNextLevel(3)), Json.Num("foragingNext", ToNextLevel(2)), Json.Num("fishingNext", ToNextLevel(1)), Json.Num("combatNext", ToNextLevel(4))));
		}
	}

	private static int ToNextLevel(int skill)
	{
		try
		{
			int num = Game1.player.experiencePoints[skill];
			int[] levelThresholds = LevelThresholds;
			foreach (int num2 in levelThresholds)
			{
				if (num < num2)
				{
					return num2 - num;
				}
			}
			return 0;
		}
		catch
		{
			return -1;
		}
	}

	private void SendGifts()
	{
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		if (!Live)
		{
			return;
		}
		if (!_config.SendVillagers)
		{
			Clear("SendGifts");
			return;
		}
		HashSet<string> hashSet = new HashSet<string>();
		foreach (Item item in Game1.player.Items)
		{
			if (item != null)
			{
				hashSet.Add(item.QualifiedItemId);
			}
		}
		// Stardual: loved gifts in a chest or the fridge count too. The app is told which ones
		// are in the backpack and which are stored (and where) by "sdv_gift_places".
		Dictionary<string, string> stored = StoredItems();
		var bagOnly = new HashSet<string>(hashSet);
		foreach (string id in stored.Keys) hashSet.Add(id);
		List<string> list = new List<string>();
		foreach (string item2 in ((IEnumerable<string>)(object)((NetDictionary<string, Friendship, NetRef<Friendship>, SerializableDictionary<string, Friendship>, NetStringDictionary<Friendship, NetRef<Friendship>>>)(object)Game1.player.friendshipData).Keys).ToList())
		{
			try
			{
				if (!Game1.NPCGiftTastes.TryGetValue(item2, out var value))
				{
					continue;
				}
				string[] array = value.Split('/');
				if (array.Length < 2)
				{
					continue;
				}
				List<(string, string, bool)> list2 = new List<(string, string, bool)>();
				HashSet<string> hashSet2 = new HashSet<string>();
				string[] array2 = array[1].Split(' ');
				foreach (string text in array2)
				{
					if (string.IsNullOrWhiteSpace(text))
					{
						continue;
					}
					string text2 = ItemRegistry.QualifyItemId(text) ?? ("(O)" + text);
					if (hashSet2.Add(text2))
					{
						ParsedItemData data = ItemRegistry.GetData(text2);
						if (data != null)
						{
							list2.Add((text2, data.DisplayName, hashSet.Contains(text2)));
						}
					}
				}
				List<string> list3 = new List<string>();
				foreach (var item3 in list2.OrderByDescending(((string Id, string Name, bool Carried) candidate) => candidate.Carried).ThenBy(((string Id, string Name, bool Carried) candidate) => candidate.Name).Take(8))
				{
					SendSprite(item3.Item1);
					list3.Add(Json.Object(Json.Str("id", item3.Item1), Json.Str("name", item3.Item2), Json.Flag("carried", item3.Item3)));
				}
				if (list3.Count != 0)
				{
					NPC characterFromName = Game1.getCharacterFromName(item2, true, false);
					list.Add(Json.Object(Json.Str("name", ((characterFromName != null) ? ((Character)characterFromName).displayName : null) ?? item2), Json.Flag("carrying", list2.Any(((string Id, string Name, bool Carried) candidate) => candidate.Carried)), Json.Array("loves", list3)));
				}
			}
			catch
			{
			}
		}
		_bridge.Broadcast("sdv_gifts", Json.Message("sdv_gifts", Json.Array("villagers", list)));
		var bagIds = bagOnly.Select(id => "\"" + Json.Escape(id) + "\"").ToList();
		var storedList = stored.Where(p => !bagOnly.Contains(p.Key)).Select(p => Json.Object(Json.Str("id", p.Key), Json.Str("where", p.Value))).ToList();
		_bridge.Broadcast("sdv_gift_places", Json.Message("sdv_gift_places", "\"backpack\":[" + string.Join(",", bagIds) + "]", Json.Array("stored", storedList)));
	}

	/// <summary>Stardual: every item in a chest or fridge, with where it is ("Chest in the Shed").</summary>
	private static Dictionary<string, string> StoredItems()
	{
		var stored = new Dictionary<string, string>();
		void Add(Item? item, string where)
		{
			if (item != null && !stored.ContainsKey(item.QualifiedItemId)) stored[item.QualifiedItemId] = where;
		}
		try
		{
			Utility.ForEachLocation(location =>
			{
				string place = LocationName(location);
				foreach (StardewValley.Object obj in location.objects.Values)
				{
					if (obj is StardewValley.Objects.Chest chest && chest.playerChest.Value)
					{
						foreach (Item? item in chest.Items) Add(item, "Chest in " + place);
					}
				}
				if (location is StardewValley.Locations.FarmHouse house && house.fridge.Value != null)
				{
					foreach (Item? item in house.fridge.Value.Items) Add(item, "Fridge");
				}
				return true;
			});
		}
		catch
		{
		}
		return stored;
	}

	private void SendCrafting()
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Expected Obj, but got Unknown
		if (!Live)
		{
			return;
		}
		if (!_config.SendCrafting)
		{
			Clear("SendCrafting");
			return;
		}
		List<string> list = new List<string>();
		try
		{
			foreach (string item in ((IEnumerable<string>)(object)((NetDictionary<string, int, NetInt, SerializableDictionary<string, int>, NetStringDictionary<int, NetInt>>)(object)Game1.player.craftingRecipes).Keys).ToList())
			{
				try
				{
					CraftingRecipe val = new CraftingRecipe(item, false);
					string text = val.GetItemData(true)?.QualifiedItemId ?? "";
					SendSprite(text);
					List<string> list2 = new List<string>();
					foreach (KeyValuePair<string, int> recipe in val.recipeList)
					{
						string spriteIndexFromRawIndex = val.getSpriteIndexFromRawIndex(recipe.Key);
						string text2 = ItemRegistry.QualifyItemId(spriteIndexFromRawIndex) ?? spriteIndexFromRawIndex;
						SendSprite(text2);
						int itemCount = Game1.player.getItemCount(recipe.Key);
						list2.Add(Json.Object(Json.Str("id", text2 ?? ""), Json.Str("name", val.getNameFromIndex(recipe.Key)), Json.Num("have", itemCount), Json.Num("required", recipe.Value)));
					}
					list.Add(Json.Object(Json.Str("id", text), Json.Str("name", val.DisplayName), Json.Str("key", item), Json.Str("description", val.description ?? ""), Json.Flag("ready", val.doesFarmerHaveIngredientsInInventory((IList<Item>)null)), Json.Num("crafted", val.timesCrafted), Json.Num("outputCount", val.numberProducedPerCraft), Json.Array("ingredients", list2)));
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		_bridge.Broadcast("sdv_crafting", Json.Message("sdv_crafting", Json.Array("recipes", list)));
	}

	private void CraftRecipe(string key)
	{
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Expected Obj, but got Unknown
		if (string.IsNullOrWhiteSpace(key) || !((NetDictionary<string, int, NetInt, SerializableDictionary<string, int>, NetStringDictionary<int, NetInt>>)(object)Game1.player.craftingRecipes).ContainsKey(key))
		{
			return;
		}
		try
		{
			CraftingRecipe recipe = new CraftingRecipe(key, false);
			if (!recipe.doesFarmerHaveIngredientsInInventory((IList<Item>)null))
			{
				return;
			}
			Item crafted = recipe.createItem();
			if (crafted != null && Game1.player.couldInventoryAcceptThisItem(crafted.QualifiedItemId, crafted.Stack, crafted.Quality))
			{
				recipe.consumeIngredients(new List<IInventory>());
				Item val = Game1.player.addItemToInventory(crafted);
				if (val != null)
				{
					Monitor.Log("Inventory changed while crafting " + recipe.DisplayName + "; the remainder was dropped safely.", LogLevel.Warn);
					Game1.createItemDebris(val, ((Character)Game1.player).getStandingPosition(), ((Character)Game1.player).FacingDirection, (GameLocation)null, -1, false);
				}
				Game1.player.NotifyQuests((Func<Quest, bool>)((Quest quest) => quest.OnRecipeCrafted(recipe, crafted, false)), false);
				NetStringDictionary<int, NetInt> craftingRecipes = Game1.player.craftingRecipes;
				int num = ((NetDictionary<string, int, NetInt, SerializableDictionary<string, int>, NetStringDictionary<int, NetInt>>)(object)craftingRecipes)[key];
				((NetDictionary<string, int, NetInt, SerializableDictionary<string, int>, NetStringDictionary<int, NetInt>>)(object)craftingRecipes)[key] = num + 1;
				Game1.stats.checkForCraftingAchievements();
				Game1.playSound("coin", (int?)null);
				_lastInventoryHash = InventoryHash();
				SendInventory();
				SendCrafting();
				SendQuests();
			}
		}
		catch (Exception ex)
		{
			Monitor.Log("Could not craft \"" + key + "\": " + ex.Message, LogLevel.Warn);
		}
	}

	private void SendCalendar()
	{
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		if (!Live)
		{
			return;
		}
		List<string> list = new List<string>();
		try
		{
			string currentSeason = Game1.currentSeason;
			Dictionary<int, List<string>> dictionary = new Dictionary<int, List<string>>();
			Dictionary<int, string> dictionary2 = new Dictionary<int, string>();
			foreach (string item in ((IEnumerable<string>)(object)((NetDictionary<string, Friendship, NetRef<Friendship>, SerializableDictionary<string, Friendship>, NetStringDictionary<Friendship, NetRef<Friendship>>>)(object)Game1.player.friendshipData).Keys).ToList())
			{
				NPC characterFromName = Game1.getCharacterFromName(item, true, false);
				if (characterFromName != null && string.Equals(characterFromName.Birthday_Season, currentSeason, StringComparison.OrdinalIgnoreCase) && characterFromName.Birthday_Day > 0)
				{
					if (!dictionary.TryGetValue(characterFromName.Birthday_Day, out var value))
					{
						value = new List<string>();
						dictionary[characterFromName.Birthday_Day] = value;
					}
					value.Add(((Character)characterFromName).displayName ?? item);
					if (!dictionary2.ContainsKey(characterFromName.Birthday_Day))
					{
						dictionary2[characterFromName.Birthday_Day] = ((Character)characterFromName).Name ?? item;
					}
					SendPortrait(characterFromName);
				}
			}
			for (int i = 1; i <= 28; i++)
			{
				string text = "";
				try
				{
					if (Utility.isFestivalDay(i, Game1.season))
					{
						text = FestivalName(i);
					}
				}
				catch
				{
				}
				dictionary.TryGetValue(i, out var value2);
				if (text.Length != 0 || (value2 != null && value2.Count != 0))
				{
					list.Add(Json.Object(Json.Num("day", i), Json.Str("festival", text), Json.Str("festivalWhen", (text.Length == 0) ? "" : FestivalWhen(i)), Json.Str("festivalWhere", (text.Length == 0) ? "" : FestivalWhere(i)), Json.Str("names", (value2 == null) ? "" : string.Join(", ", value2)), Json.Str("npc", dictionary2.TryGetValue(i, out var value3) ? value3 : "")));
				}
			}
		}
		catch
		{
		}
		_bridge.Broadcast("sdv_calendar", Json.Message("sdv_calendar", Json.Str("season", Capitalise(Game1.currentSeason)), Json.Num("today", Game1.dayOfMonth), Json.Array("days", list)));
	}

	private static string FestivalName(int day)
	{
		try
		{
			string text = Game1.currentSeason + day;
			Dictionary<string, string> dictionary = Game1.temporaryContent.Load<Dictionary<string, string>>("Data\\Festivals\\" + text);
			if (dictionary != null && dictionary.TryGetValue("name", out var value))
			{
				return value;
			}
		}
		catch
		{
		}
		return "Festival";
	}

	private static string FestivalField(int day, int index)
	{
		try
		{
			string text = Game1.currentSeason + day;
			Dictionary<string, string> dictionary = Game1.temporaryContent.Load<Dictionary<string, string>>("Data\\Festivals\\" + text);
			if (dictionary != null && dictionary.TryGetValue("conditions", out var value))
			{
				string[] array = value.Split('/');
				if (index < array.Length)
				{
					return array[index];
				}
			}
		}
		catch
		{
		}
		return "";
	}

	private static string FestivalWhere(int day)
	{
		return FestivalField(day, 0);
	}

	private static string FestivalWhen(int day)
	{
		string[] array = FestivalField(day, 1).Split(' ');
		if (array.Length < 2)
		{
			return "";
		}
		return Clock(array[0]) + " to " + Clock(array[1]);
	}

	private static string Clock(string raw)
	{
		if (!int.TryParse(raw, out var result))
		{
			return "";
		}
		int num = result / 100 % 24;
		int num2 = result % 100;
		string value = ((num < 12) ? "am" : "pm");
		int value2 = ((num % 12 == 0) ? 12 : (num % 12));
		if (num2 == 0)
		{
			return $"{value2}{value}";
		}
		return $"{value2}:{num2:00}{value}";
	}

	private void SendOrders()
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		if (!Live)
		{
			return;
		}
		List<string> list = new List<string>();
		try
		{
			var enumerator = Game1.player.team.specialOrders.GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					SpecialOrder current = enumerator.Current;
					if (current == null)
					{
						continue;
					}
					string value = "";
					try
					{
						var enumerator2 = current.objectives.GetEnumerator();
						try
						{
							while (enumerator2.MoveNext())
							{
								OrderObjective current2 = enumerator2.Current;
								string text = ((current2 != null) ? current2.GetDescription() : null);
								if (!string.IsNullOrWhiteSpace(text))
								{
									value = StripMarkup(text);
									break;
								}
							}
						}
						finally
						{
							((IDisposable)enumerator2/*cast due to constrained. prefix*/).Dispose();
						}
					}
					catch
					{
					}
					list.Add(Json.Object(Json.Str("title", StripMarkup(current.GetName())), Json.Str("objective", value), Json.Num("daysLeft", current.GetDaysLeft())));
				}
			}
			finally
			{
				((IDisposable)enumerator/*cast due to constrained. prefix*/).Dispose();
			}
		}
		catch
		{
		}
		_bridge.Broadcast("sdv_orders", Json.Message("sdv_orders", Json.Array("orders", list)));
	}

	private void SendPortrait(NPC npc)
	{
		string text = "npc:" + ((Character)npc).Name;
		if (_bridge == null || _sprites.AlreadySent(text))
		{
			return;
		}
		try
		{
			Texture2D portrait = npc.Portrait;
			if (portrait != null)
			{
				byte[] array = _sprites.EncodeRegion(portrait, new Rectangle(0, 0, 64, 64));
				if (array != null)
				{
					_sprites.Remember(text);
					_bridge.Send(Json.Message("sdv_sprite", Json.Str("id", text), Json.Num("inset", 0), Json.Str("png", Convert.ToBase64String(array))));
				}
			}
		}
		catch
		{
		}
	}

	private void SendCart()
	{
		if (!Live)
		{
			return;
		}
		bool value = false;
		try
		{
			GameLocation locationFromName = Game1.getLocationFromName("Forest");
			Forest val = (Forest)(object)((locationFromName is Forest) ? locationFromName : null);
			if (val != null)
			{
				value = val.travelingMerchantDay;
			}
		}
		catch
		{
		}
		_bridge.Broadcast("sdv_cart", Json.Message("sdv_cart", Json.Flag("open", value)));
	}

	private void SendCooking()
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Expected Obj, but got Unknown
		if (!Live)
		{
			return;
		}
		if (!_config.SendCrafting)
		{
			Clear("SendCooking");
			return;
		}
		List<string> list = new List<string>();
		try
		{
			foreach (string item in ((IEnumerable<string>)(object)((NetDictionary<string, int, NetInt, SerializableDictionary<string, int>, NetStringDictionary<int, NetInt>>)(object)Game1.player.cookingRecipes).Keys).ToList())
			{
				try
				{
					CraftingRecipe val = new CraftingRecipe(item, true);
					if (val.doesFarmerHaveIngredientsInInventory((IList<Item>)null))
					{
						string indexOfMenuView = val.getIndexOfMenuView();
						string text = ItemRegistry.QualifyItemId(indexOfMenuView) ?? indexOfMenuView;
						if (text != null)
						{
							SendSprite(text);
						}
						list.Add(Json.Object(Json.Str("id", text ?? ""), Json.Str("name", val.DisplayName)));
						if (list.Count >= 24)
						{
							break;
						}
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		_bridge.Broadcast("sdv_cooking", Json.Message("sdv_cooking", Json.Array("recipes", list)));
	}

	private void SendShipping()
	{
		if (!Live)
		{
			return;
		}
		List<string> list = new List<string>();
		int num = 0;
		try
		{
			Farm farm = Game1.getFarm();
			IInventory val = ((farm != null) ? farm.getShippingBin(Game1.player) : null);
			if (val != null)
			{
				foreach (Item item in (IEnumerable<Item>)val)
				{
					if (item == null)
					{
						continue;
					}
					int num2 = 0;
					try
					{
						Object val2 = (Object)(object)((item is Object) ? item : null);
						if (val2 != null)
						{
							num2 = ((Item)val2).sellToStorePrice(-1L) * item.Stack;
						}
					}
					catch
					{
					}
					num += num2;
					SendSprite(item.QualifiedItemId);
					if (list.Count < 12)
					{
						list.Add(Json.Object(Json.Str("id", item.QualifiedItemId), Json.Str("name", item.DisplayName), Json.Num("count", item.Stack), Json.Num("worth", num2)));
					}
				}
			}
		}
		catch
		{
		}
		_bridge.Broadcast("sdv_shipping", Json.Message("sdv_shipping", Json.Num("total", num), Json.Array("items", list)));
	}

	private void SendTrees()
	{
		if (!Live)
		{
			return;
		}
		if (!_config.SendCrops)
		{
			Clear("SendTrees");
			return;
		}
		List<string> trees = new List<string>();
		try
		{
			Utility.ForEachLocation((Func<GameLocation, bool>)((GameLocation location) =>
			{
				//IL_0006: Unknown result type (might be due to invalid IL or missing references)
				foreach (TerrainFeature item in ((IEnumerable<TerrainFeature>)(object)((NetDictionary<Vector2, TerrainFeature, NetRef<TerrainFeature>, SerializableDictionary<Vector2, TerrainFeature>, NetVector2Dictionary<TerrainFeature, NetRef<TerrainFeature>>>)(object)location.terrainFeatures).Values).ToList())
				{
					FruitTree val = (FruitTree)(object)((item is FruitTree) ? item : null);
					if (val != null)
					{
						int num = 0;
						try
						{
							num = val.fruit.Count;
						}
						catch
						{
						}
						if (num > 0)
						{
							string text = "";
							string value = "";
							try
							{
								Item val2 = val.fruit[0];
								if (val2 != null)
								{
									text = val2.QualifiedItemId;
									value = ItemRegistry.GetData(text)?.DisplayName ?? "";
									SendSprite(text);
								}
							}
							catch
							{
							}
							trees.Add(Json.Object(Json.Str("id", text), Json.Str("name", value), Json.Str("location", LocationName(location)), Json.Num("count", num)));
						}
					}
				}
				return true;
			}), true, false);
		}
		catch
		{
		}
		_bridge.Broadcast("sdv_trees", Json.Message("sdv_trees", Json.Array("trees", trees)));
	}

	private void SendPets()
	{
		if (!Live)
		{
			return;
		}
		string value = "";
		bool value2 = false;
		bool value3 = false;
		try
		{
			Pet pet = Game1.player.getPet();
			if (pet != null)
			{
				value = ((Character)pet).displayName ?? ((Character)pet).Name ?? "";
				SendPetSprite(pet);
				value2 = ((NetDictionary<long, int, NetInt, SerializableDictionary<long, int>, NetLongDictionary<int, NetInt>>)(object)pet.lastPetDay).ContainsKey(Game1.player.UniqueMultiplayerID) && ((NetDictionary<long, int, NetInt, SerializableDictionary<long, int>, NetLongDictionary<int, NetInt>>)(object)pet.lastPetDay)[Game1.player.UniqueMultiplayerID] == Game1.Date.TotalDays;
			}
			Farm farm = Game1.getFarm();
			if (farm != null)
			{
				foreach (Building building in ((GameLocation)farm).buildings)
				{
					PetBowl val = (PetBowl)(object)((building is PetBowl) ? building : null);
					if (val != null && ((NetFieldBase<bool, NetBool>)(object)val.watered).Value)
					{
						value3 = true;
						break;
					}
				}
			}
		}
		catch
		{
		}
		_bridge.Broadcast("sdv_pet", Json.Message("sdv_pet", Json.Str("name", value), Json.Flag("petted", value2), Json.Flag("bowl", value3)));
	}

	private void SendPetSprite(Pet pet)
	{
		if (_bridge == null || _sprites.AlreadySent("ui:pet"))
		{
			return;
		}
		try
		{
			AnimatedSprite sprite = ((Character)pet).Sprite;
			if (sprite != null)
			{
				Rectangle source = new Rectangle(0, 0, sprite.SpriteWidth, sprite.SpriteHeight);
				byte[] array = _sprites.EncodeRegion(sprite.Texture, source);
				if (array != null)
				{
					_sprites.Remember("ui:pet");
					_bridge.Send(Json.Message("sdv_sprite", Json.Str("id", "ui:pet"), Json.Num("inset", 0), Json.Str("png", Convert.ToBase64String(array))));
				}
			}
		}
		catch
		{
		}
	}

	private void SendMines()
	{
		if (!Live)
		{
			return;
		}
		int num = 0;
		int value = 0;
		try
		{
			num = Game1.player.deepestMineLevel;
			if (num > 120)
			{
				value = num - 120;
			}
		}
		catch
		{
		}
		_bridge.Broadcast("sdv_mines", Json.Message("sdv_mines", Json.Num("deepest", Math.Min(num, 120)), Json.Num("skull", value)));
	}

	private void SendCollections()
	{
		if (!Live)
		{
			return;
		}
		int num = 0;
		List<string> list = new List<string>();
		try
		{
			GameLocation locationFromName = Game1.getLocationFromName("ArchaeologyHouse");
			LibraryMuseum val = (LibraryMuseum)(object)((locationFromName is LibraryMuseum) ? locationFromName : null);
			if (val != null)
			{
				num = ((NetDictionary<Vector2, string, NetString, SerializableDictionary<Vector2, string>, NetVector2Dictionary<string, NetString>>)(object)val.museumPieces).Count();
				if (_lastDonated != num)
				{
					_lastDonated = num;
					Monitor.Log($"Museum: {num} pieces donated.", LogLevel.Debug);
				}
				foreach (Item item in Game1.player.Items)
				{
					if (item != null && val.isItemSuitableForDonation(item))
					{
						SendSprite(item.QualifiedItemId);
						list.Add(Json.Object(Json.Str("id", item.QualifiedItemId), Json.Str("name", item.DisplayName)));
					}
				}
			}
		}
		catch (Exception ex)
		{
			Monitor.Log("Could not read the museum: " + ex.Message, LogLevel.Warn);
		}
		_bridge.Broadcast("sdv_collections", Json.Message("sdv_collections", Json.Num("donated", num), Json.Num("total", 95), Json.Array("carried", list)));
	}

	private static string WeatherToday()
	{
		if (Game1.isLightning)
		{
			return "Storm";
		}
		if (Game1.isSnowing)
		{
			return "Snow";
		}
		if (Game1.isRaining)
		{
			return "Rain";
		}
		if (Game1.isDebrisWeather)
		{
			return "Windy";
		}
		return "Sunny";
	}

	private static string WeatherTomorrow()
	{
		try
		{
			Type typeFromHandle = typeof(Game1);
			string text = (typeFromHandle.GetProperty("weatherForTomorrow")?.GetValue(null) ?? typeFromHandle.GetField("weatherForTomorrow")?.GetValue(null))?.ToString();
			if (string.IsNullOrEmpty(text))
			{
				return "";
			}
			return text switch
			{
				"Rain" => "Rain", 
				"Storm" => "Storm", 
				"Snow" => "Snow", 
				"Wind" => "Windy", 
				"Festival" => "Festival", 
				"Sun" => "Sunny", 
				_ => Capitalise(text), 
			};
		}
		catch
		{
			return "";
		}
	}

	// Stardual: inside a building, the building's name ("Pokemon Coop"); otherwise a readable name.
	private static string LocationName(GameLocation location)
	{
		return Names.Place(location);
	}

	private static string RoomName(string key)
	{
		return key switch
		{
			"Pantry" => "Pantry", 
			"Crafts Room" => "Crafts Room", 
			"Fish Tank" => "Fish Tank", 
			"Boiler Room" => "Boiler Room", 
			"Vault" => "Vault", 
			"Bulletin Board" => "Bulletin Board", 
			"Abandoned Joja Mart" => "Joja Mart", 
			_ => key, 
		};
	}

	private static string CropName(Crop crop)
	{
		try
		{
			Item val = ItemRegistry.Create("(O)" + ((NetFieldBase<string, NetString>)(object)crop.indexOfHarvest).Value, 1, 0, false);
			return ((val != null) ? val.DisplayName : null) ?? "Crop";
		}
		catch
		{
			return "Crop";
		}
	}

	private int InventoryHash()
	{
		int num = 17;
		foreach (Item item in Game1.player.Items)
		{
			if (item == null)
			{
				num *= 31;
				continue;
			}
			num = num * 31 + item.QualifiedItemId.GetHashCode();
			num = num * 31 + item.Stack;
			WateringCan val = (WateringCan)(object)((item is WateringCan) ? item : null);
			if (val != null)
			{
				num = num * 31 + val.WaterLeft;
				num = num * 31 + val.waterCanMax;
				num = num * 31 + (val.IsBottomless ? 1 : 0);
			}
		}
		return num;
	}

	private static string StripMarkup(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}
		string text2 = text.Replace("^", " ").Trim();
		if (text2.Length <= 240)
		{
			return text2;
		}
		return text2.Substring(0, 240);
	}

	private static string Capitalise(string value)
	{
		if (!string.IsNullOrEmpty(value))
		{
			return char.ToUpperInvariant(value[0]) + value.Substring(1);
		}
		return "";
	}
}
