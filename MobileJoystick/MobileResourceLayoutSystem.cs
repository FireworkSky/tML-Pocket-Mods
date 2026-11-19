using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI.ResourceSets;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace MobileJoystick;

// Reserve space for the toolbar while keeping vanilla resource styles and overlays.
public class MobileResourceLayoutSystem : ModSystem
{
	private static bool drawingHudBuffs;
	private static bool ReserveToolbar => (MobileJoystickInput.EnabledThisFrame || MobileChatSystem.IsOpen) && MobileJoystickConfig.Instance.ShowTopButtons;

	public override void Load()
	{
		if (Main.dedServ) {
			return;
		}
		On_FancyClassicPlayerResourcesDisplaySet.DrawLifeBar += DrawFancyLife;
		On_FancyClassicPlayerResourcesDisplaySet.DrawManaBar += DrawFancyMana;
		On_ClassicPlayerResourcesDisplaySet.DrawLife += DrawClassicLife;
		On_ClassicPlayerResourcesDisplaySet.DrawMana += DrawClassicMana;
		On_ClassicPlayerResourcesDisplaySet.TryToHover += HoverClassic;
		On_HorizontalBarsPlayerResourcesDisplaySet.Draw += DrawBars;
		On_Main.DrawInterface_Resources_Buffs += DrawHudBuffs;
		On_Main.DrawBuffIcon += DrawBuffIcon;
	}

	public override void Unload()
	{
		if (Main.dedServ) {
			return;
		}
		On_FancyClassicPlayerResourcesDisplaySet.DrawLifeBar -= DrawFancyLife;
		On_FancyClassicPlayerResourcesDisplaySet.DrawManaBar -= DrawFancyMana;
		On_ClassicPlayerResourcesDisplaySet.DrawLife -= DrawClassicLife;
		On_ClassicPlayerResourcesDisplaySet.DrawMana -= DrawClassicMana;
		On_ClassicPlayerResourcesDisplaySet.TryToHover -= HoverClassic;
		On_HorizontalBarsPlayerResourcesDisplaySet.Draw -= DrawBars;
		On_Main.DrawInterface_Resources_Buffs -= DrawHudBuffs;
		On_Main.DrawBuffIcon -= DrawBuffIcon;
		drawingHudBuffs = false;
	}

	private static Rectangle Toolbar {
		get {
			var config = MobileJoystickConfig.Instance;
			int width = PlayerInput.RealScreenWidth, height = PlayerInput.RealScreenHeight;
			Rectangle bounds = MobileJoystickLayout.GetToolbarButton(config, 0, width, height);
			foreach (var button in MobileJoystickLayout.CreateControlsLayout(config, width, height).Buttons)
				if (button.Action is MobileControlAction.Chat or MobileControlAction.Map or MobileControlAction.MapStyle
					&& button.Bounds.Top < height / 4) bounds = Rectangle.Union(bounds, button.Bounds);
			return bounds;
		}
	}

	private static Vector2 LifeOffset => new(Math.Min(0f,
		20f - (PlayerInput.RealScreenWidth - Toolbar.Left + 12f) / Main.UIScale), 0f);

	private static Vector2 ManaOffset(float top) => new(0f, Math.Max(0f, (Toolbar.Bottom + 14f) / Main.UIScale - top));

	private static void DrawTranslated(Action draw, Vector2 offset)
	{
		if (!ReserveToolbar) {
			draw();
			return;
		}
		SpriteBatch spriteBatch = Main.spriteBatch;
		int mouseX = Main.mouseX;
		int mouseY = Main.mouseY;
		spriteBatch.End();
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
			DepthStencilState.None, RasterizerState.CullCounterClockwise, null,
			Matrix.CreateTranslation(offset.X, offset.Y, 0f) * Main.UIScaleMatrix);
		Main.mouseX -= (int)offset.X;
		Main.mouseY -= (int)offset.Y;
		try {
			draw();
		}
		finally {
			Main.mouseX = mouseX;
			Main.mouseY = mouseY;
			spriteBatch.End();
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
				DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
		}
	}

	private static void DrawFancyLife(On_FancyClassicPlayerResourcesDisplaySet.orig_DrawLifeBar orig,
		FancyClassicPlayerResourcesDisplaySet self, SpriteBatch spriteBatch)
		=> DrawTranslated(() => orig(self, spriteBatch), ReserveToolbar ? LifeOffset : Vector2.Zero);

	private static void DrawFancyMana(On_FancyClassicPlayerResourcesDisplaySet.orig_DrawManaBar orig,
		FancyClassicPlayerResourcesDisplaySet self, SpriteBatch spriteBatch)
		=> DrawTranslated(() => orig(self, spriteBatch), ReserveToolbar ? ManaOffset(22f) : Vector2.Zero);

	private static void DrawClassicLife(On_ClassicPlayerResourcesDisplaySet.orig_DrawLife orig, ClassicPlayerResourcesDisplaySet self)
		=> DrawTranslated(() => orig(self), ReserveToolbar ? LifeOffset : Vector2.Zero);

	private static void DrawClassicMana(On_ClassicPlayerResourcesDisplaySet.orig_DrawMana orig, ClassicPlayerResourcesDisplaySet self)
		=> DrawTranslated(() => orig(self), ReserveToolbar ? ManaOffset(30f) : Vector2.Zero);

	private static void DrawBars(On_HorizontalBarsPlayerResourcesDisplaySet.orig_Draw orig, HorizontalBarsPlayerResourcesDisplaySet self)
		=> DrawTranslated(() => orig(self), ReserveToolbar ? LifeOffset : Vector2.Zero);

	private static void HoverClassic(On_ClassicPlayerResourcesDisplaySet.orig_TryToHover orig, ClassicPlayerResourcesDisplaySet self)
	{
		if (!ReserveToolbar) {
			orig(self);
			return;
		}
		PlayerStatsSnapshot snapshot = new(Main.LocalPlayer);
		int heartCount = snapshot.AmountOfLifeHearts;
		Rectangle life = new(Main.screenWidth - 300 + (int)LifeOffset.X, 32,
			26 * Math.Min(10, heartCount), Terraria.GameContent.TextureAssets.Heart.Height() + (heartCount > 10 ? 26 : 0));
		Rectangle mana = new(Main.screenWidth - 38, 30 + (int)ManaOffset(30f).Y, 24, 28 * snapshot.AmountOfManaStars);
		if (life.Contains(Main.mouseX, Main.mouseY) && ResourceOverlayLoader.DisplayHoverText(snapshot, self, true)) {
			CommonResourceBarMethods.DrawLifeMouseOver();
		}
		if (mana.Contains(Main.mouseX, Main.mouseY) && ResourceOverlayLoader.DisplayHoverText(snapshot, self, false)) {
			CommonResourceBarMethods.DrawManaMouseOver();
		}
	}

	private static void DrawHudBuffs(On_Main.orig_DrawInterface_Resources_Buffs orig, Main self)
	{
		drawingHudBuffs = MobileJoystickInput.EnabledThisFrame;
		try {
			orig(self);
		}
		finally {
			drawingHudBuffs = false;
		}
	}

	private static int DrawBuffIcon(On_Main.orig_DrawBuffIcon orig, int drawBuffText, int buffSlotOnPlayer, int x, int y)
	{
		if (drawingHudBuffs) {
			MobileJoystickConfig config = MobileJoystickConfig.Instance;
			MobileHotbarLayout hotbar = MobileJoystickLayout.CreateHotbarLayout(config,
				PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight);
			int left = (int)((hotbar.GetSlot(9).Right + 10f * hotbar.Scale) / Main.UIScale);
			int availableWidth = (int)(PlayerInput.RealScreenWidth * 0.6f / Main.UIScale) - left;
			int columns = Math.Clamp(availableWidth / 38, 1, 10);
			x = left + buffSlotOnPlayer % columns * 38;
			y = (int)(6f * hotbar.Scale / Main.UIScale) + buffSlotOnPlayer / columns * 50;
		}
		// Passing the new coordinates into vanilla also moves tooltips and hit areas.
		return orig(drawBuffText, buffSlotOnPlayer, x, y);
	}
}
