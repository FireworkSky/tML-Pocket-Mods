using System.Diagnostics;
using Microsoft.Xna.Framework;
using Terraria;

namespace MobileJoystick;

public partial class MobileInventorySystem
{
	private readonly MobileInventoryCameraMotion inventoryCamera = new();
	private float previousInventoryCameraOffset;
	private Vector2 previousInventoryCameraPosition;

	private void UpdateInventoryCamera(On_Main.orig_DoDraw_UpdateCameraPosition orig)
	{
		RestoreInventoryCamera();
		orig();
		previousInventoryCameraPosition = Main.screenPosition;
	}

	private void RestoreInventoryCamera()
	{
		// Vanilla's camera lerp reads the last rendered position. Feed it the
		// position before our UI movement so it cannot accumulate that movement.
		if (Main.screenPosition == previousInventoryCameraPosition)
			Main.screenPosition.Y -= previousInventoryCameraOffset;
		previousInventoryCameraOffset = 0f;
	}

	public override void ModifyScreenPosition()
	{
		if (Main.dedServ || Main.gameMenu || !Enabled || Main.LocalPlayer.dead || Main.LocalPlayer.ghost) {
			inventoryCamera.Reset();
			return;
		}

		bool open = Active && !Main.mapFullscreen && !MobileMenuSystem.EditingCustomControls;
		double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
		float fraction = inventoryCamera.Advance(open, now);
		// Move the scene down, keeping the inventory and touch controls fixed.
		// Vanilla subsequently clamps this camera position to the world bounds.
		float offset = MobileInventoryCameraMotion.WorldOffset(fraction,
			Main.screenHeight, Main.GameViewMatrix.Zoom.Y, Main.LocalPlayer.gravDir);
		// Match vanilla's pixel snapping and world clamp when tracking how much
		// of our displacement survives at a world edge.
		float minY = System.MathF.Round(Main.topWorld + 656f - Main.GameViewMatrix.Translation.Y);
		float maxY = System.MathF.Round(Main.bottomWorld - Main.screenHeight / Main.GameViewMatrix.Zoom.Y
			- 672f - Main.GameViewMatrix.Translation.Y);
		previousInventoryCameraOffset = MobileInventoryCameraMotion.EffectiveOffset(Main.screenPosition.Y, offset, minY, maxY);
		Main.screenPosition.Y += offset;
	}
}
