using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;

namespace MobileJoystick;

public class MobileJoystickPlayer : ModPlayer
{
	public override void SetControls()
	{
		if (Player.whoAmI == Main.myPlayer && MobileJoystickConfig.Instance?.EnableMobileControls == true
			&& (MobileChatSystem.IsOpen || Main.mapFullscreen || Player.talkNPC >= 0
				|| Main.ingameOptionsWindow || Main.inFancyUI || Main.playerInventory && !MobileInventorySystem.Active)) {
			Player.controlUseItem = Player.controlUseTile = Player.controlHook = Player.controlMount = false;
			Player.controlLeft = Player.controlRight = Player.controlUp = Player.controlDown = Player.controlJump = false;
			return;
		}
		if (Player.whoAmI != Main.myPlayer || !MobileJoystickInput.EnabledThisFrame) {
			return;
		}

		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		Vector2 move = MobileJoystickInput.JoystickVector;
		if (move.X < -0.25f) {
			Player.controlLeft = true;
		}

		if (move.X > 0.25f) {
			Player.controlRight = true;
		}

		if (move.Y > 0.45f) {
			Player.controlDown = true;
		}

		if (move.Y < -0.45f) {
			Player.controlUp = true;
		}

		// Native FindPulley refuses to grab a rope while jumping.
		Point rope = new Vector2(Player.Center.X, Player.position.Y - 8f).ToTileCoordinates();
		bool climbingRope = Player.pulley || WorldGen.InWorld(rope.X, rope.Y, 2) && WorldGen.IsRope(rope.X, rope.Y);
		if (config != null && config.JoystickUpJumps && move.Y < -0.7f && !climbingRope) {
			Player.controlJump = true;
		}

		if (MobileJoystickInput.IsActionDown(MobileControlAction.Jump)) {
			// A separate button press must work even while the stick still holds Up.
			if (MobileJoystickInput.WasActionPressed(MobileControlAction.Jump)) Player.releaseJump = true;
			Player.controlJump = true;
		}
		if (MobileJoystickInput.IsActionDown(MobileControlAction.SwapUseInteract)) {
			bool hasInteractionTarget = Main.SmartInteractShowingGenuine || Main.SmartInteractShowingFake || Main.HoveringOverAnNPC;
			if (hasInteractionTarget) Player.controlUseTile = true;
			else {
				ApplyTouchAim();
				Player.controlUseItem = true;
			}
		}

		// Vanilla sees the fallback mouse press too. Preserve independent keyboard use.
		if (MobileJoystickInput.HasAnyControlTouch && !KeyboardUseItemHeld()) {
			Player.controlUseItem = false;
		}
		if (Main.playerInventory) Player.controlUseItem = Player.controlUseTile = false;

		bool canUseWorldItem = Main.mouseItem.IsAir && !Main.playerInventory && !MobileInventorySystem.BlocksWorldUse;
		if (MobileInventorySystem.ConsumeSelectedUse() && Main.mouseItem.IsAir) {
			// This is an explicit UI action; vanilla's previous UI hover must not
			// discard its one-update press through delayUseItem.
			Player.delayUseItem = false;
			Player.releaseUseItem = true;
			ApplyTouchAim();
			Player.controlUseItem = true;
			Player.mouseInterface = false;
			Main.blockMouse = false;
		}
		if (MobileJoystickInput.AttackHeld && canUseWorldItem) {
			ApplyTouchAim();
		}

		if (canUseWorldItem && ((MobileJoystickInput.AttackHeld && MobileJoystickInput.AimAndUseMode)
			|| MobileJoystickInput.IsActionDown(MobileControlAction.UseItem))) {
			// A movement touch can leave vanilla's UI delay latched. An explicit
			// tool-use contact must clear it regardless of which stick was first.
			Player.delayUseItem = false;
			if (MobileJoystickInput.AttackJustPressed || MobileJoystickInput.WasActionPressed(MobileControlAction.UseItem)
				|| MobileJoystickInput.WasActionPressed(MobileControlAction.AimModeToggle)) Player.releaseUseItem = true;
			ApplyTouchAim();
			Player.controlUseItem = true;
			Player.mouseInterface = false;
			Main.blockMouse = false;
		}

		if (canUseWorldItem && MobileJoystickInput.IsActionDown(MobileControlAction.Interact) && !MobileJoystickInput.InteractPrefersRightClickItem) {
			Player.controlUseTile = true;
		}

		if (MobileJoystickInput.IsActionDown(MobileControlAction.Grapple)) {
			Player.releaseHook = true;
			Player.controlHook = true;
		}
		if (MobileJoystickInput.IsActionDown(MobileControlAction.SmartInteract)) Player.controlUseTile = true;

		if (MobileJoystickInput.IsActionDown(MobileControlAction.Heal)) {
			Player.controlQuickHeal = true;
		}

		if (MobileJoystickInput.IsActionDown(MobileControlAction.Mana)) {
			Player.controlQuickMana = true;
		}

		// QuickMount is consumed before SetControls; injected in PostUpdateInput.
		if (MobileJoystickInput.IsActionDown(MobileControlAction.AutoSelect)) PlayerInput.Triggers.Current.SmartSelect = true;

		// The mobile toggle owns both states, including the off transition.
		Main.SmartCursorWanted_Mouse = MobileJoystickInput.SmartCursorEnabled && !Main.playerInventory;
		Player.controlSmart = Main.SmartCursorWanted_Mouse;

		if (MobileJoystickInput.WasActionPressed(MobileControlAction.Buff)) {
			Player.QuickBuff();
		}
		MobileWorldInteraction.SetControls(Player);
	}

	private static bool KeyboardUseItemHeld()
	{
		var bindings = PlayerInput.CurrentProfile.InputModes[InputMode.Keyboard].KeyStatus["MouseLeft"];
		foreach (string binding in bindings) {
			if (System.Enum.TryParse(binding, out Keys key) && Main.keyState.IsKeyDown(key)) return true;
		}
		return false;
	}

	internal static void ApplyTouchAim()
	{
		Player player = Main.LocalPlayer;
		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		float aimDistance = config?.AimDistance ?? 440f;
		Vector2 playerScreenCenter = MobileWorldInteraction.ToScreen(player.MountedCenter);
		Vector2 cursor;
		if (MobileJoystickInput.HasTouchAimPosition) {
			cursor = MobileJoystickInput.TouchAimPosition;
		}
		else {
			Vector2 aim = new(player.direction == 0 ? 1f : player.direction, 0f);
			cursor = playerScreenCenter + aim * aimDistance * Main.GameViewMatrix.Zoom;
		}

		MobileWorldInteraction.SetPointer(ProjectSmartBuildingAim(cursor));
	}

	internal static Vector2 ProjectSmartBuildingAim(Vector2 screen)
	{
		Player player = Main.LocalPlayer;
		Item item = player.HeldItem;
		// Native smart block placement only searches when its input is inside
		// the reachable rectangle. Preserve the stick's direction at its edge.
		if (!MobileJoystickInput.SmartCursorEnabled || !Player.SmartCursorSettings.SmartBlocksEnabled
			|| item.createTile <= -1 || !Main.tileSolid[item.createTile] || Main.tileSolidTop[item.createTile]
			|| Main.tileFrameImportant[item.createTile] || ItemID.Sets.GrassSeeds[item.type]
			|| item.type is ItemID.StaffofRegrowth or ItemID.AcornAxe) return screen;
		Vector2 world = MobileWorldInteraction.ToWorld(screen);
		Vector2 origin = player.Center;
		Vector2 delta = world - origin;
		float left = ((int)(player.position.X / 16f) - Player.tileRangeX - item.tileBoost + 1) * 16f;
		float right = ((int)(player.Right.X / 16f) + Player.tileRangeX + item.tileBoost) * 16f - 1f;
		float top = ((int)(player.position.Y / 16f) - Player.tileRangeY - item.tileBoost + 1) * 16f;
		float bottom = ((int)(player.Bottom.Y / 16f) + Player.tileRangeY + item.tileBoost - 1) * 16f - 1f;
		float fraction = 1f;
		if (delta.X > 0) fraction = System.Math.Min(fraction, (right - origin.X) / delta.X);
		else if (delta.X < 0) fraction = System.Math.Min(fraction, (left - origin.X) / delta.X);
		if (delta.Y > 0) fraction = System.Math.Min(fraction, (bottom - origin.Y) / delta.Y);
		else if (delta.Y < 0) fraction = System.Math.Min(fraction, (top - origin.Y) / delta.Y);
		return MobileWorldInteraction.ToScreen(origin + delta * MathHelper.Clamp(fraction, 0f, 1f));
	}
}
