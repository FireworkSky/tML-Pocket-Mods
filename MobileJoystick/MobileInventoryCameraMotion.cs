using System;

namespace MobileJoystick;

// Screen-space movement measured from the phone, independent of world updates.
internal sealed class MobileInventoryCameraMotion
{
	internal const float ScreenHeightFraction = .30f;
	internal const double DurationSeconds = .95;
	private float from, target;
	private double started, lastTime;

	internal float Advance(bool open, double now)
	{
		now = Math.Max(lastTime, now);
		lastTime = now;
		float current = Evaluate(now);
		float next = open ? 1f : 0f;
		if (next != target) {
			from = current;
			target = next;
			started = now;
		}
		return current;
	}

	private float Evaluate(double now)
	{
		float t = (float)Math.Clamp((now - started) / DurationSeconds, 0d, 1d);
		float eased = t < .5f ? 4f * t * t * t : 1f - MathF.Pow(2f - 2f * t, 3f) / 2f;
		return from + (target - from) * eased;
	}

	internal static float WorldOffset(float fraction, int screenHeight, float zoom, float gravity)
		=> fraction * screenHeight * ScreenHeightFraction / Math.Max(.01f, zoom) * (gravity < 0f ? 1f : -1f);

	internal static float EffectiveOffset(float baseY, float offset, float minY, float maxY)
		=> Math.Clamp((int)(baseY + offset), minY, maxY) - Math.Clamp((int)baseY, minY, maxY);

	internal void Reset()
	{
		from = target = 0f;
		started = lastTime = 0d;
	}
}
