using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace MobileJoystick;

// Compose the phone's separate button background and action glyph. Keeping the
// layers separate preserves the original icon when the background turns yellow.
internal static class MobileControlArtwork
{
	internal static string AimKnobTexture => MobileJoystickInput.AimAndUseMode && MobileJoystickInput.GetHeldItem() is { IsAir: false }
		? "AttackAimUse" : "AttackAimOnly";

	internal static Item GetActionItem(MobileControlAction action)
	{
		if (Main.myPlayer < 0 || Main.myPlayer >= Main.maxPlayers) return null;
		Player player = Main.LocalPlayer;
		return action switch {
			MobileControlAction.UseItem => player.HeldItem,
			MobileControlAction.Interact => MobileJoystickInput.GetPriorityRightClickOrHeldItem(),
			MobileControlAction.Grapple => player.QuickGrapple_GetItemToUse(),
			MobileControlAction.Mount or MobileControlAction.QuickMount => player.miscEquips[3],
			_ => null
		};
	}

	internal static string GetEmptyGlyph(MobileControlAction action) => action switch {
		MobileControlAction.UseItem => "PhoneUseGlyph",
		MobileControlAction.Interact => "PhoneInteractGlyph",
		MobileControlAction.Grapple => "PhoneEmptyGrappleGlyph",
		MobileControlAction.Mount or MobileControlAction.QuickMount => "PhoneMountGlyph",
		_ => null
	};

	internal static bool DrawButton(SpriteBatch batch, MobileControlAction action, Vector2 center,
		float radius, Color color, bool highlighted = false, Item itemOverride = null)
	{
		string background = action switch {
			MobileControlAction.Interact or MobileControlAction.UseItem or MobileControlAction.Heal
				or MobileControlAction.Mana or MobileControlAction.Buff or MobileControlAction.Unbound
				or MobileControlAction.SmartInteract or MobileControlAction.SwapUseInteract or MobileControlAction.RadialMenu => "PhonePurpleCircle",
			MobileControlAction.Jump or MobileControlAction.AimModeToggle or MobileControlAction.TargetLockToggle
				or MobileControlAction.SmartCursor => "PhoneTealCircle",
			MobileControlAction.Grapple or MobileControlAction.Mount or MobileControlAction.QuickMount => "PhoneGreenCircle",
			_ => null
		};
		if (background == null) return false;
		DrawSprite(batch, highlighted ? "PhonePressedCircle" : background, center, radius * 2f, highlighted ? Color.White : color);
		Item item = itemOverride ?? GetActionItem(action);
		if (item is { IsAir: false }) {
			MobileJoystickSystem.DrawItemIcon(batch, item, center, radius * 1.35f, highlighted ? Color.White : color);
		} else if (GetEmptyGlyph(action) is string emptyGlyph) {
			DrawSprite(batch, emptyGlyph, center, radius * 1.35f, (highlighted ? Color.White : color) * .7f);
		} else DrawActionIcon(batch, action, center, radius * 1.35f, highlighted ? Color.White : color);
		return true;
	}

	internal static bool DrawActionIcon(SpriteBatch batch, MobileControlAction action, Vector2 center, float size, Color color)
	{
		if (action == MobileControlAction.AimModeToggle) {
			DrawSprite(batch, MobileJoystickInput.AimAndUseMode ? "AimUseMode" : "AimOnlyMode", center, size, color);
			return true;
		}
		if (action is MobileControlAction.Loadout1 or MobileControlAction.Loadout2 or MobileControlAction.Loadout3 or MobileControlAction.SwitchLoadout) {
			Color[] colors = { new(100, 255, 115), new(100, 175, 255), new(255, 135, 205) };
			if (action == MobileControlAction.SwitchLoadout) {
				for (int i = 0; i < 3; i++) DrawSprite(batch, "PhoneLoadoutGlyph", center + new Vector2((i - 1) * size * .23f, (i - 1) * size * .14f), size * .65f, color.MultiplyRGB(colors[i]));
			} else DrawSprite(batch, "PhoneLoadoutGlyph", center, size, color.MultiplyRGB(colors[(int)action - (int)MobileControlAction.Loadout1]));
			return true;
		}
		string glyph = action switch {
			MobileControlAction.Jump => "PhoneJumpGlyph",
			MobileControlAction.Grapple => "PhoneGrappleGlyph",
			MobileControlAction.Mount or MobileControlAction.QuickMount => "PhoneMountGlyph",
			MobileControlAction.AimModeToggle => "PhoneAimModeGlyph",
			MobileControlAction.TargetLockToggle => "PhoneTargetGlyph",
			MobileControlAction.SmartCursor => MobileJoystickInput.SmartCursorEnabled ? "PhoneSmartCursorOnGlyph" : "PhoneSmartCursorGlyph",
			MobileControlAction.SmartInteract => "PhoneSmartInteractGlyph",
			MobileControlAction.SwapUseInteract => "PhoneSwapGlyph",
			MobileControlAction.Interact => "PhoneInteractGlyph",
			MobileControlAction.UseItem => "PhoneUseGlyph",
			MobileControlAction.AutoSelect => "PhoneAutoSelectGlyph",
			MobileControlAction.CycleUseProfile => "PhoneUseProfileGlyph",
			MobileControlAction.Unbound => "PhoneUnboundGlyph",
			MobileControlAction.RadialMenu => "PhoneRadialGlyph",
			MobileControlAction.ZoomIn => "PhoneZoomInGlyph",
			MobileControlAction.ZoomOut => "PhoneZoomOutGlyph",
			MobileControlAction.Inventory => "PhoneInventoryGlyph",
			MobileControlAction.Settings => "PhoneMenuGlyph",
			_ => null
		};
		if (glyph != null) { DrawSprite(batch, glyph, center, size, color); return true; }
		int itemType = action switch {
			MobileControlAction.Heal => ItemID.LesserHealingPotion,
			MobileControlAction.Mana => ItemID.LesserManaPotion,
			MobileControlAction.Buff => ItemID.IronskinPotion,
			_ => 0
		};
		if (itemType == 0) return false;
		Main.instance.LoadItem(itemType);
		Texture2D texture = Terraria.GameContent.TextureAssets.Item[itemType].Value;
		Rectangle frame = Main.itemAnimations[itemType]?.GetFrame(texture) ?? texture.Bounds;
		batch.Draw(texture, center, frame, color, 0f, frame.Size() * .5f,
			size / Math.Max(frame.Width, frame.Height), SpriteEffects.None, 0f);
		return true;
	}

	internal static void DrawRadialFrame(SpriteBatch batch, Vector2 center, float radius, Color color)
	{
		DrawSprite(batch, "PhoneRadialRing", center, radius * 5.6f, color);
		DrawSprite(batch, "PhonePressedCircle", center, radius * 2f, Color.White);
	}

	private static void DrawSprite(SpriteBatch batch, string name, Vector2 center, float size, Color color)
	{
		Texture2D texture = ModContent.Request<Texture2D>("MobileJoystick/Assets/MobileControls/" + name, AssetRequestMode.ImmediateLoad).Value;
		batch.Draw(texture, center, null, color, 0f, texture.Size() * .5f,
			size / Math.Max(texture.Width, texture.Height), SpriteEffects.None, 0f);
	}
}
