using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using ReLogic.Content;
using ReLogic.Graphics;

namespace MobileJoystick;

public partial class MobileInventorySystem : ModSystem
{
	private int leftPage, rightPage = 1, recipeStart, equipmentPage, housingPage, lastChest = -1;
	private int touchId = -1, selectedSlot = -1;
	private bool nativeView, wasOpen, pointerDown, pointerPressed, pointerReleased, pointerCancelled, used, touchInput;
	private bool pointerNeedsRelease;
	private bool suppressOpeningPointer;
	private Vector2 pointer;
	private ulong inputVersion, drawnVersion;
	private MobileInventoryLayout layout;
	private readonly Dictionary<string, Texture2D> textures = new();
	private readonly Item[] trashSlot = { new Item() };
	private static string T(string zh, string en) => MobileSettingsCatalog.Text(zh, en);
	private static ulong InventoryUiTick => (ulong)(System.Diagnostics.Stopwatch.GetTimestamp() * (60.0 / System.Diagnostics.Stopwatch.Frequency));
	private static MobileInventorySystem Instance => ModContent.GetInstance<MobileInventorySystem>();
	internal static bool Enabled => MobileJoystickConfig.Instance is { EnableMobileControls: true, UseMobileInventory: true };
	private static bool SpecialEditor => Main.InReforgeMenu || Main.InGuideCraftMenu
		|| Main.CreativeMenu.Enabled || Main.LocalPlayer.creativeInterface || Main.LocalPlayer.tileEntityAnchor.InUse || Main.hairWindow || Main.editChest || Main.editSign;
	private static bool MobileShop => Main.npcShop > 0 && Main.LocalPlayer.talkNPC >= 0;
	internal static bool Active => Enabled && Main.playerInventory && !Main.gameMenu && !Main.ingameOptionsWindow && !Main.inFancyUI
		&& !PlayerInput.UsingGamepadUI && !Instance.nativeView && !SpecialEditor;
	internal static bool BlocksControl(Vector2 point) => Active && Instance.HitArea(point);
	internal static bool OwnsContact(int id) => Active && Instance.touchId >= 0 && Instance.touchId == id;
	internal static void OpenedFromControl()
	{
		// The newly visible bag tab must not receive the press that opened it.
		Instance.suppressOpeningPointer = true;
		Instance.pointerNeedsRelease = true;
	}
	internal static bool BlocksWorldUse => Active && (Instance.dragSource.HasValue || Instance.touchId >= 0
		|| (Instance.pointerDown && Instance.HitArea(Instance.pointer))
		|| Instance.HitArea(new Vector2(Main.mouseX, Main.mouseY)));
	private bool HitArea(Vector2 p)
	{
		var l = new MobileInventoryLayout(PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight);
		return l.LeftArea.Contains(p.ToPoint()) || (rightPage >= 0 && l.RightArea(Main.LocalPlayer.chest != -1).Contains(p.ToPoint()))
			|| (recipeDetailsOpen && recipeDetailsBounds.Contains(p.ToPoint()))
			|| l.RightTab(0).Contains(p.ToPoint()) || l.RightTab(1).Contains(p.ToPoint());
	}

	public override void Load()
	{
		if (!Main.dedServ) On_Main.DoDraw_UpdateCameraPosition += UpdateInventoryCamera;
		if (!Main.dedServ) On_Main.DrawInventory += DrawInventory;
		if (!Main.dedServ) On_Main.CanPauseGame += CanPauseInventoryUse;
	}
	public override void Unload()
	{
		RestoreInventoryCamera();
		if (!Main.dedServ) On_Main.DoDraw_UpdateCameraPosition -= UpdateInventoryCamera;
		inventoryCamera.Reset();
		if (!Main.dedServ) On_Main.DrawInventory -= DrawInventory;
		if (!Main.dedServ) On_Main.CanPauseGame -= CanPauseInventoryUse;
		textures.Clear();
		recipeDetailsClip.Dispose();
	}
	public override void OnWorldUnload()
	{
		RestoreInventoryCamera();
		inventoryCamera.Reset();
		suppressOpeningPointer = false;
		nativeView = wasOpen = false;
		selectedSlot = -1;
		touchId = -1;
		lastChest = -1;
		CancelInventoryGesture();
		leftPage = recipeStart = equipmentPage = housingPage = 0;
		rightPage = 1;
		craftingCategory = 9;
		selectedRecipe = -1;
		craftingSearch = "";
		craftingRecipes.Clear();
		craftingExpanded = true;
	}
	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		if (!Active) return;
		foreach (var layer in layers)
			if (layer.Name == "Vanilla: Settings Button" || (rightPage >= 0 && (layer.Name == "Vanilla: Resource Bars" || layer.Name == "Vanilla: Map / Minimap"))) layer.Active = false;
	}

	// Called after the controls' single touch sample in PostUpdateInput.
	internal void ReadPointer()
	{
		inputVersion++;
		pointerCancelled = false;
		if (!Main.playerInventory || !Main.hasFocus) {
			suppressOpeningPointer = false;
			if (!Main.hasFocus) pointerNeedsRelease = true;
			wasOpen = nativeView = pointerDown = false;
			pointerPressed = pointerReleased = touchInput = false;
			touchId = -1;
			CancelInventoryGesture();
			return;
		}
		if (!Active) CancelInventoryGesture();
		if (suppressOpeningPointer) {
			suppressOpeningPointer = false;
			pointerDown = pointerPressed = touchInput = false;
			touchId = -1;
			return;
		}
		if (!wasOpen) { selectedSlot = Math.Clamp(Main.LocalPlayer.selectedItem, 0, 49); recipeStart = 0; wasOpen = true; }
		if (lastChest != Main.LocalPlayer.chest) {
			if (Main.LocalPlayer.chest != -1) rightPage = 2;
			else if (rightPage == 2) rightPage = 1;
			lastChest = Main.LocalPlayer.chest;
		}
		bool previous = pointerDown;
		bool previousTouch = touchInput;
		MouseState mouse = Mouse.GetState();
		if (touchId < 0) pointer = new(mouse.X, mouse.Y);
		if (mouse.LeftButton == ButtonState.Released) pointerNeedsRelease = false;
		bool hasTouch = false;
		foreach (var contact in MobileJoystickInput.ActiveContacts) {
			if (contact.Id >= 0) { hasTouch = true; break; }
		}
		// SDL's mouse follows one finger, which may still be holding a joystick.
		// Real contacts own their press/release edges; do not mix in that mouse.
		if (hasTouch) pointerNeedsRelease = true;
		pointerDown = !hasTouch && !previousTouch && !pointerNeedsRelease && mouse.LeftButton == ButtonState.Pressed;
		touchInput = false;
		foreach (var contact in MobileJoystickInput.ActiveContacts) {
			if (MobileJoystickInput.ContactBelongsToControl(contact)) continue;
			if (contact.Id < 0 || (touchId < 0 && (contact.State != Microsoft.Xna.Framework.Input.Touch.TouchLocationState.Pressed || !HitArea(contact.Position)))
				|| (touchId >= 0 && touchId != contact.Id)) continue;
			if (touchId < 0) previous = false;
			touchId = contact.Id;
			pointer = contact.Position;
			pointerDown = !contact.IsReleased;
			pointerCancelled = contact.State == Microsoft.Xna.Framework.Input.Touch.TouchLocationState.Invalid;
			touchInput = true;
			if (contact.IsReleased) touchId = -1;
			break;
		}
		if (touchId >= 0 && !touchInput) { pointerDown = false; touchId = -1; pointerCancelled = true; }
		// Several input updates can precede a draw. Retain an edge until the
		// inventory consumes it instead of overwriting a quick tap's press.
		pointerPressed |= pointerDown && !previous;
		pointerReleased |= !pointerDown && previous && !pointerCancelled;
		if (pointerCancelled) CancelInventoryGesture();
		if (!pointerDown && !Main.mouseRight) MobileInventoryNative.ResetCrafting();
		if (Active && HitArea(pointer)) Main.LocalPlayer.mouseInterface = true;
	}

	private void DrawInventory(On_Main.orig_DrawInventory orig, Main self)
	{
		if (!Active) {
			orig(self);
			return;
		}
		Recipe.GetThroughDelayedFindRecipes();
		layout = new(PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight);
		bool fresh = inputVersion != drawnVersion;
		used = !fresh;
		drawnVersion = inputVersion;
		int mouseX = Main.mouseX, mouseY = Main.mouseY;
		bool left = Main.mouseLeft, release = Main.mouseLeftRelease, right = Main.mouseRight, rightRelease = Main.mouseRightRelease;
		float scale = Main.inventoryScale;
		Color back = Main.inventoryBack;
		Main.mouseX = (int)pointer.X; Main.mouseY = (int)pointer.Y;
		Main.mouseLeft = fresh && pointerDown;
		Main.mouseLeftRelease = fresh && pointerPressed;
		Main.mouseRight = fresh && !touchInput && right;
		Main.mouseRightRelease = fresh && !touchInput && rightRelease;
		Main.inventoryBack = Color.White;
		Begin();
		try {
			BeginInventoryGestureFrame(fresh);
			BeginInventoryToolsFrame();
			if (pointerPressed) heldHint = null;
			if (itemDetailsSource.HasValue) ReadRecipeDetailsPointer();
			visibilityTicks.Clear();
			DrawNavigation();
			if (rightPage != 0 && !itemDetailsSource.HasValue) CloseRecipeDetails();
			if (leftPage == 0) DrawBag();
			else if (leftPage == 1) DrawHousing();
			else DrawPvp();
			if (MobileShop) DrawShop();
			else if (rightPage == 0) DrawCrafting();
			else if (rightPage == 1) DrawEquipment();
			else if (rightPage == 2 && Main.LocalPlayer.chest != -1) DrawContainer();
			DrawInventoryGesture(fresh);
			if (itemDetailsSource.HasValue) DrawRecipeDetails();
			if (leftPage == 0) Main.LocalPlayer.trashItem = trashSlot[0];
			foreach (var tick in visibilityTicks) InventoryGlyph(tick.hidden ? "VisibilityOff" : "VisibilityOn", tick.bounds, Color.White, 1f);
			DrawInventoryTools(fresh);
			DrawHeldInventoryHint();
		}
		finally {
			pointerPressed = pointerReleased = false;
			toolsBlockFrame = false;
			Main.mouseX = mouseX; Main.mouseY = mouseY;
			Main.mouseLeft = left; Main.mouseLeftRelease = release; Main.mouseRight = right; Main.mouseRightRelease = rightRelease;
			Main.inventoryScale = scale; Main.inventoryBack = back;
			Main.spriteBatch.End();
			Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
				DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
		}
	}

	private void DrawNavigation()
	{
		string[] names = { "Bag", "House", "Pvp" };
		for (int i = 0; i < names.Length; i++) {
			int index = i;
			if (NavigationButton(layout.LeftTab(i), names[i], new[] { T("背包", "Inventory"), T("房屋", "Housing"), "PvP" }[i], leftPage == i)) {
				if (i == 0 && leftPage == 0) { Main.LocalPlayer.ToggleInv(); CancelInventoryGesture(); }
				leftPage = index; Main.EquipPage = index == 1 ? 1 : 0;
			}
		}
		if (MobileShop) return;
		for (int i = 0; i < 2; i++) {
			if (NavigationButton(layout.RightTab(i), i == 0 ? "Craft" : "Equipment", i == 0 ? T("制作", "Crafting") : T("装备", "Equipment"), rightPage == i)) {
				if (i == 0 && rightPage == 0) ToggleCraftingSize();
				else rightPage = rightPage == i ? -1 : i;
			}
		}
		if (Main.LocalPlayer.chest != -1 && IconButton(layout.RightTab(2), "Bag", T("容器", "Container"), rightPage == 2)) rightPage = 2;
	}

	private void DrawBag()
	{
		Player p = Main.LocalPlayer;
		for (int i = 0; i < 50; i++) {
			Rectangle r = layout.Inventory(i);
			Slot(p.inventory, ItemSlot.Context.InventoryItem, i, r);
		}
		Text(T("钱币", "Coins"), layout.At(572, 40, 44, 20), .65f);
		Text(T("弹药", "Ammo"), layout.At(622, 40, 44, 20), .65f);
		for (int i = 50; i < 58; i++) Slot(p.inventory, i < 54 ? 1 : 2, i, layout.CoinAmmo(i));
		bool hasSelection = selectedSlot >= 0 && !p.inventory[selectedSlot].IsAir;
		bool favorite = hasSelection && p.inventory[selectedSlot].favorited;
		if (IconButton(layout.LeftAction(1), favorite ? "Unfavorite" : "Favorite", favorite ? T("取消收藏", "Unfavorite") : T("收藏", "Favorite"), false, hasSelection))
			p.inventory[selectedSlot].favorited = !p.inventory[selectedSlot].favorited;
		DrawSplitButton();
		if (IconButton(layout.LeftAction(3), "Settings", T("设置", "Settings"))) { IngameOptions.Open(); return; }
		if (IconButton(layout.LeftAction(6), "Sort", T("排序", "Sort"))) { ItemSorting.SortInventory(); Recipe.FindRecipes(); }
		if (IconButton(layout.LeftAction(7), QuickStackGlyph, T("快速整理", "Quick stack"))) QuickStackInventory();
		if (IconButton(layout.LeftAction(8), "InventorySort", T("工具", "Tools"))) {
			CancelInventoryGesture();
			inventoryToolsOpen = true;
		}
		Rectangle trash = layout.At(512, 260, 44, 44);
		trashSlot[0] = p.trashItem;
		Slot(trashSlot, ItemSlot.Context.TrashItem, 0, trash);
		DrawSelectedItemAction();
	}

	private void Slot(Item[] items, int context, int index, Rectangle r, bool locked = false)
	{
		SetSlotScale(r);
		var slot = new InventoryGestureSlot(items, context, index, r, locked);
		gestureSlots.Add(slot);
		if (Hover(r)) {
			Main.LocalPlayer.mouseInterface = true;
			if (!used) {
				if (UseNativeInventoryClick) {
					if (!locked || Main.mouseItem.IsAir) ItemSlot.Handle(items, context, index);
					else ItemSlot.MouseHover(items, Math.Abs(context), index);
				}
				else {
					ItemSlot.MouseHover(items, Math.Abs(context), index);
					if (pointerPressed && !splitSource.HasValue) {
						if (ReferenceEquals(items, Main.LocalPlayer.inventory) && index < 50) {
							selectedSlot = index;
							Main.LocalPlayer.selectedItem = index;
						}
						BeginInventoryDrag(slot);
						if (!items[index].IsAir) OpenItemDetails(slot);
					}
				}
				used = true;
			}
		}
		if (Hover(r)) { Main.HoverItem = new Item(); Main.hoverItemName = ""; }
		DrawPhoneSlot(items[index], context, index, r, locked);
	}
	private static void SetSlotScale(Rectangle r) => Main.inventoryScale = r.Width / (float)TextureAssets.InventoryBack.Value.Width;
	private bool Hover(Rectangle r) => !suppressOpeningPointer && (touchInput || !pointerNeedsRelease) && !toolsBlockFrame && !PlayerInput.IgnoreMouseInterface && r.Contains(pointer.ToPoint());
	private bool Click(Rectangle r)
	{
		if (used || !pointerPressed || !Hover(r)) return false;
		used = true; Main.mouseLeftRelease = false;
		SoundEngine.PlaySound(SoundID.MenuTick);
		return true;
	}
	private static void Begin()
	{
		Main.spriteBatch.End();
		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
			DepthStencilState.None, RasterizerState.CullCounterClockwise);
	}
	private Texture2D Texture(string name)
	{
		if (!textures.TryGetValue(name, out var texture)) textures[name] = texture = ModContent.Request<Texture2D>("MobileJoystick/Assets/MobileInventory/" + name, AssetRequestMode.ImmediateLoad).Value;
		return texture;
	}
	private bool IconButton(Rectangle r, string icon, string hint, bool active = false, bool enabled = true)
	{
		string material = active ? "ToolSelected" : icon is "Favorite" or "Unfavorite" or "Sort" ? "Slot"
			: icon.StartsWith("QuickStack", StringComparison.Ordinal) || icon == "Settings" ? "SlotPink" : "Tool";
		InventoryButtonSurface(r, material, Color.White * .86f, enabled);
		if (icon.StartsWith("Category", StringComparison.Ordinal)) DrawCraftCategoryGlyph(int.Parse(icon.AsSpan(8)), r, enabled ? Color.White : Color.White * .3f);
		else InventoryGlyph(icon, r, enabled ? Color.White : Color.White * .3f);
		Tooltip(r, hint);
		return enabled && Click(r);
	}
	private bool ControlIcon(Rectangle r, string name, string hint)
	{
		Main.spriteBatch.Draw(ModContent.Request<Texture2D>("MobileJoystick/Assets/MobileControls/" + name, AssetRequestMode.ImmediateLoad).Value, r, Color.White);
		Tooltip(r, hint);
		return Click(r);
	}
	private void Tooltip(Rectangle r, string hint)
	{
		if (!Hover(r)) return;
		Main.LocalPlayer.mouseInterface = true;
		if (pointerDown) { heldHint = hint; heldHintBounds = r; heldHintExpires = InventoryUiTick + 45; }
	}
	private bool TextButton(Rectangle r, string label, bool active = false)
	{
		InventoryButtonSurface(r, active ? "ToolSelected" : "Tool", Color.White * .86f);
		Text(label, r, .75f);
		Tooltip(r, label);
		return Click(r);
	}
	private void Text(string text, Rectangle r, float size = .8f, Color? color = null)
	{
		var font = FontAssets.MouseText.Value;
		float scale = Math.Min(size * MobileUiMetrics.FontScale * layout.Scale, (r.Width - 4 * layout.Scale) / Math.Max(1, font.MeasureString(text).X));
		int top = int.MaxValue, bottom = int.MinValue;
		foreach (char c in text) if (!char.IsWhiteSpace(c) && font.SpriteCharacters.TryGetValue(c, out var g)) { top = Math.Min(top, g.Padding.Y); bottom = Math.Max(bottom, g.Padding.Y + g.Glyph.Height); }
		Vector2 pos = new(r.Center.X - font.MeasureString(text).X * scale / 2, r.Center.Y - (top == int.MaxValue ? font.LineSpacing / 2f : (top + bottom) / 2f) * scale);
		int edge = Math.Max(1, (int)layout.Scale);
		for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) if (x != 0 || y != 0)
			Main.spriteBatch.DrawString(font, text, pos + new Vector2(x * edge, y * edge), Color.Black, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
		Main.spriteBatch.DrawString(font, text, pos, color ?? Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
	}
}
