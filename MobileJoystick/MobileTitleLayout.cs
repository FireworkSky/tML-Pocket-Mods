using System;
using Microsoft.Xna.Framework;

namespace MobileJoystick;

internal readonly struct MobileTitleLayout
{
	public readonly float Scale;
	public readonly int Width, Height;
	public MobileTitleLayout(int width, int height)
	{
		Width = width; Height = height;
		Scale = Math.Min(width / 1280f, height / 576f);
	}
	public Rectangle Entry(int index) => Center(168 + index * 54, 380, 44);
	private Rectangle Center(float y, float width, float height) => new((int)(Width / 2f - width * Scale / 2),
		(int)((Height - 576 * Scale) / 2 + y * Scale), Math.Max(1, (int)(width * Scale)), Math.Max(1, (int)(height * Scale)));
}
