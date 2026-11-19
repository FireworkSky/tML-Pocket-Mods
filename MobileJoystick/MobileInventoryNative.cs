using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.Default;

namespace MobileJoystick;

// These cached accessors retain tML's crafting locks, custom slot callbacks,
// shared loadout checks and packet handling rather than duplicating them.
internal static class MobileInventoryNative
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
	private static readonly Action<int> HoverRecipe = typeof(Main).GetMethod("HoverOverCraftingItemButton", StaticPrivate)?.CreateDelegate<Action<int>>();
	private static readonly FieldInfo CraftLock = typeof(Main).GetField("_preventCraftingBecauseClickWasUsedToChangeFocusedRecipe", StaticPrivate);
	private static readonly FieldInfo CurrentCraft = typeof(Main).GetField("_currentRecipeBeingCrafted", StaticPrivate);
	private static readonly FieldInfo Accessories = typeof(ModAccessorySlotPlayer).GetField("exAccessorySlot", Private);
	private static readonly FieldInfo Dyes = typeof(ModAccessorySlotPlayer).GetField("exDyesAccessory", Private);
	private static readonly FieldInfo Hidden = typeof(ModAccessorySlotPlayer).GetField("exHideAccessory", Private);
	private static readonly FieldInfo DrawCounter = typeof(AccessorySlotLoader).GetField("slotDrawLoopCounter", Private);
	private static readonly Action<AccessorySlotLoader, Item[], int, int, bool, int, int, bool> DrawSlot = typeof(AccessorySlotLoader).GetMethod("DrawSlot", Private)
		?.CreateDelegate<Action<AccessorySlotLoader, Item[], int, int, bool, int, int, bool>>();

	internal static bool CanCraft => HoverRecipe != null;
	internal static bool CanDrawModSlots => Accessories != null && Dyes != null && DrawCounter != null && DrawSlot != null;
	internal static bool[] ModVisibility => Hidden?.GetValue(Main.LocalPlayer.GetModPlayer<ModAccessorySlotPlayer>()) as bool[];
	internal static void CraftHover(int recipe) => HoverRecipe?.Invoke(recipe);
	internal static void CraftIntoInventory(int recipe)
	{
		if (!CanCraft || !Main.mouseItem.IsAir) return;
		HoverRecipe(recipe);
		if (Main.mouseItem.IsAir) return;
		Main.mouseItem = Main.LocalPlayer.GetItem(Main.myPlayer, Main.mouseItem, GetItemSettings.InventoryUIToInventorySettingsShowAsNew);
		Recipe.FindRecipes();
	}
	internal static void ResetCrafting()
	{
		CraftLock?.SetValue(null, false);
		CurrentCraft?.SetValue(null, -1);
	}
	internal static void DrawModSlot(int slot, int kind, Rectangle bounds, bool enabled, bool skipInput = false)
	{
		if (!CanDrawModSlots) return;
		var player = Main.LocalPlayer.GetModPlayer<ModAccessorySlotPlayer>();
		var loader = LoaderManager.Get<AccessorySlotLoader>();
		Item[] items = (Item[])(kind == 2 ? Dyes : Accessories).GetValue(player);
		int index = kind == 1 ? slot + player.SlotCount : slot;
		DrawCounter.SetValue(loader, 0);
		DrawSlot(loader, items, -10 - kind, index, !enabled, bounds.X, bounds.Y, skipInput);
	}
}
