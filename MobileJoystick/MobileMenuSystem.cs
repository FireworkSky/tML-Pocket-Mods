using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Social.Steam;
using Terraria.Social;
using Terraria.UI;
using ReLogic.Content;
using ReLogic.Graphics;
using Terraria.ModLoader.Config;

namespace MobileJoystick;

// The options window retains Terraria's pause/update lifecycle; only its view
// and navigation are replaced. Coordinates here are physical client pixels.
public class MobileMenuSystem : ModSystem
{
	private enum Page { Pause, Settings, TouchEditor }
	private enum TouchEditorView { Overview, Edit, ActionPicker, CopyPicker }
	private Page page;
	private int category;
	private bool session, returningFromFancy, forceClose, modDirty, settingsDirty;
	private bool wasDown, down, pressed, released, consumed, waitForRelease;
	private Vector2 pointer, pressPosition, previousPointer;
	private int touchId = -1;
	private float scroll;
	private bool scrolling;
	private MobileSetting slider, dropdown;
	private Rectangle sliderBounds;
	private List<MobileSetting> items = new();
	private Action pendingDisplayChange;
	private Page fancyReturnPage;
	private int fancyReturnCategory;
	private int touchEditorTab;
	private Page touchEditorReturnPage;
	private int touchEditorReturnCategory;
	private TouchEditorView touchEditorView;
	private TouchEditorView pickerReturnView;
	// No placement tool is active until the user chooses one, as on the phone.
	private MobileCustomControlKind? addControlKind;
	private bool presetPickerOpen;
	private bool editorInputBlocked;
	private MobileCustomPreset editorDraft;
	private MobileControlEditorHistory editorHistory;
	private readonly Dictionary<int, MobileCustomPreset> pendingPresetCopies = new();
	private int selectedCustomPreset;
	private int selectedRadialSlot = -1;
	private bool pickingModKeybinds;
	private int modKeybindPage;
	private string selectedControlId;
	private bool editorDragging, editorSizing, editorGestureChanged;
	private Vector2 editorGestureStart;
	private float editorControlStartX, editorControlStartY;
	private static readonly MobileControlAction[] BindingActions = {
		MobileControlAction.Interact, MobileControlAction.SwapUseInteract, MobileControlAction.SmartCursor,
		MobileControlAction.SmartInteract, MobileControlAction.AimModeToggle, MobileControlAction.Jump,
		MobileControlAction.UseItem, MobileControlAction.Inventory, MobileControlAction.TargetLockToggle,
		MobileControlAction.Buff, MobileControlAction.Settings, MobileControlAction.CycleUseProfile,
		MobileControlAction.Heal, MobileControlAction.Grapple, MobileControlAction.Loadout1,
		MobileControlAction.Mana, MobileControlAction.QuickMount, MobileControlAction.Loadout2,
		MobileControlAction.AutoSelect, MobileControlAction.ZoomIn, MobileControlAction.Loadout3,
		MobileControlAction.Unbound, MobileControlAction.ZoomOut, MobileControlAction.SwitchLoadout
	};
	private bool draggingLeftStick, draggingRightStick;
	private Vector2 dragStartPointer;
	private Vector2 dragStartOffset;
	private readonly Dictionary<string, Texture2D> textures = new();
	private readonly RasterizerState clip = new() { ScissorTestEnable = true, CullMode = CullMode.None };
	private MobileMenuLayout layout;
	private Rectangle touchEditorPanel;
	private MobilePauseAction[] pauseActions = Array.Empty<MobilePauseAction>();
	private static string T(string zh, string en) => MobileSettingsCatalog.Text(zh, en);
	internal static bool EditingCustomControls {
		get {
			if (!Main.ingameOptionsWindow) return false;
			var menu = ModContent.GetInstance<MobileMenuSystem>();
			return menu.session && menu.page == Page.TouchEditor && menu.touchEditorTab == 2 && menu.touchEditorView != TouchEditorView.Overview;
		}
	}

	public override void Load()
	{
		if (Main.dedServ) return;
		On_IngameOptions.Open += Open;
		On_IngameOptions.Close += Close;
		On_IngameOptions.Draw += Draw;
		On_IngameFancyUI.Close += CloseFancy;
	}

	public override void Unload()
	{
		if (Main.dedServ) return;
		On_IngameOptions.Open -= Open;
		On_IngameOptions.Close -= Close;
		On_IngameOptions.Draw -= Draw;
		On_IngameFancyUI.Close -= CloseFancy;
		textures.Clear();
		clip.Dispose();
	}

	public override void OnWorldUnload()
	{
		session = returningFromFancy = false;
		slider = dropdown = null;
		draggingLeftStick = draggingRightStick = false;
		ResetCustomEditor();
	}

	public override void PostUpdateInput()
	{
		if (!session || !Main.ingameOptionsWindow) return;
		ReadPointer();
	}

	private void Open(On_IngameOptions.orig_Open orig)
	{
		orig();
		if (MobileJoystickConfig.Instance?.EnableMobileControls != true) return;
		session = true;
		page = Page.Pause;
		ResetInput();
	}

	private void Close(On_IngameOptions.orig_Close orig)
	{
		if (!session || forceClose) { orig(); return; }
		if (dropdown != null) { dropdown = null; return; }
		if (page != Page.Pause) { Back(); return; }
		if (!Save()) return;
		orig();
		Main.playerInventory = false;
		session = false;
		ResetInput();
	}

	private void CloseFancy(On_IngameFancyUI.orig_Close orig)
	{
		orig();
		if (!returningFromFancy || Main.gameMenu) return;
		returningFromFancy = false;
		IngameOptions.Open();
		// Preserve the current session even if its enable switch was turned off.
		session = true;
		page = Page.Pause;
		if (fancyReturnPage == Page.Settings) Settings(fancyReturnCategory);
		Main.ingameOptionsWindow = true;
		Main.playerInventory = false;
		ResetInput();
	}

	private void ShowFancy(Action open)
	{
		if (!Save()) return;
		fancyReturnPage = page;
		fancyReturnCategory = category;
		returningFromFancy = true;
		Main.ingameOptionsWindow = false;
		open();
		consumed = true;
	}

	private void Settings(int index)
	{
		category = index;
		page = Page.Settings;
		items = index == 8 ? MobileSettingsCatalog.ModConfigs(config => ShowFancy(() => config.Open(onClose: IngameFancyUI.Close)))
			: MobileSettingsCatalog.Create(index, () => ShowFancy(IngameFancyUI.OpenKeybinds), () => Settings(8), () => Settings(9), action => pendingDisplayChange = action);
		scroll = 0;
		slider = dropdown = null;
		consumed = true;
		ClickSound();
	}

	internal static void OpenTouchEditor()
		=> ModContent.GetInstance<MobileMenuSystem>().OpenTouchEditorPage();

	private void OpenTouchEditorPage()
	{
		MobileCustomControls.EnsureInitialized(MobileJoystickConfig.Instance);
		touchEditorReturnPage = page == Page.Settings ? Page.Settings : Page.Pause;
		touchEditorReturnCategory = category;
		page = Page.TouchEditor;
		touchEditorTab = MobileJoystickConfig.Instance?.ControlLayout switch {
			MobileControlLayoutMode.Advanced => 1,
			MobileControlLayoutMode.Custom => 2,
			_ => 0
		};
		ResetCustomEditor();
		draggingLeftStick = draggingRightStick = false;
		slider = dropdown = null;
		consumed = true;
		ClickSound();
	}

	private void Back()
	{
		if (!Save()) return;
		if (page == Page.Settings && category >= 8) { Settings(category == 8 ? 7 : 4); return; }
		if (page == Page.TouchEditor) {
			if (touchEditorView is TouchEditorView.ActionPicker or TouchEditorView.CopyPicker) {
				touchEditorView = pickerReturnView;
				selectedRadialSlot = -1;
				consumed = true;
				return;
			}
			if (touchEditorView == TouchEditorView.Edit) { DiscardCustomDraft(); consumed = true; return; }
			ResetCustomEditor();
			if (touchEditorReturnPage == Page.Settings) { Settings(touchEditorReturnCategory); return; }
			page = Page.Pause;
			consumed = true;
			ClickSound();
			return;
		}
		page = Page.Pause;
		slider = dropdown = null;
		consumed = true;
		ClickSound();
	}

	private bool Save()
	{
		try {
			if (modDirty) MobileJoystickConfig.Instance.SaveFromMobileMenu();
			if (settingsDirty && (!Main.SaveSettings() || !PlayerInput.Save()))
				throw new System.IO.IOException("Terraria could not save settings or the input profile.");
			modDirty = settingsDirty = false;
			return true;
		}
		catch (Exception ex) {
			Mod.Logger.Error("Could not save mobile menu settings", ex);
			Main.NewText(T("设置保存失败，请检查文件权限后重试。", "Settings could not be saved. Check file permissions and retry."), Color.OrangeRed);
			return false;
		}
	}

	private void Quit()
	{
		if (!Save()) return;
		SteamedWraps.StopPlaytimeTracking();
		SystemLoader.PreSaveAndQuit();
		forceClose = true;
		try { IngameOptions.Close(); }
		finally { forceClose = false; }
		session = false;
		Main.menuMode = 10;
		Main.gameMenu = true;
		WorldGen.SaveAndQuit();
	}

	private void ResetInput()
	{
		waitForRelease = true;
		wasDown = false;
		pressed = released = false;
		touchId = -1;
		slider = dropdown = null;
		scrolling = false;
	}

	private void ReadPointer()
	{
		down = MobileTouchInput.ReadUiPointer(ref touchId, ref pointer, out bool cancelled);
		if (cancelled) { ResetInput(); down = false; return; }
		bool justPressed = down && !wasDown;
		pressed |= justPressed;
		released |= !down && wasDown;
		wasDown = down;
		if (waitForRelease) {
			if (!down) waitForRelease = false;
			pressed = released = false;
		}
		if (justPressed) { pressPosition = pointer; scrolling = false; }
	}

	private bool Hit(Rectangle r)
	{
		if (editorInputBlocked || consumed || !released || scrolling || slider != null || !r.Contains(pointer.ToPoint()) || !r.Contains(pressPosition.ToPoint())) return false;
		consumed = true;
		ClickSound();
		return true;
	}

	private static void ClickSound() => SoundEngine.PlaySound(SoundID.MenuTick);

	private void Draw(On_IngameOptions.orig_Draw orig, Main main, SpriteBatch sb)
	{
		if (!session) { orig(main, sb); return; }
		consumed = false;
		Main.LocalPlayer.mouseInterface = true;
		Main.LocalPlayer.controlUseItem = Main.LocalPlayer.controlUseTile = false;
		PlayerInput.LockVanillaMouseScroll("MobileJoystick: Menu");
		pauseActions = MobilePauseMenu.Create(SocialAPI.Network?.CanInvite() == true, Main.LocalPlayer.difficulty == 3);
		layout = new MobileMenuLayout(PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight, page == Page.Settings || page == Page.TouchEditor,
			page == Page.Pause ? pauseActions.Length : 6);
		sb.End();
		Begin(sb);
		try {
			sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(0, 0, PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight), Color.Black * .38f);
			if (page != Page.TouchEditor) Nine(sb, "Panel", layout.Panel, Color.White, 12);
			if (page == Page.Settings) DrawSettings(sb);
			else if (page == Page.TouchEditor) DrawTouchEditor(sb);
			else DrawPause(sb);
		}
		finally {
			editorInputBlocked = false;
			if (released) { slider = null; scrolling = false; }
			pressed = released = false;
			previousPointer = pointer;
			Main.mouseLeftRelease = false;
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
				DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
		}
		Main.DrawCursor(Main.DrawThickCursor());
		Action displayChange = pendingDisplayChange;
		pendingDisplayChange = null;
		displayChange?.Invoke();
	}

	private static void Begin(SpriteBatch sb, RasterizerState raster = null) => sb.Begin(SpriteSortMode.Deferred,
		BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, raster ?? RasterizerState.CullCounterClockwise, null, Matrix.Identity);

	private void DrawPause(SpriteBatch sb)
	{
		foreach (MobilePauseAction action in pauseActions) {
			int i = Array.IndexOf(pauseActions, action);
			Rectangle r = layout.PauseButton(i);
			Nine(sb, "PauseButton", r, r.Contains(pointer.ToPoint()) ? new Color(230, 238, 255) : Color.White);
			sb.Draw(Texture("Pause" + action), layout.At(18, 20 + i * 60, 44, 40), Color.White);
			Label(sb, PauseLabel(action), layout.At(58, 12 + i * 60, 238, 54), 1.06f, true, display: true);
			if (!Hit(r)) continue;
			switch (action) {
				case MobilePauseAction.Resume: IngameOptions.Close(); break;
				case MobilePauseAction.Invite:
					if (SocialAPI.Network?.CanInvite() == true && Save()) {
						IngameOptions.Close();
						SocialAPI.Network.OpenInviteInterface();
					}
					break;
				case MobilePauseAction.Controls: OpenTouchEditorPage(); break;
				case MobilePauseAction.Settings: Settings(0); break;
				case MobilePauseAction.Journey:
					if (Save()) Main.LocalPlayer.ToggleCreativeMenu();
					break;
				case MobilePauseAction.Achievements: ShowFancy(IngameFancyUI.OpenAchievements); break;
				case MobilePauseAction.Bestiary: ShowFancy(() => { IngameFancyUI.OpenUIState(Main.BestiaryUI); Main.BestiaryUI.OnOpenPage(); }); break;
				case MobilePauseAction.SaveExit: Quit(); break;
			}
			break;
		}
	}

	private static string PauseLabel(MobilePauseAction action) => action switch {
		MobilePauseAction.Resume => T("回到游戏", "Resume"),
		MobilePauseAction.Invite => T("邀请", "Invite"),
		MobilePauseAction.Controls => T("游戏控制", "Game controls"),
		MobilePauseAction.Settings => T("设置", "Settings"),
		MobilePauseAction.Journey => T("旅行", "Journey"),
		MobilePauseAction.Achievements => T("成就", "Achievements"),
		MobilePauseAction.Bestiary => T("图鉴", "Bestiary"),
		_ => T("保存并离开", "Save & exit")
	};

	private void DrawTouchEditorLegacy(SpriteBatch sb)
	{
		float s = layout.Scale;
		int width = PlayerInput.RealScreenWidth;
		int height = PlayerInput.RealScreenHeight;
		sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(0, 0, width, height), Color.Black * .2f);
		touchEditorPanel = new((width - (int)(536 * s)) / 2, (int)(14 * s), (int)(536 * s), (int)(200 * s));
		Nine(sb, "Panel", touchEditorPanel, Color.White, 12);

		Rectangle simple = EditorAt(10, 10, 166, 44);
		Rectangle advanced = EditorAt(185, 10, 166, 44);
		Rectangle custom = EditorAt(360, 10, 166, 44);
		DrawEditorTab(sb, simple, "Tab5", T("简单", "Simple"), touchEditorTab == 0);
		DrawEditorTab(sb, advanced, "Tab4", T("高级", "Advanced"), touchEditorTab == 1);
		DrawEditorTab(sb, custom, "Tab6", T("自定义", "Custom"), touchEditorTab == 2);
		if (Hit(simple)) SelectControlLayout(MobileControlLayoutMode.Simple);
		if (Hit(advanced)) SelectControlLayout(MobileControlLayoutMode.Advanced);
		if (Hit(custom)) touchEditorTab = 2;

		if (touchEditorTab == 2) {
			DrawEditorPresetLegacy(sb);
		}
		else {
			string description = touchEditorTab == 0
				? T("使用双摇杆移动和瞄准，快捷功能使用独立按钮。", "Use two sticks to move and aim, with separate shortcut buttons.")
				: T("使用双摇杆进行移动和瞄准。适合专家和高阶玩法。", "Use two sticks to move and aim. Suitable for advanced play.");
			Label(sb, description, EditorAt(24, 66, 488, 48), .72f, true, new Color(220, 228, 246));
		}

		MobileControlLayout controls = MobileJoystickLayout.CreateControlsLayout(MobileJoystickConfig.Instance, width, height);
		if (touchEditorTab == 2) {
			UpdateTouchEditorDrag(controls);
			controls = MobileJoystickLayout.CreateControlsLayout(MobileJoystickConfig.Instance, width, height);
		}
		DrawTouchEditorControls(sb, controls, touchEditorTab == 2);

		Rectangle back = EditorAt(10, 145, 166, 44);
		Rectangle move = EditorAt(360, 145, 166, 44);
		DrawEditorFooterButton(sb, back, "Back", T("返回", "Back"));
		DrawEditorFooterButton(sb, move, "JoystickPad", T("移动", "Move"));
		if (Hit(back)) Back();
		if (Hit(move)) touchEditorTab = 2;
		if (touchEditorTab == 2) {
			Label(sb, T("自定义模式", "Custom mode"), EditorAt(184, 145, 168, 44), .68f, true, new Color(175, 191, 228));
		}
	}

	private void DrawTouchEditor(SpriteBatch sb)
	{
		float s = layout.Scale;
		int width = PlayerInput.RealScreenWidth;
		int height = PlayerInput.RealScreenHeight;
		bool editingCustom = touchEditorTab == 2 && touchEditorView != TouchEditorView.Overview;
		if (editingCustom) MobileInventorySystem.DrawControlEditorBackdrop(sb, width, height);
		editorInputBlocked = touchEditorView is TouchEditorView.ActionPicker or TouchEditorView.CopyPicker || presetPickerOpen;
		touchEditorPanel = new((width - (int)(536 * s)) / 2, (int)(14 * s), (int)(536 * s), (int)((editingCustom ? 126 : 200) * s));
		DrawEditorPanel(sb, touchEditorPanel);

		if (!editingCustom) {
			Rectangle simple = EditorAt(10, 10, 172, 44);
			Rectangle advanced = EditorAt(182, 10, 172, 44);
			Rectangle custom = EditorAt(354, 10, 172, 44);
			DrawEditorTab(sb, simple, "EditorSimple", T("简单", "Simple"), touchEditorTab == 0);
			DrawEditorTab(sb, advanced, "EditorAdvanced", T("高级", "Advanced"), touchEditorTab == 1);
			DrawEditorTab(sb, custom, "EditorCustom", T("自定义", "Custom"), touchEditorTab == 2);
			if (Hit(simple)) SelectControlLayout(MobileControlLayoutMode.Simple);
			if (Hit(advanced)) SelectControlLayout(MobileControlLayoutMode.Advanced);
			if (Hit(custom)) {
				touchEditorTab = 2;
				touchEditorView = TouchEditorView.Overview;
				selectedCustomPreset = Math.Clamp(MobileJoystickConfig.Instance?.ActiveCustomPresetIndex ?? 0, 0, MobileCustomControls.PresetCount - 1);
			}
		}
		else {
			DrawCustomEditorHeader(sb);
		}
		// Apply/discard and Edit change the draft while this frame is being drawn.
		// Draw the new view on the next frame instead of using the previous view's state.
		if (editingCustom != (touchEditorTab == 2 && touchEditorView != TouchEditorView.Overview)) return;

		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		MobileControlLayout controls;
		MobileCustomPreset previewPreset = null;
		if (touchEditorTab == 2) {
			if (touchEditorView == TouchEditorView.Overview) {
				DrawEditorPreset(sb);
				if (config?.CustomPresets != null && selectedCustomPreset < config.CustomPresets.Count)
					previewPreset = config.CustomPresets[selectedCustomPreset];
			}
			else previewPreset = editorDraft;
			controls = previewPreset == null
				? MobileJoystickLayout.CreateControlsLayout(config, width, height)
				: MobileJoystickLayout.CreateControlsLayout(config, width, height, previewPreset);
			if (editingCustom) {
				DrawCustomAddModes(sb, width, height);
				UpdateCustomEditorGestures(width, height);
				controls = MobileJoystickLayout.CreateControlsLayout(config, width, height, editorDraft);
				DrawCustomPreview(sb, controls, editorDraft);
				DrawCustomProperties(sb, width, height);
				editorInputBlocked = false;
				if (touchEditorView == TouchEditorView.ActionPicker) DrawActionPicker(sb, width, height);
				else if (touchEditorView == TouchEditorView.CopyPicker) DrawCopyPicker(sb, width, height);
			}
			else if (touchEditorView == TouchEditorView.Overview) {
				DrawCustomPreview(sb, controls, previewPreset);
				DrawCustomOverviewFooter(sb);
				Label(sb, T("试试这些控制键吧，哪里舒服挪到哪里，\n或者编辑一套只属于你的定制方案", "Try these controls and move them where they feel comfortable,\nor edit your own custom layout."),
					EditorScreenAt(-80, 206, 696, 58), .7f, true);
				if (presetPickerOpen) DrawCustomPresetPicker(sb);
			}
		}
		else {
			string description = touchEditorTab == 0
				? T("使用双摇杆移动和瞄准，快捷功能使用独立按钮。", "Use two sticks to move and aim, with separate shortcut buttons.")
				: T("使用双摇杆进行移动和瞄准。适合专家和高阶玩法。", "Use two sticks to move and aim. Suitable for advanced play.");
			Label(sb, description, EditorAt(24, 66, 488, 48), .72f, true, new Color(220, 228, 246));
			controls = MobileJoystickLayout.CreateControlsLayout(config, width, height);
			DrawTouchEditorControls(sb, controls, false);
			Rectangle back = EditorAt(10, 145, 166, 44);
			Rectangle move = EditorAt(360, 145, 166, 44);
			DrawEditorFooterButton(sb, back, "Back", T("返回", "Back"));
			DrawEditorFooterButton(sb, move, "EditorMove", T("移动", "Move"));
			if (Hit(back)) Back();
			if (Hit(move)) { touchEditorTab = 2; touchEditorView = TouchEditorView.Overview; }
		}
	}

	private void DrawEditorPreset(SpriteBatch sb)
	{
		Rectangle preset = EditorAt(62, 66, 230, 44);
		Rectangle edit = EditorAt(300, 66, 166, 44);
		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		string name = config?.CustomPresets != null && selectedCustomPreset < config.CustomPresets.Count
			? config.CustomPresets[selectedCustomPreset].Name : "Custom " + (selectedCustomPreset + 1);
		DrawEditorSurface(sb, preset);
		Label(sb, CustomPresetLabel(name), new Rectangle(preset.X, preset.Y, preset.Width - (int)(30 * layout.Scale), preset.Height), .76f, true, display: true);
		DrawEditorDropdownArrow(sb, preset);
		if (Hit(preset)) presetPickerOpen = !presetPickerOpen;
		DrawEditorToolSurface(sb, edit);
		Texture2D gear = Texture("EditorGear");
		if (gear != null) {
			int size = Math.Max(20, (int)(27 * layout.Scale));
			Rectangle icon = new(edit.X + (int)(12 * layout.Scale), edit.Center.Y - size / 2, size, size);
			sb.Draw(gear, icon, Color.White);
			Label(sb, T("编辑", "Edit"), new Rectangle(icon.Right, edit.Y, edit.Right - icon.Right, edit.Height), .76f, true, display: true);
		}
		else Label(sb, T("编辑", "Edit"), edit, .76f, true, display: true);
		if (Hit(edit)) BeginCustomEdit();
	}

	private void DrawCustomPresetPicker(SpriteBatch sb)
	{
		editorInputBlocked = false;
		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		for (int i = 0; i < MobileCustomControls.PresetCount; i++) {
			Rectangle option = EditorAt(62, 112 + i * 38, 230, 36);
			DrawEditorSurface(sb, option, i == selectedCustomPreset);
			string optionName = config?.CustomPresets != null && i < config.CustomPresets.Count ? config.CustomPresets[i].Name : "Custom " + (i + 1);
			Label(sb, CustomPresetLabel(optionName), option, .72f, true);
			if (Hit(option)) { selectedCustomPreset = i; presetPickerOpen = false; }
		}
		if (released && !consumed) { presetPickerOpen = false; consumed = true; }
	}

	private void DrawCustomOverviewFooter(SpriteBatch sb)
	{
		Rectangle back = EditorAt(10, 145, 166, 44);
		Rectangle move = EditorAt(360, 145, 166, 44);
		DrawEditorFooterButton(sb, back, "Back", T("返回", "Back"));
		DrawEditorFooterButton(sb, move, "EditorMove", T("移动", "Move"));
		if (Hit(back)) Back();
		if (Hit(move)) BeginCustomEdit();
	}

	private void DrawCustomEditorHeader(SpriteBatch sb)
	{
		Rectangle copy = EditorAt(10, 10, 52, 44);
		Rectangle title = EditorAt(70, 10, 340, 44);
		Rectangle undo = EditorAt(420, 10, 48, 44);
		Rectangle redo = EditorAt(474, 10, 52, 44);
		DrawEditorToolSurface(sb, copy);
		Texture2D copyIcon = Texture("EditorCopy");
		sb.Draw(copyIcon, new Rectangle(copy.Center.X - (int)(15 * layout.Scale), copy.Center.Y - (int)(15 * layout.Scale), (int)(30 * layout.Scale), (int)(30 * layout.Scale)), Color.White);
		DrawEditorSurface(sb, title);
		Texture2D titleIcon = Texture("WorldName");
		sb.Draw(titleIcon, new Rectangle(title.X + (int)(10 * layout.Scale), title.Center.Y - (int)(15 * layout.Scale), (int)(30 * layout.Scale), (int)(30 * layout.Scale)), Color.White);
		Label(sb, CustomPresetLabel(editorDraft?.Name ?? "Custom " + (selectedCustomPreset + 1)), new Rectangle(title.X + (int)(45 * layout.Scale), title.Y, title.Width - (int)(50 * layout.Scale), title.Height), .82f, true, display: true);
		DrawEditorToolSurface(sb, undo, editorHistory?.CanUndo == true);
		DrawEditorToolSurface(sb, redo, editorHistory?.CanRedo == true);
		Texture2D undoIcon = Texture("EditorUndo");
		Texture2D redoIcon = Texture("EditorRedo");
		sb.Draw(undoIcon, new Rectangle(undo.Center.X - (int)(14 * layout.Scale), undo.Center.Y - (int)(14 * layout.Scale), (int)(28 * layout.Scale), (int)(28 * layout.Scale)), Color.White * (editorHistory?.CanUndo == true ? 1f : .45f));
		sb.Draw(redoIcon, new Rectangle(redo.Center.X - (int)(14 * layout.Scale), redo.Center.Y - (int)(14 * layout.Scale), (int)(28 * layout.Scale), (int)(28 * layout.Scale)), Color.White * (editorHistory?.CanRedo == true ? 1f : .45f));
		if (Hit(copy)) { pickerReturnView = TouchEditorView.Edit; touchEditorView = TouchEditorView.CopyPicker; }
		if (editorHistory?.CanUndo == true && Hit(undo)) RestoreEditorHistory(editorHistory.Undo());
		if (editorHistory?.CanRedo == true && Hit(redo)) RestoreEditorHistory(editorHistory.Redo());

		Rectangle discard = EditorAt(10, 66, 184, 48);
		Rectangle apply = EditorAt(342, 66, 184, 48);
		DrawEditorFooterButton(sb, discard, "EditorDiscard", T("放弃", "Discard"));
		DrawEditorFooterButton(sb, apply, "EditorApply", T("应用", "Apply"));
		if (Hit(discard)) DiscardCustomDraft();
		if (Hit(apply)) ApplyCustomDraft();
	}

	private void DrawCustomAddModes(SpriteBatch sb, int width, int height)
	{
		Rectangle panel = EditorScreenAt(0, 136, 536, 62);
		DrawEditorPanel(sb, panel);
		Rectangle joystick = EditorScreenAt(10, 145, 172, 44);
		Rectangle button = EditorScreenAt(182, 145, 172, 44);
		Rectangle radial = EditorScreenAt(354, 145, 172, 44);
		DrawControlModeTab(sb, joystick, "EditorJoystick", T("摇杆", "Joystick"), addControlKind == MobileCustomControlKind.Joystick);
		DrawControlModeTab(sb, button, "EditorPlainButton", T("按钮", "Button"), addControlKind == MobileCustomControlKind.Button);
		DrawControlModeTab(sb, radial, "EditorRadial", T("径向菜单", "Radial menu"), addControlKind == MobileCustomControlKind.RadialMenu);
		if (Hit(joystick)) SelectCustomAddTool(MobileCustomControlKind.Joystick);
		if (Hit(button)) SelectCustomAddTool(MobileCustomControlKind.Button);
		if (Hit(radial)) SelectCustomAddTool(MobileCustomControlKind.RadialMenu);
		string instruction = addControlKind switch {
			MobileCustomControlKind.Joystick => T("点击任意位置添加新摇杆", "Tap anywhere to add a new joystick"),
			MobileCustomControlKind.Button => T("点击任意位置添加新按钮", "Tap anywhere to add a new button"),
			MobileCustomControlKind.RadialMenu => T("点击任意位置添加新径向菜单", "Tap anywhere to add a new radial menu"),
			_ => T("点按任意控制键即可进行编辑，或者按住并拖拽来令它移动", "Tap any control to edit it, or hold and drag to move it.")
		};
		Label(sb, instruction,
			EditorScreenAt(-80, 206, 696, 38), .7f, true);
	}

	private void SelectCustomAddTool(MobileCustomControlKind kind)
	{
		addControlKind = kind;
		selectedControlId = null;
		selectedRadialSlot = -1;
		editorDragging = editorSizing = editorGestureChanged = false;
	}

	private void DrawControlModeTab(SpriteBatch sb, Rectangle bounds, string iconName, string text, bool active)
	{
		DrawEditorSurface(sb, bounds, active);
		Texture2D icon = Texture(iconName);
		if (icon != null) {
			int size = Math.Max(20, (int)(28 * layout.Scale));
			sb.Draw(icon, new Rectangle(bounds.X + (int)(8 * layout.Scale), bounds.Center.Y - size / 2, size, size), Color.White);
		}
		Label(sb, text, new Rectangle(bounds.X + (int)(42 * layout.Scale), bounds.Y, bounds.Width - (int)(46 * layout.Scale), bounds.Height), .68f, true);
	}

	private void DrawCustomPreview(SpriteBatch sb, MobileControlLayout controls, MobileCustomPreset preset)
	{
		if (preset == null) return;
		foreach (MobileCustomControl control in preset.Controls) {
			float opacity = MobileJoystickConfig.Instance?.Opacity ?? .78f;
			float radius = MobileJoystickLayout.GetCustomControlRadius(control, controls.Scale);
			Vector2 center = MobileJoystickLayout.ClampCustomControlCenter(new(control.X * PlayerInput.RealScreenWidth,
				control.Y * PlayerInput.RealScreenHeight), radius, PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight);
			bool selected = touchEditorView == TouchEditorView.Edit && control.Id == selectedControlId;
			bool held = touchEditorView == TouchEditorView.Overview && PreviewControlHeld(center, radius);
			Color color = Color.White;
			if (held) opacity = 1f;
			if (control.Kind == MobileCustomControlKind.Joystick) {
				DrawControlTexture(sb, "JoystickPad", center, radius * 2f, color * opacity);
				Vector2 knobCenter = center + PreviewStickOffset(center, radius) * radius * .46f;
				string knob = control.JoystickRole switch { MobileJoystickRole.Move => "JoystickKnob", MobileJoystickRole.Grapple => "GrappleHook", MobileJoystickRole.Unbound => "ItemButtonFrame", _ => MobileControlArtwork.AimKnobTexture };
				if (selected) {
					DrawEditorSprite(sb, "EditorYellowCircle", center, radius, Color.White);
					DrawEditorSprite(sb, control.JoystickRole switch { MobileJoystickRole.Move => "EditorMove", MobileJoystickRole.Grapple => "EditorAimCross", MobileJoystickRole.Unbound => "EditorUnbound", _ => "EditorAimCross" }, center, radius * .65f, Color.White);
				} else DrawControlTexture(sb, knob, knobCenter, radius, color * opacity);
				if (control.JoystickRole == MobileJoystickRole.Aim && MobileJoystickInput.AimAndUseMode)
					MobileJoystickSystem.DrawItemIcon(sb, MobileJoystickInput.GetHeldItem(), knobCenter, radius * .65f, color * opacity);
				continue;
			}
			if (control.Kind == MobileCustomControlKind.RadialMenu && (selected || held)) {
				DrawEditorRadialPreview(sb, control, center, radius, selected);
				continue;
			}
			if (control.Kind == MobileCustomControlKind.RadialMenu && MobileControlArtwork.DrawButton(sb, MobileControlAction.RadialMenu, center, radius, color * opacity)) continue;
			if (control.Kind == MobileCustomControlKind.Button && MobileControlArtwork.DrawButton(sb, control.Action, center, radius, color * opacity, selected || held)) continue;
			string texture = string.IsNullOrWhiteSpace(control.TextureName)
				? MobileJoystickLayout.GetDefaultTexture(control.Kind == MobileCustomControlKind.RadialMenu ? MobileControlAction.RadialMenu : control.Action)
				: control.TextureName;
			if (control.Action == MobileControlAction.AimModeToggle) texture = MobileJoystickInput.AimAndUseMode ? "AimUseMode" : "AimOnlyMode";
			if (control.Action == MobileControlAction.TargetLockToggle) texture = MobileJoystickInput.TargetLockEnabled ? "TargetLockOff" : "TargetLockOn";
			if (control.Action == MobileControlAction.SmartCursor) texture = MobileJoystickInput.SmartCursorEnabled ? "SmartCursorOff" : "SmartCursor";
			if (control.Action is MobileControlAction.Interact or MobileControlAction.UseItem) texture = "ItemButtonFrame";
			DrawControlTexture(sb, texture, center, radius * 2f, color * opacity);
			if (selected || held) DrawSelectedEditorButton(sb, control.Action, center, radius);
			if (control.Action is MobileControlAction.Interact or MobileControlAction.UseItem)
				MobileJoystickSystem.DrawItemIcon(sb, control.Action == MobileControlAction.Interact
					? MobileJoystickInput.GetPriorityRightClickOrHeldItem() : MobileJoystickInput.GetHeldItem(), center, radius * 1.35f, color * opacity);
		}
	}

	private void DrawEditorSprite(SpriteBatch sb, string name, Vector2 center, float size, Color color)
	{
		Texture2D texture = Texture(name);
		sb.Draw(texture, center, null, color, 0f, texture.Size() * .5f,
			size / Math.Max(texture.Width, texture.Height), SpriteEffects.None, 0f);
	}

	private bool PreviewControlHeld(Vector2 center, float radius)
		=> down && !waitForRelease && !editorInputBlocked && !presetPickerOpen
			&& !touchEditorPanel.Contains(pressPosition.ToPoint())
			&& Vector2.DistanceSquared(pressPosition, center) <= radius * radius;

	private Vector2 PreviewStickOffset(Vector2 center, float radius)
	{
		if (touchEditorView != TouchEditorView.Overview || !PreviewControlHeld(center, radius) || radius <= 0f) return Vector2.Zero;
		Vector2 offset = (pointer - center) / radius;
		return offset.LengthSquared() > 1f ? Vector2.Normalize(offset) : offset;
	}

	private void DrawSelectedEditorButton(SpriteBatch sb, MobileControlAction action, Vector2 center, float radius)
	{
		string glyph = action switch {
			MobileControlAction.Jump => "EditorJumpGlyph",
			MobileControlAction.Grapple => "EditorGrappleGlyph",
			MobileControlAction.Mount or MobileControlAction.QuickMount => "EditorMountGlyph",
			_ => null
		};
		if (glyph == null && action is not (MobileControlAction.UseItem or MobileControlAction.Interact or MobileControlAction.Unbound)) return;
		DrawEditorSprite(sb, "EditorYellowCircle", center, radius * 2f, Color.White);
		if (glyph != null) DrawEditorSprite(sb, glyph, center, radius * 1.35f, Color.White);
	}

	private static Vector2 EditorRadialDirection(int slot) => slot switch {
		0 => -Vector2.UnitY, 1 => Vector2.UnitX, 2 => Vector2.UnitY, _ => -Vector2.UnitX
	};

	private void DrawEditorRadialPreview(SpriteBatch sb, MobileCustomControl control, Vector2 center, float radius, bool selected)
	{
		MobileControlArtwork.DrawRadialFrame(sb, center, radius, Color.White * .8f);
		for (int slot = 0; slot < 4; slot++) {
			Vector2 position = center + EditorRadialDirection(slot) * radius * 2.05f;
			MobileControlAction action = control.RadialActions[slot];
			if (!MobileControlArtwork.DrawActionIcon(sb, action, position, radius, Color.White))
				DrawControlTexture(sb, MobileJoystickLayout.GetDefaultTexture(action), position, radius, Color.White);
			Vector2 numberCenter = center + EditorRadialDirection(slot) * radius * 1.25f;
			Label(sb, (slot + 1).ToString(), new Rectangle((int)(numberCenter.X - radius / 2), (int)(numberCenter.Y - radius / 2),
				(int)radius, (int)radius), .6f, true);
			Rectangle hit = new((int)(position.X - radius * .6f), (int)(position.Y - radius * .6f), (int)(radius * 1.2f), (int)(radius * 1.2f));
			if (selected && Hit(hit)) OpenBindingPicker(slot);
		}
	}

	private Rectangle EditorScreenAt(float x, float y, float width, float height)
	{
		int screenWidth = PlayerInput.RealScreenWidth;
		int screenHeight = PlayerInput.RealScreenHeight;
		return new((screenWidth - (int)(536 * layout.Scale)) / 2 + (int)(x * layout.Scale),
			(int)(14 * layout.Scale) + (int)(y * layout.Scale),
			Math.Max(1, (int)(width * layout.Scale)), Math.Max(1, (int)(height * layout.Scale)));
	}

	private MobileCustomControl SelectedCustomControl()
		=> editorDraft?.Controls?.FirstOrDefault(control => control.Id == selectedControlId);

	private Rectangle CustomPropertyBounds(MobileCustomControl control)
	{
		float s = layout.Scale;
		int width = (int)(440 * s);
		int height = (int)((control.Kind == MobileCustomControlKind.RadialMenu ? 284 : control.Kind == MobileCustomControlKind.Joystick ? 158 : 208) * s);
		float radius = CustomControlRadius(control) * (control.Kind == MobileCustomControlKind.RadialMenu ? 2.8f : 1f);
		Vector2 center = MobileJoystickLayout.ClampCustomControlCenter(new(control.X * PlayerInput.RealScreenWidth,
			control.Y * PlayerInput.RealScreenHeight), radius, PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight);
		float cx = center.X;
		float cy = center.Y;
		int gap = Math.Max(4, (int)(8 * s));
		int left = cx < PlayerInput.RealScreenWidth * .5f ? (int)(cx + radius + gap) : (int)(cx - radius - gap - width);
		left = Math.Clamp(left, gap, Math.Max(gap, PlayerInput.RealScreenWidth - width - gap));
		int minimumTop = Math.Min(PlayerInput.RealScreenHeight - height - gap, (int)(14 * s + 198 * s + gap));
		int top = Math.Clamp((int)(cy - height * .5f), Math.Max(gap, minimumTop), Math.Max(gap, PlayerInput.RealScreenHeight - height - gap));
		return new Rectangle(left, top, width, height);
	}

	private Rectangle PropertyAt(Rectangle panel, float x, float y, float width, float height) => new(
		panel.X + (int)(x * layout.Scale), panel.Y + (int)(y * layout.Scale),
		Math.Max(1, (int)(width * layout.Scale)), Math.Max(1, (int)(height * layout.Scale)));

	private Rectangle CustomSizeSlider(Rectangle panel)
		=> PropertyAt(panel, 218, 22, 198, 20);

	private static float CustomControlRadius(MobileCustomControl control)
		=> MobileJoystickLayout.GetCustomControlRadius(control, MobileJoystickLayout.GetControlScale(
			MobileJoystickConfig.Instance, PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight));

	private static void ConstrainCustomControl(MobileCustomControl control, int width, int height)
	{
		Vector2 center = MobileJoystickLayout.ClampCustomControlCenter(new(control.X * width, control.Y * height),
			CustomControlRadius(control), width, height);
		control.X = center.X / Math.Max(1, width);
		control.Y = center.Y / Math.Max(1, height);
	}

	private static string JoystickRoleLabel(MobileJoystickRole role) => role switch {
		MobileJoystickRole.Move => T("移动", "Move"), MobileJoystickRole.Aim => T("瞄准", "Aim"),
		MobileJoystickRole.Grapple => T("抓钩瞄准", "Grapple aim"), _ => T("未绑定", "Unbound") };

	private void DrawCustomProperties(SpriteBatch sb, int width, int height)
	{
		MobileCustomControl control = SelectedCustomControl();
		if (control == null || touchEditorView != TouchEditorView.Edit) return;
		Rectangle panel = CustomPropertyBounds(control);
		DrawEditorPanel(sb, panel);
		DrawPropertyRow(sb, panel, 8, 46, T("大小：", "Size:"));
		Rectangle track = CustomSizeSlider(panel);
		float amount = (control.Size - MobileCustomControls.MinimumSize) / (MobileCustomControls.MaximumSize - MobileCustomControls.MinimumSize);
		Rectangle rail = new(track.X, track.Center.Y - (int)(7 * layout.Scale), track.Width, (int)(14 * layout.Scale));
		MobileMenuFrame.Draw(sb, Texture("EditorSliderTrack"), rail, Color.White, layout.Scale, 6);
		int knobWidth = Math.Max(1, (int)(25 * layout.Scale)), knobHeight = Math.Max(1, (int)(32 * layout.Scale));
		sb.Draw(Texture("PlayerSliderKnob"), new Rectangle(track.X + (int)(amount * track.Width) - knobWidth / 2,
			track.Center.Y - knobHeight / 2, knobWidth, knobHeight), Color.White);

		if (control.Kind == MobileCustomControlKind.Button) {
			Rectangle multitouch = PropertyAt(panel, 10, 58, 420, 42);
			Rectangle multiRow = PropertyAt(panel, 10, 58, 420, 42);
			DrawEditorSurface(sb, multiRow, true);
			DrawEditorSprite(sb, "EditorMultitouch", new Vector2(multiRow.X + 24 * layout.Scale, multiRow.Center.Y), 28 * layout.Scale, Color.White);
			Label(sb, T("多点触控：", "Multitouch:"), PropertyAt(panel, 54, 58, 220, 42), .68f);
			MobileMenuFrame.ToggleBounds(multitouch, layout.Scale, out Rectangle off, out Rectangle on);
			MobileMenuFrame.Draw(sb, Texture("EditorButton"), new Rectangle(off.X, off.Y, off.Width + on.Width, off.Height), Color.White, layout.Scale, 8);
			MobileMenuFrame.Draw(sb, Texture(control.Multitouch ? "ToggleOn" : "ToggleOff"), control.Multitouch ? on : off, Color.White, layout.Scale, 8);
			Label(sb, T("关闭", "Off"), off, .65f, true, control.Multitouch ? Color.Gray : Color.White);
			Label(sb, T("开启", "On"), on, .65f, true, control.Multitouch ? Color.White : Color.Gray);
			Rectangle action = PropertyAt(panel, 10, 106, 420, 42);
			DrawPropertyActionRow(sb, action, control.Action == MobileControlAction.ModKeybind ? MobileModKeybinds.Label(control.ModKeybind) : control.Action == MobileControlAction.Unbound ? T("未绑定", "Unbound") : ActionLabel(control.Action), control.Action);
			if (Hit(off) && control.Multitouch) { control.Multitouch = false; RecordCustomEdit(); }
			if (Hit(on) && !control.Multitouch) { control.Multitouch = true; RecordCustomEdit(); }
			if (Hit(action)) OpenBindingPicker(-1);
			DrawCustomDeleteRow(sb, panel, 154, 42);
		}
		else if (control.Kind == MobileCustomControlKind.Joystick) {
			Rectangle role = PropertyAt(panel, 10, 58, 420, 42);
			DrawPropertyRow(sb, panel, 58, 42, T("摇杆类型", "Joystick role"));
			Label(sb, JoystickRoleLabel(control.JoystickRole),
				PropertyAt(panel, 260, 58, 158, 42), .7f, true, Color.White);
			if (Hit(role)) {
                control.JoystickRole = (MobileJoystickRole)(((int)control.JoystickRole + 1) % Enum.GetValues<MobileJoystickRole>().Length);
                RecordCustomEdit();
			}
			DrawCustomDeleteRow(sb, panel, 106, 42);
		}
		else {
			for (int i = 0; i < 4; i++) {
				Rectangle action = PropertyAt(panel, 10, 58 + i * 42, 420, 38);
				MobileControlAction bound = control.RadialActions != null && i < control.RadialActions.Count ? control.RadialActions[i] : MobileControlAction.Unbound;
				DrawPropertyActionRow(sb, action, bound == MobileControlAction.ModKeybind ? MobileModKeybinds.Label(control.RadialModKeybinds.ElementAtOrDefault(i)) : bound == MobileControlAction.Unbound ? T("未绑定", "Unbound") : ActionLabel(bound), bound, i + 1);
				if (Hit(action)) OpenBindingPicker(i);
			}
			DrawCustomDeleteRow(sb, panel, 234, 42);
		}
	}

	private void DrawPropertyRow(SpriteBatch sb, Rectangle panel, int y, int height, string text)
	{
		Rectangle row = PropertyAt(panel, 10, y, 420, height);
		DrawEditorSurface(sb, row, true);
		Label(sb, text, PropertyAt(panel, 22, y, 180, height), .68f, false);
	}

	private void DrawPropertyActionRow(SpriteBatch sb, Rectangle row, string text, MobileControlAction action, int slot = -1)
	{
		DrawEditorSurface(sb, row, true);
		Texture2D icon = action == MobileControlAction.Unbound ? Texture("EditorUnbound") : ControlTexture(MobileJoystickLayout.GetDefaultTexture(action));
		if (!MobileControlArtwork.DrawActionIcon(sb, action, new Vector2(row.X + 24 * layout.Scale, row.Center.Y), 28 * layout.Scale, Color.White) && icon != null) {
			int size = Math.Max(18, (int)(28 * layout.Scale));
			sb.Draw(icon, new Rectangle(row.X + (int)(10 * layout.Scale), row.Center.Y - size / 2, size, size), Color.White);
		}
		if (slot >= 0) Label(sb, slot.ToString(), new Rectangle(row.X + (int)(48 * layout.Scale), row.Y, (int)(40 * layout.Scale), row.Height), .66f);
		int inset = slot >= 0 ? 88 : 44;
		Label(sb, text, new Rectangle(row.X + (int)(inset * layout.Scale), row.Y, row.Width - (int)((inset + 56) * layout.Scale), row.Height), .66f, right: true);
		DrawEditorDropdownArrow(sb, row);
	}

	private void DrawCustomDeleteRow(SpriteBatch sb, Rectangle panel, int y, int height)
	{
		Rectangle row = PropertyAt(panel, 10, y, 420, height);
		DrawEditorSurface(sb, row, true);
		Texture2D trash = Texture("MobileDelete");
		int size = Math.Max(20, (int)(28 * layout.Scale));
		sb.Draw(trash, new Rectangle(row.X + (int)(14 * layout.Scale), row.Center.Y - size / 2, size, size), Color.White);
		Label(sb, T("删除控制", "Delete control"), new Rectangle(row.X + (int)(48 * layout.Scale), row.Y, row.Width - (int)(58 * layout.Scale), row.Height), .68f, true);
		if (Hit(row)) DeleteSelectedControl();
	}

	private void UpdateCustomEditorGestures(int width, int height)
	{
		if (touchEditorView != TouchEditorView.Edit || editorDraft == null) return;
		MobileCustomControl selected = SelectedCustomControl();
		if (selected != null) {
			if (selected.Kind == MobileCustomControlKind.RadialMenu && pressed && !editorInputBlocked) {
				float radius = CustomControlRadius(selected);
				Vector2 center = MobileJoystickLayout.ClampCustomControlCenter(new(selected.X * width, selected.Y * height), radius, width, height);
				for (int slot = 0; slot < 4; slot++) {
					if (Vector2.DistanceSquared(pressPosition, center + EditorRadialDirection(slot) * radius * 2.05f) <= radius * radius * .36f) {
						consumed = true;
						return;
					}
				}
			}
			Rectangle panel = CustomPropertyBounds(selected);
			Rectangle sizeTrack = CustomSizeSlider(panel);
			if (!editorSizing && pressed && !consumed && sizeTrack.Contains(pressPosition.ToPoint())) {
				editorSizing = true;
				editorGestureChanged = false;
				consumed = true;
			}
			if (editorSizing) {
				if (down || released) {
					float amount = MathHelper.Clamp((pointer.X - sizeTrack.X) / Math.Max(1f, sizeTrack.Width), 0f, 1f);
					float size = MathHelper.Lerp(MobileCustomControls.MinimumSize, MobileCustomControls.MaximumSize, amount);
					if (Math.Abs(selected.Size - size) > 0.001f) {
						selected.Size = size;
						ConstrainCustomControl(selected, width, height);
						editorGestureChanged = true;
					}
				}
				consumed = true;
				if (released || !down) {
					if (editorGestureChanged) RecordCustomEdit();
					editorSizing = editorGestureChanged = false;
				}
				return;
			}
		}

		Rectangle addPanel = EditorScreenAt(0, 136, 536, 62);
		if (pressed && !consumed && !editorInputBlocked && !touchEditorPanel.Contains(pressPosition.ToPoint())
			&& !addPanel.Contains(pressPosition.ToPoint()) && (selected == null || !CustomPropertyBounds(selected).Contains(pressPosition.ToPoint()))) {
			MobileCustomControl hit = FindCustomControlAt(pressPosition, width, height);
			if (hit != null) {
				addControlKind = null;
				selectedControlId = hit.Id;
				BeginEditorGesture(hit, false);
				consumed = true;
			}
			else {
				MobileCustomControl added = CreateEditorControlAt(pointer, width, height);
				selectedControlId = null;
				if (added != null) {
					addControlKind = null;
					editorDraft.Controls.Add(added);
					selectedControlId = added.Id;
					editorGestureStart = pointer;
					editorControlStartX = added.X;
					editorControlStartY = added.Y;
					editorGestureChanged = true;
					editorDragging = true;
					consumed = true;
				}
			}
		}

		if (!editorDragging) return;
		MobileCustomControl moving = SelectedCustomControl();
		if (moving == null) { editorDragging = editorGestureChanged = false; return; }
		if (down) {
			Vector2 delta = pointer - editorGestureStart;
			if (editorGestureChanged || delta.LengthSquared() > 36f * layout.Scale * layout.Scale) {
				moving.X = MathHelper.Clamp(editorControlStartX + delta.X / Math.Max(1, width), 0f, 1f);
				moving.Y = MathHelper.Clamp(editorControlStartY + delta.Y / Math.Max(1, height), 0f, 1f);
				ConstrainCustomControl(moving, width, height);
				editorGestureChanged = true;
			}
			consumed = true;
		}
		if (released || !down) {
			if (editorGestureChanged) RecordCustomEdit();
			editorDragging = editorGestureChanged = false;
		}
	}

	private MobileCustomControl FindCustomControlAt(Vector2 position, int width, int height)
	{
		for (int i = editorDraft.Controls.Count - 1; i >= 0; i--) {
			MobileCustomControl control = editorDraft.Controls[i];
			float radius = CustomControlRadius(control);
			Vector2 center = MobileJoystickLayout.ClampCustomControlCenter(new(control.X * width, control.Y * height), radius, width, height);
			if (Vector2.DistanceSquared(position, center) <= radius * radius) return control;
		}
		return null;
	}

	private MobileCustomControl CreateEditorControlAt(Vector2 position, int width, int height)
	{
		if (addControlKind is not { } kind) return null;
		MobileCustomControl control = new() {
			Id = "control-" + Guid.NewGuid().ToString("N"),
			Kind = kind,
			Action = MobileControlAction.Unbound,
			TextureName = addControlKind == MobileCustomControlKind.RadialMenu ? "DirectionalArrowsOriginal" : "ItemButtonFrame",
			X = MathHelper.Clamp(position.X / Math.Max(1, width), 0f, 1f),
			Y = MathHelper.Clamp(position.Y / Math.Max(1, height), 0f, 1f),
			Size = 1f,
			Multitouch = true
		};
		if (addControlKind == MobileCustomControlKind.Joystick) {
			HashSet<MobileJoystickRole> roles = editorDraft.Controls
				.Where(existing => existing.Kind == MobileCustomControlKind.Joystick)
				.Select(existing => existing.JoystickRole).ToHashSet();
			control.JoystickRole = Enum.GetValues<MobileJoystickRole>().FirstOrDefault(role => !roles.Contains(role), MobileJoystickRole.Unbound);
			control.TextureName = control.JoystickRole == MobileJoystickRole.Move ? "JoystickKnob" : "AttackAimUse";
		}
		if (addControlKind == MobileCustomControlKind.RadialMenu)
			control.RadialActions = Enumerable.Repeat(MobileControlAction.Unbound, 4).ToList();
		ConstrainCustomControl(control, width, height);
		return control;
	}

	private void BeginEditorGesture(MobileCustomControl control, bool added)
	{
		editorGestureStart = pointer;
		editorControlStartX = control.X;
		editorControlStartY = control.Y;
		editorGestureChanged = added;
		editorDragging = true;
	}

	private void RecordCustomEdit()
	{
		if (editorDraft == null || editorHistory == null) return;
		MobileCustomControls.Validate(editorDraft);
		editorHistory.Record(editorDraft, pendingPresetCopies);
	}

	private void RestoreEditorHistory(MobileCustomPreset restored)
	{
		editorDraft = restored;
		pendingPresetCopies.Clear();
		foreach (KeyValuePair<int, MobileCustomPreset> pair in editorHistory.CurrentCopies) pendingPresetCopies[pair.Key] = pair.Value;
		if (SelectedCustomControl() == null) selectedControlId = null;
	}

	private void DeleteSelectedControl()
	{
		MobileCustomControl selected = SelectedCustomControl();
		if (selected == null) return;
		editorDraft.Controls.Remove(selected);
		selectedControlId = null;
		RecordCustomEdit();
	}

	private void OpenBindingPicker(int radialSlot)
	{
		selectedRadialSlot = radialSlot;
		pickingModKeybinds = false;
		modKeybindPage = 0;
		pickerReturnView = TouchEditorView.Edit;
		touchEditorView = TouchEditorView.ActionPicker;
	}

	private void SetCustomBinding(MobileControlAction action, string modKeybind = "")
	{
		MobileCustomControl selected = SelectedCustomControl();
		if (selected == null) return;
		if (selectedRadialSlot >= 0) {
			selected.RadialActions ??= new List<MobileControlAction>();
			while (selected.RadialActions.Count < 4) selected.RadialActions.Add(MobileControlAction.Unbound);
			selected.RadialActions[selectedRadialSlot] = action;
			selected.RadialModKeybinds ??= new();
			while (selected.RadialModKeybinds.Count < 4) selected.RadialModKeybinds.Add(string.Empty);
			selected.RadialModKeybinds[selectedRadialSlot] = modKeybind;
		}
		else {
			selected.Action = action;
			selected.ModKeybind = modKeybind;
			selected.TextureName = MobileJoystickLayout.GetDefaultTexture(action);
		}
		RecordCustomEdit();
		selectedRadialSlot = -1;
		touchEditorView = TouchEditorView.Edit;
	}

	private void DrawActionPicker(SpriteBatch sb, int width, int height)
	{
		Rectangle panel = new((width - (int)(590 * layout.Scale)) / 2, (height - (int)(410 * layout.Scale)) / 2,
			(int)(590 * layout.Scale), (int)(410 * layout.Scale));
		sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(0, 0, width, height), Color.Black * .28f);
		DrawEditorPanel(sb, panel);
		Label(sb, T("选择操作", "Choose action"), PropertyAt(panel, 8, 4, 574, 38), .82f, true, display: true);
		string[] modKeys = pickingModKeybinds ? MobileModKeybinds.Registered : Array.Empty<string>();
		int pages = Math.Max(1, (modKeys.Length + 23) / 24);
		modKeybindPage = Math.Clamp(modKeybindPage, 0, pages - 1);
		int count = pickingModKeybinds ? Math.Min(24, modKeys.Length - modKeybindPage * 24) : BindingActions.Length;
		if (pickingModKeybinds && count == 0) Label(sb, T("当前没有已加载的模组按键", "No mod keybinds are loaded"), PropertyAt(panel, 10, 100, 570, 60), .75f, true);
		for (int i = 0; i < count; i++) {
			int column = i % 3;
			int row = i / 3;
			Rectangle option = PropertyAt(panel, 8 + column * 191, 43 + row * 40, 184, 36);
			MobileControlAction action = pickingModKeybinds ? MobileControlAction.ModKeybind : BindingActions[i];
			string modKey = pickingModKeybinds ? modKeys[modKeybindPage * 24 + i] : "";
			bool active = SelectedCustomControl() is { } selected && (selectedRadialSlot >= 0
				? selected.RadialActions[selectedRadialSlot] : selected.Action) == action
				&& (action != MobileControlAction.ModKeybind || (selectedRadialSlot >= 0
					? selected.RadialModKeybinds.ElementAtOrDefault(selectedRadialSlot) : selected.ModKeybind) == modKey);
			string material = action is MobileControlAction.Grapple or MobileControlAction.QuickMount ? "EditorActionGreen"
				: action == MobileControlAction.Unbound ? "EditorActionUnbound"
				: i % 3 == 0 ? "EditorSelected" : "EditorActionTeal";
			MobileMenuFrame.Draw(sb, Texture(active || EditorPressed(option) ? "EditorPressed" : material), option, Color.White, layout.Scale, 8,
				new Color(185, 185, 215));
			{
				Texture2D icon = ControlTexture(MobileJoystickLayout.GetDefaultTexture(action));
				if (!MobileControlArtwork.DrawActionIcon(sb, action, new Vector2(option.X + 20 * layout.Scale, option.Center.Y), 27 * layout.Scale, Color.White) && icon != null) {
					int size = Math.Max(17, (int)(27 * layout.Scale));
					sb.Draw(icon, new Rectangle(option.X + (int)(7 * layout.Scale), option.Center.Y - size / 2, size, size), Color.White);
				}
			}
			Label(sb, pickingModKeybinds ? MobileModKeybinds.Label(modKey) : ActionLabel(action), new Rectangle(option.X + (int)(38 * layout.Scale), option.Y,
				option.Width - (int)(48 * layout.Scale), option.Height), .64f, right: true);
			if (Hit(option)) { SetCustomBinding(action, modKey); return; }
		}
        Rectangle toggle = PropertyAt(panel, 10, 365, 192, 34);
        DrawEditorFooterButton(sb, toggle, "Settings", pickingModKeybinds ? T("基础操作", "Basic actions") : T("模组按键", "Mod keybinds"));
        if (Hit(toggle)) { pickingModKeybinds = !pickingModKeybinds; modKeybindPage = 0; }
        if (pickingModKeybinds) {
            Rectangle prev = PropertyAt(panel, 388, 365, 76, 34), next = PropertyAt(panel, 490, 365, 76, 34);
            DrawEditorFooterButton(sb, prev, "Back", "<");
            DrawEditorFooterButton(sb, next, "Back", ">");
            if (Hit(prev)) modKeybindPage = Math.Max(0, modKeybindPage - 1);
            if (Hit(next)) modKeybindPage = Math.Min(pages - 1, modKeybindPage + 1);
        }
        Rectangle cancel = PropertyAt(panel, 230, 365, 130, 34);
		DrawEditorFooterButton(sb, cancel, "Back", T("返回", "Back"));
		if (Hit(cancel)) { selectedRadialSlot = -1; touchEditorView = pickerReturnView; }
	}

	private void DrawCopyPicker(SpriteBatch sb, int width, int height)
	{
		Rectangle panel = new((width - (int)(380 * layout.Scale)) / 2, (height - (int)(230 * layout.Scale)) / 2,
			(int)(380 * layout.Scale), (int)(230 * layout.Scale));
		sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(0, 0, width, height), Color.Black * .28f);
		Nine(sb, "Panel", panel, Color.White, 12);
		Label(sb, T("复制到方案", "Copy to preset"), PropertyAt(panel, 8, 4, 364, 38), .82f, true, display: true);
		for (int i = 0; i < MobileCustomControls.PresetCount; i++) {
			Rectangle option = PropertyAt(panel, 10 + (i % 2) * 181, 48 + (i / 2) * 60, 174, 52);
			DrawEditorSurface(sb, option, i == selectedCustomPreset);
			Texture2D icon = Texture("WorldName");
			sb.Draw(icon, new Rectangle(option.X + (int)(8 * layout.Scale), option.Center.Y - (int)(14 * layout.Scale), (int)(28 * layout.Scale), (int)(28 * layout.Scale)), Color.White);
			MobileJoystickConfig config = MobileJoystickConfig.Instance;
			string name = config?.CustomPresets != null && i < config.CustomPresets.Count ? config.CustomPresets[i].Name : "Custom " + (i + 1);
			Label(sb, CustomPresetLabel(name), new Rectangle(option.X + (int)(40 * layout.Scale), option.Y, option.Width - (int)(44 * layout.Scale), option.Height), .68f, true);
			if (!Hit(option)) continue;
			if (i != selectedCustomPreset && editorDraft != null) {
				MobileCustomPreset copy = MobileCustomControls.Clone(editorDraft);
				copy.Name = name;
				pendingPresetCopies[i] = copy;
				RecordCustomEdit();
			}
			touchEditorView = pickerReturnView;
			return;
		}
		Rectangle cancel = PropertyAt(panel, 125, 176, 130, 36);
		DrawEditorFooterButton(sb, cancel, "Back", T("取消", "Cancel"));
		if (Hit(cancel)) touchEditorView = pickerReturnView;
	}

	private static string ActionLabel(MobileControlAction action) => action switch {
		MobileControlAction.Interact => T("交互", "Interact"),
		MobileControlAction.SwapUseInteract => T("切换使用/交互", "Swap Use/Interact"),
		MobileControlAction.SmartCursor => T("智能光标", "Smart Cursor"),
		MobileControlAction.SmartInteract => T("智能交互", "Smart Interact"),
		MobileControlAction.AimModeToggle => T("瞄准模式", "Aim Mode"),
		MobileControlAction.Jump => T("跳跃", "Jump"),
		MobileControlAction.UseItem => T("使用物品", "Use Item"),
		MobileControlAction.Inventory => T("物品栏", "Inventory"),
		MobileControlAction.TargetLockToggle => T("锁定", "Lock"),
		MobileControlAction.Buff => T("快速增益", "Quick Buff"),
		MobileControlAction.Settings => T("菜单", "Menu"),
		MobileControlAction.CycleUseProfile => T("循环使用配置", "Cycle Use Profile"),
		MobileControlAction.Heal => T("快速治疗", "Quick Heal"),
		MobileControlAction.Grapple => T("抓钩", "Grapple"),
		MobileControlAction.Loadout1 => T("配装 1", "Loadout 1"),
		MobileControlAction.Mana => T("快速回蓝", "Quick Mana"),
		MobileControlAction.QuickMount => T("快速坐骑", "Quick Mount"),
		MobileControlAction.Loadout2 => T("配装 2", "Loadout 2"),
		MobileControlAction.AutoSelect => T("自动选择", "Auto Select"),
		MobileControlAction.ZoomIn => T("放大", "Zoom In"),
		MobileControlAction.Loadout3 => T("配装 3", "Loadout 3"),
		MobileControlAction.Unbound => T("取消绑定", "Unbound"),
		MobileControlAction.ZoomOut => T("缩小", "Zoom Out"),
		MobileControlAction.SwitchLoadout => T("切换配装", "Switch Loadout"),
		MobileControlAction.Mount => T("快速坐骑", "Quick Mount"),
		MobileControlAction.QuickPotions => T("快速药水", "Quick Potions"),
		MobileControlAction.Zoom => T("缩放", "Zoom"),
		MobileControlAction.Map => T("地图", "Map"),
		MobileControlAction.MapStyle => T("地图样式", "Map Style"),
		MobileControlAction.Chat => T("聊天", "Chat"),
		MobileControlAction.RadialMenu => T("径向菜单", "Radial Menu"),
		_ => action.ToString()
	};

	private void ApplyCustomDraft()
	{
		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		if (config == null || editorDraft == null) return;
		MobileCustomControls.Validate(editorDraft);
		Dictionary<int, MobileCustomPreset> originals = new();
		foreach (int index in pendingPresetCopies.Keys.Append(selectedCustomPreset).Distinct())
			originals[index] = MobileCustomControls.Clone(config.CustomPresets[index]);
		MobileControlLayoutMode oldMode = config.ControlLayout;
		int oldIndex = config.ActiveCustomPresetIndex;
		bool wasDirty = modDirty;
		foreach (KeyValuePair<int, MobileCustomPreset> pair in pendingPresetCopies)
			config.CustomPresets[pair.Key] = MobileCustomControls.Clone(pair.Value);
		config.CustomPresets[selectedCustomPreset] = MobileCustomControls.Clone(editorDraft);
		config.ActiveCustomPresetIndex = selectedCustomPreset;
		config.ControlLayout = MobileControlLayoutMode.Custom;
		modDirty = true;
		if (!Save()) {
			foreach (KeyValuePair<int, MobileCustomPreset> pair in originals) config.CustomPresets[pair.Key] = pair.Value;
			config.ActiveCustomPresetIndex = oldIndex;
			config.ControlLayout = oldMode;
			modDirty = wasDirty;
			return;
		}
		ResetCustomEditor();
	}

	private void DiscardCustomDraft()
	{
		addControlKind = null;
		editorDraft = null;
		editorHistory = null;
		pendingPresetCopies.Clear();
		selectedControlId = null;
		selectedRadialSlot = -1;
		editorDragging = editorSizing = editorGestureChanged = false;
		touchEditorView = TouchEditorView.Overview;
		ClickSound();
	}

	private void BeginCustomEdit()
	{
		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		if (config == null) return;
		MobileCustomControls.EnsureInitialized(config);
		selectedCustomPreset = Math.Clamp(selectedCustomPreset, 0, MobileCustomControls.PresetCount - 1);
		editorDraft = MobileCustomControls.Clone(config.CustomPresets[selectedCustomPreset]);
		editorHistory = new MobileControlEditorHistory(editorDraft);
		pendingPresetCopies.Clear();
		selectedControlId = null;
		selectedRadialSlot = -1;
		addControlKind = null;
		presetPickerOpen = false;
		touchEditorView = TouchEditorView.Edit;
		ClickSound();
	}

	private void ResetCustomEditor()
	{
		addControlKind = null;
		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		touchEditorView = TouchEditorView.Overview;
		pickerReturnView = TouchEditorView.Overview;
		selectedCustomPreset = Math.Clamp(config?.ActiveCustomPresetIndex ?? 0, 0, MobileCustomControls.PresetCount - 1);
		selectedRadialSlot = -1;
		selectedControlId = null;
		editorDraft = null;
		editorHistory = null;
		pendingPresetCopies.Clear();
		presetPickerOpen = false;
		editorDragging = editorSizing = editorGestureChanged = false;
	}

	private void SelectControlLayout(MobileControlLayoutMode mode)
	{
		touchEditorTab = mode == MobileControlLayoutMode.Advanced ? 1 : 0;
		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		if (config != null && config.ControlLayout != mode) {
			config.ControlLayout = mode;
			modDirty = true;
		}
	}

	private Rectangle EditorAt(float x, float y, float width, float height) => new(
		touchEditorPanel.X + (int)(x * layout.Scale), touchEditorPanel.Y + (int)(y * layout.Scale),
		Math.Max(1, (int)(width * layout.Scale)), Math.Max(1, (int)(height * layout.Scale)));

	private bool EditorPressed(Rectangle bounds)
		=> !editorInputBlocked && down && !waitForRelease && !scrolling && bounds.Contains(pressPosition.ToPoint()) && bounds.Contains(pointer.ToPoint());

	private static string CustomPresetLabel(string name)
	{
		for (int i = 1; i <= MobileCustomControls.PresetCount; i++)
			if (name == "Custom " + i) return T("自定义 ", "Custom ") + i;
		return name;
	}

	private void DrawEditorDropdownArrow(SpriteBatch sb, Rectangle row)
	{
		MobileMenuFrame.DropdownDivider(sb, row, layout.Scale);
		Texture2D arrow = Texture("WorldDropdown");
		sb.Draw(arrow, new Vector2(row.Right - 22 * layout.Scale, row.Center.Y), null, Color.White, -MathHelper.PiOver2,
			arrow.Size() * .5f, 20 * layout.Scale / Math.Max(arrow.Width, arrow.Height), SpriteEffects.None, 0f);
	}

	private void DrawEditorSurface(SpriteBatch sb, Rectangle bounds, bool selected = false, bool enabled = true)
	{
		bool held = enabled && EditorPressed(bounds);
		string texture = held ? "EditorPressed" : selected ? "EditorSelected" : "EditorButton";
		MobileMenuFrame.Draw(sb, Texture(texture), bounds, enabled ? Color.White : Color.White * .5f, layout.Scale, 8,
			new Color(185, 185, 215) * (enabled ? 1f : .5f));
	}

	private void DrawEditorToolSurface(SpriteBatch sb, Rectangle bounds, bool enabled = true)
	{
		MobileMenuFrame.Draw(sb, Texture(enabled && EditorPressed(bounds) ? "EditorPressed" : "EditorToolButton"),
			bounds, Color.White, layout.Scale, 8);
	}

	private void DrawEditorPanel(SpriteBatch sb, Rectangle bounds)
		=> MobileMenuFrame.Draw(sb, Texture("EditorSelected"), bounds, new Color(130, 150, 120, 205), layout.Scale, 8);

	private void DrawEditorTab(SpriteBatch sb, Rectangle bounds, string icon, string text, bool active)
	{
		DrawEditorSurface(sb, bounds, active);
		Texture2D tabIcon = Texture(icon);
		int size = Math.Max(20, (int)(30 * layout.Scale));
		Rectangle iconBounds = new(bounds.X + (int)(8 * layout.Scale), bounds.Center.Y - size / 2, size, size);
		sb.Draw(tabIcon, iconBounds, active ? Color.White : Color.White * .65f);
		Label(sb, text, new Rectangle(iconBounds.Right, bounds.Y, bounds.Right - iconBounds.Right - (int)(5 * layout.Scale), bounds.Height), .75f, true, display: true);
	}

	private void DrawEditorFooterButton(SpriteBatch sb, Rectangle bounds, string icon, string text)
	{
		DrawEditorSurface(sb, bounds);
		Texture2D buttonIcon = ModContent.HasAsset($"MobileJoystick/Assets/MobileMenu/{icon}") ? Texture(icon) : ControlTexture(icon);
		if (buttonIcon != null) {
			int size = Math.Max(20, (int)(28 * layout.Scale));
			Rectangle iconBounds = new(bounds.X + (int)(10 * layout.Scale), bounds.Center.Y - size / 2, size, size);
			sb.Draw(buttonIcon, iconBounds, Color.White);
			Label(sb, text, new Rectangle(iconBounds.Right, bounds.Y, bounds.Right - iconBounds.Right - (int)(8 * layout.Scale), bounds.Height), .76f, true, display: true);
		}
		else Label(sb, text, bounds, .76f, true, display: true);
	}

	private void DrawEditorPresetLegacy(SpriteBatch sb)
	{
		Rectangle preset = EditorAt(62, 66, 230, 44);
		Rectangle edit = EditorAt(300, 66, 226, 44);
		Nine(sb, "Button", preset, new Color(119, 132, 172));
		Label(sb, T("自定义 1", "Custom 1"), new Rectangle(preset.X, preset.Y, preset.Width - (int)(30 * layout.Scale), preset.Height), .76f, true);
		Label(sb, ">", new Rectangle(preset.Right - (int)(30 * layout.Scale), preset.Y, (int)(25 * layout.Scale), preset.Height), .9f, true, Color.White * .85f);
		Nine(sb, "Button", edit, new Color(130, 190, 211));
		Texture2D gear = ControlTexture("Settings");
		if (gear != null) {
			int size = Math.Max(20, (int)(27 * layout.Scale));
			Rectangle icon = new(edit.X + (int)(12 * layout.Scale), edit.Center.Y - size / 2, size, size);
			sb.Draw(gear, icon, Color.White);
			Label(sb, T("编辑", "Edit"), new Rectangle(icon.Right, edit.Y, edit.Right - icon.Right, edit.Height), .76f, true, display: true);
		}
		else Label(sb, T("编辑", "Edit"), edit, .76f, true, display: true);
	}

	private void DrawTouchEditorControls(SpriteBatch sb, MobileControlLayout controls, bool editable)
	{
		Color color = Color.White * (MobileJoystickConfig.Instance?.Opacity ?? .78f);
		DrawControlTexture(sb, "JoystickPad", controls.JoystickCenter, controls.JoystickRadius * 2f, color);
		DrawControlTexture(sb, "JoystickKnob", controls.JoystickCenter + PreviewStickOffset(controls.JoystickCenter, controls.JoystickRadius) * controls.JoystickRadius * .46f, controls.JoystickRadius, color);
		if (controls.AttackRadius > 0f) {
			DrawControlTexture(sb, "JoystickPad", controls.AttackCenter, controls.AttackRadius * 2f, color);
			Vector2 knob = controls.AttackCenter + PreviewStickOffset(controls.AttackCenter, controls.AttackRadius) * controls.AttackRadius * .46f;
			DrawControlTexture(sb, MobileControlArtwork.AimKnobTexture, knob, controls.AttackRadius, color);
			if (MobileJoystickInput.AimAndUseMode)
				MobileJoystickSystem.DrawItemIcon(sb, MobileJoystickInput.GetHeldItem(), knob, controls.AttackRadius * .65f, color);
		}
		foreach (MobileControlButton button in controls.Buttons) {
			if (button.Action is MobileControlAction.Inventory or MobileControlAction.Settings) continue;
			string texture = button.TextureName;
			if (MobileControlArtwork.DrawButton(sb, button.Action, button.Center, button.Radius, color, PreviewControlHeld(button.Center, button.Radius))) continue;
			if (button.Action is MobileControlAction.Interact or MobileControlAction.UseItem) texture = "ItemButtonFrame";
			DrawControlTexture(sb, texture, button.Center, button.Radius * 2f, color * .72f);
			if (PreviewControlHeld(button.Center, button.Radius)) DrawSelectedEditorButton(sb, button.Action, button.Center, button.Radius);
			if (button.Action is MobileControlAction.Interact or MobileControlAction.UseItem)
				MobileJoystickSystem.DrawItemIcon(sb, button.Action == MobileControlAction.Interact
					? MobileJoystickInput.GetPriorityRightClickOrHeldItem() : MobileJoystickInput.GetHeldItem(), button.Center, button.Radius * 1.35f, color);
		}
		if (editable) {
			DrawControlLabel(sb, T("移动", "Move"), controls.JoystickCenter, controls.JoystickRadius);
			if (controls.AttackRadius > 0f) DrawControlLabel(sb, T("瞄准", "Aim"), controls.AttackCenter, controls.AttackRadius);
		}
	}

	private void DrawControlLabel(SpriteBatch sb, string text, Vector2 center, float radius)
	{
		Rectangle bounds = new((int)(center.X - radius), (int)(center.Y - radius), (int)(radius * 2f), (int)(radius * 2f));
		Label(sb, text, bounds, .55f, true, Color.White * .9f);
	}

	private void DrawControlTexture(SpriteBatch sb, string name, Vector2 center, float maxSize, Color color)
	{
		Texture2D texture = ControlTexture(name);
		if (texture == null) return;
		float scale = maxSize / Math.Max(texture.Width, texture.Height);
		sb.Draw(texture, center, null, color, 0f, new Vector2(texture.Width * .5f, texture.Height * .5f), scale, SpriteEffects.None, 0f);
	}

	private static Texture2D ControlTexture(string name)
	{
		return ModContent.RequestIfExists<Texture2D>($"MobileJoystick/Assets/MobileControls/{name}", out var asset,
			AssetRequestMode.ImmediateLoad) ? asset.Value : null;
	}

	private void UpdateTouchEditorDrag(MobileControlLayout controls)
	{
		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		if (config == null) return;
		if (pressed && !consumed) {
			if (Vector2.DistanceSquared(pointer, controls.JoystickCenter) <= controls.JoystickRadius * controls.JoystickRadius) {
				draggingLeftStick = true;
				dragStartPointer = pointer;
				dragStartOffset = new Vector2(config.LeftStickOffsetX, config.LeftStickOffsetY);
				consumed = true;
			}
			else if (Vector2.DistanceSquared(pointer, controls.AttackCenter) <= controls.AttackRadius * controls.AttackRadius) {
				draggingRightStick = true;
				dragStartPointer = pointer;
				dragStartOffset = new Vector2(config.RightStickOffsetX, config.RightStickOffsetY);
				consumed = true;
			}
		}

		if (draggingLeftStick || draggingRightStick) {
			Vector2 offset = dragStartOffset + pointer - dragStartPointer;
			if (draggingLeftStick) {
				config.LeftStickOffsetX = Math.Clamp(offset.X, -800f, 800f);
				config.LeftStickOffsetY = Math.Clamp(offset.Y, -800f, 800f);
			}
			else {
				config.RightStickOffsetX = Math.Clamp(offset.X, -800f, 800f);
				config.RightStickOffsetY = Math.Clamp(offset.Y, -800f, 800f);
			}
			modDirty = true;
			consumed = true;
			if (released || !down) {
				draggingLeftStick = draggingRightStick = false;
				Save();
			}
		}
	}

	private void DrawSettings(SpriteBatch sb)
	{
		Heading(sb, MobileSettingsCatalog.Heading(category));
		int activeTab = category == 8 ? 7 : category == 9 ? 4 : category;
		for (int i = 0; i < 8; i++) {
			Rectangle r = layout.Tab(i);
			if (i < 7) sb.Draw(Texture("Tab" + i), r, i == activeTab ? Color.White : Color.White * .65f);
			else {
				Nine(sb, "Button", r, i == activeTab ? Color.White : Color.White * .65f);
				Texture2D icon = ModContent.Request<Texture2D>("MobileJoystick/Assets/MobileControls/Settings", AssetRequestMode.ImmediateLoad).Value;
				Rectangle ir = new(r.Center.X - (int)(14 * layout.Scale), r.Center.Y - (int)(14 * layout.Scale), (int)(28 * layout.Scale), (int)(28 * layout.Scale));
				sb.Draw(icon, ir, Color.White);
			}
			if (i == activeTab) sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(r.X + 3, r.Bottom - (int)(3 * layout.Scale), r.Width - 6, Math.Max(2,(int)(3 * layout.Scale))), new Color(212, 220, 255));
			if (dropdown == null && Hit(r)) { Settings(i); return; }
		}

		Rectangle viewport = layout.Viewport;
		float maximum = Math.Max(0, items.Count * 52 - 326);
		if (dropdown == null && slider == null && viewport.Contains(pointer.ToPoint())) {
			scroll -= PlayerInput.ScrollWheelDelta / 120f * 40;
			if (down && !pressed && viewport.Contains(pressPosition.ToPoint()) && Math.Abs(pointer.Y - pressPosition.Y) > 8 * layout.Scale) scrolling = true;
			if (scrolling && down) scroll -= (pointer.Y - previousPointer.Y) / layout.Scale;
		}
		scroll = MathHelper.Clamp(scroll, 0, maximum);
		if (slider != null && down) Change(slider, MathHelper.Lerp(slider.Min, slider.Max, MathHelper.Clamp((pointer.X - sliderBounds.X) / sliderBounds.Width, 0, 1)));

		sb.End();
		Rectangle previousScissor = Main.graphics.GraphicsDevice.ScissorRectangle;
		Main.graphics.GraphicsDevice.ScissorRectangle = viewport;
		Begin(sb, clip);
		try {
			for (int i = 0; i < items.Count; i++) {
				Rectangle r = layout.Row(i, scroll);
				if (!r.Intersects(viewport)) continue;
				DrawSetting(sb, items[i], r, dropdown == null && viewport.Contains(pointer.ToPoint()));
			}
		}
		finally {
			sb.End();
			Main.graphics.GraphicsDevice.ScissorRectangle = previousScissor;
			Begin(sb);
		}
		if (maximum > 0) {
			Rectangle track = layout.At(521, 84, 4, 326);
			sb.Draw(TextureAssets.MagicPixel.Value, track, Color.Black * .5f);
			int thumbHeight = Math.Max((int)(25*layout.Scale), (int)(track.Height * 326f / (items.Count*52)));
			sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(track.X, track.Y + (int)((track.Height-thumbHeight)*scroll/maximum), track.Width, thumbHeight), new Color(126,151,215));
		}
		Button(sb, layout.Back, T("返回", "Back"));
		Texture2D arrow = Texture("Back");
		sb.Draw(arrow, layout.At(22, 438, 25, 21), Color.White);
		Label(sb, T("返回时保存更改", "Saved when you return"), layout.At(315, 430, 200, 36), .65f, true, new Color(175,191,228));
		if (dropdown == null && Hit(layout.Back)) Back();
		if (dropdown != null) DrawDropdown(sb);
	}

	private void DrawSetting(SpriteBatch sb, MobileSetting item, Rectangle r, bool canInput)
	{
		float s = layout.Scale;
		Nine(sb, "CreationRow", r, Color.White);
		Rectangle label = new(r.X + (int)(11*s), r.Y, (int)((item.Kind == MobileSettingKind.Slider ? 210 : 265)*s), r.Height);
		Label(sb, item.Label, label, .9f);
		Rectangle control = new(r.Right - (int)(208*s), r.Y + (int)(7*s), (int)(197*s), r.Height - (int)(14*s));
		switch (item.Kind) {
			case MobileSettingKind.Toggle:
				bool on = item.Read() > .5f;
				MobileMenuFrame.ToggleBounds(r, s, out Rectangle offRect, out Rectangle onRect);
				MobileMenuFrame.Toggle(sb, Texture("Button"), Texture("SwitchOn"), offRect, onRect, on, s);
				Label(sb, T("关闭", "Off"), offRect, .7f, true, !on ? Color.White : Color.Silver);
				Label(sb, T("开启", "On"), onRect, .7f, true, on ? Color.White : Color.Silver);
				if (canInput && Hit(offRect)) Change(item, 0);
				if (canInput && Hit(onRect)) Change(item, 1);
				break;
			case MobileSettingKind.Slider:
				Rectangle bar = MobileMenuFrame.SliderTrack(r, s);
				float fraction = MathHelper.Clamp((item.Read()-item.Min)/(item.Max-item.Min),0,1);
				MobileMenuFrame.Slider(sb, Texture("PlayerSliderKnob"), bar, fraction, s);
				Label(sb, item.Display(), new Rectangle(r.X+(int)(225*s), r.Y, (int)(68*s), r.Height), .8f, true);
				Rectangle sliderHit = new(bar.X-(int)(12*s), r.Y, bar.Width+(int)(24*s), r.Height);
				if (canInput && !consumed && pressed && sliderHit.Contains(pointer.ToPoint())) {
					slider = item; sliderBounds = bar; consumed = true;
					Change(item, MathHelper.Lerp(item.Min,item.Max,MathHelper.Clamp((pointer.X-bar.X)/bar.Width,0,1)));
				}
				break;
			case MobileSettingKind.Choice:
				Label(sb, item.Choices[Math.Clamp((int)item.Read(),0,item.Choices.Length-1)], new Rectangle(control.X, control.Y, control.Width-(int)(35*s), control.Height), .7f, true);
				MobileMenuFrame.DropdownDivider(sb, r, s);
				sb.Draw(Texture("WorldDropdown"), new Rectangle(r.Right-(int)(37*s), r.Center.Y-(int)(9*s), (int)(24*s), (int)(18*s)), Color.White);
				if (canInput && Hit(control)) dropdown = item;
				break;
			case MobileSettingKind.Action:
				Button(sb, control, item.Display?.Invoke() ?? T("编辑……", "Edit…"), .68f);
				if (canInput && Hit(control)) { item.Activate(); settingsDirty = true; }
				break;
		}
	}

	private void Change(MobileSetting item, float value)
	{
		if (Math.Abs(item.Read() - value) < .0001f) return;
		item.Write(value);
		if (item.ModSetting) modDirty = true;
		else settingsDirty = true;
	}

	private void DrawDropdown(SpriteBatch sb)
	{
		MobileSetting item = dropdown;
		sb.Draw(TextureAssets.MagicPixel.Value, layout.Panel, Color.Black * .6f);
		int h = item.Choices.Length * 44 + 64;
		Rectangle panel = layout.At(113, (480-h)/2, 310, h);
		Nine(sb, "CreationPopup", panel, Color.White, 12);
		Label(sb, item.Label, new Rectangle(panel.X+8,panel.Y+4,panel.Width-16,(int)(46*layout.Scale)), .8f,true);
		for (int i = 0; i < item.Choices.Length; i++) {
			Rectangle r = new(panel.X+(int)(10*layout.Scale),panel.Y+(int)((50+i*44)*layout.Scale),panel.Width-(int)(20*layout.Scale),(int)(38*layout.Scale));
			Nine(sb, (int)item.Read() == i ? "CreationOptionSelected" : "CreationOption", r, Color.White, 8);
			Label(sb, item.Choices[i], r, .8f, true);
			if (Hit(r)) { Change(item,i); dropdown=null; break; }
		}
		if (!consumed && released && !panel.Contains(pointer.ToPoint())) { dropdown = null; consumed = true; }
	}

	private Texture2D Texture(string name)
	{
		if (!textures.TryGetValue(name, out Texture2D texture)) textures[name] = texture = ModContent.Request<Texture2D>($"MobileJoystick/Assets/MobileMenu/{name}", AssetRequestMode.ImmediateLoad).Value;
		return texture;
	}

	private void Heading(SpriteBatch sb, string text)
	{
		Nine(sb, "Title", layout.Title, Color.White, 20);
		Label(sb, text, layout.Title, 1f, true, display: true);
	}

	private void Button(SpriteBatch sb, Rectangle r, string text, float font = .9f)
	{
		Nine(sb, "Button", r, r.Contains(pointer.ToPoint()) ? new Color(230,238,255) : Color.White);
		Label(sb, text, r, font, true);
	}

	private void Label(SpriteBatch sb, string text, Rectangle r, float font, bool center = false, Color? color = null, bool right = false, bool display = false)
	{
		font *= MobileUiMetrics.FontScale;
		if (display) font *= MobileUiMetrics.DisplayFontScale;
		if (page == Page.TouchEditor) font *= 1.45f;
		DynamicSpriteFont face = display ? FontAssets.DeathText.Value : FontAssets.MouseText.Value;
		Vector2 size = face.MeasureString(text);
		float scale = Math.Min(font*layout.Scale, (r.Width-10*layout.Scale)/Math.Max(1,size.X));
		int top = int.MaxValue, bottom = int.MinValue, lineTop = 0;
		foreach (char c in text) {
			if (c == '\n') { lineTop += face.LineSpacing; continue; }
			if (char.IsWhiteSpace(c) || !face.SpriteCharacters.TryGetValue(c, out var glyph)) continue;
			top = Math.Min(top, lineTop + glyph.Padding.Y);
			bottom = Math.Max(bottom, lineTop + glyph.Padding.Y + glyph.Glyph.Height);
		}
		float middle = top == int.MaxValue ? size.Y / 2 : (top + bottom) / 2f;
		Vector2 position = new(right ? r.Right - size.X * scale : center ? r.Center.X - size.X * scale / 2 : r.X, r.Center.Y - middle * scale);
		position = new Vector2(MathF.Round(position.X), MathF.Round(position.Y));
		int border = Math.Max(1, (int)MathF.Round(layout.Scale));
		for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
			if (x != 0 || y != 0) sb.DrawString(face, text, position + new Vector2(x * border, y * border), Color.Black, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
		sb.DrawString(face, text, position, color ?? Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
	}

	private void Nine(SpriteBatch sb, string name, Rectangle destination, Color tint, int border = 12)
	{
		Texture2D tex = Texture(name);
		int src = Math.Min(border, Math.Min(tex.Width,tex.Height)/2);
		int b = Math.Min(Math.Max(1, (int)(src*layout.Scale)),Math.Min(destination.Width,destination.Height)/2);
		int[] sx={0,src,tex.Width-src,tex.Width}, sy={0,src,tex.Height-src,tex.Height};
		int[] dx={destination.Left,destination.Left+b,destination.Right-b,destination.Right}, dy={destination.Top,destination.Top+b,destination.Bottom-b,destination.Bottom};
		for (int y=0;y<3;y++) for(int x=0;x<3;x++) {
			Rectangle dest = new(dx[x],dy[y],dx[x+1]-dx[x],dy[y+1]-dy[y]);
			if (dest.Width>0 && dest.Height>0) sb.Draw(tex,dest,new Rectangle(sx[x],sy[y],sx[x+1]-sx[x],sy[y+1]-sy[y]),tint);
		}
	}
}
