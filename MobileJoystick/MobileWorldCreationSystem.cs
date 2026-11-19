using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;

namespace MobileJoystick;

// The native state remains responsible for validation, seed processing and generation.
public sealed class MobileWorldCreationSystem : ModSystem
{
	private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly Type StateType = typeof(UIWorldCreation);
	private static readonly string[] Fields = { "_optionSize", "_optionDifficulty", "_optionEvil", "_optionwWorldName", "_optionSeed", "_sizeButtons", "_difficultyButtons", "_evilButtons" };
	private static readonly string[] Callbacks = { "ClickSizeOption", "ClickDifficultyOption", "ClickEvilOption", "ClickRandomizeName", "ClickRandomizeSeed", "Click_GoBack", "Click_NamingAndCreating", "OnFinishedSettingName", "OnFinishedSettingSeed" };
	private static readonly bool Available = Array.TrueForAll(Fields, n => StateType.GetField(n, Hidden) != null) && Array.TrueForAll(Callbacks, n => StateType.GetMethod(n, Hidden) != null);
	private UIWorldCreation state;
	private bool seedTab, down, wait = true, escape;
	private int touch = -1, pressed = -1, popup = -1, editing = -1;
	private string input = "";
	private Vector2 pointer;
	private float scale, ox, oy;
	private bool Active => Available && !Main.dedServ && Main.gameMenu && Main.menuMode == 888 && Main.MenuUI?.CurrentState is UIWorldCreation && MobileJoystickConfig.Instance?.EnableMobileControls == true && !PlayerInput.UsingGamepadUI;
	private static string T(string zh, string en) => MobileSettingsCatalog.Text(zh, en);

	public override void Load() { if (!Main.dedServ) { On_UserInterface.Draw += DrawNative; On_UserInterface.Update += UpdateNative; On_Main.DrawMenu += DrawMenu; } }
	public override void Unload() { if (Main.dedServ) return; FinishInput(false); On_UserInterface.Draw -= DrawNative; On_UserInterface.Update -= UpdateNative; On_Main.DrawMenu -= DrawMenu; }
	private void DrawNative(On_UserInterface.orig_Draw orig, UserInterface self, SpriteBatch sb, GameTime time) { if (self != Main.MenuUI || !Active) orig(self, sb, time); }
	private void UpdateNative(On_UserInterface.orig_Update orig, UserInterface self, GameTime time) { if (self != Main.MenuUI || !Active) orig(self, time); else self.CurrentState.Update(time); }
	private Rectangle R(float x, float y, float w, float h) => new((int)(ox + x * scale), (int)(oy + y * scale), Math.Max(1, (int)(w * scale)), Math.Max(1, (int)(h * scale)));
	private void Geometry() { scale = Math.Min(PlayerInput.RealScreenWidth / 1280f, PlayerInput.RealScreenHeight / 576f); ox = (PlayerInput.RealScreenWidth - 1280 * scale) / 2; oy = (PlayerInput.RealScreenHeight - 576 * scale) / 2; }
	private object Get(string name) => StateType.GetField(name, Hidden).GetValue(state);
	private void Invoke(string name, params object[] args) => StateType.GetMethod(name, Hidden).Invoke(state, args);
	private void Click(string name, UIElement element = null) { element ??= state; if (name == "Click_GoBack") MobileSelectionSystem.SuppressEscapeUntilRelease(); Invoke(name, new UIMouseEvent(element, pointer / Main.UIScale), element); }
	private void ResetPress() { down = false; touch = pressed = -1; wait = true; }
	public override void PostUpdateInput()
	{
		if (!Active) { FinishInput(false); state = null; ResetPress(); return; }
		Geometry();
		if (state != Main.MenuUI.CurrentState) { FinishInput(false); state = (UIWorldCreation)Main.MenuUI.CurrentState; seedTab = false; popup = -1; ResetPress(); }
		if (!Main.hasFocus) { ResetPress(); return; }
		bool esc = Keyboard.GetState().IsKeyDown(Keys.Escape);
		if (esc && !escape) { if (editing >= 0) FinishInput(false); else if (popup >= 0) popup = -1; else Click("Click_GoBack"); ResetPress(); }
		escape = esc;
		bool previous = down; var mouse = Mouse.GetState(); if (touch < 0) pointer = new(mouse.X, mouse.Y); down = mouse.LeftButton == ButtonState.Pressed;
		TouchCollection touches = MobileTouchInput.GetState();
		if (touch < 0) foreach (TouchLocation t in touches) if (t.State == TouchLocationState.Pressed) { touch = t.Id; break; }
		if (touch >= 0) { bool found = false; down = false; foreach (TouchLocation t in touches) if (t.Id == touch) { found = true; int w = TouchPanel.DisplayWidth > 0 ? TouchPanel.DisplayWidth : PlayerInput.RealScreenWidth; int h = TouchPanel.DisplayHeight > 0 ? TouchPanel.DisplayHeight : PlayerInput.RealScreenHeight; pointer = new(t.Position.X * PlayerInput.RealScreenWidth / w, t.Position.Y * PlayerInput.RealScreenHeight / h); down = t.State is TouchLocationState.Pressed or TouchLocationState.Moved; break; } if (!found) { ResetPress(); return; } if (!down) touch = -1; }
		if (wait) { if (!down) wait = false; return; }
		if (down && !previous) pressed = Hit(pointer.ToPoint());
		if (!down && previous) { int hit = Hit(pointer.ToPoint()); if (pressed >= 0 && hit == pressed) Execute(hit); pressed = -1; }
	}
	private int Hit(Point p)
	{
		if (popup >= 0) { for (int i = 0; i < (popup == 1 ? 4 : 3); i++) if (R(703, PopupTop() + 12 + i * 50, 185, 40).Contains(p)) return 20 + i; return 19; }
		if (R(468, 162, 170, 44).Contains(p)) return 0; if (R(640, 162, 170, 44).Contains(p)) return 1;
		if (!seedTab) { for (int i = 0; i < 3; i++) if (R(542, 226 + i * 53, 358, 46).Contains(p)) return 2 + i; if (R(379, 385, 521, 46).Contains(p)) return 5; }
		else if (R(379, 226, 521, 46).Contains(p)) return 6;
		for (int i = 0; i < 3; i++) if (R(379 + i * 177, 450, 168, 46).Contains(p)) return 7 + i;
		return -1;
	}
	private void Execute(int action)
	{
		if (popup >= 0) { if (action >= 20) { var list = (Array)Get(new[] { "_sizeButtons", "_difficultyButtons", "_evilButtons" }[popup]); int index = action - 20; if (index < list.Length) Click(Callbacks[popup], (UIElement)list.GetValue(index)); } popup = -1; return; }
		if (editing >= 0) FinishInput(true);
		switch (action) { case 0: seedTab = false; break; case 1: seedTab = true; break; case 2: case 3: case 4: popup = action - 2; break; case 5: case 6: editing = action - 5; input = (string)Get(editing == 0 ? Fields[3] : Fields[4]) ?? ""; Main.clrInput(); MobileTextInputSystem.Focus(this); break; case 7: Click("Click_GoBack"); break; case 8: Click(seedTab ? "ClickRandomizeSeed" : "ClickRandomizeName"); break; case 9: Click("Click_NamingAndCreating"); break; }
	}
	private void FinishInput(bool save) { if (editing < 0) return; int field = editing; editing = -1; MobileTextInputSystem.Blur(this); PlayerInput.WritingText = false; if (save && state != null) Invoke(field == 0 ? "OnFinishedSettingName" : "OnFinishedSettingSeed", input); input = ""; }
	private void DrawMenu(On_Main.orig_DrawMenu orig, Main self, GameTime time)
	{
		orig(self, time); if (!Active || state != Main.MenuUI.CurrentState) return; Geometry();
		if (editing >= 0 && Main.hasFocus) { PlayerInput.WritingText = true; Main.instance.HandleIME(); input = Main.GetInputText(input); int max = editing == 0 ? 27 : 40; if (input.Length > max) input = input[..max]; if (Main.inputTextEnter) FinishInput(true); else if (Main.inputTextEscape) FinishInput(false); }
		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise);
		try { Draw(); } finally { Main.spriteBatch.End(); }
	}
	private void Draw()
	{
		Nine("MultiplayerPanel", R(366, 138, 548, 370), Color.White); Nine("MultiplayerTitle", R(486, 110, 308, 44), Color.White); Label(T("新世界", "New World"), R(486, 110, 308, 44), 1.5f);
		Button(T("通用", "General"), R(468, 162, 170, 44), !seedTab); Button(T("种子", "Seed"), R(640, 162, 170, 44), seedTab);
		Icon("WorldGeneral", R(478, 171, 26, 26)); Icon("WorldSeed", R(650, 169, 29, 29));
		if (!seedTab) { DrawPreview(); string[][] options = { new[] { T("小", "Small"), T("中", "Medium"), T("大", "Large") }, new[] { T("经典", "Classic"), T("专家", "Expert"), T("大师", "Master"), T("旅途", "Journey") }, new[] { T("随机", "Random"), T("腐化", "Corruption"), T("猩红", "Crimson") } }; for (int i = 0; i < 3; i++) { int value = Convert.ToInt32(Get(Fields[i])); Color color = i == 0 ? new Color(0, 230, 150) : i == 1 ? new[] { Color.White, new Color(255, 165, 80), Color.Red, new Color(255, 110, 190) }[Math.Clamp(value, 0, 3)] : Color.White; Row(new[] { T("大小", "Size"), T("难度", "Difficulty"), T("邪恶环境", "Evil") }[i], options[i][Math.Clamp(value, 0, options[i].Length - 1)], R(542, 226 + i * 53, 358, 46), true, color); } Row(T("名字", "Name"), editing == 0 ? input + "|" : (string)Get(Fields[3]), R(379, 385, 521, 46)); Icon("WorldName", R(389, 392, 29, 29)); }
		else Row(T("种子", "Seed"), editing == 1 ? input + "|" : (string)Get(Fields[4]), R(379, 226, 521, 46));
		Button(T("返回", "Back"), R(379, 450, 168, 46)); Button(T("随机化", "Randomize"), R(556, 450, 168, 46)); Button(T("创建", "Create"), R(733, 450, 168, 46));
		Icon("Back", R(390, 459, 28, 28)); Icon("WorldDice", R(567, 460, 25, 25)); Icon("MobileAdd", R(744, 459, 28, 28));
		if (popup >= 0) { Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(0, 0, PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight), Color.Black * .55f); int count = popup == 1 ? 4 : 3; Nine("CreationPopup", R(690, PopupTop(), 210, 18 + count * 50), Color.White); string[][] names = { new[] { T("小", "Small"), T("中", "Medium"), T("大", "Large") }, new[] { T("旅途", "Journey"), T("普通", "Classic"), T("专家", "Expert"), T("大师", "Master") }, new[] { T("随机", "Random"), T("腐化", "Corruption"), T("猩红", "Crimson") } }; string[][] icons = { new[] { "WorldSizeSmall", "WorldSizeMedium", "WorldSizeLarge" }, new[] { "WorldDifficultyJourney", "WorldDifficultyClassic", "WorldDifficultyExpert", "WorldDifficultyMaster" } }; for (int i = 0; i < count; i++) { int selectedValue = popup == 1 ? (i + 3) % 4 : i; Rectangle option = R(703, PopupTop() + 12 + i * 50, 185, 40); Nine(Convert.ToInt32(Get(Fields[popup])) == selectedValue ? "CreationOptionSelected" : "CreationOption", option, Color.White); if (popup < icons.Length) Icon(icons[popup][i], new Rectangle(option.X + (int)(9 * scale), option.Center.Y - (int)(13 * scale), (int)(26 * scale), (int)(26 * scale))); Color color = popup == 0 ? new[] { Color.Cyan, new Color(0, 230, 150), Color.LimeGreen }[i] : popup == 1 ? new[] { Color.Pink, Color.White, Color.Orange, Color.Red }[i] : Color.White; Label(names[popup][i], option, 1.1f, true, color); } }
	}
	private int PopupTop() => popup == 1 ? 120 : 168;
	private void DrawPreview()
	{
		int difficulty = Convert.ToInt32(Get(Fields[1]));
		int size = Math.Clamp(Convert.ToInt32(Get(Fields[0])), 0, 2);
		int evil = Math.Clamp(Convert.ToInt32(Get(Fields[2])), 0, 2);
		Rectangle picture = R(387, 234, 134, 134);
		Icon(difficulty == 1 ? "WorldPreviewExpertSky" : difficulty == 2 ? "WorldPreviewMasterSky" : "WorldPreviewSky", picture);
		Color foreground = difficulty is 1 or 2 ? Color.DarkGray : Color.White;
		Icon(new[] { "WorldPreviewSmall", "WorldPreviewMedium", "WorldPreviewLarge" }[size], picture, foreground);
		Icon(new[] { "WorldPreviewEvilRandom", "WorldPreviewEvilCorruption", "WorldPreviewEvilCrimson" }[evil], picture, foreground);
		Icon(difficulty == 3 ? "WorldPreviewJourneyBunny" : difficulty == 0 ? "WorldPreviewClassicBunny" : "WorldPreviewExpertBunny", picture, difficulty == 2 ? new Color(255, 90, 90) : foreground);
		Icon("WorldPreviewBorder", R(379, 226, 150, 150));
	}
	private void Icon(string name, Rectangle r, Color? tint = null) => Main.spriteBatch.Draw(ModContent.Request<Texture2D>("MobileJoystick/Assets/MobileMenu/" + name, AssetRequestMode.ImmediateLoad).Value, r, tint ?? Color.White);
	private void Row(string name, string value, Rectangle r, bool dropdown = false, Color? valueColor = null) { Nine("CreationRow", r, Color.White); int inset = (int)((name == T("名字", "Name") ? 43 : 8) * scale); Label(name + "：", new Rectangle(r.X + inset, r.Y, r.Width / 2 - inset, r.Height), 1.1f, false); int end = dropdown ? (int)(54 * scale) : (int)(10 * scale); RightLabel(value ?? "", new Rectangle(r.Center.X, r.Y, r.Width / 2 - end, r.Height), 1.1f, valueColor); if (dropdown) { MobileMenuFrame.DropdownDivider(Main.spriteBatch, r, scale); Icon("WorldDropdown", new Rectangle(r.Right - (int)(37 * scale), r.Center.Y - (int)(9 * scale), (int)(24 * scale), (int)(18 * scale))); } }
	private void RightLabel(string text, Rectangle r, float size, Color? color = null) { var font = FontAssets.MouseText.Value; float s = Math.Min(size * scale, (r.Width - 4) / Math.Max(1, font.MeasureString(text).X)); Utils.DrawBorderStringFourWay(Main.spriteBatch, font, text, r.Right - font.MeasureString(text).X * s, r.Center.Y - font.LineSpacing * s / 2 + 4 * scale, color ?? Color.White, Color.Black, Vector2.Zero, s); }
	private void Button(string text, Rectangle r, bool selected = true) { Nine("MultiplayerButton", r, selected ? Color.White : new Color(160, 170, 200)); Label(text, r, 1.1f); }
	private void Nine(string name, Rectangle r, Color color) => MobileMenuFrame.Draw(Main.spriteBatch, ModContent.Request<Texture2D>("MobileJoystick/Assets/MobileMenu/" + name, AssetRequestMode.ImmediateLoad).Value, r, color, scale);
	private void Label(string text, Rectangle r, float size, bool center = true, Color? color = null) { var font = FontAssets.MouseText.Value; text ??= ""; float s = Math.Min(size * scale, (r.Width - 4) / Math.Max(1, font.MeasureString(text).X)); Utils.DrawBorderStringFourWay(Main.spriteBatch, font, text, center ? r.Center.X - font.MeasureString(text).X * s / 2 : r.X, r.Center.Y - font.LineSpacing * s / 2 + 4 * scale, color ?? Color.White, Color.Black, Vector2.Zero, s); }
}
