using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace MobileJoystick;

public partial class MobileInventorySystem
{
	private void InventoryGlyph(string name, Rectangle bounds, Color color, float fraction = .82f)
	{
		Texture2D icon = Texture(name);
		float scale = Math.Min(bounds.Width, bounds.Height) * fraction / Math.Max(icon.Width, icon.Height);
		Main.spriteBatch.Draw(icon, bounds.Center.ToVector2(), null, color, 0f, icon.Size() * .5f, scale, SpriteEffects.None, 0f);
	}

	private void InventorySurface(Rectangle bounds, string material, Color color)
		=> Main.spriteBatch.Draw(Texture(material), bounds, color);

	private void InventoryButtonSurface(Rectangle bounds, string material, Color color, bool enabled = true)
	{
		InventorySurface(bounds, material, color);
		if (enabled && pointerDown && Hover(bounds)) InventorySurface(bounds, "ToolOutline", Color.White);
	}

	internal static void DrawMobileHotbarBackground(SpriteBatch batch, Rectangle bounds, bool selected, Item item)
	{
		string material = selected ? item.favorited ? "SlotFavoriteSelected" : "SlotSelected"
			: item.favorited ? "SlotFavorite" : item.IsAir ? "Slot" : "SlotOccupied";
		batch.Draw(Instance.Texture(material), bounds, selected ? Color.White : Color.White * .86f);
	}

	private void DrawPhoneSlot(Item item, int context, int index, Rectangle bounds, bool locked = false)
	{
		bool selected = context == ItemSlot.Context.InventoryItem && index == selectedSlot;
		string material = selected ? item.favorited ? "SlotFavoriteSelected" : "SlotSelected" : context switch {
			ItemSlot.Context.InventoryItem when item.favorited => "SlotFavorite",
			ItemSlot.Context.InventoryItem when item.newAndShiny && ItemSlot.Options.HighlightNewItems => "SlotNew",
			ItemSlot.Context.InventoryItem when !item.IsAir => "SlotOccupied",
			8 or 10 => Main.LocalPlayer.CurrentLoadoutIndex switch { 1 => "SlotNew", 2 => "SlotPink", _ => "SlotGreen" },
			16 or 17 or 18 or 19 or 20 => "SlotGreen",
			9 or 11 or 12 or 33 => "SlotTeal",
			3 or 4 or 32 => "SlotPurple",
			6 => "Tool",
			_ => "Slot"
		};
		bool equipment = context is 8 or 9 or 10 or 11 or 12 or 16 or 17 or 18 or 19 or 20 or 33;
		Color tint = locked ? Color.White * .22f : selected ? Color.White : Color.White * (equipment && item.IsAir ? .4f : .86f);
		InventorySurface(bounds, material, tint);
		if (!item.IsAir) {
			SetSlotScale(bounds);
			Main.inventoryScale *= .9f;
			// ChatItem runs native item/stack drawing and mod callbacks without
			// drawing a desktop background over the original phone frame.
			ItemSlot.Draw(Main.spriteBatch, ref item, ItemSlot.Context.ChatItem, bounds.TopLeft() + bounds.Size() * .05f, locked ? Color.Gray : Color.White);
			return;
		}
		string placeholder = context switch {
			1 => "EmptyCoin", 2 => "EmptyAmmo",
			8 => index switch { 0 => "EmptyHead", 1 => "EmptyBody", _ => "EmptyLegs" },
			9 => index switch { 10 => "EmptyVanityHead", 11 => "EmptyVanityBody", _ => "EmptyVanityLegs" },
			10 => "EmptyAccessory", 11 => "EmptyVanityAccessory",
			12 or 33 => "EmptyDye", 16 => "EmptyGrapple", 17 => "EmptyMount",
			18 => "EmptyMinecart", 19 => "EmptyPet", 20 => "EmptyLightPet",
			6 => "Trash", _ => null
		};
		if (placeholder != null) InventoryGlyph(placeholder, bounds, Color.White * (locked ? .12f : .3f), .68f);
	}

	private bool NavigationButton(Rectangle bounds, string glyph, string hint, bool active)
	{
		InventoryButtonSurface(bounds, active ? "TabSelected" : "Slot", Color.White * .9f);
		InventoryGlyph(glyph, bounds, Color.White, .72f);
		Tooltip(bounds, hint);
		return Click(bounds);
	}

	private void DrawCraftCategoryGlyph(int category, Rectangle bounds, Color color)
	{
		if (category == 1 || category == 3) {
			// These item icons use the same Terraria sprites as the phone, keeping
			// animation and item drawing callbacks available to content mods.
			int type = category == 1 ? ItemID.WoodenChair : ItemID.RegenerationPotion;
			MobileJoystickSystem.DrawItemIcon(Main.spriteBatch, ContentSamples.ItemsByType[type], bounds.Center.ToVector2(), bounds.Width * .72f, color);
			return;
		}
		InventoryGlyph("Category" + category, bounds, color, .76f);
	}

	private void DrawLoadoutButton()
	{
		Player player = Main.LocalPlayer;
		Rectangle bounds = layout.LoadoutButton;
		Color[] colors = { new(110, 255, 135), new(120, 185, 255), new(255, 145, 210) };
		InventoryButtonSurface(bounds, "SlotGreen", Color.White * .86f);
		InventoryGlyph("Loadout", bounds, colors[player.CurrentLoadoutIndex], .78f);
		Tooltip(bounds, T("切换装备方案", "Switch equipment loadout") + " " + (player.CurrentLoadoutIndex + 1));
		if (Click(bounds)) player.TrySwitchingLoadout((player.CurrentLoadoutIndex + 1) % player.Loadouts.Length);
		Rectangle defense = layout.Defense;
		InventoryGlyph("Defense", defense, Color.White, 1f);
		Text(player.statDefense.ToString(), defense, 1.05f);
	}
}
