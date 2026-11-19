using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;

namespace MobileJoystick;

// Owns one contact until explicit release; a missing contact cancels the gesture.
internal sealed class MobileRadialControl
{
	public int ContactId { get; private set; } = int.MinValue;
	public bool Active => ContactId != int.MinValue;
	public MobileControlButton Button { get; private set; }
	public MobileControlAction? Selection { get; private set; }
	public int SelectedSlot { get; private set; } = -1;
	public float OuterRadius => Button.Radius * (Button.Action == MobileControlAction.QuickPotions ? 2.7f : 2.8f);

	public void Begin(MobileControlButton button, MobileTouchContact contact)
	{
		Button = button;
		ContactId = contact.Id;
		Selection = null;
		SelectedSlot = -1;
	}

	public MobileControlAction? Update(MobileTouchContact contact)
	{
		if (!Active || contact.Id != ContactId) return null;
		if (contact.State == TouchLocationState.Invalid) { Reset(); return null; }
		Selection = Select(contact.Position - Button.Center);
		if (contact.State != TouchLocationState.Released) return null;
		MobileControlAction? result = Button.Action is MobileControlAction.QuickPotions or MobileControlAction.RadialMenu ? Selection : null;
		Reset();
		return result;
	}

	private MobileControlAction? Select(Vector2 offset)
	{
		SelectedSlot = -1;
		if (offset.LengthSquared() < Button.Radius * Button.Radius * 0.64f) return null;
		SelectedSlot = Math.Abs(offset.Y) >= Math.Abs(offset.X)
			? offset.Y < 0 ? 0 : 2 : offset.X > 0 ? 1 : 3;
		if (Button.Action == MobileControlAction.RadialMenu) {
			int slot = SelectedSlot;
			if (Button.RadialActions == null || slot >= Button.RadialActions.Count) return null;
			MobileControlAction action = Button.RadialActions[slot];
			return action == MobileControlAction.Unbound ? null : action;
		}
		if (Button.Action == MobileControlAction.Zoom)
			return Math.Abs(offset.X) < Button.Radius * 0.8f ? null
				: offset.X < 0 ? MobileControlAction.ZoomOut : MobileControlAction.ZoomIn;
		if (Math.Abs(offset.Y) >= Math.Abs(offset.X))
			return offset.Y < 0 ? MobileControlAction.Heal : MobileControlAction.Buff;
		return offset.X > 0 ? MobileControlAction.Mana : null;
	}

	public void Reset()
	{
		ContactId = int.MinValue;
		Selection = null;
		SelectedSlot = -1;
	}
}
