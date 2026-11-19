using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;
using Terraria;

namespace MobileJoystick;

internal static partial class MobileJoystickInput
{
    private static int interactFingerId = int.MinValue, interactContactThisFrame = int.MinValue;
    private static MobileControlButton interactButton;
    private static Vector2 interactStart, interactPrevious, interactAim;
    private static bool interactHasAim;
    internal static bool InteractHeld => interactFingerId != int.MinValue;
    private static bool IsInteractContact(int id) => id == interactFingerId || id == interactContactThisFrame;

    private static void ResetInteractAim()
    {
        interactFingerId = interactContactThisFrame = int.MinValue;
        interactHasAim = false;
    }

    private static void UpdateInteractAim()
    {
        interactContactThisFrame = int.MinValue;
        if (InteractHeld) {
            bool found = false;
            foreach (MobileTouchContact contact in Contacts) {
                if (contact.Id != interactFingerId) continue;
                found = true;
                interactContactThisFrame = contact.Id;
                if (contact.IsReleased || !IsButtonAvailable(interactButton) || !Main.mouseItem.IsAir) {
                    interactFingerId = int.MinValue;
                    interactHasAim = false;
                    break;
                }
                if (Vector2.DistanceSquared(contact.Position, interactStart) > 16f) interactHasAim = true;
                interactAim += contact.Position - interactPrevious;
                interactAim = Vector2.Clamp(interactAim, Vector2.Zero, new(Main.screenWidth - 1f, Main.screenHeight - 1f));
                interactPrevious = contact.Position;
                break;
            }
            if (!found) ResetInteractAim();
        }
        if (!InteractHeld && interactContactThisFrame == int.MinValue) {
            foreach (MobileTouchContact contact in Contacts) {
                if (contact.State != TouchLocationState.Pressed || IsGrappleContact(contact.Id) || IsRadialContact(contact.Id)
                    || contact.Id == joystickFingerId || contact.Id == attackFingerId
                    || MobileInventorySystem.BlocksControl(contact.Position) || MobileInventorySystem.OwnsContact(contact.Id)) continue;
                foreach (MobileControlButton button in Layout.Buttons) {
                    if (button.Action != MobileControlAction.Interact || !IsButtonAvailable(button)
                        || !button.Contains(contact.Position) || !CanActivateCustomButton(button, contact)) continue;
                    interactFingerId = interactContactThisFrame = contact.Id;
                    interactButton = button;
                    interactStart = interactPrevious = contact.Position;
                    interactAim = hasTouchAimPosition ? TouchAimPosition
                        : MobileWorldInteraction.ToScreen(Main.LocalPlayer.MountedCenter)
                            + new Vector2(Main.LocalPlayer.direction * 80f, 0f) * Main.GameViewMatrix.Zoom;
                    interactHasAim = false;
                    break;
                }
                if (InteractHeld) break;
            }
        }
        if (InteractHeld) CurrentActions.Add(MobileControlAction.Interact);
        if (interactContactThisFrame != int.MinValue) HasAnyControlTouch = true;
    }
}
