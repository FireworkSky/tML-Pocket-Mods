using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.Default;
using Terraria.UI;

namespace MobileJoystick;

public partial class MobileInventorySystem
{
	private void DrawHousing()
	{
		var heads = new List<(int npc, int head)> { (-1, 0) };
		var types = new HashSet<int>();
		var drawnHeads = new HashSet<int>();
		for (int i = 0; i < Main.maxNPCs; i++) {
			NPC npc = Main.npc[i];
			if (!npc.active || npc.ModNPC?.TownNPCStayingHomeless == true || types.Contains(npc.type)) continue;
			int head = TownNPCProfiles.GetHeadIndexSafe(npc);
			if (head <= 0 || NPCHeadID.Sets.CannotBeDrawnInHousingUI[head] || !drawnHeads.Add(head)) continue;
			types.Add(npc.type); heads.Add((i, head));
		}
		int pages = Math.Max(1, (heads.Count + 49) / 50);
		housingPage = Math.Clamp(housingPage, 0, pages - 1);
		for (int cell = 0; cell < 50 && housingPage * 50 + cell < heads.Count; cell++) {
			var entry = heads[housingPage * 50 + cell]; Rectangle r = layout.Inventory(cell);
			bool selected = Main.instance.mouseNPCIndex == entry.npc && Main.instance.mouseNPCType >= 0;
			InventorySurface(r, selected ? "SlotSelected" : "Slot", Color.White * .86f);
			Texture2D head = TextureAssets.NpcHead[entry.head].Value;
			float scale = Math.Min(1f, 36f / Math.Max(head.Width, head.Height)) * layout.Scale;
			Main.spriteBatch.Draw(head, r.Center.ToVector2(), null, Color.White, 0, head.Size() / 2, scale, SpriteEffects.None, 0);
			Tooltip(r, entry.npc < 0 ? Lang.inter[8].Value : Main.npc[entry.npc].FullName);
			if (!Main.mouseItem.IsAir || !Click(r)) continue;
			if (entry.npc < 0) Main.instance.SetMouseNPC_ToHousingQuery();
			else Main.instance.SetMouseNPC(entry.npc, Main.npc[entry.npc].type);
		}
		if (pages > 1) {
			if (TextButton(layout.LeftAction(2), "<")) housingPage = Math.Max(0, housingPage - 1);
			Text($"{housingPage + 1}/{pages}", layout.LeftAction(3), .65f);
			if (TextButton(layout.LeftAction(4), ">")) housingPage = Math.Min(pages - 1, housingPage + 1);
		}
		Text(T("选择头像后，点击房间分配住房", "Select a head, then click a room"), layout.At(58, 314, 490, 32), .7f);
	}

	private void DrawPvp()
	{
		Player p = Main.LocalPlayer; Rectangle hostile = layout.At(62, 28, 54, 60);
		Texture2D pvp = TextureAssets.Pvp[0].Value;
		Rectangle frame = pvp.Frame(4, 6, (p.hostile ? 2 : 0) + (Hover(hostile) && Main.teamCooldown == 0 ? 1 : 0), p.team);
		frame.Width -= 2; frame.Height--;
		Main.spriteBatch.Draw(pvp, hostile, frame, Color.White); Tooltip(hostile, T("切换 PvP", "Toggle PvP"));
		if (Click(hostile) && Main.teamCooldown == 0) {
			Main.teamCooldown = Main.teamCooldownLen; p.hostile = !p.hostile;
			NetMessage.SendData(MessageID.TogglePVP, -1, -1, null, Main.myPlayer);
		}
		for (int i = 0; i < 6; i++) {
			Rectangle r = layout.At(148 + i % 3 * 56, 28 + i / 3 * 56);
			Texture2D teams = TextureAssets.Pvp[1].Value; Rectangle teamFrame = teams.Frame(6, 1, i, 0); teamFrame.Width -= 2;
			if (p.team == i || Hover(r)) Main.spriteBatch.Draw(TextureAssets.Pvp[2].Value, r, Color.White);
			Main.spriteBatch.Draw(teams, r, teamFrame, Color.White); Tooltip(r, T("队伍", "Team") + " " + i);
			if (!Click(r) || Main.teamCooldown != 0 || p.team == i) continue;
			if (!p.TeamChangeAllowed()) Main.NewText(Lang.misc[84].Value, 255, 240, 20);
			else { Main.teamCooldown = Main.teamCooldownLen; p.team = i; NetMessage.SendData(MessageID.PlayerTeam, -1, -1, null, Main.myPlayer); }
		}
	}

	private void DrawEquipment()
	{
		Player p = Main.LocalPlayer;
		DrawLoadoutButton();
		var modPlayer = p.GetModPlayer<ModAccessorySlotPlayer>(); var loader = LoaderManager.Get<AccessorySlotLoader>(); var slots = new List<int>();
		for (int i = 0; i < modPlayer.SlotCount; i++) if (!loader.IsHidden(i) && (loader.ModdedIsItemSlotUnlockedAndUsable(i, p) || loader.ModdedCanSlotBeShown(i))) slots.Add(i);
		int pages = 1 + (slots.Count + 9) / 10; equipmentPage = Math.Clamp(equipmentPage, 0, pages - 1);
		if (pages > 1 && IconButton(layout.ModEquipmentButton, "Grid", T("扩展装备 ", "Extra slots ") + $"{equipmentPage + 1}/{pages}", equipmentPage > 0)) equipmentPage = (equipmentPage + 1) % pages;
		if (equipmentPage > 0) { DrawModEquipment(slots, loader); return; }
		for (int i = 0; i < 8; i++) DrawArmorRow(i);
		if (p.CanDemonHeartAccessoryBeShown()) DrawArmorRow(8);
		if (p.CanMasterModeAccessoryBeShown()) DrawArmorRow(9);
		for (int i = 0; i < 5; i++) {
			int context = i switch { 0 => 19, 1 => 20, 2 => 18, 3 => 17, _ => 16 }; Rectangle r = layout.MiscEquipment(i);
			if (i < 2) Visibility(r, p.hideMisc[i], () => { if (i == 0) p.TogglePet(); else p.ToggleLight(); SyncAppearance(); });
			if (i == 2 && p.unlockedSuperCart) Visibility(r, !p.enabledSuperCart, () => { p.enabledSuperCart = !p.enabledSuperCart; SyncAppearance(); });
			Slot(p.miscEquips, context, i, r); Slot(p.miscDyes, 33, i, layout.MiscEquipment(i, dye: true));
		}
	}

	private void DrawArmorRow(int slot)
	{
		Player p = Main.LocalPlayer; bool locked = !p.IsItemSlotUnlockedAndUsable(slot); Rectangle functional = layout.Armor(slot, 0);
		if (slot >= 3) Visibility(functional, p.hideVisibleAccessory[slot], () => { p.hideVisibleAccessory[slot] = !p.hideVisibleAccessory[slot]; SyncAppearance(); });
		Slot(p.armor, slot < 3 ? 8 : 10, slot, functional, locked);
		Slot(p.armor, slot < 3 ? 9 : 11, slot + 10, layout.Armor(slot, 1), locked);
		Slot(p.dye, 12, slot, layout.Armor(slot, 2), locked);
	}

	private void DrawModEquipment(List<int> slots, AccessorySlotLoader loader)
	{
		if (!MobileInventoryNative.CanDrawModSlots) {
			Text(T("模组装备扩展暂不可用", "Modded equipment extensions are unavailable"), layout.Right(54, 204, 250, 44), .7f);
			return;
		}
		for (int cell = 0; cell < 10; cell++) {
			int index = (equipmentPage - 1) * 10 + cell; if (index >= slots.Count) break;
			int slot = slots[index], column = cell / 5 * 4, row = cell % 5; ModAccessorySlot definition = loader.Get(slot); bool[] hidden = MobileInventoryNative.ModVisibility;
			for (int kind = 0; kind < 3; kind++) {
				if (kind == 0 && !definition.DrawFunctionalSlot || kind == 1 && !definition.DrawVanitySlot || kind == 2 && !definition.DrawDyeSlot) continue;
				Rectangle r = layout.Gear(column + 2 - kind, row); SetSlotScale(r);
				if (kind == 0 && hidden != null) Visibility(r, hidden[slot], () => hidden[slot] = !hidden[slot]);
				bool enabled = kind == 2 ? definition.IsEnabled() : loader.ModdedIsSpecificItemSlotUnlockedAndUsable(slot, Main.LocalPlayer, kind == 1);
				Color back = Main.inventoryBack; if (!enabled) Main.inventoryBack = new Color(80, 80, 80, 80);
				MobileInventoryNative.DrawModSlot(slot, kind, r, enabled, used); Main.inventoryBack = back; if (Hover(r)) used = true;
			}
		}
	}

	private void DrawShop()
	{
		if (Main.npcShop <= 0 || Main.npcShop >= Main.instance.shop.Length) return;
		Chest shop = Main.instance.shop[Main.npcShop];
		if (shop == null) return;
		NPC npc = Main.LocalPlayer.talkNPC >= 0 && Main.LocalPlayer.talkNPC < Main.maxNPCs
			? Main.npc[Main.LocalPlayer.talkNPC] : null;
		string title = npc == null ? T("商店", "Shop") : npc.FullName;
		Text(title, layout.Right(54, 4, 498, 28), .8f);
		for (int i = 0; i < Math.Min(40, shop.item.Length); i++) {
			Rectangle r = layout.Container(i);
			SetSlotScale(r);
			DrawPhoneSlot(shop.item[i], ItemSlot.Context.ShopItem, i, r);
			if (!Hover(r)) continue;
			Main.LocalPlayer.mouseInterface = true;
			ItemSlot.MouseHover(shop.item, ItemSlot.Context.ShopItem, i);
			if (!used && pointerPressed) {
				NativeTouchClick(() => ItemSlot.Handle(shop.item, ItemSlot.Context.ShopItem, i));
				used = true;
				Recipe.FindRecipes();
			}
		}
		Text(T("点击物品购买", "Tap an item to buy"), layout.Right(110, 262, 250, 34), .68f, new Color(175, 191, 228));
		if (TextButton(layout.RightAction(0), T("关闭", "Close"))) {
			Main.LocalPlayer.SetTalkNPC(-1);
			Recipe.FindRecipes();
		}
	}

	private void NativeTouchClick(Action action)
	{
		bool oldLeft = Main.mouseLeft;
		bool oldRelease = Main.mouseLeftRelease;
		bool oldRight = Main.mouseRight;
		bool oldRightRelease = Main.mouseRightRelease;
		try {
			Main.mouseLeft = true;
			Main.mouseLeftRelease = true;
			Main.mouseRight = Main.mouseRightRelease = false;
			action();
		}
		finally {
			Main.mouseLeft = oldLeft;
			Main.mouseLeftRelease = oldRelease;
			Main.mouseRight = oldRight;
			Main.mouseRightRelease = oldRightRelease;
		}
	}

	private readonly List<(Rectangle bounds, bool hidden)> visibilityTicks = new();
	private void Visibility(Rectangle slot, bool hidden, Action toggle)
	{
		Rectangle r = new(slot.Right - (int)(16 * layout.Scale), slot.Top, Math.Max(1, (int)(16 * layout.Scale)), Math.Max(1, (int)(16 * layout.Scale)));
		visibilityTicks.Add((r, hidden)); Tooltip(r, hidden ? T("显示", "Show") : T("隐藏", "Hide")); if (Click(r)) toggle();
	}
	private static void SyncAppearance() { if (Main.netMode == NetmodeID.MultiplayerClient) NetMessage.SendData(MessageID.SyncPlayer, -1, -1, null, Main.myPlayer); }
	private static void Border(Rectangle r, Color color)
	{
		Texture2D pixel = TextureAssets.MagicPixel.Value;
		Main.spriteBatch.Draw(pixel, new Rectangle(r.X, r.Y, r.Width, 2), color); Main.spriteBatch.Draw(pixel, new Rectangle(r.X, r.Bottom - 2, r.Width, 2), color);
		Main.spriteBatch.Draw(pixel, new Rectangle(r.X, r.Y, 2, r.Height), color); Main.spriteBatch.Draw(pixel, new Rectangle(r.Right - 2, r.Y, 2, r.Height), color);
	}

	private void DrawContainer()
	{
		Player p = Main.LocalPlayer; if (p.chest >= 0 && Main.chest[p.chest] == null) return;
		ChestUI.GetContainerUsageInfo(out _, out Item[] items); int context = p.chest >= 0 ? 3 : p.chest == -5 ? 32 : 4;
		for (int i = 0; i < Math.Min(40, items.Length); i++) Slot(items, context, i, layout.Container(i));
		string name = p.chest >= 0 ? Main.chest[p.chest].name : p.chest switch { -2 => Lang.inter[32].Value, -3 => Lang.inter[33].Value, -4 => Lang.GetItemNameValue(3813), _ => Lang.GetItemNameValue(4076) };
		Text(string.IsNullOrWhiteSpace(name) ? T("宝箱", "Chest") : name, layout.Right(54, 4, 498, 28), .8f);
		string[] icons = { "Loot", "Deposit", "QuickStack", "Restock", "Sort" };
		string[] hints = { T("全部取出", "Loot all"), T("全部存入", "Deposit all"), T("快速堆叠", "Quick stack"), T("补充物品", "Restock"), T("整理容器", "Sort container") };
		for (int i = 0; i < icons.Length; i++) {
			if (!IconButton(layout.RightAction(i + 1), icons[i], hints[i])) continue;
			switch (i) { case 0: ChestUI.LootAll(); break; case 1: ChestUI.DepositAll(ContainerTransferContext.FromUnknown(p)); break; case 2: ChestUI.QuickStack(ContainerTransferContext.FromUnknown(p)); break; case 3: ChestUI.Restock(); break; case 4: ItemSorting.SortChest(); break; }
			Recipe.FindRecipes();
		}
		if (p.chest >= 0 && TextButton(layout.Right(354, 260, 90, 44), T("重命名", "Rename"))) ChestUI.RenameChest();
		if (p.chest == -5 && TextButton(layout.Right(354, 260, 90, 44), T("自动收纳", "Vacuum"), p.IsVoidVaultEnabled)) p.IsVoidVaultEnabled = !p.IsVoidVaultEnabled;
		if (TextButton(layout.RightAction(0), "×")) { p.chest = -1; Recipe.FindRecipes(); }
	}
}
