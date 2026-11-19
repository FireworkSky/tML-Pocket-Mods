namespace MobileJoystick;

// Shared UI metrics so the menu, inventory and HUD keep the same text scale.
internal static class MobileUiMetrics
{
	internal const float DefaultFontScale = 1.18f;

	// Death_Text is the display counterpart of Mouse_Text; it is about 2.5x as
	// large at the same scale, so headings use 0.4x of the body multiplier.
	internal const float DisplayFontScale = 0.4f;

	internal static float FontScale => MobileJoystickConfig.Instance?.UIFontScale ?? DefaultFontScale;
}