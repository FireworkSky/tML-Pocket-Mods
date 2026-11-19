using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;

namespace MobileJoystick;

public sealed class MobileCharacterCreationSystem : ModSystem
{
	private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly Type StateType = typeof(UICharacterCreation);
	private static readonly string[] Methods = { "Click_GoBack", "Click_RandomizePlayer", "Click_NamingAndCreating", "OnFinishedNaming", "Click_CharClothStyle" };
	private static readonly bool Available = StateType.GetField("_player", Hidden) != null && Array.TrueForAll(Methods, n => StateType.GetMethod(n, Hidden) != null);
	private static readonly int[] Styles = { 0, 2, 1, 3, 8, 4, 6, 5, 7, 9 };
	private UICharacterCreation state;
	private UICharacterCreation randomizedState;
	private Player player;
	private UICharacter preview;
	private bool down, wait = true, escape, popup, editing, dragging;
	private int touch = -1, pressed = -1, tab, hairRow, wheel;
	private string input = "";
	private Vector2 pointer, start;
	private Vector3 sliderHsl;
	private float scale, ox, oy;
	private bool Active => Available && !Main.dedServ && Main.gameMenu && Main.menuMode == 888 && Main.MenuUI?.CurrentState is UICharacterCreation && MobileJoystickConfig.Instance?.EnableMobileControls == true && !PlayerInput.UsingGamepadUI;
	private static string T(string zh, string en) => MobileSettingsCatalog.Text(zh, en);
	public override void Load() { if (!Main.dedServ) { On_UserInterface.Draw += DrawNative; On_UserInterface.Update += UpdateNative; On_Main.DrawMenu += DrawMenu; } }
	public override void Unload() { if (Main.dedServ) return; FinishInput(false); On_UserInterface.Draw -= DrawNative; On_UserInterface.Update -= UpdateNative; On_Main.DrawMenu -= DrawMenu; }
	private void DrawNative(On_UserInterface.orig_Draw orig, UserInterface self, SpriteBatch sb, GameTime time) { if (self != Main.MenuUI || !Active) orig(self, sb, time); }
	private void UpdateNative(On_UserInterface.orig_Update orig, UserInterface self, GameTime time) { if (self != Main.MenuUI || !Active) orig(self, time); else self.CurrentState.Update(time); }
	private void Geometry() { scale = Math.Min(PlayerInput.RealScreenWidth / 1280f, PlayerInput.RealScreenHeight / 576f); ox = (PlayerInput.RealScreenWidth - 1280 * scale) / 2; oy = (PlayerInput.RealScreenHeight - 576 * scale) / 2; }
	private Rectangle R(float x, float y, float w, float h) => new((int)(ox + x * scale), (int)(oy + y * scale), Math.Max(1, (int)(w * scale)), Math.Max(1, (int)(h * scale)));
	private void Invoke(string method, params object[] args) => StateType.GetMethod(method, Hidden).Invoke(state, args);
	private void Click(string method) { if (method == "Click_GoBack") MobileSelectionSystem.SuppressEscapeUntilRelease(); Invoke(method, new UIMouseEvent(state, pointer / Main.UIScale), state); }
	private void ResetPress() { down = false; touch = pressed = -1; wait = true; dragging = false; }
	public override void PostUpdateInput()
	{
		if (!Active) { FinishInput(false); state = null; player = null; preview = null; ResetPress(); return; }
		Geometry();
		if (state != Main.MenuUI.CurrentState) { FinishInput(false); state = (UICharacterCreation)Main.MenuUI.CurrentState; player = (Player)StateType.GetField("_player", Hidden).GetValue(state); if (randomizedState != state) { Invoke("Click_RandomizePlayer", null, null); randomizedState = state; } preview = null; tab = hairRow = 0; popup = false; ResetPress(); }
		if (!Main.hasFocus) { ResetPress(); return; }
		bool esc = Keyboard.GetState().IsKeyDown(Keys.Escape);
		if (esc && !escape) { if (editing) FinishInput(false); else if (popup) popup = false; else Click("Click_GoBack"); ResetPress(); }
		escape = esc;
		bool previous = down; var mouse = Mouse.GetState(); if (touch < 0) pointer = new(mouse.X, mouse.Y); down = mouse.LeftButton == ButtonState.Pressed;
		TouchCollection touches = MobileTouchInput.GetState();
		if (touch < 0) foreach (TouchLocation t in touches) if (t.State == TouchLocationState.Pressed) { touch = t.Id; break; }
		if (touch >= 0) { bool found = false; down = false; foreach (TouchLocation t in touches) if (t.Id == touch) { found = true; int w = TouchPanel.DisplayWidth > 0 ? TouchPanel.DisplayWidth : PlayerInput.RealScreenWidth; int h = TouchPanel.DisplayHeight > 0 ? TouchPanel.DisplayHeight : PlayerInput.RealScreenHeight; pointer = new(t.Position.X * PlayerInput.RealScreenWidth / w, t.Position.Y * PlayerInput.RealScreenHeight / h); down = t.State is TouchLocationState.Pressed or TouchLocationState.Moved; break; } if (!found) { ResetPress(); return; } if (!down) touch = -1; }
		int delta = mouse.ScrollWheelValue - wheel; wheel = mouse.ScrollWheelValue;
		if (tab == 5 && !popup && R(540, 262, 360, 153).Contains(pointer.ToPoint()) && delta != 0) ScrollHair(delta > 0 ? -1 : 1);
		if (wait) { if (!down) wait = false; return; }
		if (down && !previous) { pressed = Hit(pointer.ToPoint()); start = pointer; dragging = false; if (pressed >= 200 && pressed < 203) sliderHsl = Hsl(); }
		if (down && pressed >= 200 && pressed < 203) SetSlider(pressed - 200);
		if (down && tab == 5 && pressed >= 100 && Math.Abs(pointer.Y - start.Y) > 18 * scale) { ScrollHair(pointer.Y < start.Y ? 1 : -1); start = pointer; dragging = true; }
		if (!down && previous) { int hit = Hit(pointer.ToPoint()); if (!dragging && pressed >= 0 && hit == pressed) Execute(hit); pressed = -1; }
	}
	private void ScrollHair(int rows) => hairRow = Math.Clamp(hairRow + rows, 0, Math.Max(0, (Main.Hairstyles.AvailableHairstyles.Count + 6) / 7 - 3));
	private int Hit(Point p)
	{
		if (popup) { for (int i = 0; i < 4; i++) if (R(703, 227 + i * 47, 185, 39).Contains(p)) return 40 + i; return 39; }
		for (int i = 0; i < 11; i++) if (R(379 + i * 48, 200, 46, 44).Contains(p)) return i;
		for (int i = 0; i < 3; i++) if (R(379 + i * 177, 434, 168, 46).Contains(p)) return 20 + i;
		if (tab == 0) { if (R(542, 264, 358, 46).Contains(p)) return 24; if (R(542, 317, 358, 46).Contains(p)) return 25; }
		if (tab == 1) for (int i = 0; i < 10; i++) if (R(540 + i % 5 * 74, 265 + i / 5 * 77, 68, 68).Contains(p)) return 50 + i;
		if (tab == 5) for (int i = 0; i < 21; i++) if (R(543 + i % 7 * 51, 265 + i / 7 * 51, 46, 46).Contains(p)) return 100 + i;
		if (IsColorTab()) for (int i = 0; i < 3; i++) if (R(694, 264 + i * 53, 198, 46).Contains(p)) return 200 + i;
		return -1;
	}
	private void Execute(int id)
	{
		if (popup) { if (id >= 40 && id < 44) player.difficulty = (byte)new[] { 2, 1, 0, 3 }[id - 40]; popup = false; return; }
		if (editing) FinishInput(true);
		if (id < 11) { tab = id; return; }
		if (id == 20) Click("Click_GoBack"); else if (id == 21) Click("Click_RandomizePlayer"); else if (id == 22) Click("Click_NamingAndCreating");
		else if (id == 24) { editing = true; input = player.name ?? ""; Main.clrInput(); MobileTextInputSystem.Focus(this, R(542, 264, 358, 46)); }
		else if (id == 25) popup = true;
		else if (id >= 50 && id < 60) { player.skinVariant = Styles[id - 50]; Invoke("Click_CharClothStyle", null, null); }
		else if (id >= 100 && id < 121) { int index = hairRow * 7 + id - 100; if (index < Main.Hairstyles.AvailableHairstyles.Count) player.hair = Main.Hairstyles.AvailableHairstyles[index]; }
	}
	private bool IsColorTab() => tab is 3 or 4 or 6 or 7 or 8 or 9 or 10;
	private Color CurrentColor() => tab switch { 3 => player.skinColor, 4 => player.eyeColor, 6 => player.hairColor, 7 => player.underShirtColor, 8 => player.shirtColor, 9 => player.pantsColor, _ => player.shoeColor };
	private void SetColor(Color c) { switch (tab) { case 3: player.skinColor = c; break; case 4: player.eyeColor = c; break; case 6: player.hairColor = c; break; case 7: player.underShirtColor = c; break; case 8: player.shirtColor = c; break; case 9: player.pantsColor = c; break; case 10: player.shoeColor = c; break; } }
	private Vector3 Hsl() { Vector3 h = Main.rgbToHsl(CurrentColor()); h.Z = (h.Z - .15f) / .85f; return Vector3.Clamp(h, Vector3.Zero, Vector3.One); }
	private void SetSlider(int slider) { float v = Math.Clamp((pointer.X - R(703, 0, 1, 1).X) / (174 * scale), 0f, 1f); if (slider == 0) sliderHsl.X = v; else if (slider == 1) sliderHsl.Y = v; else sliderHsl.Z = v; SetColor(Main.hslToRgb(sliderHsl.X, sliderHsl.Y, sliderHsl.Z * .85f + .15f)); }
	private void FinishInput(bool save) { if (!editing) return; editing = false; MobileTextInputSystem.Blur(this); PlayerInput.WritingText = false; if (save && state != null) Invoke("OnFinishedNaming", input); input = ""; }
	private void DrawMenu(On_Main.orig_DrawMenu orig, Main self, GameTime time)
	{
		orig(self, time); if (!Active || state != Main.MenuUI.CurrentState) return; Geometry();
		if (editing && Main.hasFocus) { PlayerInput.WritingText = true; Main.instance.HandleIME(); input = Main.GetInputText(input); if (input.Length > 20) input = input[..20]; if (Main.inputTextEnter) FinishInput(true); else if (Main.inputTextEscape) FinishInput(false); }
		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise);
		try { Draw(time); } finally { Main.spriteBatch.End(); }
	}
	private void Draw(GameTime time)
	{
		string[] titles = { T("新玩家", "New Player"), T("类型", "Style"), T("声音", "Voice"), T("肤色", "Skin"), T("眼睛", "Eyes"), T("头发", "Hair"), T("头发颜色", "Hair Color"), T("内衣", "Undershirt"), T("上衣", "Shirt"), T("裤子", "Pants"), T("鞋子", "Shoes") };
		Nine("MultiplayerPanel", R(366, 174, 548, 320), Color.White); Nine("MultiplayerTitle", R(486, 146, 308, 46), Color.White); Label(titles[tab], R(486, 146, 308, 46), 1.5f);
		string[] icons = { "CharacterInfo", null, "CharacterVoice", "CharacterSkin", "CharacterEye", null, null, "CharacterUndershirt", "CharacterShirt", "CharacterPants", "CharacterShoes" };
		string[] nativeIcons = { null, "ClothStyleMale", null, null, null, "HairStyle_Hair", "ColorHair", "ColorUndershirt", "ColorShirt", "ColorPants", "ColorShoes" };
		for (int i = 0; i < 11; i++) { Rectangle r = R(379 + i * 48, 200, 46, 44); Nine("MultiplayerButton", r, i == tab ? Color.White : new Color(160, 175, 205)); Rectangle icon = R(388 + i * 48, 207, 28, 28); Color tint = i switch { 3 => player.skinColor, 7 => player.underShirtColor, 8 => player.shirtColor, 9 => player.pantsColor, 10 => player.shoeColor, _ => Color.White }; if (icons[i] != null) { Icon(icons[i], icon, tint); if (i == 4) Icon("CharacterIris", icon, player.eyeColor); } else NativeIcon(nativeIcons[i], icon, i is 5 or 6 ? player.hairColor : Color.White); }
		Nine("PreviewFrame", R(379, 264, 151, 153), Color.White); DrawPlayer(R(379, 296, 151, 138), 2.15f, time, true);
		if (tab == 0) { bool empty = !editing && string.IsNullOrEmpty(player.name); Row(T("名字", "Name"), editing ? input + "|" : empty ? T("输入您的名称……", "Enter your name...") : player.name, R(542, 264, 358, 46), false, empty ? Color.Gray : Color.White); string[] d = { T("软核", "Softcore"), T("中核", "Mediumcore"), T("硬核", "Hardcore"), T("旅途", "Journey") }; int difficulty = Math.Clamp((int)player.difficulty, 0, 3); Row(T("难度", "Difficulty"), d[difficulty], R(542, 317, 358, 46), true, new[] { Color.Cyan, Color.Orange, Color.Red, Color.Pink }[difficulty]); }
		else if (tab == 1) { int saved = player.skinVariant; for (int i = 0; i < 10; i++) { Rectangle r = R(540 + i % 5 * 74, 265 + i / 5 * 77, 68, 68); Nine(saved == Styles[i] ? "PreviewSelected" : "PreviewFrame", r, Color.White); player.skinVariant = Styles[i]; DrawPlayer(r, .9f, time); } player.skinVariant = saved; }
		else if (tab == 5) DrawHair();
		else if (IsColorTab()) DrawColors();
		else Label(T("此版本不支持角色声音", "Character voices are unavailable in this version"), R(542, 280, 358, 60), 1f);
		string[] footer = { T("返回", "Back"), T("随机化", "Randomize"), T("创建", "Create") }; string[] footerIcons = { "Back", "WorldDice", "MobileAdd" }; for (int i = 0; i < 3; i++) { Rectangle r = R(379 + i * 177, 434, 168, 46); Nine("MultiplayerButton", r, Color.White); Icon(footerIcons[i], R(389 + i * 177, 444, 27, 27)); Label(footer[i], new Rectangle(r.X + (int)(30 * scale), r.Y, r.Width - (int)(30 * scale), r.Height), 1.1f); }
		if (popup) { Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(0, 0, PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight), Color.Black * .55f); Nine("CreationPopup", R(690, 216, 210, 207), Color.White); string[] names = { T("硬核", "Hardcore"), T("中核", "Mediumcore"), T("软核", "Softcore"), T("旅途", "Journey") }; for (int i = 0; i < 4; i++) { Rectangle r = R(703, 227 + i * 47, 185, 39); Nine(player.difficulty == new[] { 2, 1, 0, 3 }[i] ? "CreationOptionSelected" : "CreationOption", r, Color.White); Label(names[i], r, 1.1f, true, new[] { Color.Red, Color.Orange, Color.Cyan, Color.Pink }[i]); } }
	}
	private void DrawHair()
	{
		for (int i = 0; i < 21; i++) { int index = hairRow * 7 + i; if (index >= Main.Hairstyles.AvailableHairstyles.Count) break; int hair = Main.Hairstyles.AvailableHairstyles[index]; Rectangle r = R(543 + i % 7 * 51, 265 + i / 7 * 51, 46, 46); Nine("MultiplayerButton", r, hair == player.hair ? Color.Yellow : Color.White); int old = player.hair; player.hair = hair; Main.spriteBatch.End(); Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix); Main.PlayerRenderer.DrawPlayerHead(Main.Camera, player, r.Center.ToVector2() / Main.UIScale + new Vector2(-5, -5)); Main.spriteBatch.End(); Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise); player.hair = old; }
	}
	private void DrawColors()
	{
		Color c = CurrentColor(); Label($"#{c.R:X2}{c.G:X2}{c.B:X2}", R(379, 264, 151, 24), .8f); Vector3 h = Hsl(); string[] names = { T("色调", "Hue"), T("饱和度", "Saturation"), T("亮度", "Brightness") };
		for (int i = 0; i < 3; i++) { Rectangle r = R(542, 264 + i * 53, 358, 46); Nine("CreationRow", r, Color.White); Label(names[i] + ":", R(548, 264 + i * 53, 135, 46), 1.1f, false); Rectangle outer = R(701, 278 + i * 53, 178, 18); Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, outer, Color.Black); Rectangle rim = R(703, 280 + i * 53, 174, 14); Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, rim, Color.White); Rectangle bar = R(705, 282 + i * 53, 170, 10); for (int x = 0; x < bar.Width; x++) { float v = x / (float)Math.Max(1, bar.Width - 1); Color color = i == 0 ? Main.hslToRgb(v, 1, .5f) : i == 1 ? Main.hslToRgb(h.X, v, h.Z * .85f + .15f) : Main.hslToRgb(h.X, h.Y, v * .85f + .15f); Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(bar.X + x, bar.Y, 1, bar.Height), color); } float value = i == 0 ? h.X : i == 1 ? h.Y : h.Z; Icon("PlayerSliderKnob", R(703 + 174 * value - 13, 272 + i * 53, 26, 30)); }
	}
	private void DrawPlayer(Rectangle r, float size, GameTime time, bool animated = false) { preview = new UICharacter(player, animated, false, size * scale / Main.UIScale); preview.Left.Set(r.X / Main.UIScale, 0); preview.Top.Set(r.Y / Main.UIScale, 0); preview.Width.Set(r.Width / Main.UIScale, 0); preview.Height.Set(r.Height / Main.UIScale, 0); preview.Recalculate(); preview.Update(time); Main.spriteBatch.End(); Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix); preview.Draw(Main.spriteBatch); Main.spriteBatch.End(); Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise); }
	private void Row(string name, string value, Rectangle r, bool dropdown = false, Color? color = null) { Nine("CreationRow", r, Color.White); Label(name + ":", new Rectangle(r.X + (int)(8 * scale), r.Y, r.Width / 2, r.Height), 1.1f, false); Rectangle field = new(r.Center.X, r.Y, r.Width / 2 - (int)((dropdown ? 54 : 10) * scale), r.Height); var font = FontAssets.MouseText.Value; value ??= ""; float s = Math.Min(1.1f * scale, (field.Width - 4) / Math.Max(1, font.MeasureString(value).X)); Utils.DrawBorderStringFourWay(Main.spriteBatch, font, value, field.Right - font.MeasureString(value).X * s, field.Center.Y - font.LineSpacing * s / 2 + 4 * scale, color ?? Color.White, Color.Black, Vector2.Zero, s); if (dropdown) { MobileMenuFrame.DropdownDivider(Main.spriteBatch, r, scale); Icon("WorldDropdown", new Rectangle(r.Right - (int)(37 * scale), r.Center.Y - (int)(9 * scale), (int)(24 * scale), (int)(18 * scale))); } }
	private void Icon(string name, Rectangle r, Color? color = null) => Main.spriteBatch.Draw(ModContent.Request<Texture2D>("MobileJoystick/Assets/MobileMenu/" + name, AssetRequestMode.ImmediateLoad).Value, r, color ?? Color.White);
	private void NativeIcon(string name, Rectangle r, Color color) { Texture2D tex = Main.Assets.Request<Texture2D>("Images/UI/CharCreation/" + name).Value; Main.spriteBatch.Draw(tex, r, color); }
	private void Nine(string name, Rectangle r, Color color) => MobileMenuFrame.Draw(Main.spriteBatch, ModContent.Request<Texture2D>("MobileJoystick/Assets/MobileMenu/" + name, AssetRequestMode.ImmediateLoad).Value, r, color, scale);
	private void Label(string text, Rectangle r, float size, bool center = true, Color? color = null) { var font = FontAssets.MouseText.Value; text ??= ""; float s = Math.Min(size * scale, (r.Width - 4) / Math.Max(1, font.MeasureString(text).X)); Utils.DrawBorderStringFourWay(Main.spriteBatch, font, text, center ? r.Center.X - font.MeasureString(text).X * s / 2 : r.X, r.Center.Y - font.LineSpacing * s / 2 + 4 * scale, color ?? Color.White, Color.Black, Vector2.Zero, s); }
}
