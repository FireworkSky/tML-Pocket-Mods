using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using ReLogic.Content;
using ReLogic.Graphics;

namespace MobileJoystick;

internal sealed class MobileTitleSettingsSystem : ModSystem
{
	private static bool active;
	private static int category;
	private static float scroll;
	private static bool down, previousDown, pressed, released, scrolling;
	private static int touchId = -1;
	private static Vector2 pointer, pressPosition, previousPointer;
	private static List<MobileSetting> items = new();
	private static MobileMenuLayout layout;
	private static MobileSetting slider;
	private static MobileSetting dropdown;
	private static Rectangle sliderBounds;
	private static bool consumed;
	private static bool returningFromKeybinds, settingsDirty, modDirty;
	private static string T(string zh, string en) => MobileSettingsCatalog.Text(zh, en);

	internal static bool Active => active && !Main.dedServ && Main.gameMenu && Main.menuMode == 0;

	public override void OnWorldUnload() => Close();

	internal static void Open()
	{
		active = true;
		Main.menuMode = 0;
		category = 0;
		scroll = 0;
		slider = null;
		dropdown = null;
		items = CreateCategory(category);
		ResetInput(true);
		SoundEngine.PlaySound(SoundID.MenuOpen);
	}

	private static List<MobileSetting> CreateCategory(int index)
	{
		if (index == 8)
			return MobileSettingsCatalog.ModConfigs(config => config.Open(onClose: () => { Main.menuMode = 0; active = true; SelectCategory(8); ResetInput(true); }));
		List<MobileSetting> result = MobileSettingsCatalog.Create(index, () => { returningFromKeybinds = true; Close(); Main.menuMode = 26; }, () => SelectCategory(8), () => SelectCategory(9), action => pendingDisplayChange = action);
		// The touch editor is an in-world overlay and cannot run from the title screen.
		if (index == 5) result.RemoveAt(0);
		return result;
	}

	private static Action pendingDisplayChange;
	private static void SelectCategory(int index) { category = index; items = CreateCategory(index); scroll = 0; slider = null; dropdown = null; }

	private static void Close()
	{
		active = false;
		slider = null;
		dropdown = null;
		ResetInput(true);
	}

	private static void ResetInput(bool wait)
	{
		down = previousDown = pressed = released = scrolling = false;
		touchId = -1;
		if (wait) pressPosition = pointer;
		previousPointer = pointer;
		consumed = false;
	}

	public override void PostUpdateInput()
	{
		if (returningFromKeybinds && Main.gameMenu && Main.menuMode == 11) {
			returningFromKeybinds = false;
			Main.menuMode = 0;
			active = true;
			SelectCategory(category);
			ResetInput(true);
		}
		else if (returningFromKeybinds && (!Main.gameMenu || (Main.menuMode != 26 && Main.menuMode != 11))) returningFromKeybinds = false;
		if (!Active) {
			if (active && (!Main.gameMenu || Main.menuMode != 0)) Close();
			return;
		}
		ReadPointer();
	}

	private static void ReadPointer()
	{
		down = MobileTouchInput.ReadUiPointer(ref touchId, ref pointer, out bool cancelled);
		if (cancelled) { down = previousDown = pressed = released = false; slider = null; scrolling = false; return; }
		bool justPressed = down && !previousDown;
		pressed |= justPressed;
		released |= !down && previousDown;
		if (justPressed) { pressPosition = pointer; scrolling = false; }
		previousDown = down;
	}

	internal static void DrawOverlay()
	{
		layout = new MobileMenuLayout(PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight, true);
		consumed = false;
		Main.LocalPlayer.mouseInterface = true;
		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
			DepthStencilState.None, RasterizerState.CullCounterClockwise);
		try {
			Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(0, 0, PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight), Color.Black * .42f);
			Nine("Panel", layout.Panel, Color.White, 12);
			Heading(MobileSettingsCatalog.Heading(category));
			DrawTabs();
			DrawRows();
			DrawFooter();
			if (dropdown != null) DrawDropdown();
		}
		finally {
			Main.spriteBatch.End();
			if (released) { slider = null; scrolling = false; }
			pressed = released = false;
			previousPointer = pointer;
		}
		Action displayChange = pendingDisplayChange;
		pendingDisplayChange = null;
		displayChange?.Invoke();
		if (slider == null) SaveChanges();
	}

	private static void DrawTabs()
	{
		for (int i = 0; i < 8; i++) {
			Rectangle r = layout.Tab(i);
			int activeTab = category == 8 ? 7 : category == 9 ? 4 : category;
			if (i < 7) Main.spriteBatch.Draw(Texture("Tab" + i), r, i == activeTab ? Color.White : Color.White * .65f);
			else { Nine("Button", r, i == activeTab ? Color.White : Color.White * .65f, 12); Texture2D icon = ModContent.Request<Texture2D>("MobileJoystick/Assets/MobileControls/Settings", AssetRequestMode.ImmediateLoad).Value; int size = (int)(28 * layout.Scale); Main.spriteBatch.Draw(icon, new Rectangle(r.Center.X - size / 2, r.Center.Y - size / 2, size, size), Color.White); }
			if (i == activeTab) Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(r.X + 3, r.Bottom - Math.Max(2, (int)(3 * layout.Scale)), r.Width - 6, Math.Max(2, (int)(3 * layout.Scale))), new Color(212, 220, 255));
			if (dropdown == null && !consumed && Hit(r)) {
				SelectCategory(i);
				consumed = true;
			}
		}
	}

	private static void DrawRows()
	{
		Rectangle viewport = layout.Viewport;
		float maximum = Math.Max(0, items.Count * 52 - 326);
		if (viewport.Contains(pointer.ToPoint()) && slider == null && dropdown == null) {
			scroll -= PlayerInput.ScrollWheelDelta / 120f * 40;
			if (down && !pressed && viewport.Contains(pressPosition.ToPoint()) && Math.Abs(pointer.Y - pressPosition.Y) > 8 * layout.Scale) scrolling = true;
			if (scrolling && down) scroll -= (pointer.Y - previousPointer.Y) / layout.Scale;
		}
		scroll = MathHelper.Clamp(scroll, 0, maximum);
		if (slider != null && down) Change(slider, MathHelper.Lerp(slider.Min, slider.Max, MathHelper.Clamp((pointer.X - sliderBounds.X) / (float)Math.Max(1, sliderBounds.Width), 0, 1)));
		Rectangle old = Main.instance.GraphicsDevice.ScissorRectangle;
		Main.instance.GraphicsDevice.ScissorRectangle = viewport;
		Main.spriteBatch.End();
		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, new RasterizerState { ScissorTestEnable = true });
		try {
			for (int i = 0; i < items.Count; i++) {
				Rectangle r = layout.Row(i, scroll);
				if (r.Intersects(viewport)) DrawRow(items[i], r);
			}
		}
		finally {
			Main.spriteBatch.End();
			Main.instance.GraphicsDevice.ScissorRectangle = old;
			Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise);
		}
		if (maximum > 0) {
			Rectangle track = layout.At(521, 84, 4, 326);
			Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, track, Color.Black * .5f);
			int thumb = Math.Max((int)(25 * layout.Scale), (int)(track.Height * 326f / (items.Count * 52)));
			Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(track.X, track.Y + (int)((track.Height - thumb) * scroll / maximum), track.Width, thumb), new Color(126, 151, 215));
		}
	}

	private static void DrawRow(MobileSetting item, Rectangle r)
	{
		float s = layout.Scale;
		bool canInput = dropdown == null && layout.Viewport.Contains(pointer.ToPoint()) && layout.Viewport.Contains(pressPosition.ToPoint());
		Nine("CreationRow", r, Color.White, 12);
		Label(item.Label, new Rectangle(r.X + (int)(11 * s), r.Y, (int)((item.Kind == MobileSettingKind.Slider ? 210 : 265) * s), r.Height), .9f, false);
		Rectangle control = new(r.Right - (int)(208 * s), r.Y + (int)(7 * s), (int)(197 * s), r.Height - (int)(14 * s));
		switch (item.Kind) {
			case MobileSettingKind.Toggle:
				bool on = item.Read() > .5f;
				MobileMenuFrame.ToggleBounds(r, s, out Rectangle off, out Rectangle yes);
				MobileMenuFrame.Toggle(Main.spriteBatch, Texture("Button"), Texture("SwitchOn"), off, yes, on, s);
				Label(T("关闭", "Off"), off, .7f); Label(T("开启", "On"), yes, .7f);
				if (canInput && Hit(off)) Change(item, 0); if (canInput && Hit(yes)) Change(item, 1);
				break;
			case MobileSettingKind.Slider:
				Rectangle bar = MobileMenuFrame.SliderTrack(r, s);
				float fraction = MathHelper.Clamp((item.Read() - item.Min) / Math.Max(.0001f, item.Max - item.Min), 0, 1);
				MobileMenuFrame.Slider(Main.spriteBatch, Texture("PlayerSliderKnob"), bar, fraction, s);
				Label(item.Display?.Invoke() ?? "", new Rectangle(r.X + (int)(225 * s), r.Y, (int)(68 * s), r.Height), .8f);
				Rectangle sliderHit = new(bar.X - (int)(12 * s), r.Y, bar.Width + (int)(24 * s), r.Height);
				if (canInput && !consumed && pressed && sliderHit.Contains(pointer.ToPoint())) { slider = item; sliderBounds = bar; consumed = true; Change(item, MathHelper.Lerp(item.Min, item.Max, MathHelper.Clamp((pointer.X - bar.X) / (float)Math.Max(1, bar.Width), 0, 1))); }
				break;
			case MobileSettingKind.Choice:
				Label(item.Choices[Math.Clamp((int)item.Read(), 0, item.Choices.Length - 1)], new Rectangle(control.X, control.Y, control.Width - (int)(35 * s), control.Height), .7f);
				MobileMenuFrame.DropdownDivider(Main.spriteBatch, r, s);
				Main.spriteBatch.Draw(Texture("WorldDropdown"), new Rectangle(r.Right - (int)(37 * s), r.Center.Y - (int)(9 * s), (int)(24 * s), (int)(18 * s)), Color.White);
				if (canInput && Hit(control)) dropdown = item;
				break;
			case MobileSettingKind.Action:
				ButtonText(item.Display?.Invoke() ?? T("编辑…", "Edit..."), control, .68f);
				if (canInput && Hit(control)) item.Activate?.Invoke();
				break;
		}
	}

	private static void DrawFooter()
	{
		ButtonText(T("返回", "Back"), layout.Back, .8f);
		Main.spriteBatch.Draw(Texture("Back"), layout.At(22, 438, 25, 21), Color.White);
		if (dropdown == null && Hit(layout.Back)) { if (category >= 8) SelectCategory(category == 8 ? 7 : 4); else Close(); return; }
	}

	private static void DrawDropdown()
	{
		MobileSetting item = dropdown;
		Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, layout.Panel, Color.Black * .6f);
		int height = item.Choices.Length * 44 + 64;
		Rectangle panel = layout.At(113, (480 - height) / 2, 310, height);
		Nine("CreationPopup", panel, Color.White, 12);
		Label(item.Label, new Rectangle(panel.X + 8, panel.Y + 4, panel.Width - 16, (int)(46 * layout.Scale)), .8f);
		for (int i = 0; i < item.Choices.Length; i++) {
			Rectangle r = new(panel.X + (int)(10 * layout.Scale), panel.Y + (int)((50 + i * 44) * layout.Scale), panel.Width - (int)(20 * layout.Scale), (int)(38 * layout.Scale));
			Nine((int)item.Read() == i ? "CreationOptionSelected" : "CreationOption", r, Color.White, 8);
			Label(item.Choices[i], r, .8f);
			if (Hit(r)) { Change(item, i); dropdown = null; break; }
		}
		if (dropdown != null && !consumed && released && !panel.Contains(pointer.ToPoint())) { dropdown = null; consumed = true; }
	}

	private static void Change(MobileSetting item, float value)
	{
		if (Math.Abs(item.Read() - value) < .0001f) return;
		item.Write(value);
		if (item.ModSetting) modDirty = true;
		else settingsDirty = true;
	}

	private static void SaveChanges()
	{
		try {
			if (modDirty) { MobileJoystickConfig.Instance.SaveFromMobileMenu(); modDirty = false; }
			if (settingsDirty) {
				if (!Main.SaveSettings() || !PlayerInput.Save()) throw new System.IO.IOException("Terraria could not save settings or the input profile.");
				settingsDirty = false;
			}
		}
		catch (Exception ex) {
			ModContent.GetInstance<MobileTitleSettingsSystem>().Mod.Logger.Error("Could not save mobile title settings", ex);
			modDirty = settingsDirty = false;
		}
	}

	private static bool Hit(Rectangle r)
	{
		if (consumed || !released || scrolling || slider != null || !r.Contains(pointer.ToPoint()) || !r.Contains(pressPosition.ToPoint())) return false;
		consumed = true;
		SoundEngine.PlaySound(SoundID.MenuTick);
		return true;
	}

	private static void Heading(string text) { Nine("Title", layout.Title, Color.White, 20); Label(text, layout.Title, 1f, display: true); }
	private static void ButtonText(string text, Rectangle r, float size) { Nine("Button", r, r.Contains(pointer.ToPoint()) ? new Color(230, 238, 255) : Color.White, 8); Label(text, r, size); }
	private static void Label(string text, Rectangle r, float size, bool center = true, Color? color = null, bool display = false)
	{
		if (display) size *= MobileUiMetrics.DisplayFontScale;
		DynamicSpriteFont font = display ? FontAssets.DeathText.Value : FontAssets.MouseText.Value;
		float scale = Math.Min(size * MobileUiMetrics.FontScale * layout.Scale, (r.Width - 10 * layout.Scale) / Math.Max(1, font.MeasureString(text).X));
		int top = int.MaxValue, bottom = int.MinValue;
		foreach (char c in text) {
			if (char.IsWhiteSpace(c) || !font.SpriteCharacters.TryGetValue(c, out var glyph)) continue;
			top = Math.Min(top, glyph.Padding.Y);
			bottom = Math.Max(bottom, glyph.Padding.Y + glyph.Glyph.Height);
		}
		float middle = top == int.MaxValue ? font.LineSpacing / 2f : (top + bottom) / 2f;
		Vector2 pos = new(center ? r.Center.X - font.MeasureString(text).X * scale / 2 : r.X, r.Center.Y - middle * scale);
		pos = new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y));
		Utils.DrawBorderStringFourWay(Main.spriteBatch, font, text, pos.X, pos.Y, color ?? Color.White, Color.Black, Vector2.Zero, scale);
	}
	private static Texture2D Texture(string name) => ModContent.Request<Texture2D>($"MobileJoystick/Assets/MobileMenu/{name}", AssetRequestMode.ImmediateLoad).Value;
	private static void Nine(string name, Rectangle destination, Color tint, int border)
	{
		Texture2D tex = Texture(name); int src = Math.Min(border, Math.Min(tex.Width, tex.Height) / 2); int b = Math.Min(Math.Max(1, (int)(src * layout.Scale)), Math.Min(destination.Width, destination.Height) / 2);
		int[] sx = { 0, src, tex.Width - src, tex.Width }, sy = { 0, src, tex.Height - src, tex.Height }; int[] dx = { destination.Left, destination.Left + b, destination.Right - b, destination.Right }, dy = { destination.Top, destination.Top + b, destination.Bottom - b, destination.Bottom };
		for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) if (dx[x + 1] > dx[x] && dy[y + 1] > dy[y]) Main.spriteBatch.Draw(tex, new Rectangle(dx[x], dy[y], dx[x + 1] - dx[x], dy[y + 1] - dy[y]), new Rectangle(sx[x], sy[y], sx[x + 1] - sx[x], sy[y + 1] - sy[y]), tint);
	}
}
