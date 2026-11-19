using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace MobileJoystick;

// These two tML menu integrations have no public enumeration/cycling API.
// Use the installed loader's own collections and callback so other mods keep
// their server permissions, custom config editors and boss-bar selection.
internal static class MobileNativeSettings
{
	private static readonly FieldInfo ConfigsField = typeof(ConfigManager).GetField("Configs", BindingFlags.Static | BindingFlags.NonPublic);
	private static readonly MethodInfo BossMenu = typeof(BossBarLoader).GetMethod("InsertMenu", BindingFlags.Static | BindingFlags.NonPublic);

	internal static IEnumerable<ModConfig> Configs()
	{
		var all = (IDictionary<Mod, List<ModConfig>>)ConfigsField.GetValue(null);
		foreach (var list in all.Values) foreach (ModConfig config in list) yield return config;
	}

	internal static string BossBarLabel()
	{
		object[] arguments = { null };
		return (string)BossMenu.Invoke(null, arguments);
	}

	internal static void CycleBossBar()
	{
		object[] arguments = { null };
		BossMenu.Invoke(null, arguments);
		((Action)arguments[0])();
	}

	internal static void CycleResolution()
	{
		if (Main.numDisplayModes <= 0) return;
		int selected = 0;
		for (int i=0; i<Main.numDisplayModes; i++) {
			if (Main.displayWidth[i] == Main.PendingResolutionWidth && Main.displayHeight[i] == Main.PendingResolutionHeight) { selected=i; break; }
		}
		selected = (selected+1)%Main.numDisplayModes;
		Main.PendingResolutionWidth=Main.displayWidth[selected];
		Main.PendingResolutionHeight=Main.displayHeight[selected];
		Main.SetResolution(Main.PendingResolutionWidth, Main.PendingResolutionHeight);
	}

	internal static float CursorComponent(bool border, int component)
	{
		var hsl = (border ? Main.mouseBorderColorSlider : Main.mouseColorSlider).GetHSLVector();
		return component == 0 ? hsl.X : component == 1 ? hsl.Y : hsl.Z;
	}

	internal static void SetCursorComponent(bool border, int component, float value)
	{
		var color = border ? Main.mouseBorderColorSlider : Main.mouseColorSlider;
		var hsl = color.GetHSLVector();
		if (component == 0) hsl.X=value;
		else if (component == 1) hsl.Y=value;
		else hsl.Z=value;
		color.SetHSL(hsl);
		color.ApplyToMainLegacyBars();
		if (border) Main.MouseBorderColor=color.GetColor();
		else Main.mouseColor=color.GetColor();
	}
}
