using System;
using Microsoft.Xna.Framework;

namespace MobileJoystick;

internal readonly struct MobileChatLayout
{
	internal readonly float Scale;
	internal readonly Rectangle Panel;
	internal readonly Rectangle History;
	internal readonly Rectangle Input;
	internal readonly Rectangle Emote;
	internal readonly Rectangle EmotesPanel;
	internal readonly Rectangle EmotesTitle;
	internal readonly Rectangle EmotesClose;
	internal MobileChatLayout(int width, int height)
	{
		Scale = Math.Min(width / 1280f, height / 576f);
		float s = Scale;
		Rectangle At(float x, float y, float w, float h) => new((int)(x * s), (int)(y * s), Math.Max(1,(int)(w * s)), Math.Max(1,(int)(h * s)));
		float right = width / s;
		Panel = At(right - 434, 64, 364, 220);
		History = At(right - 424, 74, 344, 168);
		Input = At(right - 424, 246, 320, 34);
		Emote = At(right - 104, 246, 30, 34);
		float center = right / 2;
		float middle = (height / s - 576) * .5f;
		EmotesPanel = At(center - 290, middle + 168, 580, 334);
		EmotesTitle = At(center - 200, middle + 138, 400, 48);
		EmotesClose = At(center - 276, middle + 440, 176, 46);
	}
	internal Rectangle Category(int index) => new(EmotesPanel.Center.X + (int)((index - 3.5f) * 52 * Scale),
		EmotesPanel.Y + (int)(26 * Scale), (int)(52 * Scale), (int)(48 * Scale));
	internal Rectangle EmoteCell(int index) => new(EmotesPanel.X + (int)((18 + index % 10 * 54) * Scale),
		EmotesPanel.Y + (int)((94 + index / 10 * 54) * Scale), (int)(50 * Scale), (int)(50 * Scale));
}
