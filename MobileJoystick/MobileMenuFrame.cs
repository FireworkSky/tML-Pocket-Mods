using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MobileJoystick;

// Draw the original pixel-art corners without stretching them into the edges.
internal static class MobileMenuFrame
{
	internal static void ToggleBounds(Rectangle row, float scale, out Rectangle off, out Rectangle on)
	{
		int width = Math.Max(2, (int)(130 * scale));
		int height = Math.Max(2, (int)(34 * scale));
		int right = row.Right - (int)(7 * scale);
		off = new Rectangle(right - width, row.Center.Y - height / 2, width / 2, height);
		on = new Rectangle(off.Right, off.Y, width - off.Width, height);
	}

	internal static void Toggle(SpriteBatch batch, Texture2D button, Texture2D selectedOn, Rectangle off, Rectangle on, bool enabled, float scale)
	{
		int inset = Math.Max(1, (int)(4 * scale));
		Rectangle background = new(off.X, off.Y + inset, off.Width + on.Width, Math.Max(1, off.Height - 2 * inset));
		Draw(batch, button, background, new Color(90, 99, 125), scale, 8);
		Draw(batch, enabled ? selectedOn : button, enabled ? on : off,
			enabled ? Color.White : new Color(165, 174, 190), scale, 8);
	}

	internal static Rectangle SliderTrack(Rectangle row, float scale) => new(
		row.X + (int)(305 * scale), row.Center.Y - Math.Max(2, (int)(7 * scale)),
		Math.Max(1, (int)(178 * scale)), Math.Max(4, (int)(14 * scale)));

	internal static void Slider(SpriteBatch batch, Texture2D knob, Rectangle track, float fraction, float scale)
	{
		Texture2D pixel = Terraria.GameContent.TextureAssets.MagicPixel.Value;
		batch.Draw(pixel, track, new Color(8, 11, 20));
		int inset = Math.Max(1, (int)(2 * scale));
		Rectangle inner = new(track.X + inset, track.Y + inset, Math.Max(1, track.Width - inset * 2), Math.Max(1, track.Height - inset * 2));
		batch.Draw(pixel, inner, new Color(52, 56, 68));
		int width = Math.Max(1, (int)(25 * scale)), height = Math.Max(1, (int)(32 * scale));
		int x = track.X + (int)(MathHelper.Clamp(fraction, 0, 1) * track.Width);
		batch.Draw(knob, new Rectangle(x - width / 2, track.Center.Y - height / 2, width, height), Color.White);
	}

	internal static void DropdownDivider(SpriteBatch batch, Rectangle row, float scale)
	{
		int width = Math.Max(1, (int)MathF.Round(2 * scale));
		int x = row.Right - (int)(44 * scale);
		int inset = Math.Max(1, (int)(3 * scale));
		batch.Draw(Terraria.GameContent.TextureAssets.MagicPixel.Value,
			new Rectangle(x, row.Top + inset, width, row.Height - inset * 2), new Color(66, 86, 151));
	}

	internal static void Draw(SpriteBatch batch, Texture2D texture, Rectangle destination, Color tint, float scale, int corner = 20, Color? bottomTint = null)
	{
		int sourceX = Math.Min(corner, texture.Width / 2);
		int sourceY = Math.Min(corner, texture.Height / 2);
		int edgeX = Math.Min(Math.Max(1, (int)MathF.Round(sourceX * scale)), destination.Width / 2);
		int edgeY = Math.Min(Math.Max(1, (int)MathF.Round(sourceY * scale)), destination.Height / 2);
		int[] sx = { 0, sourceX, texture.Width - sourceX, texture.Width };
		int[] sy = { 0, sourceY, texture.Height - sourceY, texture.Height };
		int[] dx = { destination.Left, destination.Left + edgeX, destination.Right - edgeX, destination.Right };
		int[] dy = { destination.Top, destination.Top + edgeY, destination.Bottom - edgeY, destination.Bottom };
		for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) {
			if (dx[x + 1] <= dx[x] || dy[y + 1] <= dy[y] || sx[x + 1] <= sx[x] || sy[y + 1] <= sy[y]) continue;
			if (bottomTint.HasValue) {
				// Shade the original sprite mask, including its pixel corners. An
				// unmasked rectangle here would reintroduce square corner fringes.
				for (int row = dy[y]; row < dy[y + 1]; row++) {
					int sourceRow = sy[y] + (row - dy[y]) * (sy[y + 1] - sy[y]) / (dy[y + 1] - dy[y]);
					Color shade = Color.Lerp(tint, bottomTint.Value, (row - destination.Top) / (float)System.Math.Max(1, destination.Height - 1));
					batch.Draw(texture, new Rectangle(dx[x], row, dx[x + 1] - dx[x], 1),
						new Rectangle(sx[x], sourceRow, sx[x + 1] - sx[x], 1), shade);
				}
				continue;
			}
			batch.Draw(texture, new Rectangle(dx[x], dy[y], dx[x + 1] - dx[x], dy[y + 1] - dy[y]),
				new Rectangle(sx[x], sy[y], sx[x + 1] - sx[x], sy[y + 1] - sy[y]), tint);
		}
	}
}
