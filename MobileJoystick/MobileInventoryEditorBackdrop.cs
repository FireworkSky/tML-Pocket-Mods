using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ModLoader;

namespace MobileJoystick;

public partial class MobileInventorySystem
{
	// The phone exposes the inventory silhouette while editing controls. This
	// pass only draws; it never changes inventory visibility or handles slots.
	internal static void DrawControlEditorBackdrop(SpriteBatch batch, int width, int height)
	{
		var preview = new MobileInventoryLayout(width, height);
		Player player = Main.LocalPlayer;
		Color tint = Color.White * .25f;
		void DrawSlot(Item item, Rectangle bounds) {
			batch.Draw(Instance.Texture(item.IsAir ? "Slot" : "SlotOccupied"), bounds, tint);
			if (!item.IsAir) Terraria.UI.ItemSlot.DrawItemIcon(item, Terraria.UI.ItemSlot.Context.InventoryItem, batch,
				bounds.Center.ToVector2(), bounds.Width / 52f * .9f, 32f, tint);
		}
		for (int i = 0; i < 50; i++) DrawSlot(player.inventory[i], preview.Inventory(i));
		for (int i = 50; i < 58; i++) DrawSlot(player.inventory[i], preview.CoinAmmo(i));
		void ArmorRow(int index, int column, int row) {
			DrawSlot(player.dye[index], preview.Gear(column, row));
			DrawSlot(player.armor[index + 10], preview.Gear(column + 1, row));
			DrawSlot(player.armor[index], preview.Gear(column + 2, row));
		}
		for (int i = 0; i < 3; i++) ArmorRow(i, 5, i);
		for (int i = 3; i <= 7; i++) ArmorRow(i, 2, i - 3);
		if (player.CanDemonHeartAccessoryBeShown()) ArmorRow(8, 5, 3);
		if (player.CanMasterModeAccessoryBeShown()) ArmorRow(9, 5, 4);
		for (int i = 0; i < 5; i++) {
			DrawSlot(player.miscDyes[i], preview.Gear(0, i));
			DrawSlot(player.miscEquips[i], preview.Gear(1, i));
		}
		string[] leftIcons = { "Bag", "House", "Pvp" };
		for (int i = 0; i < leftIcons.Length; i++) batch.Draw(Instance.Texture(leftIcons[i]), preview.LeftTab(i), tint);
		batch.Draw(Instance.Texture("Craft"), preview.RightTab(0), tint);
		batch.Draw(Instance.Texture("Equipment"), preview.RightTab(1), tint);
	}
}
