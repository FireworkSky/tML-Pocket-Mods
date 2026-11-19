using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;
using Terraria;

namespace MobileJoystick;

internal static partial class MobileJoystickInput
{
	private static readonly MobileGrappleControl Grapple = new();
	private static int grappleContactThisFrame = int.MinValue;
	private static Vector2? pendingGrappleDirection;
	internal static bool GrappleHeld => Grapple.Active;
	internal static MobileGrappleControl ActiveGrapple => Grapple;
	private static bool IsGrappleContact(int id) => (Grapple.Active && Grapple.ContactId == id) || grappleContactThisFrame == id;

	private static void UpdateGrappleControl()
	{
		pendingGrappleDirection = null;
		grappleContactThisFrame = int.MinValue;
		if (Grapple.Active) {
			MobileControlButton? currentButton = null;
			foreach (MobileControlButton button in Layout.Buttons)
				if (button.Action == MobileControlAction.Grapple && button.Id == Grapple.Button.Id && button.Center == Grapple.Button.Center
					&& button.Radius == Grapple.Button.Radius) { currentButton = button; break; }
			if (!currentButton.HasValue) Grapple.Reset();
		}
		if (Grapple.Active) {
			bool found = false;
			foreach (MobileTouchContact contact in Contacts) {
				if (contact.Id != Grapple.ContactId) continue;
				found = true;
				grappleContactThisFrame = contact.Id;
				if (!IsButtonAvailable(Grapple.Button) || !Main.mouseItem.IsAir) { Grapple.Reset(); break; }
				Vector2? direction = Grapple.Update(contact);
				if (direction.HasValue && Main.LocalPlayer.QuickGrapple_GetItemToUse() != null) {
					pendingGrappleDirection = direction;
					CurrentActions.Add(MobileControlAction.Grapple);
				}
				break;
			}
			if (!found) Grapple.Reset();
		}
		if (!Grapple.Active) {
			foreach (MobileTouchContact contact in Contacts) {
				if (contact.State != TouchLocationState.Pressed || IsGrappleContact(contact.Id) || IsRadialContact(contact.Id)
					|| contact.Id == joystickFingerId || contact.Id == attackFingerId || !Main.mouseItem.IsAir
					|| MobileInventorySystem.BlocksControl(contact.Position) || MobileInventorySystem.OwnsContact(contact.Id)) continue;
				foreach (MobileControlButton button in Layout.Buttons) {
					if (button.Action != MobileControlAction.Grapple || !IsButtonAvailable(button)
						|| !button.Contains(contact.Position) || !CanActivateCustomButton(button, contact)) continue;
					Vector2 fallback = hasTouchAimPosition
						? touchAimPosition - Main.ReverseGravitySupport(Main.LocalPlayer.Center - Main.screenPosition)
						: new Vector2(Main.LocalPlayer.direction, 0f);
					if (fallback.LengthSquared() < .0001f) fallback = new Vector2(Main.LocalPlayer.direction, 0f);
					Grapple.Begin(button, contact, fallback);
					grappleContactThisFrame = contact.Id;
					break;
				}
				if (Grapple.Active) break;
			}
		}
		if (Grapple.Active || grappleContactThisFrame != int.MinValue) HasAnyControlTouch = true;
	}

	internal static void AimNativeGrapple(On_Player.orig_QuickGrapple orig, Player player)
	{
		if (player.whoAmI != Main.myPlayer || !EnabledThisFrame || !pendingGrappleDirection.HasValue) {
			orig(player);
			return;
		}
		Vector2 direction = pendingGrappleDirection.Value;
		pendingGrappleDirection = null;
		int oldX = Main.mouseX, oldY = Main.mouseY;
		try {
			// Native QuickGrapple handles gravity when reading the cursor. The
			// captured direction is already in display coordinates.
			Vector2 origin = Main.ReverseGravitySupport(player.Center - Main.screenPosition);
			Vector2 cursor = origin + direction * 440f;
			Main.mouseX = (int)cursor.X;
			Main.mouseY = (int)cursor.Y;
			orig(player);
		}
		finally { Main.mouseX = oldX; Main.mouseY = oldY; }
	}
}
