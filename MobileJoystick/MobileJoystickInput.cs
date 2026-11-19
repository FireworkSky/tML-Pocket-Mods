using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace MobileJoystick;

public enum MobileControlAction
{
	Jump,
	Grapple,
	Heal,
	Mana,
	Buff,
	Mount,
	SmartInteract,
	SmartCursor,
	AimModeToggle,
	TargetLockToggle,
	Interact,
	UseItem,
	Inventory,
	Map,
	MapStyle,
	Settings,
	Chat,
	ZoomIn,
	ZoomOut,
	QuickPotions,
	Zoom,
	RadialMenu,
	Unbound = 22,
	SwapUseInteract,
	CycleUseProfile,
	QuickMount,
	AutoSelect,
	Loadout1,
	Loadout2,
	Loadout3,
	SwitchLoadout,
	ModKeybind
}

internal readonly struct MobileControlButton
{
	public MobileControlButton(MobileControlAction action, string textureName, Vector2 center, float radius,
		string id = null, bool multitouch = true, IReadOnlyList<MobileControlAction> radialActions = null, string modKeybind = null, IReadOnlyList<string> radialModKeybinds = null)
	{
		Action = action;
		TextureName = textureName;
		Center = center;
		Radius = radius;
		Id = id;
		Multitouch = multitouch;
		RadialActions = radialActions;
		ModKeybind = modKeybind;
		RadialModKeybinds = radialModKeybinds;
	}

	public MobileControlAction Action { get; }

	public string TextureName { get; }

	public Vector2 Center { get; }

	public float Radius { get; }

	public string Id { get; }

	public bool Multitouch { get; }

	public IReadOnlyList<MobileControlAction> RadialActions { get; }

	public string ModKeybind { get; }
	public IReadOnlyList<string> RadialModKeybinds { get; }

	public Rectangle Bounds => new((int)(Center.X - Radius), (int)(Center.Y - Radius), (int)(Radius * 2f), (int)(Radius * 2f));

	public bool Contains(Vector2 point) => Action is MobileControlAction.Inventory or MobileControlAction.Chat or MobileControlAction.Map or MobileControlAction.MapStyle
		? Bounds.Contains(point.ToPoint())
		: Vector2.DistanceSquared(point, Center) <= Radius * Radius;
}

internal readonly record struct MobileControlStick(string Id, MobileJoystickRole Role, Vector2 Center, float Radius);

internal readonly struct MobileControlLayout
{
	public MobileControlLayout(Vector2 joystickCenter, float joystickRadius, Vector2 attackCenter, float attackRadius, MobileControlButton[] buttons, float scale, IReadOnlyList<MobileControlStick> sticks = null)
	{
		JoystickCenter = joystickCenter;
		JoystickRadius = joystickRadius;
		AttackCenter = attackCenter;
		AttackRadius = attackRadius;
		Buttons = buttons;
		Scale = scale;
		Sticks = sticks ?? new[] { new MobileControlStick(null, MobileJoystickRole.Move, joystickCenter, joystickRadius),
			new MobileControlStick(null, MobileJoystickRole.Aim, attackCenter, attackRadius) };
	}

	public Vector2 JoystickCenter { get; }

	public float JoystickRadius { get; }

	public Vector2 AttackCenter { get; }

	public float AttackRadius { get; }

	public MobileControlButton[] Buttons { get; }

	public float Scale { get; }
	public IReadOnlyList<MobileControlStick> Sticks { get; }
}

internal readonly struct MobileTouchContact
{
	public MobileTouchContact(int id, Vector2 position, TouchLocationState state)
	{
		Id = id;
		Position = position;
		State = state;
	}

	public int Id { get; }

	public Vector2 Position { get; }

	public TouchLocationState State { get; }

	public bool IsReleased => State == TouchLocationState.Released || State == TouchLocationState.Invalid;
}

internal static partial class MobileJoystickInput
{
	private const int MouseContactId = -1000;
	private const int StatusTextFrames = 150;

	private static readonly List<MobileTouchContact> Contacts = new();
	private static readonly HashSet<int> touchFingerIds = new();
	private static readonly HashSet<int> currentTouchFingerIds = new();
	private static readonly HashSet<MobileControlAction> CurrentActions = new();
	private static readonly HashSet<MobileControlAction> PreviousActions = new();
	private static readonly HashSet<MobileControlAction> PressedActions = new();

	private static int joystickFingerId = int.MinValue;
	private static int attackFingerId = int.MinValue;
	private static MobileControlStick activeMoveStick, activeAimStick;
	internal static MobileControlStick MoveStick => activeMoveStick.Radius > 0 ? activeMoveStick : new(null, MobileJoystickRole.Move, Layout.JoystickCenter, Layout.JoystickRadius);
	internal static MobileControlStick AimStick => activeAimStick.Radius > 0 ? activeAimStick : new(null, MobileJoystickRole.Aim, Layout.AttackCenter, Layout.AttackRadius);
	private static Vector2 touchAimPosition;
	private static Vector2 previousAttackTouchPosition;
	private static Vector2? previousAimOrigin;
	private static bool hasTouchAimPosition;
	private static int statusTextTime;
	private static bool mouseWasDown;
	private static bool waitForMouseRelease;
	private static bool gameplayUseWasDown;
	private static readonly MobileRadialControl Radial = new();
	private static int radialContactThisFrame = int.MinValue;
	internal static MobileRadialControl ActiveRadial => Radial;
	private static bool IsRadialContact(int id) => (Radial.Active && Radial.ContactId == id) || radialContactThisFrame == id;

	public static MobileControlLayout Layout { get; private set; }

	public static Vector2 JoystickVector { get; private set; }

	public static bool AttackHeld { get; private set; }
	internal static bool AttackJustPressed { get; private set; }

	public static Vector2 TouchAimPosition => interactHasAim ? interactAim : touchAimPosition
		+ (previousAimOrigin.HasValue ? MobileWorldInteraction.ToScreen(Main.LocalPlayer.MountedCenter) - previousAimOrigin.Value : Vector2.Zero);

	public static bool HasTouchAimPosition => interactHasAim || hasTouchAimPosition;

	internal static Vector2 TouchCrosshairPosition => MobileWorldInteraction.PrecisionHeld
		? MobileWorldInteraction.PrecisionPosition
		: HasTouchAimPosition ? TouchAimPosition : MobileWorldInteraction.ToScreen(Main.MouseWorld);

	internal static bool ShowTouchCrosshair => MobileWorldInteraction.PrecisionHeld || AttackHeld || InteractHeld
		|| IsActionDown(MobileControlAction.UseItem);

	public static bool HasAnyControlTouch { get; private set; }

	public static bool EnabledThisFrame { get; private set; }

	public static bool AimAndUseMode { get; private set; } = true;

	public static bool TargetLockEnabled { get; private set; }

	public static bool SmartCursorEnabled { get; private set; } = true;

	public static bool InteractPrefersRightClickItem { get; private set; }

	public static string StatusText { get; private set; } = string.Empty;

	public static float StatusTextOpacity => MathHelper.Clamp(statusTextTime / 30f, 0f, 1f);

	public static Vector2 AttackKnobOffset { get; private set; }

	public static IReadOnlyCollection<MobileControlAction> ActiveActions => CurrentActions;

	public static IReadOnlyList<MobileTouchContact> ActiveContacts => Contacts;

	public static bool IsActionDown(MobileControlAction action) => CurrentActions.Contains(action);

	public static bool WasActionPressed(MobileControlAction action) => PressedActions.Contains(action);

	public static bool IsFingerOnJoystick(int fingerId) => joystickFingerId == fingerId;

	public static bool IsFingerOnAttack(int fingerId) => attackFingerId == fingerId;

	public static void Reset()
	{
		gameplayUseWasDown = false;
		MobileWorldInteraction.Reset();
		ResetInteractAim();
		MobileModKeybinds.Reset();
		ResetMapInteraction();
		Grapple.Reset();
		pendingGrappleDirection = null;
		grappleContactThisFrame = int.MinValue;
		Radial.Reset();
		radialContactThisFrame = int.MinValue;
		Contacts.Clear();
		touchFingerIds.Clear();
		currentTouchFingerIds.Clear();
		CurrentActions.Clear();
		PreviousActions.Clear();
		PressedActions.Clear();
		joystickFingerId = attackFingerId = int.MinValue;
		activeMoveStick = activeAimStick = default;
		JoystickVector = AttackKnobOffset = Vector2.Zero;
		AttackHeld = AttackJustPressed = HasAnyControlTouch = EnabledThisFrame = false;
		InteractPrefersRightClickItem = TargetLockEnabled = false;
		SmartCursorEnabled = true;
		AimAndUseMode = true;
		StatusText = string.Empty;
		statusTextTime = 0;
		mouseWasDown = false;
		waitForMouseRelease = true;
		ResetTouchAim();
		LockOnHelper.ForceUsability = false;
		if (!Main.dedServ) LockOnHelper.Toggle(forceOff: true);
	}

	internal static bool IsButtonAvailable(MobileControlButton button)
	{
		if (Main.mapFullscreen) return button.Action is MobileControlAction.Chat or MobileControlAction.Map or MobileControlAction.MapStyle
			or MobileControlAction.ZoomIn or MobileControlAction.ZoomOut;
		if (MobileChatSystem.IsOpen) return false;
		if (Main.playerInventory && button.Action is MobileControlAction.Inventory or MobileControlAction.Settings
			or MobileControlAction.Chat or MobileControlAction.Map or MobileControlAction.MapStyle) return false;
		return !MobileInventorySystem.BlocksControl(button.Center);
	}

	public static void Update()
	{
		bool previousAttackHeld = AttackHeld;
		AttackJustPressed = false;
		if (statusTextTime > 0) {
			statusTextTime--;
		}

		MobileJoystickConfig config = MobileJoystickConfig.Instance;
		if (!Main.hasFocus) waitForMouseRelease = true;
		EnabledThisFrame = ShouldUseControls(config);
		MobileModKeybinds.BeginFrame();
		PreviousActions.Clear();
		foreach (MobileControlAction action in CurrentActions) {
			PreviousActions.Add(action);
		}

		CurrentActions.Clear();
		PressedActions.Clear();
		Contacts.Clear();
		JoystickVector = Vector2.Zero;
		AttackHeld = false;
		AttackKnobOffset = Vector2.Zero;
		HasAnyControlTouch = false;
		InteractPrefersRightClickItem = false;
		radialContactThisFrame = int.MinValue;

		if (!EnabledThisFrame) {
			ResetMapInteraction();
			ResetInteractAim();
			Grapple.Reset();
			pendingGrappleDirection = null;
			grappleContactThisFrame = int.MinValue;
			Radial.Reset();
			if (Main.hasFocus && config?.EnableMobileControls == true && (config.UseMobileInventory && Main.playerInventory || MobileChatSystem.IsOpen || Main.drawingPlayerChat) && !Main.gameMenu && !Main.dedServ && !Main.ingameOptionsWindow && !Main.inFancyUI)
				ReadTouchContacts(config);
			joystickFingerId = int.MinValue;
			attackFingerId = int.MinValue;
			ResetTouchAim();
			LockOnHelper.ForceUsability = false;
			return;
		}

		LockOnHelper.ForceUsability = TargetLockEnabled;

		Layout = CreateLayout(config);
		ReadTouchContacts(config);
		UpdateRadialControls();
		UpdateGrappleControl();
		UpdateInteractAim();
		UpdateFingerAssignments();
		UpdateAnalogControls(config);
		AttackJustPressed = AttackHeld && !previousAttackHeld;
		UpdateButtonActions();
		if (!Main.playerInventory && !Main.mapFullscreen) UpdateHotbarSelection(config);
		ApplyImmediateActions();
		// Cache the logical aim before native world/UI zoom contexts restore the
		// input. Movement, jump and toolbar fingers must never become the aim.
		if ((HasAnyControlTouch || hasTouchAimPosition && Contacts.Count == 0 && MobileTouchInput.UsingTouch)
			&& !Main.playerInventory && !Main.mapFullscreen && Main.LocalPlayer.talkNPC < 0) {
			MobileJoystickPlayer.ApplyTouchAim();
			if (!MobileWorldInteraction.OwnsWorldPress) {
				Main.mouseLeft = PlayerInput.Triggers.Current.MouseLeft = false;
				PlayerInput.Triggers.JustPressed.MouseLeft = false;
			}
		}
		UpdateHeldZoom();
		UpdateFullscreenMapGesture();

		if (HasAnyControlTouch && Main.myPlayer >= 0 && Main.myPlayer < Main.maxPlayers) {
			Main.LocalPlayer.mouseInterface = true;
		}
	}

	public static bool ContactBelongsToControl(MobileTouchContact contact)
	{
		if (EnabledThisFrame && (IsGrappleContact(contact.Id) || IsInteractContact(contact.Id))) return true;
		if (EnabledThisFrame && IsRadialContact(contact.Id)) return true;
		if (!EnabledThisFrame || MobileInventorySystem.OwnsContact(contact.Id)
			|| MobileInventorySystem.BlocksControl(contact.Position)) return false;
		if (contact.IsReleased) {
			return joystickFingerId == contact.Id || attackFingerId == contact.Id;
		}

		if (joystickFingerId == contact.Id || attackFingerId == contact.Id) {
			return true;
		}

		if (TouchesStick(contact)) return true;

		foreach (MobileControlButton button in Layout.Buttons) {
			if (IsButtonAvailable(button) && button.Contains(contact.Position)) {
				return true;
			}
		}

		if (Main.playerInventory) return false;
		MobileHotbarLayout hotbar = MobileJoystickLayout.CreateHotbarLayout(MobileJoystickConfig.Instance);
		for (int i = 0; i < 10; i++) {
			if (hotbar.GetSlot(i).Contains(contact.Position.ToPoint())) {
				return true;
			}
		}

		return false;
	}

	private static bool ShouldUseControls(MobileJoystickConfig config)
	{
		if (config == null || !config.EnableMobileControls) {
			return false;
		}

		if (Main.dedServ || Main.gameMenu || !Main.hasFocus || Main.myPlayer < 0 || Main.myPlayer >= Main.maxPlayers) {
			return false;
		}

		Player player = Main.LocalPlayer;
		if (!player.active || player.dead || player.ghost) {
			return false;
		}

		if (Main.LocalPlayer.talkNPC >= 0 || (Main.playerInventory && !MobileInventorySystem.Active) || Main.drawingPlayerChat || MobileChatSystem.IsOpen || Main.editSign || Main.editChest || Main.ingameOptionsWindow || Main.inFancyUI) {
			return false;
		}

		return true;
	}

	private static MobileControlLayout CreateLayout(MobileJoystickConfig config)
		=> Main.mapFullscreen ? CreateMapLayout(config) : MobileJoystickLayout.CreateControlsLayout(config, Main.screenWidth, Main.screenHeight);

	private static void UpdateHotbarSelection(MobileJoystickConfig config)
	{
		MobileHotbarLayout hotbar = MobileJoystickLayout.CreateHotbarLayout(config);
		foreach (MobileTouchContact contact in Contacts) {
			if (contact.IsReleased || IsRadialContact(contact.Id) || IsGrappleContact(contact.Id) || IsInteractContact(contact.Id) || contact.Id == joystickFingerId || contact.Id == attackFingerId) {
				continue;
			}
			for (int i = 0; i < 10; i++) {
				if (hotbar.GetSlot(i).Contains(contact.Position.ToPoint())) {
					Main.LocalPlayer.selectedItem = i;
					HasAnyControlTouch = true;
					break;
				}
			}
		}
	}

	private static void ReadTouchContacts(MobileJoystickConfig config)
	{
		currentTouchFingerIds.Clear();
		if (config.EnableTouchInput) {
			TouchCollection touches = MobileTouchInput.GetState();
			for (int i = 0; i < touches.Count; i++) {
				TouchLocation touch = touches[i];
				Vector2 position = ScaleTouchPosition(touch.Position);
				TouchLocationState state = touch.State;
				if (state is TouchLocationState.Pressed or TouchLocationState.Moved) {
					currentTouchFingerIds.Add(touch.Id);
					// A render-only poll may advance Pressed to Moved; catch-up
					// updates may also read the same Pressed sample more than once.
					state = touchFingerIds.Contains(touch.Id) ? TouchLocationState.Moved : TouchLocationState.Pressed;
				}
				Contacts.Add(new MobileTouchContact(touch.Id, position, state));
			}
		}
		touchFingerIds.Clear();
		touchFingerIds.UnionWith(currentTouchFingerIds);

		if (!Main.mouseLeft) waitForMouseRelease = false;
		// The launcher maps a real touch to Main.mouseLeft. Do not add it twice,
		// or let that synthetic hold become a fallback finger after touch-up.
		if (Contacts.Count > 0 && Main.mouseLeft) waitForMouseRelease = true;
		bool fallbackDown = config.EnableMouseFallback && Main.mouseLeft && !waitForMouseRelease && Contacts.Count == 0;
		if (fallbackDown) {
			Contacts.Add(new MobileTouchContact(MouseContactId, new Vector2(Main.mouseX, Main.mouseY),
				mouseWasDown ? TouchLocationState.Moved : TouchLocationState.Pressed));
		}
		else if (mouseWasDown) {
			Contacts.Add(new MobileTouchContact(MouseContactId, new Vector2(Main.mouseX, Main.mouseY), TouchLocationState.Released));
		}
		mouseWasDown = fallbackDown;
	}

	private static Vector2 ScaleTouchPosition(Vector2 position)
	{
		float displayWidth = TouchPanel.DisplayWidth <= 0 ? PlayerInput.RealScreenWidth : TouchPanel.DisplayWidth;
		float displayHeight = TouchPanel.DisplayHeight <= 0 ? PlayerInput.RealScreenHeight : TouchPanel.DisplayHeight;
		if (displayWidth <= 0f || displayHeight <= 0f) {
			return position;
		}

		return new Vector2(position.X * PlayerInput.RealScreenWidth / displayWidth, position.Y * PlayerInput.RealScreenHeight / displayHeight);
	}

	private static void UpdateFingerAssignments()
	{
		bool joystickStillDown = false;
		bool attackStillDown = false;

		foreach (MobileTouchContact contact in Contacts) {
			if (contact.IsReleased || IsRadialContact(contact.Id) || IsGrappleContact(contact.Id) || IsInteractContact(contact.Id) || MobileInventorySystem.BlocksControl(contact.Position) || MobileInventorySystem.OwnsContact(contact.Id)) {
				continue;
			}
			if (IsBlockedByExclusiveControl(contact.Id)) continue;

			if (contact.Id == joystickFingerId) {
				joystickStillDown = true;
			}

			if (contact.Id == attackFingerId) {
				attackStillDown = true;
			}
		}

		if (!joystickStillDown) {
			joystickFingerId = int.MinValue;
		}

		if (!attackStillDown) {
			attackFingerId = int.MinValue;
			ResetTouchAim();
		}

		foreach (MobileTouchContact contact in Contacts) {
			if (contact.IsReleased) {
				continue;
			}

			if (contact.State != TouchLocationState.Pressed || IsRadialContact(contact.Id) || IsGrappleContact(contact.Id) || IsInteractContact(contact.Id) || contact.Id == joystickFingerId || contact.Id == attackFingerId
				|| MobileInventorySystem.BlocksControl(contact.Position) || MobileInventorySystem.OwnsContact(contact.Id)
				|| IsBlockedByExclusiveControl(contact.Id)) continue;

			foreach (MobileControlStick stick in Layout.Sticks) {
                if (stick.Radius <= 0f || Vector2.DistanceSquared(contact.Position, stick.Center) > stick.Radius * stick.Radius) continue;
                if (stick.Role == MobileJoystickRole.Move && joystickFingerId == int.MinValue) {
                    joystickFingerId = contact.Id;
                    activeMoveStick = stick;
                    HasAnyControlTouch = true;
                    break;
                }
                if (stick.Role == MobileJoystickRole.Aim && attackFingerId == int.MinValue) {
                    attackFingerId = contact.Id;
                    activeAimStick = stick;
                    InitializeTouchAim(contact.Position);
                    HasAnyControlTouch = true;
                    break;
                }
            }
		}
	}

	private static void UpdateAnalogControls(MobileJoystickConfig config)
	{
		if (hasTouchAimPosition && previousAimOrigin.HasValue) {
			touchAimPosition = TouchAimPosition;
			previousAimOrigin = MobileWorldInteraction.ToScreen(Main.LocalPlayer.MountedCenter);
		}
		foreach (MobileTouchContact contact in Contacts) {
			if (contact.IsReleased) {
				continue;
			}

			if (contact.Id == joystickFingerId) {
				Vector2 delta = contact.Position - MoveStick.Center;
				float length = delta.Length();
				if (length > MoveStick.Radius) {
					delta *= MoveStick.Radius / length;
				}

				JoystickVector = delta / MoveStick.Radius;
				if (JoystickVector.Length() < config.JoystickDeadzone) {
					JoystickVector = Vector2.Zero;
				}

				HasAnyControlTouch = true;
			}

			if (contact.Id == attackFingerId) {
				if (!hasTouchAimPosition) {
					InitializeTouchAim(contact.Position);
				}

				Vector2 knobDelta = (contact.Position - AimStick.Center) / AimStick.Radius;
				if (knobDelta.LengthSquared() > 1f) {
					knobDelta.Normalize();
				}
				AttackKnobOffset = knobDelta;
				Vector2 dragDelta = contact.Position - previousAttackTouchPosition;
				touchAimPosition += dragDelta;
				touchAimPosition.X = MathHelper.Clamp(touchAimPosition.X, 0f, PlayerInput.RealScreenWidth - 1f);
				touchAimPosition.Y = MathHelper.Clamp(touchAimPosition.Y, 0f, PlayerInput.RealScreenHeight - 1f);
				previousAttackTouchPosition = contact.Position;
				AttackHeld = true;
				HasAnyControlTouch = true;
			}
		}
	}

	private static void InitializeTouchAim(Vector2 contactPosition)
	{
		Player player = Main.LocalPlayer;
		// A released stick returns to rest; each new drag starts at the player.
		touchAimPosition = hasTouchAimPosition ? TouchAimPosition : MobileWorldInteraction.ToScreen(player.MountedCenter);
		previousAimOrigin = MobileWorldInteraction.ToScreen(player.MountedCenter);
		previousAttackTouchPosition = contactPosition;
		hasTouchAimPosition = true;
	}

	private static void ResetTouchAim()
	{
		touchAimPosition = Vector2.Zero;
		previousAttackTouchPosition = Vector2.Zero;
		previousAimOrigin = null;
		hasTouchAimPosition = false;
	}

	private static void UpdateRadialControls()
	{
		if (Radial.Active) {
			bool found = false;
			foreach (MobileTouchContact contact in Contacts) {
				if (contact.Id != Radial.ContactId) continue;
				found = true;
				radialContactThisFrame = contact.Id;
				var radialButton = Radial.Button;
				int slot = Math.Abs(contact.Position.Y - radialButton.Center.Y) >= Math.Abs(contact.Position.X - radialButton.Center.X)
					? contact.Position.Y < radialButton.Center.Y ? 0 : 2 : contact.Position.X > radialButton.Center.X ? 1 : 3;
				MobileControlAction? committed = Radial.Update(contact);
				if (committed == MobileControlAction.ModKeybind && radialButton.RadialModKeybinds?.Count > slot)
					MobileModKeybinds.Hold(radialButton.RadialModKeybinds[slot]);
				if (committed.HasValue) CurrentActions.Add(committed.Value);
				break;
			}
			if (!found) Radial.Reset();
		}
		if (!Radial.Active) {
			foreach (MobileTouchContact contact in Contacts) {
				if (contact.State != TouchLocationState.Pressed || IsRadialContact(contact.Id) || IsGrappleContact(contact.Id) || IsInteractContact(contact.Id)
					|| contact.Id == joystickFingerId || contact.Id == attackFingerId
					|| MobileInventorySystem.BlocksControl(contact.Position) || MobileInventorySystem.OwnsContact(contact.Id)) continue;
				foreach (MobileControlButton button in Layout.Buttons) {
					if (button.Action is not (MobileControlAction.QuickPotions or MobileControlAction.Zoom or MobileControlAction.RadialMenu)
						|| !IsButtonAvailable(button) || !button.Contains(contact.Position)
						|| !CanActivateCustomButton(button, contact)) continue;
					Radial.Begin(button, contact);
					radialContactThisFrame = contact.Id;
					break;
				}
				if (Radial.Active) break;
			}
		}
		if (Radial.Active) {
			HasAnyControlTouch = true;
			if (Radial.Button.Action == MobileControlAction.Zoom && Radial.Selection.HasValue) CurrentActions.Add(Radial.Selection.Value);
		}
		if (radialContactThisFrame != int.MinValue) HasAnyControlTouch = true;
	}

	private static void UpdateButtonActions()
	{
		foreach (MobileTouchContact contact in Contacts) {
			if (contact.IsReleased || IsRadialContact(contact.Id) || IsGrappleContact(contact.Id) || IsInteractContact(contact.Id) || MobileInventorySystem.BlocksControl(contact.Position) || MobileInventorySystem.OwnsContact(contact.Id)) {
				continue;
			}

			if (contact.Id == joystickFingerId || contact.Id == attackFingerId) {
				continue;
			}

			foreach (MobileControlButton button in Layout.Buttons) {
				if (button.Action is MobileControlAction.Interact or MobileControlAction.Grapple or MobileControlAction.QuickPotions or MobileControlAction.Zoom or MobileControlAction.RadialMenu or MobileControlAction.Unbound) continue;
				// Closing the bag exposes this HUD button beneath the same contact.
				// A held or moved contact must not immediately open it again.
				if (button.Action is MobileControlAction.Inventory or MobileControlAction.Chat or MobileControlAction.Map or MobileControlAction.MapStyle
					&& contact.State != TouchLocationState.Pressed) continue;
				if (!IsButtonAvailable(button) || !button.Contains(contact.Position)) {
					continue;
				}
				if (!CanActivateCustomButton(button, contact)) continue;

				CurrentActions.Add(button.Action);
				if (button.Action == MobileControlAction.ModKeybind) MobileModKeybinds.Hold(button.ModKeybind);
				HasAnyControlTouch = true;
			}
		}

		foreach (MobileControlAction action in CurrentActions) {
			if (!PreviousActions.Contains(action)) {
				PressedActions.Add(action);
			}
		}
	}

	private static bool CanActivateCustomButton(MobileControlButton button, MobileTouchContact contact)
	{
		if (button.Id == null) return true;
		foreach (MobileTouchContact other in Contacts) {
			if (other.Id == contact.Id || other.IsReleased || MobileInventorySystem.BlocksControl(other.Position)
				|| MobileInventorySystem.OwnsContact(other.Id) || !TouchesAnyControl(other)) continue;
			if (!button.Multitouch || IsExclusiveButtonTouch(other.Id)) return false;
		}
		return true;
	}

	private static bool IsBlockedByExclusiveControl(int contactId)
	{
		foreach (MobileTouchContact other in Contacts) {
			if (other.Id != contactId && !other.IsReleased && IsExclusiveButtonTouch(other.Id)) return true;
		}
		return false;
	}

	private static bool IsExclusiveButtonTouch(int contactId)
	{
		if (Grapple.Active && Grapple.ContactId == contactId && Grapple.Button.Id != null && !Grapple.Button.Multitouch) return true;
		if (InteractHeld && interactFingerId == contactId && interactButton.Id != null && !interactButton.Multitouch) return true;
		foreach (MobileTouchContact contact in Contacts) {
			if (contact.Id != contactId || contact.IsReleased) continue;
			foreach (MobileControlButton button in Layout.Buttons) {
				if (button.Id != null && !button.Multitouch && button.Action != MobileControlAction.Unbound
					&& IsButtonAvailable(button) && button.Contains(contact.Position)) return true;
			}
		}
		return false;
	}

    private static bool TouchesStick(MobileTouchContact contact)
    {
        if (Layout.Sticks == null) return false;
        foreach (var stick in Layout.Sticks)
            if (stick.Role is MobileJoystickRole.Move or MobileJoystickRole.Aim && stick.Radius > 0f
                && Vector2.DistanceSquared(contact.Position, stick.Center) <= stick.Radius * stick.Radius) return true;
        return false;
    }

	private static bool TouchesAnyControl(MobileTouchContact contact)
	{
		if (IsGrappleContact(contact.Id) || IsInteractContact(contact.Id)) return true;
		if (TouchesStick(contact)) return true;
		foreach (MobileControlButton button in Layout.Buttons)
			if (IsButtonAvailable(button) && button.Contains(contact.Position)) return true;
		return false;
	}

    internal static void ApplyGameplayTriggers()
    {
        // Held projectiles (including InnoVault ranged weapons) read the native
        // trigger directly, after SetControls. Keep it down for the entire drag
        // without synthesizing Main.mouseLeft, which would also click UI.
        bool canUse = EnabledThisFrame && !Main.playerInventory && !Main.mapFullscreen
            && !Main.ingameOptionsWindow && !Main.inFancyUI && Main.LocalPlayer.talkNPC < 0
            && !MobileChatSystem.IsOpen && Main.mouseItem.IsAir && !MobileInventorySystem.BlocksWorldUse;
        bool use = canUse && !MobileWorldInteraction.UsesRightTrigger
            && (MobileWorldInteraction.PrecisionHeld || AttackHeld && AimAndUseMode || IsActionDown(MobileControlAction.UseItem));
        if (use) {
            PlayerInput.Triggers.Current.MouseLeft = true;
            PlayerInput.Triggers.JustPressed.MouseLeft |= !gameplayUseWasDown;
            PlayerInput.Triggers.JustReleased.MouseLeft = false;
        }
        else if (gameplayUseWasDown && !PlayerInput.Triggers.Current.MouseLeft) {
            PlayerInput.Triggers.JustReleased.MouseLeft = true;
        }
        gameplayUseWasDown = use;
        if (!EnabledThisFrame || Main.mapFullscreen || MobileChatSystem.IsOpen || MobileInventorySystem.BlocksWorldUse) return;
        bool mount = IsActionDown(MobileControlAction.Mount) || IsActionDown(MobileControlAction.QuickMount);
        PlayerInput.Triggers.Current.QuickMount |= mount;
        if (mount) {
            PlayerInput.Triggers.JustPressed.QuickMount = !PlayerInput.Triggers.Old.QuickMount;
            PlayerInput.Triggers.JustReleased.QuickMount = false;
        }
    }

	private static void ApplyImmediateActions()
	{
		Player player = Main.LocalPlayer;
		InteractPrefersRightClickItem = false;

		if (WasActionPressed(MobileControlAction.Inventory)) {
			player.ToggleInv();
			if (Main.playerInventory && MobileInventorySystem.Enabled) MobileInventorySystem.OpenedFromControl();
		}

		if (WasActionPressed(MobileControlAction.MapStyle)) {
			if (Main.mapFullscreen) { Main.mapFullscreen = false; Main.mapStyle = 1; }
			else Main.mapStyle = Main.mapStyle == 1 ? 0 : 1;
			SoundEngine.PlaySound(SoundID.MenuTick);
		}

		if (WasActionPressed(MobileControlAction.Map)) {
			if (Main.mapFullscreen) {
				Main.mapFullscreen = false;
				SoundEngine.PlaySound(SoundID.MenuClose);
			}
			else {
				MobileChatSystem.Hide();
				player.TryOpeningFullscreenMap();
			}
		}

		if (WasActionPressed(MobileControlAction.Settings)) {
			IngameOptions.Open();
		}

		if (WasActionPressed(MobileControlAction.AimModeToggle)) {
			AimAndUseMode = !AimAndUseMode;
			ShowStatusText(AimAndUseMode ? "瞄准并使用" : "仅瞄准");
			SoundEngine.PlaySound(SoundID.MenuTick);
		}

		if (WasActionPressed(MobileControlAction.TargetLockToggle)) {
			TargetLockEnabled = !TargetLockEnabled;
			LockOnHelper.ForceUsability = TargetLockEnabled;
			if (TargetLockEnabled) {
				LockOnHelper.Toggle();
				ShowStatusText("目标锁定开启");
			}
			else {
				LockOnHelper.Toggle(forceOff: true);
				ShowStatusText("目标锁定关闭");
			}

			SoundEngine.PlaySound(SoundID.MenuTick);
		}

		if (WasActionPressed(MobileControlAction.SmartCursor)) {
			SmartCursorEnabled = !SmartCursorEnabled;
			ShowStatusText(SmartCursorEnabled ? "智能光标开启" : "智能光标关闭");
			SoundEngine.PlaySound(SoundID.MenuTick);
		}

		if (WasActionPressed(MobileControlAction.Chat)) {
			MobileChatSystem.Toggle();
		}

		if (WasActionPressed(MobileControlAction.CycleUseProfile)) LockOnHelper.CycleUseModes();
		if (WasActionPressed(MobileControlAction.Loadout1)) player.TrySwitchingLoadout(0);
		if (WasActionPressed(MobileControlAction.Loadout2)) player.TrySwitchingLoadout(1);
		if (WasActionPressed(MobileControlAction.Loadout3)) player.TrySwitchingLoadout(2);
		if (WasActionPressed(MobileControlAction.SwitchLoadout)) player.TrySwitchingLoadout((player.CurrentLoadoutIndex + 1) % player.Loadouts.Length);
	}

	private static void ShowStatusText(string text)
	{
		StatusText = text;
		statusTextTime = StatusTextFrames;
	}

	public static Item GetPriorityRightClickOrHeldItem()
	{
		if (Main.myPlayer < 0 || Main.myPlayer >= Main.maxPlayers) {
			return null;
		}

		Player player = Main.LocalPlayer;
		return player.HeldItem;
	}

	public static Item GetHeldItem()
	{
		if (Main.myPlayer < 0 || Main.myPlayer >= Main.maxPlayers) {
			return null;
		}

		return Main.LocalPlayer.HeldItem;
	}

	internal static void UseRightClickItem(Player player, int slot)
	{
		bool oldMouseRight = Main.mouseRight;
		bool oldMouseRightRelease = Main.mouseRightRelease;
		try {
			Main.mouseRight = true;
			Main.mouseRightRelease = true;
			ItemSlot.RightClick(player.inventory, 0, slot);
		}
		finally {
			Main.mouseRight = oldMouseRight;
			Main.mouseRightRelease = oldMouseRightRelease;
		}
	}
}
