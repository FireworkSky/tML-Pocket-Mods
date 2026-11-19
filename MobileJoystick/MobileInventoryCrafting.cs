using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Creative;
using Terraria.GameContent.UI.States;
using Terraria.UI;

namespace MobileJoystick;

public partial class MobileInventorySystem
{
	private bool craftingExpanded = true;
	private int CraftPageSize => craftingExpanded ? 40 : 5;
	private readonly List<int> craftingRecipes = new();
	private readonly IItemEntryFilter[] craftingFilters = {
		new ItemFilters.Vanity(), new ItemFilters.Furniture(), new ItemFilters.MiscAccessories(),
		new ItemFilters.Consumables(), new ItemFilters.Materials(), new ItemFilters.Tools(),
		new ItemFilters.BuildingBlock(), null, new ItemFilters.Accessories(), null,
		new ItemFilters.Weapon(), new ItemFilters.Armor()
	};
	private int craftingCategory = 9, selectedRecipe = -1, heldCraftRecipe = -1;
	private string craftingSearch = "";
	private bool craftingCategoryOpen;

	private static string CraftCategoryName(int index) => index switch {
		0 => T("时装", "Vanity"), 1 => T("家具", "Furniture"), 2 => T("装备", "Equipment"),
		3 => T("消耗品", "Consumables"), 4 => T("材料", "Materials"), 5 => T("工具", "Tools"),
		6 => T("物块", "Blocks"), 7 => T("其他", "Other"), 8 => T("配饰", "Accessories"),
		9 => T("全部", "All"), 10 => T("武器", "Weapons"), _ => T("盔甲", "Armor")
	};

	private bool MatchesCraftCategory(Item item)
	{
		if (craftingCategory == 9) return true;
		if (craftingCategory != 7) return craftingFilters[craftingCategory].FitsFilter(item);
		// Unlike the research menu, crafting also includes items without a research cap.
		foreach (var filter in craftingFilters) if (filter != null && filter.FitsFilter(item)) return false;
		return true;
	}

	private void RefreshCraftingRecipes()
	{
		craftingRecipes.Clear();
		for (int i = 0; i < Main.numAvailableRecipes; i++) {
			int id = Main.availableRecipe[i];
			Item item = Main.recipe[id].createItem;
			if (MatchesCraftCategory(item) && (craftingSearch.Length == 0
				|| item.Name.Contains(craftingSearch, StringComparison.CurrentCultureIgnoreCase))) craftingRecipes.Add(id);
		}
		if (!craftingRecipes.Contains(selectedRecipe)) {
			selectedRecipe = craftingRecipes.Count == 0 ? -1 : craftingRecipes[0];
			CloseRecipeDetails();
		}
		int maxStart = Math.Max(0, (craftingRecipes.Count - 1) / CraftPageSize * CraftPageSize);
		recipeStart = Math.Clamp(recipeStart, 0, maxStart);
	}

	private void DrawCrafting()
	{
		if (!itemDetailsSource.HasValue) ReadRecipeDetailsPointer();
		if (!MobileInventoryNative.CanCraft) {
			Text(T("制作功能由原版界面提供", "Crafting is provided by the native interface"), layout.Right(54, 204, 250, 44), .7f);
			return;
		}
		if (craftingCategoryOpen && pointerPressed && !used) {
			for (int i = 0; i < 12; i++) if (Click(layout.CraftCategoryButton(i, craftingExpanded))) {
				craftingCategory = i;
				recipeStart = 0;
				craftingCategoryOpen = false;
				break;
			}
			if (!used && !layout.CraftFilterButton(craftingExpanded).Contains(pointer.ToPoint())) {
				craftingCategoryOpen = false;
				used = true;
			}
		}
		RefreshCraftingRecipes();
		for (int cell = 0; cell < CraftPageSize; cell++) {
			Rectangle r = layout.Recipe(cell);
			int index = recipeStart + cell;
			if (index >= craftingRecipes.Count) {
				InventorySurface(r, "Slot", Color.White * .24f);
				InventoryGlyph("CraftEmpty", r, Color.White * .18f, .68f);
				continue;
			}
			int id = craftingRecipes[index];
			Item item = Main.recipe[id].createItem;
			InventorySurface(r, id == selectedRecipe ? "SlotSelected" : "SlotPurple", Color.White * .86f);
			SetSlotScale(r);
			ItemSlot.Draw(Main.spriteBatch, ref item, ItemSlot.Context.ChatItem, r.TopLeft());
			if (Hover(r) && !craftingCategoryOpen) {
				Main.HoverItem = item.Clone();
				Main.hoverItemName = item.Name;
				Main.LocalPlayer.mouseInterface = true;
				if (Click(r)) {
					selectedRecipe = id;
					OpenRecipeDetails(r);
					Main.focusRecipe = Array.IndexOf(Main.availableRecipe, id, 0, Main.numAvailableRecipes);
					heldCraftRecipe = -1;
				}
			}
		}
		if (IconButton(layout.CraftFilterButton(craftingExpanded), "Category" + craftingCategory, CraftCategoryName(craftingCategory), craftingCategoryOpen)) {
			craftingCategoryOpen = !craftingCategoryOpen;
			heldCraftRecipe = -1;
		}
		if (IconButton(layout.CraftSearchButton(craftingExpanded), "Search", T("搜索", "Search"), craftingSearch.Length > 0)) OpenCraftingSearch();
		if (craftingSearch.Length > 0) {
			int offset = craftingExpanded ? 0 : 100;
			Text(craftingSearch, layout.Right(126 + offset, 260, 192, 44), .7f);
			if (TextButton(layout.Right(76 + offset, 260, 44, 44), "×")) { craftingSearch = ""; recipeStart = 0; }
		}
		if (craftingRecipes.Count > CraftPageSize) {
			int maxStart = (craftingRecipes.Count - 1) / CraftPageSize * CraftPageSize;
			if (TextButton(layout.Right(380, 312, 44, 32), "<")) recipeStart = Math.Max(0, recipeStart - CraftPageSize);
			Text($"{recipeStart / CraftPageSize + 1}/{maxStart / CraftPageSize + 1}", layout.Right(280, 312, 90, 32), .65f);
			if (TextButton(layout.Right(230, 312, 44, 32), ">")) recipeStart = Math.Min(maxStart, recipeStart + CraftPageSize);
		}
		if (craftingRecipes.Count == 0) Text(T("暂无可制作物品", "No matching recipes"), layout.Right(78, 108, 344, 44), .75f);
		DrawCraftAction();
		if (!itemDetailsSource.HasValue) DrawRecipeDetails();
		if (!craftingCategoryOpen) return;
		MobileMenuFrame.Draw(Main.spriteBatch, Texture("Tool"), layout.CraftCategoryPanel(craftingExpanded), Color.White, layout.Scale, 6);
		for (int i = 0; i < 12; i++) {
			Rectangle r = layout.CraftCategoryButton(i, craftingExpanded);
			InventoryButtonSurface(r, "Tool", Color.White);
			DrawCraftCategoryGlyph(i, r, Color.White);
			Tooltip(r, CraftCategoryName(i));
		}
	}

	private void ToggleCraftingSize()
	{
		CloseRecipeDetails();
		craftingExpanded = !craftingExpanded;
		craftingCategoryOpen = false;
		heldCraftRecipe = -1;
		recipeStart = Math.Max(0, craftingRecipes.IndexOf(selectedRecipe)) / CraftPageSize * CraftPageSize;
	}

	private void DrawCraftAction()
	{
		Rectangle r = layout.CraftAction;
		bool enabled = selectedRecipe >= 0 && Main.mouseItem.IsAir && Main.LocalPlayer.ItemSpace(Main.recipe[selectedRecipe].createItem).CanTakeItemToPersonalInventory;
		InventoryButtonSurface(r, "SlotPurple", enabled ? Color.White * .86f : Color.White * .3f, enabled);
		if (selectedRecipe >= 0) {
			Item output = Main.recipe[selectedRecipe].createItem;
			Rectangle icon = layout.Right(14, 264, 42, 42);
			SetSlotScale(icon);
			ItemSlot.Draw(Main.spriteBatch, ref output, ItemSlot.Context.ChatItem, icon.TopLeft(), enabled ? Color.White : Color.Gray);
		}
		Text(T("制造", "Craft"), layout.Right(4, 302, 62, 20), .8f);
		Tooltip(r, T("制造", "Craft"));
		if (!pointerDown || !Hover(r) || !enabled || craftingCategoryOpen) { heldCraftRecipe = -1; return; }
		if (pointerPressed && !used) heldCraftRecipe = selectedRecipe;
		if (used || heldCraftRecipe != selectedRecipe) return;
		used = true;
		int available = Array.IndexOf(Main.availableRecipe, selectedRecipe, 0, Main.numAvailableRecipes);
		if (available < 0) return;
		Main.focusRecipe = available;
		MobileInventoryNative.CraftIntoInventory(available);
	}

	private void OpenCraftingSearch()
	{
		craftingCategoryOpen = false;
		CancelInventoryGesture();
		void Return() {
			IngameFancyUI.Close();
			Main.playerInventory = true;
			nativeView = false;
		}
		IngameFancyUI.OpenUIState(new UIVirtualKeyboard(T("搜索", "Search"), craftingSearch, text => {
			craftingSearch = text.Trim();
			recipeStart = 0;
			Return();
		}, Return, allowEmpty: true));
	}
}
