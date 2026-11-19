using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Chat;

namespace MobileJoystick;

public partial class MobileInventorySystem
{
	private bool recipeDetailsOpen, recipeDetailsDragging;
	private InventoryGestureSlot? itemDetailsSource;
	private Item itemDetailsItem;
	private Rectangle recipeDetailsAnchor, recipeDetailsBounds;
	private float recipeDetailsScroll, recipeDetailsLastY, recipeDetailsMaxScroll;
	private ulong recipeDetailsExpires;
	private readonly List<TooltipLine> recipeDetailLines = new();
	private readonly List<Item> recipeDetailMaterials = new();
	private readonly RasterizerState recipeDetailsClip = new() { ScissorTestEnable = true, CullMode = CullMode.None };

	private void CloseRecipeDetails()
	{
		itemDetailsSource = null;
		itemDetailsItem = null;
		recipeDetailsOpen = recipeDetailsDragging = false;
		recipeDetailsScroll = recipeDetailsMaxScroll = 0;
		recipeDetailLines.Clear();
		recipeDetailMaterials.Clear();
	}

	private void OpenRecipeDetails(Rectangle anchor)
	{
		itemDetailsSource = null;
		itemDetailsItem = null;
		recipeDetailsOpen = true;
		recipeDetailsAnchor = anchor;
		recipeDetailsScroll = 0;
		recipeDetailsDragging = false;
		recipeDetailsExpires = InventoryUiTick + 300;
	}

	private void ReadRecipeDetailsPointer()
	{
		if (!recipeDetailsOpen) return;
		if (pointerDown && (recipeDetailsDragging || Hover(recipeDetailsAnchor))) recipeDetailsExpires = InventoryUiTick + 300;
		if (InventoryUiTick >= recipeDetailsExpires) { CloseRecipeDetails(); return; }
		if (pointerPressed) {
			if (!itemDetailsSource.HasValue && (layout.CraftFilterButton(craftingExpanded).Contains(pointer.ToPoint())
				|| layout.CraftSearchButton(craftingExpanded).Contains(pointer.ToPoint())
				|| layout.CraftAction.Contains(pointer.ToPoint()))) { CloseRecipeDetails(); return; }
			if (!recipeDetailsBounds.Contains(pointer.ToPoint())) { CloseRecipeDetails(); return; }
			recipeDetailsDragging = true;
			recipeDetailsLastY = pointer.Y;
		}
		if (recipeDetailsDragging) {
			if (pointerDown) recipeDetailsScroll += recipeDetailsLastY - pointer.Y;
			recipeDetailsExpires = InventoryUiTick + 300;
			recipeDetailsLastY = pointer.Y;
			used = true;
			if (!pointerDown) recipeDetailsDragging = false;
		}
		if (!Hover(recipeDetailsBounds)) return;
		Main.LocalPlayer.mouseInterface = true;
		PlayerInput.LockVanillaMouseScroll("MobileJoystick: Recipe details");
		if (inputVersion != recipeDetailsInputVersion && PlayerInput.ScrollWheelDelta != 0) {
			recipeDetailsScroll -= PlayerInput.ScrollWheelDelta / 120f * 36 * layout.Scale;
			recipeDetailsExpires = InventoryUiTick + 300;
		}
		recipeDetailsInputVersion = inputVersion;
	}
	private ulong recipeDetailsInputVersion;
	private void OpenItemDetails(InventoryGestureSlot source)
	{
		OpenRecipeDetails(source.Bounds);
		itemDetailsSource = source;
		itemDetailsItem = source.Item;
	}

	private Rectangle DetailsBounds(float width, float height) => itemDetailsSource.HasValue
		? layout.ItemDetails(recipeDetailsAnchor, width, height) : layout.RecipeDetails(recipeDetailsAnchor, width, height);

	private void BuildItemDetails(Item item)
	{
		int capacity = 30 + (item.ToolTip?.Lines ?? 0), count = 1, logo = -1, research = -1;
		string[] text = new string[capacity], names = new string[capacity];
		bool[] modifier = new bool[capacity], bad = new bool[capacity];
		Main.MouseText_DrawItemTooltip_GetLinesInfo(item, ref logo, ref research, item.knockBack,
			ref count, text, modifier, bad, names, out int prefixIndex);
		recipeDetailLines.Clear();
		recipeDetailLines.AddRange(ItemLoader.ModifyTooltips(item, ref count, names, ref text, ref modifier, ref bad, ref logo, out _, prefixIndex));
		recipeDetailMaterials.Clear();
	}

	private void DrawRecipeDetails()
	{
		bool itemDetails = itemDetailsSource.HasValue;
		if (!recipeDetailsOpen || (!itemDetails && selectedRecipe < 0) || craftingCategoryOpen || inventoryToolsOpen) return;
		if (itemDetails && (itemDetailsSource.Value.Item.IsAir || !ReferenceEquals(itemDetailsSource.Value.Item, itemDetailsItem))) { CloseRecipeDetails(); return; }
		Recipe recipe = itemDetails ? null : Main.recipe[selectedRecipe];
		Item item = itemDetails ? itemDetailsSource.Value.Item : recipe.createItem;
		float opacity = Math.Clamp((recipeDetailsExpires - InventoryUiTick) / 60f, 0, 1);
		Main.HoverItem = item;
		BuildItemDetails(item);
		if (recipe != null) foreach (Item required in recipe.requiredItem) {
			if (required.IsAir) continue;
			Item material = required.Clone();
			if (recipe.ProcessGroupsForText(required.type, out string name)) material.SetNameOverride(name);
			recipeDetailMaterials.Add(material);
		}
		float scale = .9f * layout.Scale, padding = 10 * layout.Scale, gap = 3 * layout.Scale;
		float desiredWidth = 170 * layout.Scale;
		foreach (TooltipLine line in recipeDetailLines) desiredWidth = Math.Max(desiredWidth,
			ChatManager.GetStringSize(FontAssets.MouseText.Value, line.Text, new Vector2(scale)).X + padding * 2);
		desiredWidth = Math.Max(desiredWidth, Math.Min(8, recipeDetailMaterials.Count) * 36 * layout.Scale + padding * 2);
		Rectangle panel = DetailsBounds(desiredWidth, 1);
		float contentWidth = Math.Max(1, panel.Width - padding * 2), contentHeight = 0;
		var sizes = new List<Vector2>();
		var scales = new List<float>();
		foreach (TooltipLine line in recipeDetailLines) {
			Vector2 size = ChatManager.GetStringSize(FontAssets.MouseText.Value, line.Text, new Vector2(scale));
			// A single CJK word can exceed ChatManager's word-wrap width. Keep rich
			// text and item tags intact and fit that line to the available column.
			float lineScale = scale * Math.Min(1, contentWidth / Math.Max(1, size.X));
			size *= lineScale / scale;
			if (line.Mod == "Terraria" && line.Name == "OneDropLogo") size = TextureAssets.OneDropLogo.Value.Size() * lineScale;
			sizes.Add(size); scales.Add(lineScale);
			contentHeight += Math.Max(size.Y, 20 * layout.Scale) + gap;
		}
		int materialsPerRow = Math.Max(1, (int)(contentWidth / (36 * layout.Scale)));
		int materialRows = (recipeDetailMaterials.Count + materialsPerRow - 1) / materialsPerRow;
		float materialsHeight = materialRows * 36 * layout.Scale;
		contentHeight += materialsHeight;
		recipeDetailsBounds = panel = DetailsBounds(desiredWidth, contentHeight + padding * 2);
		recipeDetailsMaxScroll = Math.Max(0, contentHeight - panel.Height + padding * 2);
		recipeDetailsScroll = Math.Clamp(recipeDetailsScroll, 0, recipeDetailsMaxScroll);
		MobileMenuFrame.Draw(Main.spriteBatch, Texture("Slot"), panel, new Color(90, 90, 140, 235) * opacity, layout.Scale, 6);

		Rectangle oldClip = Main.instance.GraphicsDevice.ScissorRectangle;
		Main.spriteBatch.End();
		Main.instance.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(panel, Main.instance.GraphicsDevice.Viewport.Bounds);
		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
			DepthStencilState.None, recipeDetailsClip);
		try {
			int originX = panel.X, originY = panel.Y;
			bool draw = ItemLoader.PreDrawTooltip(item, recipeDetailLines.AsReadOnly(), ref originX, ref originY);
			float y = originY + padding - recipeDetailsScroll;
			var drawableLines = new List<DrawableTooltipLine>();
			for (int i = 0; i < recipeDetailLines.Count; i++) {
				TooltipLine line = recipeDetailLines[i];
				var drawable = new DrawableTooltipLine(line, i, (int)(itemDetails ? originX + padding : originX + panel.Width - padding - sizes[i].X), (int)y,
					RecipeDetailColor(line, item)) { BaseScale = new Vector2(scales[i]), Spread = Math.Max(1, layout.Scale) };
				drawableLines.Add(drawable);
				int offset = 0;
				if (ItemLoader.PreDrawTooltipLine(item, drawable, ref offset) && draw) {
					Color color = (drawable.OverrideColor ?? drawable.Color) * opacity;
					if (drawable.OneDropLogo) Main.spriteBatch.Draw(TextureAssets.OneDropLogo.Value, new Vector2(drawable.X, drawable.Y), null,
						color, drawable.Rotation, drawable.Origin, drawable.BaseScale, SpriteEffects.None, 0);
					else ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, drawable.Font, drawable.Text, new Vector2(drawable.X, drawable.Y),
						color, drawable.Rotation, drawable.Origin, drawable.BaseScale, drawable.MaxWidth, drawable.Spread);
				}
				ItemLoader.PostDrawTooltipLine(item, drawable);
				y += Math.Max(sizes[i].Y, 20 * layout.Scale) + gap + offset;
			}
			ItemLoader.PostDrawTooltip(item, drawableLines.AsReadOnly());
			for (int i = 0; i < recipeDetailMaterials.Count; i++) {
				Item material = recipeDetailMaterials[i];
				int column = i % materialsPerRow, row = i / materialsPerRow;
				var r = new Rectangle((int)(panel.Right - padding - (column + 1) * 36 * layout.Scale),
					(int)(y + row * 36 * layout.Scale), Math.Max(1, (int)(34 * layout.Scale)), Math.Max(1, (int)(34 * layout.Scale)));
				ItemSlot.DrawItemIcon(material, ItemSlot.Context.ChatItem, Main.spriteBatch,
					new Vector2(r.Center.X, r.Top + 13 * layout.Scale), layout.Scale, 26, Color.White * opacity);
				Utils.DrawBorderStringFourWay(Main.spriteBatch, FontAssets.MouseText.Value, material.stack.ToString(),
					r.X + 5 * layout.Scale, r.Bottom - 15 * layout.Scale, Color.White * opacity, Color.Black * opacity, Vector2.Zero, .75f * layout.Scale);
			}
		}
		finally {
			Main.spriteBatch.End();
			Main.instance.GraphicsDevice.ScissorRectangle = oldClip;
			Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
				DepthStencilState.None, RasterizerState.CullCounterClockwise);
			// The anchored mobile tooltip replaces the desktop tooltip at the cursor.
			Main.HoverItem = new Item();
			Main.hoverItemName = "";
		}
		if (recipeDetailsMaxScroll <= 0) return;
		var track = new Rectangle(panel.Left + 3, panel.Top + 8, Math.Max(2, (int)(2 * layout.Scale)), panel.Height - 16);
		Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, track, Color.Black * (.45f * opacity));
		int thumb = Math.Max(8, (int)(track.Height * track.Height / (track.Height + recipeDetailsMaxScroll)));
		Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle(track.X, track.Y + (int)((track.Height - thumb) * recipeDetailsScroll / recipeDetailsMaxScroll), track.Width, thumb), Color.LightSlateGray * opacity);
	}

	private static Color RecipeDetailColor(TooltipLine line, Item item)
	{
		if (line.Mod != "Terraria") return Color.White;
		if (line.Name == "JourneyResearch") return Colors.JourneyMode;
		if (line.IsModifier) return line.IsModifierBad ? new Color(190, 120, 120) : new Color(120, 190, 120);
		if (line.Name != "ItemName") return Color.White;
		if (item.master || item.rare == ItemRarityID.Master) return new Color(255, (int)(Main.masterColor * 200), 0);
		if (item.expert || item.rare == ItemRarityID.Expert) return Main.DiscoColor;
		if (item.rare >= ItemRarityID.Count) return RarityLoader.GetRarity(item.rare).RarityColor;
		return item.rare switch {
			-11 => new Color(255, 175, 0), -1 => new Color(130, 130, 130),
			1 => new Color(150, 150, 255), 2 => new Color(150, 255, 150), 3 => new Color(255, 200, 150),
			4 => new Color(255, 150, 150), 5 => new Color(255, 150, 255), 6 => new Color(210, 160, 255),
			7 => new Color(150, 255, 10), 8 => new Color(255, 255, 10), 9 => new Color(5, 200, 255),
			10 => new Color(255, 40, 100), 11 => new Color(180, 40, 255), _ => Color.White
		};
	}
}
