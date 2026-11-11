using System;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace StickCursor;

/// <summary>
/// Brings back the console-style cursor on Android: moving the right stick shows the game's
/// pointer and moves it around the screen, and the game treats the tile under it as the
/// hovered tile (for placing things, Lookup Anything and so on). It fades after a few seconds
/// without stick movement, like on the Switch.
/// </summary>
internal sealed class ModEntry : Mod
{
    private const float DeadZone = 0.2f;
    private const int ShowMilliseconds = 4000;

    private ModConfig _config = new();
    private Vector2 _raw;          // cursor position in screen pixels
    private int _visibleFor;       // milliseconds left before it fades
    private bool _placed;          // _raw has been set since the cursor last faded
    private bool _claimed;         // the game has been told the cursor is showing

    public override void Entry(IModHelper helper)
    {
        _config = helper.ReadConfig<ModConfig>();
        helper.Events.GameLoop.UpdateTicking += OnTicking;
        helper.Events.GameLoop.UpdateTicked += OnTick;
        helper.Events.Display.Rendered += OnRendered;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
#if PC
        // The PC game treats A as a click on any HUD box under the (hidden) mouse pointer.
        // Cinderbox opens the main menu when the date/time box is clicked, so A often opened
        // the menu instead of talking or picking things up. A only clicks the HUD while our
        // pointer is showing; taps and real clicks are unaffected.
        _instance = this;
        var harmony = new Harmony(ModManifest.UniqueID + ".hud");
        foreach (var method in new[]
        {
            AccessTools.Method(typeof(StardewValley.Menus.IClickableMenu), nameof(StardewValley.Menus.IClickableMenu.isWithinBounds)),
            AccessTools.DeclaredMethod(typeof(StardewValley.Menus.DayTimeMoneyBox), nameof(StardewValley.Menus.IClickableMenu.isWithinBounds)),
            AccessTools.DeclaredMethod(typeof(StardewValley.Menus.Toolbar), nameof(StardewValley.Menus.IClickableMenu.isWithinBounds)),
        })
        {
            if (method != null)
            {
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(ModEntry), nameof(BeforeHudHitTest)));
            }
        }
        // Last line of defense: A on its own never opens the main menu, whatever route the
        // game or another mod takes to get there. Anything else A opens is logged.
        MethodInfo? setMenu = AccessTools.PropertySetter(typeof(Game1), nameof(Game1.activeClickableMenu));
        if (setMenu != null)
        {
            harmony.Patch(setMenu, prefix: new HarmonyMethod(typeof(ModEntry), nameof(BeforeMenuOpens)));
        }
        // Android draws its own placement preview; the PC game under Cinderbox does not.
        helper.Events.Display.RenderedWorld += OnRenderedWorld;
        // So Many Toolbars: keep its pinned items really in their slots (see PinnedSlots).
        PinnedSlots.HideTooltips = _config.HideToolbarTooltips;
        helper.Events.GameLoop.GameLaunched += (_, _) => PinnedSlots.Setup(helper, Monitor, harmony);
        helper.Events.GameLoop.GameLaunched += (_, _) => AButton.Setup(helper, Monitor);
        helper.Events.GameLoop.UpdateTicked += (_, e) => { if (e.IsMultipleOf(15)) PinnedSlots.Enforce(); };
        // Notice when the game draws its own preview, so the two never show together.
        new Harmony(ModManifest.UniqueID).Patch(
            AccessTools.Method(typeof(StardewValley.Object), nameof(StardewValley.Object.drawPlacementBounds)),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(BeforePlacementBounds)));
#else
        // L2 / R2 reach the game's own pressSwitchToolButton however Android delivers them,
        // and that wraps at slot 12. Replace it so they walk the whole backpack instead.
        var harmony = new Harmony(ModManifest.UniqueID);
        harmony.Patch(AccessTools.Method(typeof(Game1), nameof(Game1.pressSwitchToolButton)),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(BeforeSwitchTool)));
#endif
    }

    private static ModEntry? _instance;

    /// <summary>A HUD box asks whether the mouse is over it. Say no when that "click" is really A.</summary>
    private static bool BeforeHudHitTest(StardewValley.Menus.IClickableMenu __instance, ref bool __result)
    {
        try
        {
            if (Game1.activeClickableMenu != null || Game1.onScreenMenus == null || !Game1.onScreenMenus.Contains(__instance))
            {
                return true;
            }
            if (_instance != null && _instance._visibleFor > 0)
            {
                return true; // the pointer is showing: pointing at the HUD is on purpose
            }
            GamePadState pad = Game1.input.GetGamePadState();
            if (pad.IsButtonDown(Buttons.A) && Game1.input.GetMouseState().LeftButton != ButtonState.Pressed)
            {
                __result = false;
                return false;
            }
        }
        catch
        {
        }
        return true;
    }

    private static int _aPressedAt = -100;

    /// <summary>A menu is about to open. Stop the main menu when the only button behind it is A.</summary>
    private static bool BeforeMenuOpens(StardewValley.Menus.IClickableMenu value)
    {
        try
        {
            if (value == null || Game1.activeClickableMenu != null || !Context.IsWorldReady || _instance == null)
            {
                return true;
            }
            GamePadState pad = Game1.input.GetGamePadState();
            bool fromA = pad.IsButtonDown(Buttons.A) || Game1.ticks - _aPressedAt <= 3;
            if (!fromA)
            {
                return true;
            }
            bool menuButton = pad.IsButtonDown(Buttons.Y) || pad.IsButtonDown(Buttons.B)
                || pad.IsButtonDown(Buttons.Start) || pad.IsButtonDown(Buttons.Back);
            bool realClick = Game1.input.GetMouseState().LeftButton == ButtonState.Pressed;
            bool pointer = _instance._visibleFor > 0;
            string from = string.Join(" < ", new System.Diagnostics.StackTrace(1).GetFrames()
                .Select(f => f.GetMethod())
                .Where(m => m != null && m.DeclaringType != null)
                .Take(8)
                .Select(m => m!.DeclaringType!.Name + "." + m.Name));
            bool block = value is StardewValley.Menus.GameMenu && !menuButton && !realClick && !pointer;
            _instance.Monitor.Log(
                $"A opened {value.GetType().Name} (pointer {pointer}, mouse {Game1.getMouseX()},{Game1.getMouseY()}, "
                + $"click {realClick}){(block ? " - blocked" : "")}: {from}", LogLevel.Debug);
            return !block;
        }
        catch
        {
            return true;
        }
    }

    private static bool CanUse() =>
        Context.IsWorldReady && Game1.activeClickableMenu == null && !Game1.eventUp
        && Game1.currentMinigame == null && Game1.CurrentEvent == null;

    private void OnTicking(object? sender, UpdateTickingEventArgs e)
    {
        if (_visibleFor > 0 && CanUse())
        {
            ClaimCursor();
        }
#if PC
        else if (CanUse() && Context.IsPlayerFree && Game1.input.GetGamePadState().IsButtonDown(Buttons.A))
        {
            // A with no pointer showing: the game would act on the tile under the hidden
            // mouse (wherever the last tap was) when that tile is next to the player, so A
            // missed things in front of you. Say the mouse isn't in use, so A uses the tile
            // you're facing, like on a console.
            Game1.wasMouseVisibleThisFrame = false;
            Game1.mouseCursorTransparency = 0f;
            Game1.lastCursorMotionWasMouse = false;
        }
#endif
    }

    /// <summary>
    /// The game only aims the action button (A) at the cursor's tile when it believes the
    /// cursor is visible and was the last thing moved. Android never draws the cursor, so
    /// it never believes that; say so while ours is showing.
    /// </summary>
    private void ClaimCursor()
    {
        Game1.timerUntilMouseFade = ShowMilliseconds;
        Game1.mouseCursorTransparency = 1f;
        Game1.wasMouseVisibleThisFrame = true;
        Game1.lastCursorMotionWasMouse = true;
        _claimed = true;
    }

    /// <summary>Hand A back to the tile in front of the player.</summary>
    private void ReleaseCursor()
    {
        if (!_claimed)
        {
            return;
        }
        Game1.lastCursorMotionWasMouse = false;
        Game1.wasMouseVisibleThisFrame = false;
        Game1.timerUntilMouseFade = 0;
        _claimed = false;
    }

#if !PC
    /// <summary>Move the toolbar selection one slot, wrapping around the whole backpack (Android's toolbar).</summary>
    private static void StepToolbar(int dir)
    {
        Farmer player = Game1.player;
        int slots = Math.Max(1, player.MaxItems);
        player.CurrentToolIndex = ((player.CurrentToolIndex + dir) % slots + slots) % slots;
        ShowSelected(page: false);
        Game1.playSound("toolSwap");
        if (player.ActiveObject != null)
        {
            player.showCarrying();
        }
        else
        {
            player.showNotCarrying();
        }
    }

    /// <summary>
    /// Scroll Android's toolbar so the selected slot is on screen. A page jump (L1 / R1) puts
    /// the selected slot at the left edge; a one-slot step only scrolls when it goes off screen.
    /// </summary>
    private static void ShowSelected(bool page)
    {
        StardewValley.Menus.Toolbar? toolbar = StardewValley.Menus.Toolbar.GetToolbar();
        if (toolbar == null)
        {
            return;
        }
        int index = Game1.player.CurrentToolIndex;
        int visible = Math.Max(1, toolbar.maxVisibleItems);
        if (page)
        {
            toolbar.startIndex = index;
        }
        else if (index < toolbar.startIndex)
        {
            toolbar.startIndex = index;
        }
        else if (index >= toolbar.startIndex + visible)
        {
            toolbar.startIndex = index - visible + 1;
        }
    }

    /// <summary>
    /// Replaces Game1.pressSwitchToolButton when a trigger is down: step one slot through the
    /// whole backpack (Android's toolbar), wrapping at the ends, and keep it on screen. The
    /// game still handles the timing, including repeat while the trigger is held. Anything
    /// else (a mouse wheel) goes to the game's own code.
    /// </summary>
    private static bool BeforeSwitchTool()
    {
        try
        {
            GamePadState pad = Game1.input.GetGamePadState();
            int dir = pad.IsButtonDown(Buttons.LeftTrigger) ? -1 : pad.IsButtonDown(Buttons.RightTrigger) ? 1 : 0;
            if (dir == 0 || Game1.player == null)
            {
                return true;
            }
            if (Game1.player.netItemStowed.Value)
            {
                Game1.player.netItemStowed.Set(false);
                Game1.player.UpdateItemStow();
            }
            StepToolbar(dir);
            return false;
        }
        catch
        {
            return true;
        }
    }
#endif

    private void OnTick(object? sender, UpdateTickedEventArgs e)
    {

        if (!CanUse())
        {
            _visibleFor = 0;
            _placed = false;
            ReleaseCursor();
            return;
        }
        int elapsed = (int)Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;
        Vector2 stick = GamePad.GetState(PlayerIndex.One).ThumbSticks.Right;
        if (stick.Length() > DeadZone)
        {
            if (!_placed)
            {
                // Start on the player, the way the console cursor reappears.
                Vector2 local = Game1.player.getLocalPosition(Game1.viewport) + new Vector2(32f, -32f);
                _raw = local * Game1.options.zoomLevel;
                _placed = true;
            }
            float speed = _config.Speed * Game1.viewport.Height * Game1.options.zoomLevel / 720f;
            _raw.X += stick.X * speed * elapsed / 1000f;
            _raw.Y -= stick.Y * speed * elapsed / 1000f;
            Rectangle screen = Game1.graphics.GraphicsDevice.Viewport.Bounds;
            _raw.X = Math.Clamp(_raw.X, 0, screen.Width - 1);
            _raw.Y = Math.Clamp(_raw.Y, 0, screen.Height - 1);
            _visibleFor = ShowMilliseconds;
        }
        else if (_visibleFor > 0)
        {
            _visibleFor -= elapsed;
            if (_visibleFor <= 0)
            {
                _placed = false;
                ReleaseCursor();
            }
        }
        if (_visibleFor > 0)
        {
            // Keep the game's idea of the mouse on the cursor so it uses the tile under it.
            Game1.setMousePositionRaw((int)_raw.X, (int)_raw.Y);
            ClaimCursor();
        }
    }

    /// <summary>The tile under the cursor.</summary>
    private Vector2 CursorTile()
    {
        Vector2 world = _raw / Game1.options.zoomLevel + new Vector2(Game1.viewport.X, Game1.viewport.Y);
        return new Vector2((int)(world.X / 64f), (int)(world.Y / 64f));
    }

    /// <summary>
    /// Android's action button always uses the tile in front of the player, whatever the cursor
    /// says. While the cursor is showing, A is handled here instead: check (talk, open, pick up,
    /// harvest...) or place the held item at the cursor's tile, using the game's own code and
    /// range limits. If neither applies, A goes through to the game as normal.
    /// </summary>
    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        // Toolbar: L2 / R2 step one slot and wrap around (held, they repeat); L1 jumps to
        // the first slot, and R1 jumps 12 slots further each press (back to the start after the end).
#if !PC
        if (Context.IsPlayerFree && Game1.activeClickableMenu == null)
        {
            if (e.Button == SButton.LeftShoulder && _config.L1JumpsToFirstSlot)
            {
                Helper.Input.Suppress(e.Button);
                if (Game1.player.CurrentToolIndex != 0)
                {
                    Game1.player.CurrentToolIndex = 0;
                    Game1.playSound("toolSwap");
                }
                ShowSelected(page: true);
                return;
            }
            if (e.Button == SButton.RightShoulder && _config.R1JumpsToLastItem)
            {
                Helper.Input.Suppress(e.Button);
                // Android's toolbar is the whole backpack in one scrolling row: each press
                // jumps 12 slots further, and from the last slot it goes back to the start.
                Farmer farmer = Game1.player;
                int last = Math.Max(0, farmer.MaxItems - 1);
                int target = farmer.CurrentToolIndex >= last ? 0 : Math.Min(farmer.CurrentToolIndex + 12, last);
                if (farmer.CurrentToolIndex != target)
                {
                    farmer.CurrentToolIndex = target;
                    Game1.playSound("toolSwap");
                }
                ShowSelected(page: true);
                return;
            }
        }
#else
        // The PC game's L1 / R1 rotate the whole toolbar to the next backpack row, which
        // looked like the toolbar reshuffling itself. Do what they do on Android instead:
        // L1 jumps to the first slot, R1 to the last item on the toolbar.
        if (Context.IsPlayerFree && Game1.activeClickableMenu == null)
        {
            if (e.Button == SButton.LeftShoulder && _config.L1JumpsToFirstSlot)
            {
                Helper.Input.Suppress(e.Button);
                if (Game1.player.CurrentToolIndex != 0)
                {
                    Game1.player.CurrentToolIndex = 0;
                    Game1.playSound("toolSwap");
                }
                return;
            }
            if (e.Button == SButton.RightShoulder && _config.R1JumpsToLastItem)
            {
                Helper.Input.Suppress(e.Button);
                Farmer farmer = Game1.player;
                int slots = Math.Min(Math.Max(1, _config.ToolbarSlots), farmer.Items.Count);
                int target = 0;
                for (int i = slots - 1; i >= 0; i--)
                {
                    if (farmer.Items[i] != null) { target = i; break; }
                }
                if (farmer.CurrentToolIndex != target)
                {
                    farmer.CurrentToolIndex = target;
                    Game1.playSound("toolSwap");
                }
                return;
            }
        }
#endif
        if (e.Button == SButton.ControllerA)
        {
            _aPressedAt = Game1.ticks;
#if PC
            if (Context.IsWorldReady && Game1.activeClickableMenu == null) AButton.OnAPressed();
#endif
        }
        if (e.Button != SButton.ControllerA || _visibleFor <= 0 || !CanUse() || !Context.IsPlayerFree)
        {
            return;
        }
        Farmer player = Game1.player;
        GameLocation location = Game1.currentLocation;
        Vector2 tile = CursorTile();
        Monitor.Log($"A at cursor tile {tile} (player at {player.Tile})", LogLevel.Trace);
        if (Game1.tryToCheckAt(tile, player))
        {
            Helper.Input.Suppress(e.Button);
            return;
        }
        StardewValley.Object? held = player.ActiveObject;
        if (held != null && (held.isPlaceable() || held is StardewValley.Objects.Furniture))
        {
            int x = (int)tile.X * 64 + 32;
            int y = (int)tile.Y * 64 + 32;
            if (Utility.tryToPlaceItem(location, held, x, y))
            {
                Helper.Input.Suppress(e.Button);
            }
        }
    }

    private bool _loggedPreview;
    private static bool _drawingOurs;
    private static int _gameDrewAt = -100;

    private static void BeforePlacementBounds()
    {
        if (!_drawingOurs)
        {
            _gameDrewAt = Game1.ticks;
        }
    }

    /// <summary>
    /// Show where the held item would go: a green (fits) or red (blocked) square with a faded
    /// copy of the item, at the pointer while it is showing, otherwise in front of the player.
    /// The game's own preview only draws alongside a mouse cursor, which Cinderbox hides.
    /// </summary>
    private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
    {
        if (!_config.ShowPlacementPreview || !CanUse() || !Context.IsPlayerFree)
        {
            return;
        }
        Farmer player = Game1.player;
        StardewValley.Object? held = player.ActiveObject;
        if (held == null || !player.CanMove || player.isRidingHorse() || !Game1.displayFarmer)
        {
            return;
        }
        if (!_loggedPreview)
        {
            _loggedPreview = true;
            Monitor.Log($"Preview: mouseCursor={Game1.mouseCursor} transparency={Game1.mouseCursorTransparency} gamepad={Game1.options.gamepadControls}", LogLevel.Trace);
        }
        if (Game1.ticks - _gameDrewAt <= 2)
        {
            return; // the game is already showing it
        }
        try
        {
            _drawingOurs = true;
            held.drawPlacementBounds(e.SpriteBatch, Game1.currentLocation);
        }
        catch (Exception ex)
        {
            Monitor.LogOnce($"Could not draw the placement preview: {ex.Message}", LogLevel.Warn);
        }
        finally
        {
            _drawingOurs = false;
        }
    }

    private void OnRendered(object? sender, RenderedEventArgs e)
    {
        if (_visibleFor <= 0 || !CanUse())
        {
            return;
        }
        SpriteBatch batch = e.SpriteBatch;
        batch.End();
        Game1.PushUIMode();
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        float alpha = Math.Min(1f, _visibleFor / 500f);
        Vector2 at = _raw / Game1.options.uiScale;
        Rectangle source = Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, Game1.cursor_default, 16, 16);
        batch.Draw(Game1.mouseCursors, at, source, Color.White * alpha, 0f, Vector2.Zero,
            4f * _config.Scale, SpriteEffects.None, 1f);
        batch.End();
        Game1.PopUIMode();
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
    }
}

internal sealed class ModConfig
{
    /// <summary>How fast the cursor moves, in game-screen heights per second at full tilt (720 = one screen).</summary>
    public float Speed { get; set; } = 900f;

    /// <summary>Cursor size multiplier.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>Show a green or red square where the held item would be placed.</summary>
    public bool ShowPlacementPreview { get; set; } = true;

    /// <summary>L1 jumps to the first toolbar slot (instead of paging the backpack rows backwards).</summary>
#if PC
    // The PC game's L1 / R1 page through the backpack rows; keep that unless asked.
    public bool L1JumpsToFirstSlot { get; set; } = false;
#else
    public bool L1JumpsToFirstSlot { get; set; } = true;
#endif

    /// <summary>R1 jumps 12 slots along the toolbar each press, wrapping to the start.</summary>
#if PC
    public bool R1JumpsToLastItem { get; set; } = false;
#else
    public bool R1JumpsToLastItem { get; set; } = true;
#endif

    /// <summary>PC: how many slots the toolbar shows (R1 jumps to the last item among them).</summary>
    public int ToolbarSlots { get; set; } = 12;

    /// <summary>PC: hide So Many Toolbars' item tooltip, which shows in the top-left corner under Cinderbox.</summary>
    public bool HideToolbarTooltips { get; set; } = true;
}
