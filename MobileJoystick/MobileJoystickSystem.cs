using System.Collections.Generic;
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameInput;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace MobileJoystick;

public partial class MobileJoystickSystem : ModSystem
{
	private readonly Dictionary<string, Asset<Texture2D>> textures = new();

	public override void Load()
	{
		if (Main.dedServ) {
			return;
		}
		MobileTouchInput.Load();
		On_Main.DrawCursor += DrawNativeCursor;
		On_Main.DrawThickCursor += DrawNativeCursorBorder;
		On_Player.QuickGrapple += MobileJoystickInput.AimNativeGrapple;
		On_Player.LookForTileInteractions += MobileWorldInteraction.LookForInteractions;

		LoadTexture("JoystickFrame");
		LoadTexture("JoystickKnob");
		LoadTexture("ItemButtonFrame");
		LoadTexture("AimReticle");
		LoadTexture("AttackAimUse");
		LoadTexture("AttackAimOnly");
		LoadTexture("AimUseMode");
		LoadTexture("AimOnlyMode");
		LoadTexture("TargetLockOn");
		LoadTexture("TargetLockOff");
		LoadTexture("SmartCursorOff");
		LoadTexture("BackpackIcon");
		LoadTexture("GrappleHook");
		LoadTexture("QuickPotions");
		LoadTexture("ZoomPair");
		LoadTexture("ZoomInPlus");
		LoadTexture("ChatBubble");
		LoadTexture("Jump");
		LoadTexture("Mana");
		LoadTexture("Mount");
		LoadTexture("SmartCursor");
		LoadTexture("Map");
		LoadTexture("Settings");
		LoadTexture("Chat");
		LoadTexture("JoystickPad");
		LoadTexture("MoveJumpHint");
		LoadTexture("Heal");
		LoadTexture("Buff");
		LoadTexture("RadialBuff");
		LoadTexture("ZoomIn");
		LoadTexture("ZoomOut");
		LoadTexture("MapStyle");
		LoadTexture("GrappleHookIdle");
		LoadTexture("PhoneGrapplePad");
	}

	public override void Unload()
	{
		MobileTouchInput.Unload();
		if (!Main.dedServ) {
			On_Main.DrawCursor -= DrawNativeCursor;
			On_Main.DrawThickCursor -= DrawNativeCursorBorder;
		}
		if (!Main.dedServ) On_Player.QuickGrapple -= MobileJoystickInput.AimNativeGrapple;
		if (!Main.dedServ) On_Player.LookForTileInteractions -= MobileWorldInteraction.LookForInteractions;
		MobileWorldInteraction.Reset();
		MobileJoystickInput.Reset();
		radialSector?.Dispose();
		radialSector = null;
		textures.Clear();
	}

	public override void OnWorldUnload() { MobileJoystickInput.Reset(); MobileTouchInput.Reset(); }

	public override void PostUpdateInput()
	{
		if (!Main.hasFocus) MobileTouchInput.Reset();
		MobileJoystickInput.Update();
		MobileChatSystem.UpdateInput();
		if (MobileInventorySystem.Enabled)
			ModContent.GetInstance<MobileInventorySystem>().ReadPointer();
		MobileWorldInteraction.UpdateInput();
		MobileModKeybinds.Apply();
		MobileJoystickInput.ApplyGameplayTriggers();
	}

	private static bool HideNativeCursor => MobileTouchInput.UsingTouch && MobileJoystickConfig.Instance?.EnableMobileControls == true;
	private static void DrawNativeCursor(On_Main.orig_DrawCursor orig, Vector2 bonus, bool smart)
	{
		if (!HideNativeCursor) orig(bonus, smart);
	}
	private static Vector2 DrawNativeCursorBorder(On_Main.orig_DrawThickCursor orig, bool smart)
		=> HideNativeCursor ? Vector2.Zero : orig(smart);

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		if (HideNativeCursor) foreach (var layer in layers)
			if (layer.Name == "Vanilla: Cursor") layer.Active = false;
		if (MobileMenuSystem.EditingCustomControls) {
			foreach (var layer in layers)
				if (layer.Name is "Vanilla: Map / Minimap" or "Vanilla: Hotbar" or "Vanilla: Resource Bars") layer.Active = false;
		}
		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		if (config != null && config.EnableMobileControls && !Main.playerInventory && !Main.gameMenu) {
			int hotbarIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Hotbar"));
			if (hotbarIndex != -1) {
				layers[hotbarIndex] = new LegacyGameInterfaceLayer(
					"MobileJoystick: Mobile Hotbar",
					() => {
						DrawMobileHotbar();
						return true;
					},
					InterfaceScaleType.None);
			}
		}

		int mouseTextIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Mouse Text"));
		if (mouseTextIndex == -1) {
			return;
		}

		layers.Insert(mouseTextIndex, new LegacyGameInterfaceLayer(
			"MobileJoystick: Mobile Touch Controls",
			() => {
				DrawControls();
				return true;
			},
			InterfaceScaleType.None));
	}

	private void LoadTexture(string name)
	{
		try {
			textures[name] = ModContent.Request<Texture2D>($"{Mod.Name}/Assets/MobileControls/{name}");
		}
		catch {
			// Some texture sets intentionally replace older button names with semantic variants.
		}
	}

	private void DrawControls()
	{
		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		if ((!MobileJoystickInput.EnabledThisFrame && !MobileChatSystem.IsOpen) || config == null) {
			return;
		}

		SpriteBatch spriteBatch = Main.spriteBatch;
		BeginPixelControls(spriteBatch);
		MobileControlLayout layout = MobileChatSystem.IsOpen ? MobileJoystickLayout.CreateControlsLayout(config, Main.screenWidth, Main.screenHeight) : MobileJoystickInput.Layout;
		Color color = Color.White * config.Opacity * (MobileChatSystem.IsOpen ? .55f : 1f);

        foreach (MobileControlStick stick in layout.Sticks) {
            if (stick.Radius <= 0 || stick.Role == MobileJoystickRole.Grapple) continue;
            bool moving = stick.Role == MobileJoystickRole.Move;
            bool active = moving ? MobileJoystickInput.MoveStick == stick : MobileJoystickInput.AimStick == stick;
            Vector2 offset = !active ? Vector2.Zero : moving ? MobileJoystickInput.JoystickVector : MobileJoystickInput.AttackKnobOffset;
            DrawTexture(spriteBatch, "JoystickPad", stick.Center, stick.Radius * 2f, color);
            Vector2 knob = stick.Center + offset * stick.Radius * .46f;
            DrawTexture(spriteBatch, moving ? "JoystickKnob" : stick.Role == MobileJoystickRole.Unbound ? "ItemButtonFrame" : MobileControlArtwork.AimKnobTexture,
                knob, stick.Radius, color);
            if (moving && config.JoystickUpJumps) DrawTexture(spriteBatch, "MoveJumpHint", stick.Center - new Vector2(0, stick.Radius * .76f), 25f * layout.Scale, color);
            if (stick.Role == MobileJoystickRole.Aim && MobileJoystickInput.AimAndUseMode)
                DrawSelectedItem(spriteBatch, knob, stick.Radius * .65f, color);
        }

		foreach (MobileControlButton button in layout.Buttons) {
			if (!MobileJoystickInput.IsButtonAvailable(button) && !MobileChatSystem.IsOpen) continue;
			if (MobileHudArtwork.DrawToolbar(button, MobileJoystickInput.IsActionDown(button.Action))) continue;
			if (button.Id == null && button.Action is MobileControlAction.Inventory or MobileControlAction.Settings) {
				continue;
			}

			bool active = button.Action == MobileControlAction.ModKeybind
				? MobileModKeybinds.IsHeld(button.ModKeybind) : MobileJoystickInput.IsActionDown(button.Action);
			if (button.Action == MobileControlAction.ModKeybind) {
				DrawTexture(spriteBatch, "ItemButtonFrame", button.Center, button.Radius * 2f, active ? Color.White : color);
				string label = MobileModKeybinds.Label(button.ModKeybind);
				float textScale = Math.Min(.55f * layout.Scale, button.Radius * 1.65f / Math.Max(1f, FontAssets.MouseText.Value.MeasureString(label).X));
				Utils.DrawBorderString(spriteBatch, label, button.Center, active ? Color.White : color, textScale, .5f, .5f);
				continue;
			}
			if (button.Action == MobileControlAction.Grapple) {
				var grapple = MobileJoystickInput.ActiveGrapple;
				bool held = grapple.Active && (button.Id != null ? grapple.Button.Id == button.Id : grapple.Button.Center == button.Center);
				Vector2 center = button.Center;
				if (held) {
					DrawTexture(spriteBatch, "PhoneGrapplePad", center, button.Radius * 2.6f, new Color(26, 40, 48) * (color.A / 255f));
					center += grapple.KnobOffset * button.Radius;
				}
				MobileControlArtwork.DrawButton(spriteBatch, button.Action, center, button.Radius, held ? Color.White : color);
				continue;
			}
			if (button.Action != MobileControlAction.Heal && MobileControlArtwork.DrawButton(spriteBatch, button.Action, button.Center, button.Radius, color, active)) continue;
			if (button.Action == MobileControlAction.Interact) {
				DrawItemButton(spriteBatch, button.Center, button.Radius, MobileJoystickInput.GetPriorityRightClickOrHeldItem(), active ? Color.White : color);
				continue;
			}

			if (button.Action == MobileControlAction.UseItem) {
				DrawItemButton(spriteBatch, button.Center, button.Radius, MobileJoystickInput.GetHeldItem(), active ? Color.White : color);
				continue;
			}

			if (button.Action == MobileControlAction.Heal) {
				Item heal = FindHealingItem();
				if (heal != null) {
					MobileControlArtwork.DrawButton(spriteBatch, button.Action, button.Center, button.Radius, color, active, heal);
				}
				else {
					MobileControlArtwork.DrawButton(spriteBatch, button.Action, button.Center, button.Radius, color * .45f);
				}
				continue;
			}

			DrawTexture(spriteBatch, GetButtonTexture(button), button.Center, button.Radius * 2f, active ? Color.White : color);
		}

		if (MobileJoystickInput.ShowTouchCrosshair) {
			Vector2 crosshairPosition = MobileJoystickInput.TouchCrosshairPosition;
			MobilePrecisionMagnifier.DrawAimMarker(spriteBatch, crosshairPosition, layout.Scale * 1.08f);
		}
		if (MobileJoystickInput.GrappleHeld) {
			Vector2 origin = Main.ReverseGravitySupport(Main.LocalPlayer.Center - Main.screenPosition);
			Vector2 target = origin + MobileJoystickInput.ActiveGrapple.Direction * 440f;
			target.X = MathHelper.Clamp(target.X, 0f, Main.screenWidth - 1f);
			target.Y = MathHelper.Clamp(target.Y, 0f, Main.screenHeight - 1f);
			DrawTouchCrosshair(spriteBatch, target, layout.Scale, Color.White);
		}

		DrawRadialControl(spriteBatch, color);
		DrawStatusText(spriteBatch, layout);
	}

	private void DrawMobileHotbar()
	{
		if (Main.mapFullscreen) return;
		if (Main.myPlayer < 0 || Main.myPlayer >= Main.maxPlayers) {
			return;
		}

		Player player = Main.LocalPlayer;
		if (!player.active || player.dead || player.ghost) {
			return;
		}

		SpriteBatch spriteBatch = Main.spriteBatch;
		BeginPixelControls(spriteBatch);
		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		MobileHotbarLayout layout = MobileJoystickLayout.CreateHotbarLayout(config);
		float scale = layout.Scale;
		Color buttonColor = Color.White * (config?.Opacity ?? 0.78f);

		if (config?.ControlLayout != MobileControlLayoutMode.Custom) {
			DrawHotbarIcon(spriteBatch, layout.InventoryButton, "BackpackIcon", MobileJoystickInput.IsActionDown(MobileControlAction.Inventory), buttonColor);
			DrawHotbarIcon(spriteBatch, layout.SettingsButton, "Settings", MobileJoystickInput.IsActionDown(MobileControlAction.Settings), buttonColor);
		}

		for (int i = 0; i < 10; i++) {
			Rectangle slot = layout.GetSlot(i);
			bool selected = player.selectedItem == i;
			Item item = player.inventory[i];
			MobileInventorySystem.DrawMobileHotbarBackground(spriteBatch, slot, selected, item);
			if (!item.IsAir) {
				DrawItemInSlot(spriteBatch, item, slot, scale);
			}

			if (slot.Contains(Main.mouseX, Main.mouseY)) {
				player.mouseInterface = true;
				if (!item.IsAir) {
					Main.HoverItem = item.Clone();
					Main.hoverItemName = item.Name;
				}
			}
		}
	}

	private void DrawHotbarIcon(SpriteBatch spriteBatch, Rectangle bounds, string textureName, bool active, Color color)
	{
		DrawTexture(spriteBatch, textureName, bounds.Center.ToVector2(), bounds.Width * (active ? 0.88f : 0.8f), active ? Color.White : color);
	}

	private static void BeginPixelControls(SpriteBatch spriteBatch)
	{
		// These layers use physical screen coordinates. Linear sampling hides the
		// uneven stair steps that fractional scaling would create with point sampling.
		spriteBatch.End();
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
			DepthStencilState.None, RasterizerState.CullCounterClockwise);
	}

	private void DrawItemButton(SpriteBatch spriteBatch, Vector2 center, float radius, Item item, Color color)
	{
		DrawTexture(spriteBatch, "ItemButtonFrame", center, radius * 2.12f, color);
		DrawItemIcon(spriteBatch, item, center, radius * 1.35f, color);
	}

	private bool DrawTexture(SpriteBatch spriteBatch, string name, Vector2 center, float maxSize, Color color)
	{
		if (!textures.TryGetValue(name, out Asset<Texture2D> asset) || !asset.IsLoaded) {
			return false;
		}

		Texture2D texture = asset.Value;
		float scale = maxSize / MathHelper.Max(texture.Width, texture.Height);
		Vector2 origin = new(texture.Width * 0.5f, texture.Height * 0.5f);
		spriteBatch.Draw(texture, center, null, color, 0f, origin, scale, SpriteEffects.None, 0f);
		return true;
	}

	private void DrawTouchCrosshair(SpriteBatch spriteBatch, Vector2 center, float scale, Color color)
	{
		if (DrawTexture(spriteBatch, "AimReticle", center, 28f * scale, color)) {
			return;
		}

		int length = (int)MathHelper.Clamp(13f * scale, 12f, 24f);
		int thickness = Math.Max(2, (int)MathF.Round(3f * scale));
		DrawCrosshairBars(spriteBatch, center + Vector2.One, length, thickness, Color.Black * 0.65f);
		DrawCrosshairBars(spriteBatch, center, length, thickness, color);
	}

	private static void DrawCrosshairBars(SpriteBatch spriteBatch, Vector2 center, int length, int thickness, Color color)
	{
		Texture2D pixel = TextureAssets.MagicPixel.Value;
		int x = (int)MathF.Round(center.X);
		int y = (int)MathF.Round(center.Y);
		spriteBatch.Draw(pixel, new Rectangle(x - length, y - thickness / 2, length * 2, thickness), color);
		spriteBatch.Draw(pixel, new Rectangle(x - thickness / 2, y - length, thickness, length * 2), color);
	}

	private static string GetButtonTexture(MobileControlButton button)
	{
		return button.Action switch {
			MobileControlAction.AimModeToggle => MobileJoystickInput.AimAndUseMode ? "AimUseMode" : "AimOnlyMode",
			MobileControlAction.TargetLockToggle => MobileJoystickInput.TargetLockEnabled ? "TargetLockOff" : "TargetLockOn",
			MobileControlAction.SmartCursor => MobileJoystickInput.SmartCursorEnabled ? "SmartCursorOff" : "SmartCursor",
			MobileControlAction.Grapple => "GrappleHook",
			MobileControlAction.Buff => "Buff",
			_ => button.TextureName
		};
	}

	private static void DrawStatusText(SpriteBatch spriteBatch, MobileControlLayout layout)
	{
		string text = MobileJoystickInput.StatusText;
		if (string.IsNullOrWhiteSpace(text) || MobileJoystickInput.StatusTextOpacity <= 0f) {
			return;
		}

		float textScale = MathHelper.Clamp(layout.Scale * 0.5f, .78f, 1.02f) * MobileUiMetrics.FontScale;
		Vector2 position = new(Main.screenWidth * 0.5f, Main.screenHeight * 0.705f);
		Utils.DrawBorderString(spriteBatch, text, position, Color.White * MobileJoystickInput.StatusTextOpacity, textScale, 0.5f, 0.5f);
	}

	private static void DrawSelectedItem(SpriteBatch spriteBatch, Vector2 center, float maxSize, Color color)
	{
		if (Main.myPlayer < 0 || Main.myPlayer >= Main.maxPlayers) {
			return;
		}

		DrawItemIcon(spriteBatch, Main.LocalPlayer.HeldItem, center, maxSize, color);
	}

	private static Item FindHealingItem()
	{
		Item best = null;
		foreach (Item item in Main.LocalPlayer.inventory) {
			if (item != null && !item.IsAir && item.healLife > 0 && (best == null || item.healLife > best.healLife)) {
				best = item;
			}
		}
		return best;
	}

	internal static void DrawItemIcon(SpriteBatch spriteBatch, Item item, Vector2 center, float maxSize, Color color)
	{
		if (item == null) {
			return;
		}

		if (item.IsAir) {
			return;
		}

		Main.instance.LoadItem(item.type);
		Texture2D texture = TextureAssets.Item[item.type].Value;
		Rectangle frame = Main.itemAnimations[item.type]?.GetFrame(texture) ?? texture.Frame();
		float scale = Math.Min(maxSize / frame.Width, maxSize / frame.Height);
		Vector2 origin = frame.Size() * 0.5f;
		spriteBatch.Draw(texture, center, frame, color, 0f, origin, scale, SpriteEffects.None, 0f);
	}

	private static void DrawItemInSlot(SpriteBatch spriteBatch, Item item, Rectangle slot, float scale)
	{
		Vector2 center = slot.Center.ToVector2();
		ItemSlot.DrawItemIcon(item, ItemSlot.Context.InventoryItem, spriteBatch, center, slot.Width / 52f * .9f, 32f, Color.White);

		if (item.stack > 1) {
			Utils.DrawBorderString(spriteBatch, item.stack.ToString(), new Vector2(slot.X + 3f * scale, slot.Bottom - 15f * scale), Color.White, 0.72f * scale);
		}
	}

}
