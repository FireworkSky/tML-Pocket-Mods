using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;
using Microsoft.Xna.Framework.Input;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.GameInput;

namespace MobileJoystick;

// SDL can deliver both edges between FNA's state polls. Keep the press until
// a mobile UI consumer samples it, then deliver its release on the next sample.
internal static class MobileTouchInput
{
    private delegate void TouchEvent(int id, TouchLocationState state, float x, float y, float dx, float dy);
    private delegate void TouchEventHook(TouchEvent orig, int id, TouchLocationState state, float x, float y, float dx, float dy);
    private static Hook eventHook, updateHook;
    private static readonly object Sync = new();
    private static readonly Dictionary<int, Vector2> presses = new(), releases = new(), deferredReleases = new();
    private static TouchCollection cached;
    private static bool sampled;
    private static readonly Dictionary<int, int> contactIds = new();
    private static int nextContactId;
    internal static bool UsingTouch { get; private set; }
    private static Point lastMousePosition;
    private static bool suppressMouseUntilRelease;

    internal static void Load()
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var onEvent = typeof(TouchPanel).GetMethod("INTERNAL_onTouchEvent", flags);
        var update = typeof(TouchPanel).GetMethod("Update", flags);
        if (onEvent == null || update == null) return;
        eventHook = new Hook(onEvent, (TouchEventHook)OnTouch);
        updateHook = new Hook(update, (Action<Action>)(orig => {
            orig();
            lock (Sync) sampled = false;
        }));
    }

    internal static void Unload()
    {
        updateHook?.Dispose(); eventHook?.Dispose();
        updateHook = eventHook = null;
        Reset();
    }

    internal static void Reset()
    {
        lock (Sync) {
            presses.Clear(); releases.Clear(); deferredReleases.Clear();
            contactIds.Clear(); nextContactId = 0; UsingTouch = false;
            suppressMouseUntilRelease = false;
            sampled = false; cached = default;
        }
    }

    private static void OnTouch(TouchEvent orig, int id, TouchLocationState state, float x, float y, float dx, float dy)
    {
        orig(id, state, x, y, dx, dy);
        lock (Sync) {
            var position = new Vector2(MathF.Round(x * TouchPanel.DisplayWidth), MathF.Round(y * TouchPanel.DisplayHeight));
            if (state == TouchLocationState.Pressed) {
                // Keep a bounded buffer even when mobile controls are disabled.
                if (presses.Count >= 32) { presses.Clear(); releases.Clear(); }
                presses[id] = position;
                releases.Remove(id);
            }
            else if (state is TouchLocationState.Released or TouchLocationState.Invalid) releases[id] = position;
        }
    }

    internal static TouchCollection GetState()
    {
        if (eventHook == null) return NormalizeContacts(TouchPanel.GetState());
        lock (Sync) {
            if (sampled) return cached;
            var contacts = new Dictionary<int, TouchLocation>();
            foreach (var touch in TouchPanel.GetState()) contacts[touch.Id] = touch;
            foreach (var release in deferredReleases)
                if (!presses.ContainsKey(release.Key)) contacts[release.Key] = new(release.Key, TouchLocationState.Released, release.Value);
            deferredReleases.Clear();
            foreach (var release in releases)
                if (!presses.ContainsKey(release.Key)) contacts[release.Key] = new(release.Key, TouchLocationState.Released, release.Value);
            foreach (var press in presses) {
                var position = contacts.TryGetValue(press.Key, out var touch) && touch.State is TouchLocationState.Pressed or TouchLocationState.Moved
                    ? touch.Position : press.Value;
                contacts[press.Key] = new(press.Key, TouchLocationState.Pressed, position);
                if (releases.TryGetValue(press.Key, out var end)) deferredReleases[press.Key] = end;
            }
            presses.Clear(); releases.Clear();
            cached = NormalizeContacts(new TouchCollection(new List<TouchLocation>(contacts.Values).ToArray()));
            sampled = true;
            return cached;
        }
    }

    // SDL finger IDs are signed 64-bit values; launchers can truncate them to
    // negative ints. UI consumers reserve negative IDs for mouse/no contact.
    private static TouchCollection NormalizeContacts(TouchCollection touches)
    {
        var result = new List<TouchLocation>();
        var present = new HashSet<int>();
        foreach (var touch in touches) {
            if (touch.State == TouchLocationState.Invalid) continue;
            present.Add(touch.Id);
            if (!contactIds.TryGetValue(touch.Id, out int id)) {
                id = nextContactId = nextContactId == int.MaxValue ? 1 : nextContactId + 1;
                contactIds[touch.Id] = id;
            }
            result.Add(new TouchLocation(id, touch.State, touch.Position));
        }
        foreach (int id in new List<int>(contactIds.Keys))
            if (!present.Contains(id)) contactIds.Remove(id);
        var mouse = PlayerInput.MouseInfo;
        Point mousePosition = new(mouse.X, mouse.Y);
        if (result.Count > 0) UsingTouch = true;
        else if (mousePosition != lastMousePosition) UsingTouch = false;
        lastMousePosition = mousePosition;
        return new TouchCollection(result.ToArray());
    }

    internal static bool ReadUiPointer(ref int id, ref Vector2 position, out bool cancelled)
    {
        cancelled = !Main.hasFocus;
        if (cancelled) { id = -1; return false; }
        TouchCollection touches = GetState();
        if (touches.Count > 0) suppressMouseUntilRelease = true;
        bool captured = id >= 0;
        if (!captured) foreach (var touch in touches) {
            if (touch.State is not (TouchLocationState.Pressed or TouchLocationState.Moved)) continue;
            id = touch.Id;
            captured = true;
            break;
        }
        if (captured) {
            foreach (var touch in touches) {
                if (touch.Id != id) continue;
                int width = TouchPanel.DisplayWidth > 0 ? TouchPanel.DisplayWidth : PlayerInput.RealScreenWidth;
                int height = TouchPanel.DisplayHeight > 0 ? TouchPanel.DisplayHeight : PlayerInput.RealScreenHeight;
                position = new Vector2(touch.Position.X * PlayerInput.RealScreenWidth / Math.Max(1, width),
                    touch.Position.Y * PlayerInput.RealScreenHeight / Math.Max(1, height));
                bool down = touch.State is TouchLocationState.Pressed or TouchLocationState.Moved;
                cancelled = touch.State == TouchLocationState.Invalid;
                if (!down) id = -1;
                return down;
            }
            id = -1;
            cancelled = true;
            return false;
        }
        // Do not turn the launcher's lingering emulated mouse hold into a
        // second UI press after the touch release has already been consumed.
        if (touches.Count > 0) return false;
        var mouse = PlayerInput.MouseInfo;
        if (mouse.LeftButton == ButtonState.Released) suppressMouseUntilRelease = false;
        if (suppressMouseUntilRelease) return false;
        position = new Vector2(mouse.X, mouse.Y) * PlayerInput.RawMouseScale;
        return mouse.LeftButton == ButtonState.Pressed;
    }
}
