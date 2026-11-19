using System;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace MobileJoystick;

// Keep native title text, fonts, themes and interaction. Hide the native title
// buttons only while the mobile settings overlay is open.
public class MobileTitleSystem : ModSystem
{
	public override void Load()
	{
		if (Main.dedServ) return;
		IL_Main.DrawMenu += PatchTitle;
		On_Main.DrawMenu += DrawMenu;
	}

	public override void Unload()
	{
		if (Main.dedServ) return;
		IL_Main.DrawMenu -= PatchTitle;
		On_Main.DrawMenu -= DrawMenu;
	}

	private void PatchTitle(ILContext il)
	{
		var cursor = new ILCursor(il);
		if (!cursor.TryGotoNext(MoveType.Before, i => i.Operand is MethodReference method
			&& method.DeclaringType.FullName == "Terraria.ModLoader.UI.Interface" && method.Name == "AddMenuButtons")
			|| cursor.Prev?.Operand is not VariableDefinition count) {
			Mod.Logger.Warn("Mobile title settings hook unavailable; retaining the native title.");
			return;
		}
		cursor.Index++;
		cursor.Emit(OpCodes.Ldloc, count);
		cursor.EmitDelegate<Func<int, int>>(value => MobileTitleSettingsSystem.Active ? 0 : value);
		cursor.Emit(OpCodes.Stloc, count);
	}

	private void DrawMenu(On_Main.orig_DrawMenu orig, Main main, GameTime time)
	{
		bool fromTitle = Main.gameMenu && Main.menuMode == 0 && !MobileTitleSettingsSystem.Active;
		orig(main, time);
		if (fromTitle && Main.gameMenu && Main.menuMode == 11
			&& MobileJoystickConfig.Instance?.EnableMobileControls == true && !PlayerInput.UsingGamepadUI)
			MobileTitleSettingsSystem.Open();
		if (MobileTitleSettingsSystem.Active) MobileTitleSettingsSystem.DrawOverlay();
	}
}
