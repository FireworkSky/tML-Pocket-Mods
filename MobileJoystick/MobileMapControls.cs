using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;
using Terraria;

namespace MobileJoystick;

internal static partial class MobileJoystickInput
{
	private static double lastZoomTime;
	private static int mapContact = int.MinValue;
	private static Vector2 mapLastPosition, mapStartPosition;
	private static bool mapGestureWasOpen;
	private static float mapPinchDistance;
	private static Vector2 mapPinchCenter;
	private static int mapPinchFirst = int.MinValue, mapPinchSecond = int.MinValue;
	internal static Vector2? PendingMapTap { get; private set; }
	internal static Vector2? ConsumeMapTap() { var tap = PendingMapTap; PendingMapTap = null; return tap; }

	internal static MobileControlLayout CreateMapLayout(MobileJoystickConfig config)
	{
		var normal = MobileJoystickLayout.CreateControlsLayout(config, Main.screenWidth, Main.screenHeight);
		var buttons = new List<MobileControlButton>();
		for (int i = 0; i < 3; i++) {
			Rectangle r = MobileJoystickLayout.GetToolbarButton(config, i, Main.screenWidth, Main.screenHeight);
			buttons.Add(new(i == 0 ? MobileControlAction.Chat : i == 1 ? MobileControlAction.MapStyle : MobileControlAction.Map,
				i == 0 ? "Chat" : i == 1 ? "MapStyle" : "Map", r.Center.ToVector2(), r.Width * .5f));
		}
		bool minus = false, plus = false;
		foreach (var button in normal.Buttons) {
			if (button.Action == MobileControlAction.ZoomIn) { buttons.Add(button); plus = true; }
			if (button.Action == MobileControlAction.ZoomOut) { buttons.Add(button); minus = true; }
		}
		float scale = normal.Scale;
		float y = Main.screenHeight - 42 * scale;
		if (!minus) buttons.Add(new(MobileControlAction.ZoomOut, "ZoomOut", new(Main.screenWidth * .295f, y), 16 * scale));
		if (!plus) buttons.Add(new(MobileControlAction.ZoomIn, "ZoomIn", new(Main.screenWidth * .705f, y), 16 * scale));
		return new(Vector2.Zero, 0, Vector2.Zero, 0, buttons.ToArray(), scale);
	}

	private static void ResetMapInteraction()
	{
		lastZoomTime = 0; mapContact = int.MinValue; PendingMapTap = null; mapGestureWasOpen = false;
		mapPinchDistance = 0; mapPinchFirst = mapPinchSecond = int.MinValue;
	}
	private static void UpdateHeldZoom()
	{
		double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
		float elapsed = lastZoomTime == 0 ? 1f / 60 : (float)Math.Clamp(now - lastZoomTime, 0, .05);
		lastZoomTime = now;
		int direction = (IsActionDown(MobileControlAction.ZoomIn) ? 1 : 0) - (IsActionDown(MobileControlAction.ZoomOut) ? 1 : 0);
		if (direction == 0) return;
		if (Main.mapFullscreen) {
			float minimum = Main.screenWidth / (float)Math.Max(1, Main.maxTilesX) * .599f;
			Main.mapFullscreenScale = MathHelper.Clamp(Main.mapFullscreenScale * MathF.Pow(2, direction * elapsed), minimum, 31.18f);
		}
		else {
			// Let Terraria and zoom mods apply their own speed and bounds.
			Terraria.GameInput.PlayerInput.Triggers.Current.ViewZoomIn |= direction > 0;
			Terraria.GameInput.PlayerInput.Triggers.Current.ViewZoomOut |= direction < 0;
		}
	}

	private static void UpdateFullscreenMapGesture()
	{
		PendingMapTap = null;
		if (!Main.mapFullscreen) { mapContact = int.MinValue; mapGestureWasOpen = false; mapPinchDistance = 0; return; }
		// The opening toolbar contact must be lifted before it can drag the map.
		if (!mapGestureWasOpen) { mapGestureWasOpen = true; mapContact = int.MinValue; return; }
		MobileTouchContact? first = null, second = null;
		foreach (var contact in Contacts) {
			if (contact.IsReleased || TouchesMapButton(contact.Position)) continue;
			if (!first.HasValue) first = contact;
			else { second = contact; break; }
		}
		if (first.HasValue && second.HasValue) {
			float distance = Vector2.Distance(first.Value.Position, second.Value.Position);
			Vector2 center = (first.Value.Position + second.Value.Position) * .5f;
			if (mapPinchDistance > 1 && distance > 1 && first.Value.Id == mapPinchFirst && second.Value.Id == mapPinchSecond) {
				float before = Main.mapFullscreenScale;
				float minimum = Main.screenWidth / (float)Math.Max(1, Main.maxTilesX) * .599f;
				Main.mapFullscreenScale = MathHelper.Clamp(before * distance / mapPinchDistance, minimum, 31.18f);
				Vector2 middle = new(Main.screenWidth * .5f, Main.screenHeight * .5f);
				Main.mapFullscreenPos += (mapPinchCenter - middle) / before - (center - middle) / Main.mapFullscreenScale;
			}
			mapPinchDistance = distance; mapPinchCenter = center;
			mapPinchFirst = first.Value.Id; mapPinchSecond = second.Value.Id;
			mapContact = int.MinValue; HasAnyControlTouch = true;
			return;
		}
		mapPinchDistance = 0; mapPinchFirst = mapPinchSecond = int.MinValue;
		bool found = false;
		foreach (var contact in Contacts) {
			if (mapContact == int.MinValue) {
				if (contact.State != TouchLocationState.Pressed || TouchesMapButton(contact.Position)) continue;
				mapContact = contact.Id; mapLastPosition = mapStartPosition = contact.Position;
			}
			if (contact.Id != mapContact) continue;
			found = true;
			if (contact.State == TouchLocationState.Invalid) { mapContact = int.MinValue; break; }
			Vector2 delta = contact.Position - mapLastPosition;
			if (contact.State == TouchLocationState.Released) {
				if (Vector2.DistanceSquared(contact.Position, mapStartPosition) < 144) PendingMapTap = contact.Position;
				mapContact = int.MinValue;
			}
			else if (contact.State == TouchLocationState.Moved) Main.mapFullscreenPos -= delta / Math.Max(.001f, Main.mapFullscreenScale);
			mapLastPosition = contact.Position;
			HasAnyControlTouch = true;
			break;
		}
		if (!found) mapContact = int.MinValue;
	}
	private static bool TouchesMapButton(Vector2 position)
	{
		foreach (var button in Layout.Buttons) if (button.Contains(position)) return true;
		return false;
	}
}
