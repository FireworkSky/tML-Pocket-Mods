using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.Social;

namespace MobileJoystick;

internal sealed partial class MobileMultiplayerSystem
{
	private void DrawMenu(On_Main.orig_DrawMenu orig, Main self, GameTime time)
	{
		orig(self, time);
		if (!Active) return;
		layout = new(PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight);
		UpdateTextInput();
		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
			DepthStencilState.None, RasterizerState.CullCounterClockwise);
		try {
			if (page == Page.Browser) DrawBrowser();
			else if (page == Page.Connect) DrawConnect();
			else DrawHostOptions();
			if (notice.Length > 0) Label(notice, layout.Hint, .82f, color: Color.LightGoldenrodYellow);
		} finally { Main.spriteBatch.End(); }
	}

	private void DrawBrowser()
	{
		DrawFrame(layout, false);
		Header(layout.LocalRow, T("本地", "Local"), "LocalIcon", localExpanded);
		if (localExpanded) Label(T("使用“添加”通过 IP 加入本地 tModLoader 会话", "Use Add to join a local tModLoader session by IP"), layout.LocalStatus, .97f);
		Header(layout.RemoteHeader(localExpanded), T("远程", "Remote"), "RemoteIcon", remoteExpanded);
		if (remoteExpanded) {
			Label(endpoints.Count == 0 ? T("暂无已保存会话，点击“添加”", "No saved sessions. Tap Add") : T("已保存的会话", "Saved sessions"), layout.RemoteStatus(localExpanded), 1.05f);
			Rectangle area = EndpointArea;
			for (int i = 0; i < endpoints.Count; i++) {
				Rectangle row = EndpointBounds(i);
				// Draw complete rows only so neither content nor hit targets cross the footer.
				if (!area.Contains(row)) continue;
				Nine("MultiplayerRow", row, Color.White);
				Icon("RemoteIcon", new Rectangle(row.X + S(8), row.Y + S(5), S(31), S(31)));
				Label($"{endpoints[i].Address}:{endpoints[i].Port}", new Rectangle(row.X + S(48), row.Y, row.Width - S(58), row.Height), 1.05f, false);
			}
		}
		FooterButton(T("返回", "Back"), layout.Back, "Back");
		FooterButton(T("添加", "Add"), layout.Add, "AddServerIcon");
	}

	internal static void DrawFrame(MobileMultiplayerLayout geometry, bool host)
	{
		Nine("MultiplayerPanel", geometry.Panel, Color.White);
		Nine("MultiplayerTitle", geometry.Title, Color.White);
		Label(T("多人模式", "Multiplayer"), geometry.Title, 1.5f * geometry.Scale, display: true);
		DrawTab(geometry.JoinTab, T("加入", "Join"), "JoinIcon", !host, geometry.Scale);
		DrawTab(geometry.HostTab, T("主机", "Host"), "HostIcon", host, geometry.Scale);
		Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, geometry.At(369, 130, 542, 2), new Color(29, 43, 80, 175));
		Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, geometry.At(369, 473, 542, 2), new Color(29, 43, 80, 175));
	}

	private static void DrawTab(Rectangle bounds, string text, string icon, bool selected, float scale)
	{
		Nine("MultiplayerTab", bounds, selected ? Color.White : new Color(155, 170, 205, 210));
		Icon(icon, new Rectangle(bounds.X + (int)(5 * scale), bounds.Y + (int)(3 * scale), (int)(39 * scale), (int)(39 * scale)));
		Label(text, new Rectangle(bounds.X + (int)(48 * scale), bounds.Y, bounds.Width - (int)(55 * scale), bounds.Height), 1.2f * scale);
	}
	private void Header(Rectangle bounds, string text, string icon, bool expanded)
	{
		Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, bounds, new Color(79, 105, 180, 210));
		Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(bounds.X, bounds.Y, bounds.Width, S(2)), new Color(112, 139, 207, 180));
		Icon(icon, new Rectangle(bounds.X + S(8), bounds.Y + S(5), S(33), S(33)));
		Label(text, new Rectangle(bounds.X + S(49), bounds.Y, bounds.Width - S(95), bounds.Height), 1.2f * layout.Scale, false);
		Icon(expanded ? "ExpandUp" : "ExpandDown", new Rectangle(bounds.Right - S(36), bounds.Y + S(9), S(25), S(25)));
	}

	private void DrawConnect()
	{
		Nine("MultiplayerPanel", layout.FormPanel, Color.White);
		Nine("MultiplayerTitle", layout.FormTitle, Color.White);
		Label(T("连接", "Connect"), layout.FormTitle, 1.5f * layout.Scale, display: true);
		Field(layout.Field(0), T("IP 地址：", "IP Address:"), address);
		Field(layout.Field(1), T("端口：", "Port:"), port);
		Field(layout.Field(2), T("密码：", "Password:"), password.Length == 0 ? T("无", "None") : new string('*', Math.Min(24, password.Length)));
		Toggle(layout.Field(3), T("记住：", "Remember:"), remember);
		FooterButton(T("返回", "Back"), layout.FormBack, "Back");
		FooterButton(T("加入", "Join"), layout.FormJoin, "Play", !string.IsNullOrWhiteSpace(address));
	}

	private void DrawHostOptions()
	{
		Nine("MultiplayerPanel", layout.HostPanel, Color.White);
		Nine("MultiplayerTitle", layout.HostTitle, Color.White);
		Label(T("主机", "Host"), layout.HostTitle, 1.5f * layout.Scale, display: true);
		DrawWorldSummary();
		Rectangle row = layout.HostOption(0);
		Nine("MultiplayerRow", row, Color.White);
		Label(T("最大玩家数：", "Max players:"), new Rectangle(row.X + S(10), row.Y, S(240), row.Height), 1.2f * layout.Scale, false);
		Label($"{maximumPlayers}/8", new Rectangle(row.X + S(242), row.Y, S(62), row.Height), 1.2f * layout.Scale);
		Rectangle track = layout.Slider;
		Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, track, new Color(10, 15, 25));
		Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(track.X + S(3), track.Y + S(4), track.Width - S(6), track.Height - S(8)), new Color(73, 78, 77));
		int knobX = track.X + (int)((maximumPlayers - 1) / 7f * track.Width);
		Icon("PlayerSliderKnob", new Rectangle(knobX - S(15), row.Y + S(5), S(30), S(38)));
		Toggle(layout.HostOption(1), T("仅限邀请", "Invite only"), inviteOnly, SocialAPI.Network != null);
		Toggle(layout.HostOption(2), T("跨平台：", "Cross-platform:"), false, false);
		Field(layout.HostOption(3), T("密码：", "Password:"), hostPassword.Length == 0 ? T("无", "None") : new string('*', Math.Min(24, hostPassword.Length)));
		FooterButton(T("返回", "Back"), layout.HostBack, "Back");
		FooterButton(T("开始游戏", "Play"), layout.HostStart, "Play", hostArgumentsPatched);
	}

	private UIWorldListItem summaryItem;
    private Terraria.IO.WorldFileData summaryData;
    private static readonly MethodInfo SummaryIcon = typeof(AWorldListItem).GetMethod("GetIcon", BindingFlags.Instance | BindingFlags.NonPublic);
    private void DrawWorldSummary()
	{
		var data = Main.ActiveWorldFileData;
		if (data == null) return;
		Rectangle area = layout.WorldSummary;
		if (summaryData != data) { summaryData = data; summaryItem = new UIWorldListItem(data, 0, true); }
		if (SummaryIcon?.Invoke(summaryItem, null) is Asset<Texture2D> icon) {
			Rectangle picture = new(area.X, area.Y, S(70), S(70));
			DrawIcon(icon.Value, picture);
		}
		Label(data.Name, new Rectangle(area.X + S(80), area.Y, area.Width - S(85), S(35)), 1.3f * layout.Scale, false);
		string difficulty = Terraria.Localization.Language.GetTextValue(data.GameMode switch { 1 => "UI.Expert", 2 => "UI.Master", 3 => "UI.Creative", _ => "UI.Normal" });
		Label(difficulty + "    " + data.WorldSizeName, new Rectangle(area.X + S(80), area.Y + S(42), area.Width - S(85), S(29)), layout.Scale, false,
			data.GameMode == 3 ? Main.creativeModeColor : Color.White);
	}

	private void Field(Rectangle row, string title, string value)
	{
		bool editing = saveEdit != null && row == (page == Page.Connect ? layout.Field(editingField) : layout.HostOption(editingField));
		Nine("MultiplayerRow", row, editing ? new Color(155, 180, 255) : Color.White);
		if (editing) value = (secretEdit ? new string('*', editText.Length) : editText) + (Main.GameUpdateCount % 60 < 30 ? "|" : "");
		Label(title, new Rectangle(row.X + S(10), row.Y, S(155), row.Height), 1.2f * layout.Scale, false);
		float width = FontAssets.MouseText.Value.MeasureString(value).X * 1.15f * layout.Scale;
		int left = editing ? row.X + S(168) : Math.Max(row.X + S(168), row.Right - S(12) - (int)width);
		Label(value, new Rectangle(left, row.Y, row.Right - S(10) - left, row.Height), 1.15f * layout.Scale, false);
		if (editing) Main.instance.DrawWindowsIMEPanel(new Vector2(row.Left, row.Bottom), layout.Scale);
	}
	private void Toggle(Rectangle row, string title, bool value, bool enabled = true)
	{
		Nine("MultiplayerRow", row, Color.White);
		Color tint = enabled ? Color.White : Color.White * .48f;
		Label(title, new Rectangle(row.X + S(10), row.Y, S(340), row.Height), 1.2f * layout.Scale, false, tint);
		foreach (bool on in new[] { false, true }) {
			Rectangle toggle = layout.Toggle(row, on);
			if (value == on) Nine(on ? "ToggleOn" : "ToggleOff", toggle, enabled ? Color.White : Color.White * .55f);
			Label(on ? T("开启", "On") : T("关闭", "Off"), toggle, 1.05f * layout.Scale, color: value == on ? tint : Color.Gray);
		}
	}
	private void FooterButton(string text, Rectangle bounds, string icon, bool enabled = true)
	{
		Nine("MultiplayerButton", bounds, enabled ? Color.White : new Color(145, 150, 170));
		Rectangle picture = new(bounds.X + S(7), bounds.Y + S(5), S(36), S(36));
		if (icon == "Play") Icon("MobilePlay", picture, enabled ? Color.White : Color.White * .4f);
		else Icon(icon, picture, enabled ? Color.White : Color.White * .4f);
		Label(text, new Rectangle(bounds.X + S(48), bounds.Y, bounds.Width - S(56), bounds.Height), 1.2f * layout.Scale, color: enabled ? Color.White : Color.Gray);
	}
	private int S(float value) => Math.Max(1, (int)(value * layout.Scale));
	private static Texture2D Texture(string name) => ModContent.Request<Texture2D>($"MobileJoystick/Assets/MobileMenu/{name}", AssetRequestMode.ImmediateLoad).Value;
	private static void Icon(string name, Rectangle bounds, Color? color = null) => DrawIcon(Texture(name), bounds, color);
	private static void DrawIcon(Texture2D tex, Rectangle bounds, Color? color = null)
	{
		float fit = Math.Min(bounds.Width / (float)tex.Width, bounds.Height / (float)tex.Height);
		Main.spriteBatch.Draw(tex, bounds.Center.ToVector2(), null, color ?? Color.White, 0, tex.Size() / 2, fit, SpriteEffects.None, 0);
	}
	private static void Nine(string name, Rectangle destination, Color tint)
	{
		float scale = Math.Min(PlayerInput.RealScreenWidth / 1280f, PlayerInput.RealScreenHeight / 576f);
		MobileMenuFrame.Draw(Main.spriteBatch, Texture(name), destination, tint, scale);
	}
	private static void Label(string text, Rectangle bounds, float size, bool center = true, Color? color = null, bool display = false)
	{
		if (display) size *= MobileUiMetrics.DisplayFontScale;
		var font = display ? FontAssets.DeathText.Value : FontAssets.MouseText.Value;
		float scale = Math.Min(size * MobileUiMetrics.FontScale, (bounds.Width - 8) / Math.Max(1, font.MeasureString(text).X));
		int top = int.MaxValue, bottom = int.MinValue;
		foreach (char c in text) if (!char.IsWhiteSpace(c) && font.SpriteCharacters.TryGetValue(c, out var glyph)) {
			top = Math.Min(top, glyph.Padding.Y); bottom = Math.Max(bottom, glyph.Padding.Y + glyph.Glyph.Height);
		}
		float middle = top == int.MaxValue ? font.LineSpacing / 2f : (top + bottom) / 2f;
		Utils.DrawBorderStringFourWay(Main.spriteBatch, font, text, center ? bounds.Center.X - font.MeasureString(text).X * scale / 2 : bounds.X,
			bounds.Center.Y - middle * scale, color ?? Color.White, Color.Black, Vector2.Zero, scale);
	}
}
