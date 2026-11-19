using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using ReLogic.Content;
using ReLogic.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace MobileJoystick;

// The original select states own loading, migration, compatibility and all save
// operations. This view changes presentation and routes completed gestures to
// their original event handlers. No independent save-file implementation.
public class MobileSelectionSystem : ModSystem
{
	private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly FieldInfo PlayerList = typeof(UICharacterSelect).GetField("_playerList", Hidden | BindingFlags.Public);
	private static readonly FieldInfo WorldList = typeof(UIWorldSelect).GetField("_worldList", Hidden | BindingFlags.Public);
	private static readonly MethodInfo WorldIcon = typeof(AWorldListItem).GetMethod("GetIcon", Hidden);
	private static readonly MethodInfo WorldDifficulty = typeof(AWorldListItem).GetMethod("GetDifficulty", Hidden);
	private readonly Dictionary<(Type, string), MethodInfo> methods = new();
	private readonly Dictionary<UICharacterListItem, UICharacter> previews = new();
	private readonly Dictionary<string, Texture2D> textures = new();
	private readonly RasterizerState clip = new() { ScissorTestEnable = true, CullMode = CullMode.None };
	private UIState state;
	private UIElement selected, pressedItem;
	private List<UIElement> items = new();
	private int touchId = -1, pressAction = -1, wheel;
	private bool nativeView, down, waitForRelease = true, dragging, escapeDown;
	private Vector2 pointer, origin, lastPointer;
	private float scroll, maximumScroll;
	private MobileSelectionLayout layout;
	private static MobileSelectionSystem Instance => ModContent.GetInstance<MobileSelectionSystem>();
	internal static void SuppressEscapeUntilRelease() => Instance.escapeDown = true;
	private bool IsHostBrowser => Main.MenuUI?.CurrentState is UIWorldSelect && MobileMultiplayerSystem.HostWorldSelection;
	private bool IsSelection => Main.gameMenu && Main.menuMode == 888 && Main.MenuUI?.CurrentState is UICharacterSelect or UIWorldSelect;
	private bool Enabled => PlayerList != null && WorldList != null && WorldIcon != null && WorldDifficulty != null
		&& MobileJoystickConfig.Instance?.EnableMobileControls == true && !PlayerInput.UsingGamepadUI;
	internal static bool Active => !Main.dedServ && Main.MenuUI != null
		&& ModContent.GetInstance<MobileSelectionSystem>() is { } instance
		&& instance.Enabled && instance.IsSelection && !instance.nativeView;
	private static string T(string zh, string en) => MobileSettingsCatalog.Text(zh, en);

	public override void Load()
	{
		if (Main.dedServ) return;
		On_UserInterface.Draw += DrawNative;
		On_UserInterface.Update += UpdateNative;
		On_Main.DrawMenu += DrawMenu;
	}
	public override void Unload()
	{
		if (Main.dedServ) return;
		On_UserInterface.Draw -= DrawNative; On_UserInterface.Update -= UpdateNative; On_Main.DrawMenu -= DrawMenu;
		clip.Dispose(); previews.Clear(); textures.Clear(); methods.Clear();
	}
	private void DrawNative(On_UserInterface.orig_Draw orig, UserInterface self, SpriteBatch sb, GameTime time)
	{ if (self != Main.MenuUI || !Active) orig(self, sb, time); }
	private void UpdateNative(On_UserInterface.orig_Update orig, UserInterface self, GameTime time)
	{
		if (self != Main.MenuUI || !Active) { orig(self, time); return; }
		// Update cloned player previews and the native list, without forwarding
		// screen coordinates to hidden desktop buttons.
		self.CurrentState.Update(time);
	}

	private void RefreshItems()
	{
		var list = (UIList)(state is UICharacterSelect ? PlayerList : WorldList).GetValue(state);
		items = list._items.Where(i => i is UICharacterListItem or UIWorldListItem).ToList();
		if (selected != null && !items.Contains(selected)) selected = null;
		if (selected == null && items.Count > 0) selected = items[0];
		maximumScroll = Math.Max(0, items.Sum(CardHeight) + Math.Max(0, items.Count - 1) * 5 - layout.List.Height / layout.Scale);
		scroll = Math.Clamp(scroll, 0, maximumScroll);
	}
	private int CardHeight(UIElement item) => layout.CardHeight(item == selected);
	private void CancelPress() { down = dragging = false; touchId = pressAction = -1; pressedItem = null; waitForRelease = true; }
	public override void PostUpdateInput()
	{
		if (!Enabled || !IsSelection) { state = null; nativeView = false; previews.Clear(); CancelPress(); return; }
		layout = new(PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight, IsHostBrowser, Main.MenuUI.CurrentState is UIWorldSelect);
		if (state != Main.MenuUI.CurrentState) {
			state = Main.MenuUI.CurrentState; nativeView = false; selected = null; scroll = 0; previews.Clear(); CancelPress();
		}
		RefreshItems();
		if (!Main.hasFocus) { CancelPress(); return; }
		bool escape = Keyboard.GetState().IsKeyDown(Keys.Escape);
		if (escape && !escapeDown && !nativeView) { escapeDown = escape; InvokeState("GoBackClick"); return; }
		escapeDown = escape;
		bool previous = down, cancelled = false;
		var mouse = Mouse.GetState();
		if (touchId < 0) pointer = new(mouse.X, mouse.Y);
		down = mouse.LeftButton == ButtonState.Pressed;
		TouchCollection touches = MobileTouchInput.GetState();
		if (touchId < 0) foreach (TouchLocation t in touches) if (t.State == TouchLocationState.Pressed) { touchId = t.Id; break; }
		if (touchId >= 0) {
			bool found = false; down = false;
			foreach (TouchLocation t in touches) {
				if (t.Id != touchId) continue;
				found = true;
				int width = TouchPanel.DisplayWidth > 0 ? TouchPanel.DisplayWidth : PlayerInput.RealScreenWidth;
				int height = TouchPanel.DisplayHeight > 0 ? TouchPanel.DisplayHeight : PlayerInput.RealScreenHeight;
				pointer = new(t.Position.X * PlayerInput.RealScreenWidth / width, t.Position.Y * PlayerInput.RealScreenHeight / height);
				cancelled = t.State == TouchLocationState.Invalid; down = !cancelled && t.State != TouchLocationState.Released; break;
			}
			cancelled |= !found; if (!down) touchId = -1;
		}
		int delta = mouse.ScrollWheelValue - wheel; wheel = mouse.ScrollWheelValue;
		if (waitForRelease) { if (!down) waitForRelease = false; return; }
		if (cancelled) { CancelPress(); return; }
		if (!nativeView && layout.List.Contains(pointer.ToPoint()) && delta != 0) scroll = Math.Clamp(scroll - delta / 120f * 48, 0, maximumScroll);
		if (down && !previous) {
			origin = lastPointer = pointer; dragging = false;
			(pressAction, pressedItem) = Hit(pointer);
		}
		if (down && !nativeView && layout.List.Contains(origin.ToPoint())) {
			if (Vector2.DistanceSquared(origin, pointer) > 64 * layout.Scale * layout.Scale) dragging = true;
			if (dragging) scroll = Math.Clamp(scroll + (lastPointer.Y - pointer.Y) / layout.Scale, 0, maximumScroll);
		}
		lastPointer = pointer;
		if (!down && previous) {
			var hit = Hit(pointer);
			if (!dragging && pressAction >= 0 && hit.Item1 == pressAction && hit.Item2 == pressedItem) Execute(pressAction, pressedItem);
			pressAction = -1; pressedItem = null; dragging = false;
		}
	}

	private (int, UIElement) Hit(Vector2 point)
	{
		if (layout.Native.Contains(point.ToPoint())) return (-1, null);
		if (nativeView) return (-1, null);
		if (IsHostBrowser) {
            var browser = new MobileMultiplayerLayout(PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight);
            if (browser.JoinTab.Contains(point.ToPoint())) return (9, null);
        }
        if (layout.Back.Contains(point.ToPoint())) return (0, null);
		if (layout.New.Contains(point.ToPoint())) return (1, null);
		if (!layout.List.Contains(point.ToPoint())) return (-1, null);
		float top = -scroll;
		foreach (UIElement item in items) {
			bool expanded = item == selected;
			Rectangle card = layout.Card(top, expanded); top += CardHeight(item) + 5;
			if (!card.Contains(point.ToPoint())) continue;
			if (expanded) {
				if (layout.Play(card).Contains(point.ToPoint())) return (4, item);
				for (int i = 0; i < (item is UIWorldListItem ? 4 : 3); i++) if (layout.Action(card, i).Contains(point.ToPoint())) return (5 + i, item);
			}
			return (3, item);
		}
		return (-1, null);
	}
	private MethodInfo Method(Type type, string name)
	{
		if (!methods.TryGetValue((type, name), out var method)) methods[(type, name)] = method = type.GetMethod(name, Hidden);
		return method;
	}
	private void InvokeState(string name)
	{
		Invoke(state, name, state); CancelPress();
	}
	private void Invoke(object target, string name, UIElement element)
	{
		var method = Method(target.GetType(), name);
		if (method == null) { nativeView = true; Mod.Logger.Warn($"Mobile selection missing {target.GetType().Name}.{name}; using native view."); return; }
		method.Invoke(target, new object[] { new UIMouseEvent(element, pointer / Main.UIScale), element });
	}
	private void Execute(int action, UIElement item)
	{
		if (action == 9) { MobileMultiplayerSystem.ShowBrowser(); CancelPress(); return; }
		if (action == 0) { InvokeState("GoBackClick"); return; }
		if (action == 1) { InvokeState(state is UICharacterSelect ? "NewCharacterClick" : "NewWorldClick"); return; }
		if (action == 3) { selected = selected == item ? null : item; RefreshItems(); SoundEngine.PlaySound(SoundID.MenuTick); return; }
		if (action == 5 && (item is UICharacterListItem p && p.IsFavorite || item is UIWorldListItem w && w.IsFavorite)) return;
		if (action == 6) {
			// Reuse the real image button because the native favorite callback updates
			// its texture and delete-handler subscription.
			var button = item.Children.OfType<UIImageButton>().FirstOrDefault(b => b.GetSnapPoint(out var snap) && snap.Name == "Favorite");
			if (button != null) Invoke(item, "FavoriteButtonClick", button);
			return;
		}
		if (action == 8 && item is UIWorldListItem world && world.Data.WorldGeneratorVersion == 0) return;
		Invoke(item, action switch { 4 => "PlayGame", 5 => "DeleteButtonClick", 7 => "RenameButtonClick", _ => "SeedButtonClick" }, item);
		CancelPress();
	}

	private void DrawMenu(On_Main.orig_DrawMenu orig, Main self, GameTime time)
	{
		orig(self, time);
		if (!Enabled || !IsSelection || state != Main.MenuUI.CurrentState) return;
		layout = new(PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight, IsHostBrowser, state is UIWorldSelect);
		Begin();
		try {
			if (!nativeView) DrawSelection(time);
		} finally { Main.spriteBatch.End(); }
	}
	private static void Begin() => Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise);
	private void DrawSelection(GameTime time)
	{
		if (IsHostBrowser) MobileMultiplayerSystem.DrawFrame(new(PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight), true);
		else {
			Nine("MultiplayerPanel", layout.Panel, Color.White);
			Nine("MultiplayerTitle", layout.Title, Color.White); Label(state is UICharacterSelect ? T("选择玩家", "Select Player") : T("选择世界", "Select World"), layout.Title, 1.5f, display: true);
		}
		Button(T("返回", "Back"), layout.Back, 1.2f, "Back");
		Button(T("新建", "New"), layout.New, 1.2f, "+");
		Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, layout.At(367, 474, 546, 2), new Color(12, 20, 42, 150));
		Rectangle oldClip = Main.instance.GraphicsDevice.ScissorRectangle;
		Main.spriteBatch.End(); Main.instance.GraphicsDevice.ScissorRectangle = layout.List;
		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, clip);
		try {
			float top = -scroll;
			foreach (UIElement item in items) {
				bool expanded = selected == item;
				Rectangle card = layout.Card(top, expanded); top += CardHeight(item) + 5;
				if (card.Intersects(layout.List)) DrawCard(item, card, time);
			}
			if (items.Count == 0) Label(T("点击新建开始游戏", "Create a save to start"), layout.List, 1);
		} finally { Main.spriteBatch.End(); Main.instance.GraphicsDevice.ScissorRectangle = oldClip; Begin(); }
		if (maximumScroll <= 0) return;
		int height = Math.Max(12, (int)(layout.List.Height * (layout.List.Height / layout.Scale) / (layout.List.Height / layout.Scale + maximumScroll)));
		Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(layout.List.Right + 3, layout.List.Top + (int)((layout.List.Height - height) * scroll / maximumScroll), Math.Max(2, (int)(3 * layout.Scale)), height), Color.LightSlateGray);
	}
	private void DrawCard(UIElement item, Rectangle card, GameTime time)
	{
		bool expanded = selected == item;
		Nine(expanded ? "SelectionSelected" : "SelectionNormal", card, Color.White);
		bool isWorld = item is UIWorldListItem;
		Rectangle picture = layout.CardAt(card, 8, 8, 76, isWorld ? 70 : 76);
		if (!isWorld) Nine("PreviewFrame", picture, Color.White);
		string name, info, difficulty, extra; bool favorite; Color difficultyColor = Color.White;
		if (item is UICharacterListItem player) {
			name = player.Data.Name; favorite = player.IsFavorite;
			info = $"{player.Data.Player.statLifeMax} HP    {player.Data.Player.statManaMax} MP";
			difficulty = Language.GetTextValue(player.Data.Player.difficulty switch { 1 => "UI.Mediumcore", 2 => "UI.Hardcore", 3 => "UI.Creative", _ => "UI.Softcore" });
			if (player.Data.Player.difficulty == 3) difficultyColor = Main.creativeModeColor;
			TimeSpan played = player.Data.GetPlayTime(); extra = $"{(int)played.TotalHours:00}:{played.Minutes:00}:{played.Seconds:00}";
			DrawPlayer(player, picture, time);
		} else {
			var world = (UIWorldListItem)item; name = world.Data.Name; favorite = world.IsFavorite;
			object[] args = { "", Color.White }; WorldDifficulty.Invoke(world, args); difficulty = (string)args[0]; difficultyColor = (Color)args[1];
			if (difficulty == "经典") difficulty = "普通";
			info = world.Data.WorldSizeName switch { "小" => "小世界", "中" => "中世界", "大" => "大世界", _ => world.Data.WorldSizeName };
			extra = T("创建日期: ", "Created: ") + world.Data.CreationTime.ToString("M/d/yyyy", System.Globalization.CultureInfo.InvariantCulture);
			Texture2D icon = ((Asset<Texture2D>)WorldIcon.Invoke(world, null)).Value;
			float fit = Math.Min(picture.Width / (float)icon.Width, picture.Height / (float)icon.Height);
			Main.spriteBatch.Draw(icon, picture.Center.ToVector2(), null, Color.White, 0, icon.Size() / 2, fit, SpriteEffects.None, 0);
		}
		Label(name, layout.CardAt(card, 96, 9, 385, 30), 1.25f, false, display: true);
		Icon("Images/UI/ButtonFavorite" + (favorite ? "Active" : "Inactive"), layout.CardAt(card, 489, 9, 22, 22), favorite ? Color.White : Color.White * .5f);
		Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, layout.CardAt(card, 86, 40, 430, 2), new Color(37, 53, 99));
		if (item is UICharacterListItem) {
			DrawCharacterResources((UICharacterListItem)item, layout.CardAt(card, 96, 51, 194, 26));
			Info(difficulty, layout.CardAt(card, 296, 51, 110, 26), difficultyColor);
			Info(extra, layout.CardAt(card, 413, 51, 98, 26));
		} else {
			Info(difficulty, layout.CardAt(card, 88, 49, 110, 26), difficultyColor);
			Info(info, layout.CardAt(card, 205, 49, 137, 26));
			Info(extra, layout.CardAt(card, 349, 49, 162, 26));
		}
		if (!expanded) return;
		string[] actions = { "ButtonDelete", "ButtonFavoriteActive", "ButtonRename", "ButtonSeed" };
		for (int i = 0; i < (item is UICharacterListItem ? 3 : 4); i++) {
			Rectangle r = layout.Action(card, i);
			Nine("MultiplayerButton", r, Color.White);
			if (i == 1 && !favorite) actions[i] = "ButtonFavoriteInactive";
			Icon("Images/UI/" + actions[i], Inset(r, 5 * layout.Scale), i == 0 && favorite ? Color.White * .3f : Color.White);
		}
		Button(IsHostBrowser ? T("主机", "Host") : T("开始游戏", "Play"), layout.Play(card), 1.15f, "Images/UI/ButtonPlay");
	}
	private void DrawCharacterResources(UICharacterListItem player, Rectangle bounds)
	{
		Player character = player.Data.Player;
		Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, bounds, new Color(37, 53, 99));
		float iconSize = 17 * layout.Scale;
		int iconWidth = Math.Max(1, (int)iconSize);
		Rectangle heart = new(bounds.X + (int)(10 * layout.Scale), bounds.Center.Y - iconWidth / 2,
			iconWidth, iconWidth);
		Rectangle mana = new(bounds.X + (int)(103 * layout.Scale), bounds.Center.Y - iconWidth / 2,
			iconWidth, iconWidth);
		DrawResourceIcon(TextureAssets.Heart.Value, heart);
		DrawResourceIcon(TextureAssets.Mana.Value, mana);
		Label($"{character.statLifeMax} HP", new Rectangle(heart.Right + (int)(4 * layout.Scale), bounds.Y,
			Math.Max(1, mana.X - heart.Right - (int)(7 * layout.Scale)), bounds.Height), .95f, false);
		Label($"{character.statManaMax} MP", new Rectangle(mana.Right + (int)(4 * layout.Scale), bounds.Y,
			Math.Max(1, bounds.Right - mana.Right - (int)(6 * layout.Scale)), bounds.Height), .95f, false);
	}
	private static void DrawResourceIcon(Texture2D texture, Rectangle bounds)
	{
		float fit = Math.Min(bounds.Width / (float)texture.Width, bounds.Height / (float)texture.Height);
		Main.spriteBatch.Draw(texture, bounds.Center.ToVector2(), null, Color.White, 0, texture.Size() / 2, fit,
			SpriteEffects.None, 0);
	}
	private void DrawPlayer(UICharacterListItem item, Rectangle bounds, GameTime time)
	{
		float previewScale = 1.4f * layout.Scale;
		float verticalCenterOffset = (previewScale - 1f) * item.Data.Player.height * .5f;
		if (!previews.TryGetValue(item, out var preview)) previews[item] = preview = new UICharacter(item.Data.Player, false, false, previewScale / Main.UIScale, true);
		preview.SetAnimated(item == selected);
		preview.Left.Set(bounds.Left / Main.UIScale, 0); preview.Top.Set((bounds.Top + verticalCenterOffset) / Main.UIScale, 0);
		preview.Width.Set(bounds.Width / Main.UIScale, 0); preview.Height.Set(bounds.Height / Main.UIScale, 0); preview.Recalculate(); preview.Update(time);
		Main.spriteBatch.End();
		Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, clip, null, Main.UIScaleMatrix);
		preview.Draw(Main.spriteBatch);
		Main.spriteBatch.End();
		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, clip);
	}
	private static Rectangle Inset(Rectangle r, float padding) => new(r.X + (int)padding, r.Y + (int)padding, Math.Max(1, r.Width - (int)(2 * padding)), Math.Max(1, r.Height - (int)(2 * padding)));
	private void Info(string text, Rectangle r, Color? color = null) { Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, r, new Color(37, 53, 99)); Label(text, r, .95f, true, color); }
	private void Button(string text, Rectangle r, float size, string icon = null)
	{
		Nine("MultiplayerButton", r, Color.White);
		if (icon != null) {
			Rectangle image = new(r.X + (int)(9 * layout.Scale), r.Y + (int)(10 * layout.Scale), (int)(26 * layout.Scale), (int)(26 * layout.Scale));
			if (icon == "Back") Main.spriteBatch.Draw(Tex("Back"), image, Color.White);
			else if (icon == "+") DrawResourceIcon(Tex("MobileAdd"), image);
			else Icon(icon, image, Color.White);
			r = new(r.X + (int)(30 * layout.Scale), r.Y, r.Width - (int)(35 * layout.Scale), r.Height);
		}
		Label(text, r, size, display: true);
	}
	private static void Icon(string asset, Rectangle bounds, Color color)
	{
		string mobile = asset switch {
			"Images/UI/ButtonPlay" => "MobilePlay",
			"Images/UI/ButtonDelete" => "MobileDelete",
			"Images/UI/ButtonRename" => "MobileRename",
			"Images/UI/ButtonSeed" => "WorldSeed",
			"Images/UI/ButtonFavoriteActive" or "Images/UI/ButtonFavoriteInactive" => "MobileFavorite",
			_ => null
		};
		Texture2D tex = mobile == null ? Main.Assets.Request<Texture2D>(asset).Value
			: ModContent.Request<Texture2D>("MobileJoystick/Assets/MobileMenu/" + mobile, AssetRequestMode.ImmediateLoad).Value;
		if (asset == "Images/UI/ButtonFavoriteInactive") color *= .45f;
		float fit = Math.Min(bounds.Width / (float)tex.Width, bounds.Height / (float)tex.Height);
		Main.spriteBatch.Draw(tex, bounds.Center.ToVector2(), null, color, 0, tex.Size() / 2, fit, SpriteEffects.None, 0);
	}
	private Texture2D Tex(string name)
	{ if (!textures.TryGetValue(name, out var tex)) textures[name] = tex = ModContent.Request<Texture2D>("MobileJoystick/Assets/MobileMenu/" + name, AssetRequestMode.ImmediateLoad).Value; return tex; }
	private void Nine(string name, Rectangle r, Color color)
	{
		int requestedCorner = name switch {
			"MultiplayerTitle" or "MultiplayerPanel" or "MultiplayerButton" or "SelectionSelected" or "SelectionNormal" or "PreviewFrame" => 20,
			_ => 8
		};
		MobileMenuFrame.Draw(Main.spriteBatch, Tex(name), r, color, layout.Scale, requestedCorner);
	}
	private void Label(string text, Rectangle r, float size, bool center = true, Color? color = null, bool display = false)
	{
		if (display) size *= MobileUiMetrics.DisplayFontScale;
		var font = display ? FontAssets.DeathText.Value : FontAssets.MouseText.Value;
		float scale = Math.Min(size * MobileUiMetrics.FontScale * layout.Scale, (r.Width - 6 * layout.Scale) / Math.Max(1, font.MeasureString(text).X));
		int top = int.MaxValue, bottom = int.MinValue;
		foreach (char c in text) if (!char.IsWhiteSpace(c) && font.SpriteCharacters.TryGetValue(c, out var g)) { top = Math.Min(top, g.Padding.Y); bottom = Math.Max(bottom, g.Padding.Y + g.Glyph.Height); }
		float middle = top == int.MaxValue ? font.LineSpacing / 2f : (top + bottom) / 2f;
		Utils.DrawBorderStringFourWay(Main.spriteBatch, font, text, center ? r.Center.X - font.MeasureString(text).X * scale / 2 : r.X, r.Center.Y - middle * scale, color ?? Color.White, Color.Black, Vector2.Zero, scale);
	}
}
