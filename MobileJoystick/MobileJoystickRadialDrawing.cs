using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace MobileJoystick;

public partial class MobileJoystickSystem
{
	private Texture2D radialSector;

	private void DrawRadialControl(SpriteBatch spriteBatch, Color color)
	{
		MobileRadialControl radial = MobileJoystickInput.ActiveRadial;
		if (!radial.Active) return;
		EnsureRadialSector();
		bool potions = radial.Button.Action == MobileControlAction.QuickPotions;
		bool custom = radial.Button.Action == MobileControlAction.RadialMenu;
		Vector2 center = radial.Button.Center;
		float radius = radial.OuterRadius;
		MobileControlArtwork.DrawRadialFrame(spriteBatch, center, radial.Button.Radius, Color.White * .9f);
		for (int i = 0; i < 4; i++) {
			MobileControlAction? action = custom
				? radial.Button.RadialActions != null && i < radial.Button.RadialActions.Count && radial.Button.RadialActions[i] != MobileControlAction.Unbound
					? radial.Button.RadialActions[i] : null
				: i switch {
					0 => potions ? MobileControlAction.Heal : null,
					1 => potions ? MobileControlAction.Mana : MobileControlAction.ZoomIn,
					2 => potions ? MobileControlAction.Buff : null,
					_ => potions ? null : MobileControlAction.ZoomOut
				};
			if (action.HasValue && radial.SelectedSlot == i)
				spriteBatch.Draw(radialSector, center, null, new Color(225, 205, 85) * .35f, i * MathHelper.PiOver2,
					new Vector2(64), radius / 64f, SpriteEffects.None, 0f);
			Vector2 iconPosition = center + new Vector2((float)Math.Sin(i * MathHelper.PiOver2), -(float)Math.Cos(i * MathHelper.PiOver2)) * radius * 0.75f;
			if (custom) {
				MobileControlAction bound = action ?? MobileControlAction.Unbound;
				if (!MobileControlArtwork.DrawActionIcon(spriteBatch, bound, iconPosition, radial.Button.Radius, color))
					DrawTexture(spriteBatch, MobileJoystickLayout.GetDefaultTexture(bound), iconPosition, radial.Button.Radius, color);
				Vector2 numberPosition = center + (iconPosition - center) * .6f;
				Utils.DrawBorderString(spriteBatch, (i + 1).ToString(), numberPosition, Color.White,
					radial.Button.Radius / 48f, 0.5f, 0.5f);
			}
			else if (potions && action.HasValue) {
				if (i == 2) {
					DrawTexture(spriteBatch, "RadialBuff", iconPosition, radial.Button.Radius * 0.95f, color);
					continue;
				}
				int item = i == 0 ? ItemID.LesserHealingPotion : ItemID.LesserManaPotion;
				Main.instance.LoadItem(item);
				Texture2D texture = TextureAssets.Item[item].Value;
				spriteBatch.Draw(texture, iconPosition, null, color, 0f, texture.Size() / 2f,
					radial.Button.Radius * 0.85f / Math.Max(texture.Width, texture.Height), SpriteEffects.None, 0f);
			}
			else if (action.HasValue) MobileControlArtwork.DrawActionIcon(spriteBatch, action.Value, iconPosition, radial.Button.Radius, color);
		}
	}

	private void EnsureRadialSector()
	{
		if (radialSector != null) return;
		var pixels = new Color[128 * 128];
		for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) {
			float dx = x - 63.5f, dy = y - 63.5f;
			float distance = dx * dx + dy * dy;
			if (distance >= 30 * 30 && distance <= 64 * 64 && -dy >= Math.Abs(dx)) pixels[y * 128 + x] = Color.White;
		}
		radialSector = new Texture2D(Main.graphics.GraphicsDevice, 128, 128);
		radialSector.SetData(pixels);
	}
}
