using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace MobileJoystick;

internal sealed class MobileNpcDialogSystem : ModSystem
{
	private string primaryText = "", secondaryText = "";
	private int pendingAction = -1;
	private bool drawingNative;
	private readonly RasterizerState raster = new() { ScissorTestEnable = true, CullMode = CullMode.None };
	private bool down, previousDown, released;
	private int touchId = -1;
	private Vector2 pointer, pressPosition;
	private Rectangle panel, primary, secondary, close, happiness;
	private float scale;

	private static string T(string zh, string en) => MobileSettingsCatalog.Text(zh, en);
	internal static bool IsTalking => MobileJoystickConfig.Instance?.EnableMobileControls == true
		&& !Main.gameMenu && !Main.playerInventory && !Main.editSign && !Main.editChest
		&& Main.LocalPlayer.talkNPC >= 0 && Main.LocalPlayer.talkNPC < Main.maxNPCs
		&& !string.IsNullOrEmpty(Main.npcChatText);

	public override void Load()
	{
		if (Main.dedServ) return;
		On_Main.GUIChatDrawInner += DrawDialog;
		On_Main.DrawNPCChatButtons += NativeButtons;
	}
	public override void Unload()
	{
		if (!Main.dedServ) {
			On_Main.GUIChatDrawInner -= DrawDialog;
			On_Main.DrawNPCChatButtons -= NativeButtons;
		}
		raster.Dispose();
	}

	public override void PostUpdateInput()
	{
		if (!IsTalking) { down = previousDown = released = false; touchId = -1; pendingAction = -1; return; }
		ReadPointer();
	}

	private void ReadPointer()
	{
		down = MobileTouchInput.ReadUiPointer(ref touchId, ref pointer, out bool cancelled);
		if (cancelled) { down = previousDown = released = false; pendingAction = -1; return; }
		released |= !down && previousDown;
		if (down && !previousDown) pressPosition = pointer;
		previousDown = down;
	}

	// Keep native labels and dispatch, including every ModNPC/GlobalNPC hook.
	// Native drawing is clipped; our mobile panel replaces only its presentation.
	private void DrawDialog(On_Main.orig_GUIChatDrawInner orig, Main self)
	{
		if (!IsTalking) { orig(self); return; }
		BuildLayout();
		if (released) {
			if (Hit(close)) pendingAction = 0;
			else if (!string.IsNullOrEmpty(primaryText) && Hit(primary)) pendingAction = 1;
			else if (!string.IsNullOrEmpty(secondaryText) && Hit(secondary)) pendingAction = 2;
			else if (happiness.Width > 0 && Hit(happiness)) pendingAction = 3;
		}
		int x = Main.mouseX, y = Main.mouseY;
		bool left = Main.mouseLeft, release = Main.mouseLeftRelease;
		Rectangle scissor = Main.instance.GraphicsDevice.ScissorRectangle;
		Main.spriteBatch.End();
		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
			DepthStencilState.None, raster, null, Main.UIScaleMatrix);
		Main.instance.GraphicsDevice.ScissorRectangle = Rectangle.Empty;
		drawingNative = true;
		try {
			Main.mouseLeft = Main.mouseLeftRelease = false;
			orig(self);
		}
		finally {
			drawingNative = false;
			pendingAction = -1;
			Main.mouseX = x; Main.mouseY = y;
			Main.mouseLeft = left; Main.mouseLeftRelease = release;
			Main.spriteBatch.End();
			Main.instance.GraphicsDevice.ScissorRectangle = scissor;
			Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
				DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
		}
		DrawLayer();
		released = false;
	}

	private bool Hit(Rectangle bounds) => bounds.Contains(pointer.ToPoint()) && bounds.Contains(pressPosition.ToPoint());

	private void NativeButtons(On_Main.orig_DrawNPCChatButtons orig, int color, Color chatColor, int lines, string first, string second)
	{
		if (!drawingNative) { orig(color, chatColor, lines, first, second); return; }
		primaryText = first;
		secondaryText = second;
		Main.npcChatFocus1 = pendingAction == 0;
		Main.npcChatFocus2 = pendingAction == 1 && !string.IsNullOrEmpty(first);
		Main.npcChatFocus3 = pendingAction == 2 && !string.IsNullOrEmpty(second);
		Main.npcChatFocus4 = pendingAction == 3;
		// The native dispatcher also checks its dialogue rectangle.
		Main.mouseX = Main.screenWidth / 2;
		Main.mouseY = 110;
		Main.mouseLeft = Main.mouseLeftRelease = pendingAction >= 0;
	}

	private bool DrawLayer()
	{
		if (!IsTalking) return true;
		BuildLayout();
		Main.LocalPlayer.mouseInterface = true;
		Main.LocalPlayer.controlUseItem = false;
		Main.LocalPlayer.controlUseTile = false;
		Main.spriteBatch.End();
		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, raster);
		try {
			Nine("Panel", panel, Color.White);
			NPC npc = Main.npc[Main.LocalPlayer.talkNPC];
			DrawNpcHead(npc);
			Label(npc.FullName, new Rectangle(panel.X + (int)(96 * scale), panel.Y + (int)(12 * scale), panel.Width - (int)(110 * scale), (int)(32 * scale)), .95f, false);
			DrawText(Main.npcChatText, new Rectangle(panel.X + (int)(96 * scale), panel.Y + (int)(48 * scale), panel.Width - (int)(112 * scale), panel.Height - (int)(122 * scale)));
			if (!string.IsNullOrEmpty(primaryText)) Button(primaryText, primary);
			if (!string.IsNullOrEmpty(secondaryText)) Button(secondaryText, secondary);
			Button(T("关闭", "Close"), close);
			if (happiness.Width > 0) Button(T("幸福度", "Happiness"), happiness);
		}
		finally {
			Main.spriteBatch.End();
			Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
		}
		return true;
	}

	private void BuildLayout()
	{
		scale = Math.Min(PlayerInput.RealScreenWidth / 1280f, PlayerInput.RealScreenHeight / 576f);
		int width = Math.Min((int)(620 * scale), (int)(PlayerInput.RealScreenWidth * .72f));
		int height = (int)(260 * scale);
		panel = new((PlayerInput.RealScreenWidth - width) / 2, (PlayerInput.RealScreenHeight - height) / 2, width, height);
		int count = 1 + (string.IsNullOrEmpty(primaryText) ? 0 : 1) + (string.IsNullOrEmpty(secondaryText) ? 0 : 1)
			+ (Main.LocalPlayer.currentShoppingSettings.HappinessReport.Length == 0 ? 0 : 1);
		int buttonWidth = (width - (int)(28 * scale) - (count - 1) * (int)(10 * scale)) / count;
		int buttonHeight = (int)(44 * scale);
		int gap = (int)(10 * scale);
		int y = panel.Bottom - buttonHeight - (int)(14 * scale);
		close = new(panel.Right - buttonWidth - (int)(14 * scale), y, buttonWidth, buttonHeight);
		int next = close.X - buttonWidth - gap;
		primary = string.IsNullOrEmpty(primaryText) ? Rectangle.Empty : new(next, y, buttonWidth, buttonHeight);
		if (primary.Width > 0) next -= buttonWidth + gap;
		secondary = string.IsNullOrEmpty(secondaryText) ? Rectangle.Empty : new(next, y, buttonWidth, buttonHeight);
		if (secondary.Width > 0) next -= buttonWidth + gap;
		happiness = Main.LocalPlayer.currentShoppingSettings.HappinessReport.Length == 0
			? Rectangle.Empty : new(next, y, buttonWidth, buttonHeight);
	}

	private void DrawNpcHead(NPC npc)
	{
		int head = TownNPCProfiles.GetHeadIndexSafe(npc);
		if (head <= 0 || head >= TextureAssets.NpcHead.Length) return;
		Texture2D texture = TextureAssets.NpcHead[head].Value;
		float fit = Math.Min((int)(78 * scale) / (float)texture.Width, (int)(78 * scale) / (float)texture.Height);
		Main.spriteBatch.Draw(texture, new Vector2(panel.X + 50 * scale, panel.Y + 52 * scale), null, Color.White, 0f, texture.Size() / 2f, fit, SpriteEffects.None, 0f);
	}

	private void DrawText(string text, Rectangle bounds)
	{
		string[] words = text.Replace("\r", "").Split('\n');
		int line = 0; string current = "";
		foreach (string word in words) {
			foreach (char c in word) {
				string candidate = current + c;
				if (FontAssets.MouseText.Value.MeasureString(candidate).X * .72f > bounds.Width && current.Length > 0) {
					Label(current, new Rectangle(bounds.X, bounds.Y + line * (int)(23 * scale), bounds.Width, (int)(24 * scale)), .72f, false, new Color(235, 240, 255));
					line++; current = c.ToString();
				}
				else current = candidate;
			}
			Label(current, new Rectangle(bounds.X, bounds.Y + line * (int)(23 * scale), bounds.Width, (int)(24 * scale)), .72f, false, new Color(235, 240, 255));
			line++; current = "";
			if (bounds.Y + line * (int)(23 * scale) > bounds.Bottom - (int)(24 * scale)) break;
		}
	}

	private void Button(string text, Rectangle bounds)
	{
		Nine("PauseButton", bounds, bounds.Contains(pointer.ToPoint()) ? Color.White : new Color(210, 218, 242));
		Label(text, bounds, .72f);
	}

	private static Texture2D Texture(string name) => ModContent.Request<Texture2D>($"MobileJoystick/Assets/MobileMenu/{name}", AssetRequestMode.ImmediateLoad).Value;
	private void Nine(string name, Rectangle destination, Color tint)
	{
		Texture2D texture = Texture(name); int src = Math.Min(12, Math.Min(texture.Width, texture.Height) / 2); int border = Math.Min(Math.Max(1, (int)(src * scale * .65f)), Math.Min(destination.Width, destination.Height) / 2);
		int[] sx = { 0, src, texture.Width - src, texture.Width }, sy = { 0, src, texture.Height - src, texture.Height };
		int[] dx = { destination.Left, destination.Left + border, destination.Right - border, destination.Right }, dy = { destination.Top, destination.Top + border, destination.Bottom - border, destination.Bottom };
		for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) if (dx[x + 1] > dx[x] && dy[y + 1] > dy[y])
			Main.spriteBatch.Draw(texture, new Rectangle(dx[x], dy[y], dx[x + 1] - dx[x], dy[y + 1] - dy[y]), new Rectangle(sx[x], sy[y], sx[x + 1] - sx[x], sy[y + 1] - sy[y]), tint);
	}
	private void Label(string text, Rectangle bounds, float size, bool center = true, Color? color = null)
	{
		DynamicSpriteFont font = FontAssets.MouseText.Value;
		float textScale = Math.Min(size * scale, (bounds.Width - 8 * scale) / Math.Max(1, font.MeasureString(text).X));
		Vector2 position = center ? new(bounds.Center.X - font.MeasureString(text).X * textScale / 2f, bounds.Center.Y - font.LineSpacing * textScale / 2f) : new(bounds.X, bounds.Center.Y - font.LineSpacing * textScale / 2f);
		Utils.DrawBorderStringFourWay(Main.spriteBatch, font, text, position.X, position.Y, color ?? Color.White, Color.Black, Vector2.Zero, textScale);
	}
}
