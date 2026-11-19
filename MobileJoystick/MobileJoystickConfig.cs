using System;
using System.ComponentModel;
using Terraria.ModLoader.Config;
using System.IO;
using Newtonsoft.Json;

namespace MobileJoystick;

public enum MobileControlLayoutMode { Simple, Advanced, Custom }

public class MobileJoystickConfig : ModConfig
{
	public static MobileJoystickConfig Instance { get; private set; }

	public override ConfigScope Mode => ConfigScope.ClientSide;

	internal void SaveFromMobileMenu()
	{
		Directory.CreateDirectory(ConfigManager.ModConfigPath);
		string path = Path.Combine(ConfigManager.ModConfigPath, Mod.Name + "_" + Name + ".json");
		File.WriteAllText(path, JsonConvert.SerializeObject(this, ConfigManager.serializerSettings));
		OnChanged();
	}

	[Header("General")]
	[JsonIgnore]
	[DefaultValue(true)]
	public bool EnableMobileControls { get; set; }

	[JsonIgnore]
	[DefaultValue(true)]
	public bool UseMobileInventory { get; set; }

	[DefaultValue(true)]
	public bool EnableTouchInput { get; set; }

	[DefaultValue(true)]
	public bool EnableMouseFallback { get; set; }

	[DefaultValue(true)]
	public bool QuickStackToNearbyChests { get; set; } = true;

	[DefaultValue(true)]
	public bool JoystickUpJumps { get; set; }

	[DefaultValue(true)]
	public bool ShowTopButtons { get; set; }

	[JsonIgnore]
	[DefaultValue(true)]
	public bool UseMobileMinimap { get; set; }

	[Header("Layout")]
	[DefaultValue(MobileControlLayoutMode.Simple)]
	public MobileControlLayoutMode ControlLayout { get; set; }

	[DefaultValue(0)]
	public int ActiveCustomPresetIndex { get; set; }

	public System.Collections.Generic.List<MobileCustomPreset> CustomPresets { get; set; } = new();

	[Range(0.6f, 1.8f)]
	[Increment(0.05f)]
	[DefaultValue(1.8f)]
	[Slider]
	public float ControlScale { get; set; }

	[Range(0.35f, 1f)]
	[Increment(0.05f)]
	[DefaultValue(0.78f)]
	[Slider]
	public float Opacity { get; set; }

	// Multiplies every menu, inventory and HUD label. The phone reference uses a
	// larger body size than the old 0.65-0.9 multipliers produced.
	[Header("Text")]
	[Range(0.9f, 1.4f)]
	[Increment(0.02f)]
	[DefaultValue(1.18f)]
	[Slider]
	public float UIFontScale { get; set; } = 1.18f;

	[Header("Layout")]
	[Range(0, 96)]
	[Increment(4)]
	[DefaultValue(18)]
	[Slider]
	public int EdgePadding { get; set; }


	[Range(-800, 800)]
	[Increment(8)]
	[DefaultValue(0)]
	public float LeftStickOffsetX { get; set; }

	[Range(-800, 800)]
	[Increment(8)]
	[DefaultValue(0)]
	public float LeftStickOffsetY { get; set; }

	[Range(-800, 800)]
	[Increment(8)]
	[DefaultValue(0)]
	public float RightStickOffsetX { get; set; }

	[Range(-800, 800)]
	[Increment(8)]
	[DefaultValue(0)]
	public float RightStickOffsetY { get; set; }

	[Header("Touch")]
	[Range(0.05f, 0.55f)]
	[Increment(0.05f)]
	[DefaultValue(0.25f)]
	[Slider]
	public float JoystickDeadzone { get; set; }

	[Range(160, 720)]
	[Increment(20)]
	[DefaultValue(440)]
	[Slider]
	public int AimDistance { get; set; }

	public override void OnLoaded()
	{
		EdgePadding = Math.Clamp(EdgePadding, 0, 96);
		EnableMobileControls = true;
		UseMobileInventory = true;
		UseMobileMinimap = true;
		if (ControlScale > 1.8f) {
			ControlScale = 1.8f;
		}

		if (ControlScale < 0.6f) {
			ControlScale = 0.6f;
		}

		UIFontScale = Math.Clamp(UIFontScale, 0.9f, 1.4f);

		LeftStickOffsetX = Math.Clamp(LeftStickOffsetX, -800f, 800f);
		LeftStickOffsetY = Math.Clamp(LeftStickOffsetY, -800f, 800f);
		RightStickOffsetX = Math.Clamp(RightStickOffsetX, -800f, 800f);
		RightStickOffsetY = Math.Clamp(RightStickOffsetY, -800f, 800f);

		Instance = this;
		MobileCustomControls.EnsureInitialized(this);
	}
}
