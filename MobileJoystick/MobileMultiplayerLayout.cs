using System;
using Microsoft.Xna.Framework;

namespace MobileJoystick;

internal readonly struct MobileMultiplayerLayout
{
	public readonly float Scale;
	private readonly float x, y;

	public MobileMultiplayerLayout(int width, int height)
	{
		Scale = Math.Min(width / 1280f, height / 576f);
		x = (width - 1280 * Scale) / 2;
		y = (height - 576 * Scale) / 2;
	}

	public Rectangle At(float left, float top, float width, float height) => new(
		(int)(x + left * Scale), (int)(y + top * Scale),
		Math.Max(1, (int)(width * Scale)), Math.Max(1, (int)(height * Scale)));
	public Rectangle Panel => At(366, 54, 548, 488);
	public Rectangle Title => At(486, 24, 308, 44);
	public Rectangle JoinTab => At(466, 76, 174, 46);
	public Rectangle HostTab => At(640, 76, 174, 46);
	public Rectangle LocalRow => At(369, 142, 542, 44);
	public Rectangle LocalStatus => At(379, 186, 522, 60);
	public Rectangle RemoteRow => RemoteHeader(true);
	public Rectangle RemoteHeader(bool localExpanded) => At(369, localExpanded ? 246 : 186, 542, 44);
	public Rectangle RemoteStatus(bool localExpanded) => At(379, localExpanded ? 290 : 230, 522, 58);
	public Rectangle Status => At(379, 350, 522, 120);
	public Rectangle Back => At(379, 484, 184, 46);
	public Rectangle Add => At(715, 484, 186, 46);
	public Rectangle Hint => At(366, 545, 548, 26);
	public Rectangle FormPanel => At(366, 175, 548, 303);
	public Rectangle FormTitle => At(456, 146, 368, 44);
	public Rectangle Field(int index) => At(379, 199 + index * 52, 522, 46);
	public Rectangle FormBack => At(379, 420, 184, 46);
	public Rectangle FormJoin => At(715, 420, 186, 46);
	public Rectangle HostPanel => At(366, 123, 548, 396);
	public Rectangle HostTitle => At(486, 95, 308, 44);
	public Rectangle WorldSummary => At(379, 145, 522, 72);
	public Rectangle HostOption(int index) => At(379, 235 + index * 52, 522, 48);
	public Rectangle Slider => At(696, 251, 188, 18);
	public Rectangle HostBack => At(379, 460, 184, 46);
	public Rectangle HostStart => At(715, 460, 186, 46);
	public Rectangle Toggle(Rectangle row, bool on) => new(row.Right - (int)((on ? 80 : 150) * Scale),
		row.Y + (int)(6 * Scale), (int)(70 * Scale), (int)(36 * Scale));
}
