using System;
using Microsoft.Xna.Framework;

namespace MobileJoystick;

internal readonly struct MobileSelectionLayout
{
	public readonly float Scale;
	private readonly float x, y;
	private readonly bool multiplayer, world;
	public MobileSelectionLayout(int width, int height, bool multiplayer = false, bool world = false)
	{
		this.multiplayer = multiplayer;
		this.world = world;
		Scale = Math.Min(width / 1280f, height / 576f);
		x = (width - 1280 * Scale) / 2; y = (height - 576 * Scale) / 2;
	}
	public Rectangle At(float left, float top, float width, float height) => new((int)(x + left * Scale), (int)(y + top * Scale),
		Math.Max(1, (int)(width * Scale)), Math.Max(1, (int)(height * Scale)));
	public Rectangle Panel => At(366, 54, 548, 488);
	public Rectangle Title => At(486, 24, 308, 44);
	public Rectangle List => At(379, multiplayer ? 140 : 76, 522, multiplayer ? 333 : 397);
	public Rectangle Back => At(379, 484, 184, 46);
	public Rectangle New => At(715, 484, 186, 46);
	public Rectangle Native => At(1100, 8, 168, 38);
	public int CardHeight(bool expanded) => world ? (expanded ? 140 : 84) : (expanded ? 150 : 92);
	public Rectangle Card(float top, bool expanded) => At(379, (multiplayer ? 140 : 76) + top, 522, CardHeight(expanded));
	public Rectangle CardAt(Rectangle card, float left, float top, float width, float height) => new(card.X + (int)(left * Scale),
		card.Y + (int)(top * Scale), Math.Max(1, (int)(width * Scale)), Math.Max(1, (int)(height * Scale)));
	public Rectangle Action(Rectangle card, int index) => CardAt(card, 10 + index * 52, world ? 84 : 92, 46, 46);
	public Rectangle Play(Rectangle card) => CardAt(card, 326, world ? 84 : 92, 186, 46);
}
