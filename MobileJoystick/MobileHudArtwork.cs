using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ModLoader;

namespace MobileJoystick;

internal static class MobileHudArtwork
{
	internal static Texture2D Texture(string name) => ModContent.Request<Texture2D>(
		"MobileJoystick/Assets/MobileHud/" + name, AssetRequestMode.ImmediateLoad).Value;

	internal static bool DrawToolbar(MobileControlButton button, bool pressed = false)
	{
		string name = button.Action switch {
			MobileControlAction.Chat => MobileChatSystem.IsOpen ? "ChatSelected" : "Chat",
			MobileControlAction.MapStyle => Main.mapStyle == 1 && !Main.mapFullscreen ? "MinimapSelected" : "Minimap",
			MobileControlAction.Map => Main.mapFullscreen ? "MapSelected" : "Map",
			_ => null
		};
		if (name == null) return false;
		Rectangle bounds = button.Bounds;
		Nine("Toolbar", bounds, Color.White, bounds.Width / 100f);
		Icon(name, bounds, pressed ? Color.White : Color.White * .95f, .72f);
		return true;
	}

	internal static void Icon(string name, Rectangle bounds, Color color, float fraction = .8f)
	{
		Texture2D texture = Texture(name);
		Main.spriteBatch.Draw(texture, bounds.Center.ToVector2(), null, color, 0f, texture.Size() * .5f,
			Math.Min(bounds.Width, bounds.Height) * fraction / Math.Max(texture.Width, texture.Height), SpriteEffects.None, 0f);
	}

	internal static void Nine(string name, Rectangle bounds, Color color, float scale)
	{
		Texture2D texture = Texture(name);
		int corner = Math.Min(10, Math.Min(texture.Width, texture.Height) / 2);
		int edgeX = Math.Min(Math.Max(1, (int)MathF.Round(corner * bounds.Width / (float)texture.Width)), bounds.Width / 2);
		int edgeY = Math.Min(Math.Max(1, (int)MathF.Round(corner * bounds.Height / (float)texture.Height)), bounds.Height / 2);
		int[] sx = { 0, corner, texture.Width - corner, texture.Width }, sy = { 0, corner, texture.Height - corner, texture.Height };
		int[] dx = { bounds.Left, bounds.Left + edgeX, bounds.Right - edgeX, bounds.Right };
		int[] dy = { bounds.Top, bounds.Top + edgeY, bounds.Bottom - edgeY, bounds.Bottom };
		for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
			if (dx[x + 1] > dx[x] && dy[y + 1] > dy[y])
				Main.spriteBatch.Draw(texture, new Rectangle(dx[x], dy[y], dx[x + 1] - dx[x], dy[y + 1] - dy[y]),
					new Rectangle(sx[x], sy[y], sx[x + 1] - sx[x], sy[y + 1] - sy[y]), color);
	}
}
