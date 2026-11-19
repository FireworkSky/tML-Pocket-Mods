using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.Graphics;
using Terraria.Localization;
using Terraria.UI;
using Terraria.ModLoader.Config;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.Social;
using System.Linq;

namespace MobileJoystick;

internal enum MobileSettingKind { Toggle, Slider, Choice, Action }

internal sealed class MobileSetting
{
	public string Label;
	public MobileSettingKind Kind;
	public Func<float> Read;
	public Action<float> Write;
	public Func<string> Display;
	public Action Activate;
	public string[] Choices;
	public float Min, Max;
	public bool ModSetting;
}

internal static class MobileSettingsCatalog
{
	internal static string Text(string chinese, string english) => Language.ActiveCulture.Name.StartsWith("zh") ? chinese : english;
	internal static string[] Titles => new[] {
		Text("常规", "General"), Text("界面", "Interface"), Text("视频", "Video"), Text("声音", "Audio"),
		Text("游戏控制", "Controls"), Text("触摸", "Touch"), Text("手柄", "Gamepad"), Text("模组", "Mod") };
	internal static string Heading(int category) => category switch {
		0 => Text("设置", "Settings"), 3 => Text("音效", "Audio"),
		8 => Text("模组配置", "Mod configuration"), 9 => Text("光标颜色", "Cursor colors"),
		_ => Titles[category]
	};

	public static List<MobileSetting> Create(int category, Action keybinds, Action modConfigs, Action cursorColors, Action<System.Action> displayChange)
	{
		var items = new List<MobileSetting>();
		MobileJoystickConfig c = MobileJoystickConfig.Instance;
		void Toggle(string zh, string en, Func<bool> get, Action<bool> set, bool mod = false) => items.Add(new MobileSetting {
			Label = Text(zh, en), Kind = MobileSettingKind.Toggle, Read = () => get() ? 1 : 0,
			Write = v => set(v > .5f), ModSetting = mod });
		void Slider(string zh, string en, Func<float> get, Action<float> set, float min, float max, bool mod = false, bool percent = true) => items.Add(new MobileSetting {
			Label = Text(zh, en), Kind = MobileSettingKind.Slider, Read = get, Write = set, Min = min, Max = max,
			Display = () => percent ? $"{get() * 100:0}%" : $"{get():0}", ModSetting = mod });
		void Choice(string zh, string en, string[] choices, Func<int> get, Action<int> set) => items.Add(new MobileSetting {
			Label = Text(zh, en), Kind = MobileSettingKind.Choice, Choices = choices,
			Read = () => get(), Write = v => set((int)v) });
		void Action(string zh, string en, System.Action action) => items.Add(new MobileSetting {
			Label = Text(zh, en), Kind = MobileSettingKind.Action, Activate = action });

		switch (category) {
			case 0:
				Toggle("自动保存", "Autosave", () => Main.autoSave, v => Main.autoSave = v);
				Toggle("背包和菜单自动暂停", "Autopause with inventory", () => Main.autoPause, v => Main.autoPause = v);
				Toggle("启用地图", "Enable map", () => Main.mapEnabled, v => Main.mapEnabled = v);
				Toggle("自动使用物品", "Autofire", () => Main.SettingsEnabled_AutoReuseAllItems, v => Main.SettingsEnabled_AutoReuseAllItems = v);
				Toggle("反转套装效果按键", "Reverse armor set key", () => Main.ReversedUpDownArmorSetBonuses, v => Main.ReversedUpDownArmorSetBonuses = v);
				Choice("自动开门", "Smart doors", new[] { Text("关闭", "Off"), Text("仅手柄", "Gamepad only"), Text("所有输入", "All inputs") },
					() => DoorOpeningHelper.PreferenceSettings == DoorOpeningHelper.DoorAutoOpeningPreference.EnabledForEverything ? 2 : DoorOpeningHelper.PreferenceSettings == DoorOpeningHelper.DoorAutoOpeningPreference.EnabledForGamepadOnly ? 1 : 0,
					v => { for(int i=0;i<3;i++) { int current=DoorOpeningHelper.PreferenceSettings == DoorOpeningHelper.DoorAutoOpeningPreference.EnabledForEverything ? 2 : DoorOpeningHelper.PreferenceSettings == DoorOpeningHelper.DoorAutoOpeningPreference.EnabledForGamepadOnly ? 1 : 0; if(current==v) break; DoorOpeningHelper.CyclePreferences(); } });
				Choice("悬停控制", "Hover control", new[]{Text("按住", "Hold"),Text("切换", "Toggle")}, () => Player.Settings.HoverControl == Player.Settings.HoverControlMode.Hold ? 0 : 1,
					v => { if ((Player.Settings.HoverControl == Player.Settings.HoverControlMode.Hold ? 0 : 1) != v) Player.Settings.CycleHoverControl(); });
				Action("模组配置（全部模组）", "Mod configuration (all mods)", modConfigs);
				Action("键位设置", "Key bindings", keybinds);
				if(SocialAPI.Network?.CanInvite()==true) Action("邀请好友", "Invite friends", () => SocialAPI.Network.OpenInviteInterface());
				break;
			case 1:
				Slider("界面比例", "UI scale", () => Main.UIScaleWanted, v => Main.UIScale = v, .5f, 2f);
				Slider("界面字体大小", "UI font size", () => c.UIFontScale, v => c.UIFontScale = v, .9f, 1.4f, true);
				Slider("画面缩放", "Zoom", () => Main.GameZoomTarget, v => Main.GameZoomTarget = v, 1f, 2f);
				Slider("小地图比例", "Minimap scale", () => Main.MapScale, v => Main.MapScale = v, .5f, 1f);
				Toggle("物品名称", "Item names", () => Main.showItemText, v => Main.showItemText = v);
				Toggle("突出新物品", "Highlight new items", () => ItemSlot.Options.HighlightNewItems, v => ItemSlot.Options.HighlightNewItems = v);
				Toggle("不透明提示框", "Opaque tooltips", () => Main.SettingsEnabled_OpaqueBoxBehindTooltips, v => Main.SettingsEnabled_OpaqueBoxBehindTooltips = v);
				Action("生命与魔力样式", "Health and mana style", () => Main.ResourceSetsManager.CycleResourceSet());
				items[^1].Display = () => Main.ResourceSetsManager.ActiveSet.DisplayedName;
				Choice("入侵进度条", "Invasion progress", Enumerable.Range(124,3).Select(i => Lang.menu[i].Value).ToArray(), () => Main.invasionProgressMode, v => Main.invasionProgressMode=v);
				Toggle("方块放置预览", "Placement preview", () => Main.placementPreview, v => Main.placementPreview=v);
				Toggle("建筑网格", "Building grid", () => Main.MouseShowBuildingGrid, v => Main.MouseShowBuildingGrid=v);
				Toggle("显示手柄操作提示", "Gamepad instructions", () => !Main.GamepadDisableInstructionsDisplay, v => Main.GamepadDisableInstructionsDisplay=!v);
				Action("Boss 血条样式", "Boss bar style", MobileNativeSettings.CycleBossBar);
				items[^1].Display = MobileNativeSettings.BossBarLabel;
				Action("原版小地图边框", "Native minimap border", () => Main.MinimapFrameManagerInstance.CycleSelection());
				items[^1].Display = () => Language.GetTextValue("UI.MinimapFrame_"+Main.MinimapFrameManagerInstance.ActiveSelectionKeyName);
				Toggle("显示 Boss 血量文字", "Boss health text", () => BigProgressBarSystem.ShowText, v => { if(BigProgressBarSystem.ShowText!=v) BigProgressBarSystem.ToggleShowText(); });
				break;
			case 2:
				Choice("照明模式", "Lighting mode", new[] { "White", "Retro", "Trippy", "Color" }.Select(mode => Language.GetTextValue("UI.LightMode_" + mode)).ToArray(), () => (int)Lighting.Mode, v => { for (int i=0; i<4 && (int)Lighting.Mode != v; i++) Lighting.NextLightMode(); });
				Toggle("背景", "Background", () => Main.BackgroundEnabled, v => Main.BackgroundEnabled = v);
				Toggle("血液和尸块", "Blood and gore", () => ChildSafety.Disabled, v => ChildSafety.Disabled=v);
				Choice("画质", "Quality", new[] { Text("自动", "Auto"), Text("低", "Low"), Text("中", "Medium"), Text("高", "High") }, () => Main.qaStyle, v => Main.qaStyle = v);
				Slider("背景视差", "Parallax", () => Main.bgScroll, v => { Main.bgScroll = (int)v; Main.caveParallax = 1f - Main.bgScroll / 500f; }, 0, 100, percent:false);
				Toggle("热浪扭曲", "Heat distortion", () => Main.UseHeatDistortion, v => Main.UseHeatDistortion = v);
				Toggle("暴风雨效果", "Storm effects", () => Main.UseStormEffects, v => Main.UseStormEffects = v);
				Toggle("风中摇摆", "Wind sway", () => Main.SettingsEnabled_TilesSwayInWind, v => Main.SettingsEnabled_TilesSwayInWind = v);
				Choice("波浪质量", "Waves", new[] { Text("关闭", "Off"), Text("低", "Low"), Text("中", "Medium"), Text("高", "High") }, () => Main.WaveQuality, v => Main.WaveQuality = v);
				Toggle("全屏", "Fullscreen", () => Main.graphics.IsFullScreen, v => { if(Main.graphics.IsFullScreen!=v) displayChange(Main.ToggleFullScreen); });
				Action("分辨率（点击切换）", "Resolution (click to cycle)", () => displayChange(MobileNativeSettings.CycleResolution));
				items[^1].Display = () => Main.PendingResolutionWidth+" × "+Main.PendingResolutionHeight;
				Choice("跳帧", "Frame skip", Enumerable.Range(247,3).Select(i => Lang.menu[i].Value).ToArray(), () => (int)Main.FrameSkipMode,
					v => { for(int i=0;i<3 && (int)Main.FrameSkipMode!=v;i++) Main.CycleFrameSkipMode(); });
				break;
			case 3:
				Slider("音乐", "Music", () => Main.musicVolume, v => Main.musicVolume = v, 0, 1);
				Slider("音效", "Sound", () => Main.soundVolume, v => Main.soundVolume = v, 0, 1);
				Slider("环境", "Ambient", () => Main.ambientVolume, v => Main.ambientVolume = v, 0, 1);
				break;
			case 4:
				Toggle("方块放置预览", "Placement preview", () => Main.placementPreview, v => Main.placementPreview = v);
				Toggle("智能光标切换", "Toggle smart cursor", () => Main.cSmartCursorModeIsToggleAndNotHold, v => Main.cSmartCursorModeIsToggleAndNotHold = v);
				Toggle("智能方块放置", "Smart block placement", () => Player.SmartCursorSettings.SmartBlocksEnabled, v => Player.SmartCursorSettings.SmartBlocksEnabled = v);
				Toggle("挖掘后智能砍树", "Smart axe after pickaxe", () => Player.SmartCursorSettings.SmartAxeAfterPickaxe, v => Player.SmartCursorSettings.SmartAxeAfterPickaxe = v);
				Toggle("左摇杆上推跳跃", "Left stick up to jump", () => c.JoystickUpJumps, v => c.JoystickUpJumps = v, true);
				Action("键盘与手柄按键", "Key bindings", keybinds);
				Action("光标和边框颜色", "Cursor and border colors", cursorColors);
				Choice("锁定目标方式", "Target lock mode", Enumerable.Range(232,3).Select(i => Lang.menu[i].Value).ToArray(), () => (int)LockOnHelper.UseMode,
					v => { for(int i=0;i<3 && (int)LockOnHelper.UseMode!=v;i++) LockOnHelper.CycleUseModes(); });
				break;
			case 5:
				Action("\u89e6\u6478\u63a7\u4ef6", "Touch controls", () => MobileMenuSystem.OpenTouchEditor());
				Toggle("触摸输入", "Touch input", () => c.EnableTouchInput, v => c.EnableTouchInput = v, true);
				Toggle("鼠标模拟触摸", "Mouse touch simulation", () => c.EnableMouseFallback, v => c.EnableMouseFallback = v, true);
				Slider("按钮大小", "Control size", () => c.ControlScale, v => c.ControlScale = MathF.Round(v*20)/20, .6f, 1.8f, true);
				Slider("按钮不透明度", "Control opacity", () => c.Opacity, v => c.Opacity = v, .35f, 1, true);
				Slider("摇杆死区", "Stick deadzone", () => c.JoystickDeadzone, v => c.JoystickDeadzone = v, .05f, .55f, true);
				Slider("瞄准距离", "Aim distance", () => c.AimDistance, v => c.AimDistance = (int)v, 160, 720, true, false);
				Slider("边缘间距", "Edge padding", () => c.EdgePadding, v => c.EdgePadding = (int)v, 0, 96, true, false);
				break;
			case 6:
				Slider("左摇杆水平死区", "Left stick X deadzone", () => PlayerInput.CurrentProfile.LeftThumbstickDeadzoneX, v => PlayerInput.CurrentProfile.LeftThumbstickDeadzoneX=v, 0, .5f);
				Slider("左摇杆垂直死区", "Left stick Y deadzone", () => PlayerInput.CurrentProfile.LeftThumbstickDeadzoneY, v => PlayerInput.CurrentProfile.LeftThumbstickDeadzoneY=v, 0, .5f);
				Slider("右摇杆水平死区", "Right stick X deadzone", () => PlayerInput.CurrentProfile.RightThumbstickDeadzoneX, v => PlayerInput.CurrentProfile.RightThumbstickDeadzoneX=v, 0, .5f);
				Slider("右摇杆垂直死区", "Right stick Y deadzone", () => PlayerInput.CurrentProfile.RightThumbstickDeadzoneY, v => PlayerInput.CurrentProfile.RightThumbstickDeadzoneY=v, 0, .5f);
				Action("手柄按键绑定", "Gamepad bindings", keybinds);
				break;
			case 7:
				Action("模组配置（全部模组）", "Mod configuration (all mods)", modConfigs);
				Toggle("显示顶部快捷按钮", "Top shortcuts", () => c.ShowTopButtons, v => c.ShowTopButtons=v, true);
				Slider("按钮大小", "Control size", () => c.ControlScale, v => c.ControlScale=MathF.Round(v*20)/20, .6f, 1.8f, true);
				Slider("按钮不透明度", "Opacity", () => c.Opacity, v => c.Opacity=v, .35f, 1, true);
				break;
			case 9:
				foreach (bool border in new[]{false,true}) {
					string prefix=border ? Text("边框", "Border") : Text("光标", "Cursor");
					for(int component=0;component<3;component++) {
						int axis=component;
						string suffix=component==0 ? Text("色相", "hue") : component==1 ? Text("饱和度", "saturation") : Text("亮度", "lightness");
						Slider(prefix+suffix,prefix+" "+suffix, () => MobileNativeSettings.CursorComponent(border,axis), v => MobileNativeSettings.SetCursorComponent(border,axis,v), !border && axis==2 ? .15f : 0,1);
					}
				}
				Slider("边框不透明度", "Border opacity", () => Main.mouseBorderColorSlider.Alpha, v => {Main.mouseBorderColorSlider.Alpha=v; Main.MouseBorderColor=Main.mouseBorderColorSlider.GetColor();},0,1);
				break;
		}
		return items;
	}

	internal static List<MobileSetting> ModConfigs(Action<ModConfig> open) => MobileNativeSettings.Configs()
		.OrderBy(c => c.Mod.DisplayName).ThenBy(c => c.DisplayName.Value)
		.Select(c => new MobileSetting { Label=c.Mod.DisplayName+" · "+c.DisplayName.Value, Kind=MobileSettingKind.Action,
			Display=()=>Text("打开", "Open"), Activate=()=>open(c) }).ToList();
}
