using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;

namespace MobileJoystick;

// One captured contact aims from the button's center until explicit release.
internal sealed class MobileGrappleControl
{
	internal int ContactId { get; private set; } = int.MinValue;
	internal bool Active => ContactId != int.MinValue;
	internal MobileControlButton Button { get; private set; }
	internal Vector2 Direction { get; private set; }
	internal Vector2 KnobOffset { get; private set; }

	internal void Begin(MobileControlButton button, MobileTouchContact contact, Vector2 fallbackDirection)
	{
		Button = button;
		ContactId = contact.Id;
		Direction = fallbackDirection.LengthSquared() > .0001f ? Vector2.Normalize(fallbackDirection) : Vector2.UnitX;
		KnobOffset = Vector2.Zero;
	}

	internal Vector2? Update(MobileTouchContact contact)
	{
		if (!Active || contact.Id != ContactId) return null;
		if (contact.State == TouchLocationState.Invalid) { Reset(); return null; }
		Vector2 delta = contact.Position - Button.Center;
		float radius = System.Math.Max(1f, Button.Radius);
		KnobOffset = delta / radius;
		if (KnobOffset.LengthSquared() > 1f) KnobOffset = Vector2.Normalize(KnobOffset);
		if (delta.LengthSquared() >= radius * radius * .04f) Direction = Vector2.Normalize(delta);
		if (contact.State != TouchLocationState.Released) return null;
		Vector2 result = Direction;
		Reset();
		return result;
	}

	internal void Reset()
	{
		ContactId = int.MinValue;
		Direction = KnobOffset = Vector2.Zero;
	}
}
