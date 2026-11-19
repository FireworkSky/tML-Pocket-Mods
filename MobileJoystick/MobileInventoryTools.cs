using System;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace MobileJoystick;

public partial class MobileInventorySystem
{
	private bool inventoryToolsOpen, toolsBlockFrame, toolsOpenAtFrameStart;
	private string heldHint;
	private Rectangle heldHintBounds;
	private ulong heldHintExpires;
	private string QuickStackGlyph => MobileJoystickConfig.Instance.QuickStackToNearbyChests ? "QuickStackNearby" : "QuickStackCurrent";

	private void QuickStackInventory()
	{
		if (MobileJoystickConfig.Instance.QuickStackToNearbyChests) Main.LocalPlayer.QuickStackAllChests();
		else if (Main.LocalPlayer.chest != -1) ChestUI.QuickStack(Terraria.DataStructures.ContainerTransferContext.FromUnknown(Main.LocalPlayer));
		Recipe.FindRecipes();
	}

	private void BeginInventoryToolsFrame()
	{
		toolsOpenAtFrameStart = inventoryToolsOpen;
		toolsBlockFrame = inventoryToolsOpen;
		if (!inventoryToolsOpen) return;
		used = true;
		if (pointerPressed && (!layout.InventoryTools.Contains(pointer.ToPoint()) || layout.LeftAction(8).Contains(pointer.ToPoint())))
			inventoryToolsOpen = false;
	}

	private void DrawInventoryTools(bool fresh)
	{
		toolsBlockFrame = false;
		if (!inventoryToolsOpen) return;
		// Opening and outside-dismissal presses cannot also act on the covered slots.
		used = !fresh || !toolsOpenAtFrameStart;
		MobileMenuFrame.Draw(Main.spriteBatch, Texture("Tool"), layout.InventoryTools, Color.White, layout.Scale, 6);
		DrawInventoryBuilderToggle(BuilderToggle.RulerLine, layout.InventoryTool(0), "Ruler", T("标尺", "Ruler"));
		DrawInventoryBuilderToggle(BuilderToggle.BlockSwap, layout.InventoryTool(1), BuilderToggle.BlockSwap.CurrentState == 0 ? "BlockSwapOn" : "BlockSwapOff", T("物块替换", "Block swap"));
		string hint = MobileJoystickConfig.Instance.QuickStackToNearbyChests
			? T("关闭相似堆叠至附近的宝箱", "Disable quick stack to nearby chests")
			: T("开启相似堆叠至附近的宝箱", "Enable quick stack to nearby chests");
		if (!IconButton(layout.InventoryTool(2), QuickStackGlyph, hint)) return;
		var config = MobileJoystickConfig.Instance;
		bool previous = config.QuickStackToNearbyChests;
		config.QuickStackToNearbyChests = !previous;
		try { config.SaveFromMobileMenu(); }
		catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
			config.QuickStackToNearbyChests = previous;
			Mod.Logger.Warn("Unable to save inventory quick-stack preference", error);
			Main.NewText(T("无法保存设置", "Unable to save settings"), Color.OrangeRed);
		}
	}

	private void DrawInventoryBuilderToggle(BuilderToggle toggle, Rectangle bounds, string glyph, string name)
	{
		bool enabled = toggle.Active();
		InventoryButtonSurface(bounds, "Tool", Color.White * .86f, enabled);
		InventoryGlyph(glyph, bounds, Color.White * (!enabled ? .3f : toggle.CurrentState == 0 ? 1f : .55f));
		string label = name + T("：", ": ") + (toggle.CurrentState == 0 ? T("开", "On") : T("关", "Off"));
		Tooltip(bounds, label);
		if (!enabled || used || !pointerPressed || !Hover(bounds)) return;
		used = true;
		Main.mouseLeftRelease = false;
		SoundStyle? sound = SoundID.MenuTick;
		if (toggle.OnLeftClick(ref sound)) {
			toggle.CurrentState = (toggle.CurrentState + 1) % toggle.NumberOfStates;
			SoundEngine.PlaySound(sound);
		}
	}

	private void DrawHeldInventoryHint()
	{
		if (string.IsNullOrEmpty(heldHint) || InventoryUiTick >= heldHintExpires) return;
		int width = Math.Min(layout.Width, (int)(FontAssets.MouseText.Value.MeasureString(heldHint).X * .8f * layout.Scale + 12 * layout.Scale));
		int x = Math.Clamp(heldHintBounds.Center.X - width / 2, 0, Math.Max(0, layout.Width - width));
		Text(heldHint, new Rectangle(x, heldHintBounds.Bottom + (int)(4 * layout.Scale), width, (int)(28 * layout.Scale)), .8f);
	}
}
