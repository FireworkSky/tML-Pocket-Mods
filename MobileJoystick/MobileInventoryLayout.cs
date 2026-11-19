using System;
using Microsoft.Xna.Framework;

namespace MobileJoystick;

internal readonly struct MobileInventoryLayout
{
	public readonly float Scale;
	public readonly int Width;
	public readonly int Height;
	public MobileInventoryLayout(int width, int height)
	{
		Width = width;
		Height = height;
		Scale = Math.Min(width / 1280f, height / 576f);
	}
	public Rectangle At(float x, float y, float w = 48, float h = 48) => new(
		(int)MathF.Round(x * Scale), (int)MathF.Round(y * Scale),
		Math.Max(1, (int)MathF.Round(w * Scale)), Math.Max(1, (int)MathF.Round(h * Scale)));
	public Rectangle Right(float x, float y, float w = 48, float h = 48) => At(Width / Scale - x - w, y, w, h);
	public Rectangle LeftTab(int index) => At(0, 50 * index, 50, 50);
	public Rectangle RightTab(int index) => Right(0, 50 * index, 50, 50);
	public Rectangle Armor(int slot, int kind) => Gear((slot < 3 || slot >= 8 ? 5 : 2) + 2 - kind,
		slot < 3 ? slot : slot >= 8 ? slot - 5 : slot - 3);
	public Rectangle MiscEquipment(int slot, bool dye = false) => Gear(dye ? 0 : 1, slot);
	public Rectangle ModEquipmentButton => Right(380, 260, 44, 44);
	public Rectangle LoadoutButton => Right(330, 260, 44, 44);
	public Rectangle Defense => Right(280, 260, 44, 44);
	// The first ten slots keep their two-column hotbar order when the bag opens.
	public Rectangle Inventory(int slot) => At(54 + slot / 5 * 50, 4 + slot % 5 * 50);
	public Rectangle CoinAmmo(int slot) => At(572 + (slot >= 54 ? 50 : 0), 60 + (slot - 50) % 4 * 50, 44, 44);
	public Rectangle Gear(int column, int row) => Right(54 + (7 - column) * 50, 4 + row * 50);
	public Rectangle Recipe(int index) => Right(54 + index / 5 * 50, 4 + index % 5 * 50);
	public Rectangle CraftFilter => Right(380, 260, 44, 44);
	public Rectangle CraftSearch => Right(330, 260, 44, 44);
	public Rectangle CraftAction => Right(4, 260, 62, 62);
	public Rectangle CraftCategory(int index) => Right(404 - index % 3 * 50, 54 + index / 3 * 50, 46, 46);
	public Rectangle CraftCategories => Right(300, 50, 154, 204);
	public Rectangle CraftFilterButton(bool expanded) => expanded ? CraftFilter : Right(130, 260, 44, 44);
	public Rectangle CraftSearchButton(bool expanded) => expanded ? CraftSearch : Right(80, 260, 44, 44);
	public Rectangle CraftCategoryButton(int index, bool expanded) => expanded ? CraftCategory(index)
		: Right(154 - index % 3 * 50, 54 + index / 3 * 50, 46, 46);
	public Rectangle CraftCategoryPanel(bool expanded) => expanded ? CraftCategories : Right(50, 50, 154, 204);
	public Rectangle RecipeDetails(Rectangle anchor, float desiredWidth, float desiredHeight)
	{
		int padding = Math.Max(1, (int)(4 * Scale));
		int right = anchor.Left - padding;
		int leftLimit = LeftArea.Right + padding;
		int width = Math.Max(1, Math.Min((int)Math.Min(desiredWidth, 380 * Scale), right - leftLimit));
		int height = Math.Max(1, Math.Min((int)desiredHeight, RightArea(false).Height - padding * 2));
		int top = Math.Clamp(anchor.Center.Y - height / 3, padding, RightArea(false).Bottom - padding - height);
		return new Rectangle(right - width, top, width, height);
	}
	public Rectangle Container(int slot) => Right(54 + (9 - slot % 10) * 50, 36 + slot / 10 * 50);
	public Rectangle ItemDetails(Rectangle anchor, float desiredWidth, float desiredHeight)
	{
		int padding = Math.Max(1, (int)(6 * Scale));
		int width = Math.Max(1, Math.Min((int)Math.Min(desiredWidth, 360 * Scale), Width - padding * 2));
		int height = Math.Max(1, Math.Min((int)desiredHeight, Math.Min(Height - padding * 2, (int)(340 * Scale))));
		int x = anchor.Center.X < Width / 2 ? anchor.Right + padding : anchor.Left - padding - width;
		int y = Math.Clamp(anchor.Top, padding, Math.Max(padding, Height - padding - height));
		return new Rectangle(Math.Clamp(x, padding, Math.Max(padding, Width - padding - width)), y, width, height);
	}
	public Rectangle LeftAction(int index) => At(26 + index * 52, 260, 44, 44);
	public Rectangle InventoryTools => At(404, 204, 156, 52);
	public Rectangle InventoryTool(int index) => At(408 + index * 50, 208, 44, 44);
	public Rectangle RightAction(int index) => Right(4 + index * 50, 260, 44, 44);
	public Rectangle LeftArea => At(0, 0, 670, 355);
	public Rectangle RightArea(bool container) => Right(0, 0, container ? 560 : 460, 355);
}
