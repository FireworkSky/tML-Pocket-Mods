using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.Cil;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Minimap;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace MobileJoystick;

// Keep vanilla map tiles, player/NPC markers, zoom and other mods' map layers.
// Only the minimap viewport and frame are changed.
public partial class MobileMinimapSystem : ModSystem
{
	private Asset<Texture2D> frame;
	private bool viewportHookApplied;

	private bool UseMobileMap => viewportHookApplied && MobileJoystickInput.EnabledThisFrame
		&& MobileJoystickConfig.Instance.UseMobileMinimap && !Main.mapFullscreen && Main.mapStyle == 1;

	public override void Load()
	{
		if (Main.dedServ) {
			return;
		}

		frame = ModContent.Request<Texture2D>($"{Mod.Name}/Assets/MobileControls/MinimapFrame");
		IL_Main.DrawMap += ModifyMinimapViewport;
		On_Main.DrawMap += DrawMap;
		On_Main.DrawMapFullscreenBackground += DrawFullscreenBackground;
		On_MinimapFrame.DrawBackground += DrawBackground;
		On_MinimapFrame.DrawForeground += DrawForeground;
		On_MinimapFrame.Update += UpdateFrame;
	}

	public override void Unload()
	{
		if (!Main.dedServ) {
			IL_Main.DrawMap -= ModifyMinimapViewport;
			On_Main.DrawMap -= DrawMap;
			On_Main.DrawMapFullscreenBackground -= DrawFullscreenBackground;
			On_MinimapFrame.DrawBackground -= DrawBackground;
			On_MinimapFrame.DrawForeground -= DrawForeground;
			On_MinimapFrame.Update -= UpdateFrame;
		}
		frame = null;
		viewportHookApplied = false;
	}

	private void DrawMap(On_Main.orig_DrawMap orig, Main self, GameTime time)
	{
		// IngameOptions also calls DrawMap directly, bypassing interface layers.
		if (MobileMenuSystem.EditingCustomControls || MobileChatSystem.IsOpen) return;
		if (!UseMobileFullscreen) { orig(self, time); return; }
		bool left = Main.mouseLeft, release = Main.mouseLeftRelease;
		int x = Main.mouseX, y = Main.mouseY;
		try {
			Vector2? tap = MobileJoystickInput.ConsumeMapTap();
			Main.mouseLeft = Main.mouseLeftRelease = tap.HasValue;
			if (tap.HasValue) { Main.mouseX = (int)tap.Value.X; Main.mouseY = (int)tap.Value.Y; }
			orig(self, time);
		}
		finally { Main.mouseLeft = left; Main.mouseLeftRelease = release; Main.mouseX = x; Main.mouseY = y; }
		if (Main.mapFullscreen) DrawFullscreenControls();
	}

	private void ModifyMinimapViewport(ILContext il)
	{
		RemoveDesktopMapClose(il);
		ILCursor cursor = new(il);
		// The second assignment follows vanilla's map-scale conversion. Override all
		// four viewport fields here, before any tile ranges or icon bounds are read.
		if (!cursor.TryGotoNext(MoveType.After, instruction => instruction.MatchStsfld<Main>(nameof(Main.miniMapY)))
			|| !cursor.TryGotoNext(MoveType.After, instruction => instruction.MatchStsfld<Main>(nameof(Main.miniMapY)))) {
			Mod.Logger.Warn("Mobile minimap viewport hook unavailable; retaining the vanilla minimap.");
			return;
		}
		cursor.EmitDelegate<Action>(ApplyViewport);
		viewportHookApplied = true;
	}

	private void ApplyViewport()
	{
		if (!UseMobileMap) {
			return;
		}
		Rectangle bounds = MobileJoystickLayout.GetMinimapBounds(MobileJoystickConfig.Instance,
			PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight);
		float drawScale = Main.UIScale * Main.MapScale;
		Main.miniMapX = (int)(bounds.X / drawScale);
		Main.miniMapY = (int)(bounds.Y / drawScale);
		Main.miniMapWidth = Math.Max(1, (int)(bounds.Width / drawScale));
		Main.miniMapHeight = Math.Max(1, (int)(bounds.Height / drawScale));
	}

	private void UpdateFrame(On_MinimapFrame.orig_Update orig, MinimapFrame self)
	{
		// Vanilla's square frame buttons are replaced by the mobile zoom buttons.
		if (!UseMobileMap) {
			orig(self);
		}
	}

	private void DrawBackground(On_MinimapFrame.orig_DrawBackground orig, MinimapFrame self, SpriteBatch spriteBatch)
	{
		if (!UseMobileMap) {
			orig(self, spriteBatch);
			return;
		}
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle(Main.miniMapX, Main.miniMapY, Main.miniMapWidth, Main.miniMapHeight), Color.Black * Main.mapMinimapAlpha);
	}

	private void DrawForeground(On_MinimapFrame.orig_DrawForeground orig, MinimapFrame self, SpriteBatch spriteBatch)
	{
		if (!UseMobileMap) {
			orig(self, spriteBatch);
			return;
		}
		float scale = MobileJoystickLayout.GetControlScale(MobileJoystickConfig.Instance,
			PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight) / (Main.UIScale * Main.MapScale);
		int border = Math.Max(1, (int)(8f * scale));
		Rectangle bounds = new(Main.miniMapX - border, Main.miniMapY - border,
			Main.miniMapWidth + border * 2, Main.miniMapHeight + border * 2);
		DrawFrame(spriteBatch, frame.Value, bounds, Math.Max(1, (int)(12f * scale)), Color.White * Main.mapMinimapAlpha);
	}

	private static void DrawFrame(SpriteBatch spriteBatch, Texture2D texture, Rectangle bounds, int corner, Color color)
	{
		const int sourceCorner = 12;
		int[] sourceX = { 0, sourceCorner, texture.Width - sourceCorner, texture.Width };
		int[] sourceY = { 0, sourceCorner, texture.Height - sourceCorner, texture.Height };
		int[] targetX = { bounds.Left, bounds.Left + corner, bounds.Right - corner, bounds.Right };
		int[] targetY = { bounds.Top, bounds.Top + corner, bounds.Bottom - corner, bounds.Bottom };
		for (int y = 0; y < 3; y++) {
			for (int x = 0; x < 3; x++) {
				if (x == 1 && y == 1) {
					continue;
				}
				spriteBatch.Draw(texture,
					new Rectangle(targetX[x], targetY[y], targetX[x + 1] - targetX[x], targetY[y + 1] - targetY[y]),
					new Rectangle(sourceX[x], sourceY[y], sourceX[x + 1] - sourceX[x], sourceY[y + 1] - sourceY[y]), color);
			}
		}
	}
}
