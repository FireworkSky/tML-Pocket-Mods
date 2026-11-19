using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace MobileJoystick;

public partial class MobileMinimapSystem
{
	private static readonly Action<Main> DrawResources = typeof(Main).GetMethod("GUIBarsDraw", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
		?.CreateDelegate<Action<Main>>();
	private static bool UseMobileFullscreen => Main.mapFullscreen && MobileJoystickConfig.Instance?.EnableMobileControls == true && !Main.gameMenu;

	private void DrawFullscreenBackground(On_Main.orig_DrawMapFullscreenBackground orig, Vector2 screenPosition, int width, int height)
	{
		if (!UseMobileFullscreen) { orig(screenPosition, width, height); return; }
		Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(0, 0, width, height), Color.Black);
	}

	private void RemoveDesktopMapClose(ILContext il)
	{
		var start = new ILCursor(il);
		// Skip only the desktop bottom-left close control; retain map/mod layers.
		if (!start.TryGotoNext(MoveType.After, i => i.MatchCall<PlayerInput>(nameof(PlayerInput.SetZoom_UI)),
			i => i.MatchLdcI4(10))) { Mod.Logger.Warn("Mobile map close-control hook unavailable."); return; }
		start.Index--; // The SetZoom_UI call stays in place.
		Instruction beginning = start.Next;
		var end = new ILCursor(il);
		if (!end.TryGotoNext(i => i.MatchCall(typeof(SystemLoader), nameof(SystemLoader.PostDrawFullscreenMap)))) {
			Mod.Logger.Warn("Mobile map close-control end hook unavailable."); return;
		}
		end.Index--; // Preserve loading the text reference for other map mods.
		ILLabel target = end.MarkLabel();
		start.Goto(beginning, MoveType.Before);
		start.EmitDelegate<Func<bool>>(() => UseMobileFullscreen);
		start.Emit(OpCodes.Brtrue, target);
	}

	private void DrawFullscreenControls()
	{
		Main.spriteBatch.End();
		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
			DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
		DrawResources?.Invoke(Main.instance);
		Main.spriteBatch.End();
		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
			DepthStencilState.None, RasterizerState.CullCounterClockwise);
		foreach (var button in MobileJoystickInput.CreateMapLayout(MobileJoystickConfig.Instance).Buttons) {
			bool pressed = MobileJoystickInput.IsActionDown(button.Action);
			if (MobileHudArtwork.DrawToolbar(button, pressed)) continue;
			Texture2D texture = ModContent.Request<Texture2D>("MobileJoystick/Assets/MobileControls/" + button.TextureName, AssetRequestMode.ImmediateLoad).Value;
			Main.spriteBatch.Draw(texture, button.Center, null, Color.White * (pressed ? 1 : .78f), 0,
				texture.Size() * .5f, button.Radius * 2 / Math.Max(texture.Width, texture.Height), SpriteEffects.None, 0);
		}
	}
}
