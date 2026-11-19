using System;
using Microsoft.Xna.Framework;

namespace MobileJoystick;

internal readonly struct MobileMenuLayout
{
	public readonly float Scale;
	public readonly Rectangle Panel;
	public readonly bool Settings;

	public MobileMenuLayout(int width, int height, bool settings, int pauseRows = 6)
	{
		Settings = settings;
		Scale = Math.Min(width / 1280f, height / 576f);
		int w = (int)((settings ? 536 : 336) * Scale);
		int h = (int)((settings ? 480 : 20 + 60 * Math.Clamp(pauseRows, 6, 8)) * Scale);
		Panel = new Rectangle((width - w) / 2, (height - h) / 2 + (settings ? (int)(10 * Scale) : 0), w, h);
	}

	public Rectangle At(float x, float y, float width, float height) => new(
		Panel.X + (int)(x * Scale), Panel.Y + (int)(y * Scale),
		Math.Max(1, (int)(width * Scale)), Math.Max(1, (int)(height * Scale)));
	public Rectangle Title => At(Settings ? 113 : 34, -28, Settings ? 310 : 260, 44);
	public Rectangle Tab(int index) => At(20 + index * 62, 24, 62, 46);
	public Rectangle Viewport => At(12, 84, 512, 326);
	public Rectangle Row(int index, float scroll) => At(12, 84 + index * 52 - scroll, 502, 46);
	public Rectangle Back => At(12, 426, 174, 42);
	public Rectangle PauseButton(int index) => At(12, 12 + index * 60, 312, 54);
}
