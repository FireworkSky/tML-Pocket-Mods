using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameInput;
using Terraria.Localization;

namespace MobileJoystick;

internal static class MobileModKeybinds
{
    private static readonly HashSet<string> current = new(StringComparer.Ordinal);
    private static readonly HashSet<string> previous = new(StringComparer.Ordinal);

    internal static string[] Registered => PlayerInput.Triggers.Current.KeyStatus.Keys
        .Where(key => key.Contains('/')).OrderBy(key => key, StringComparer.Ordinal).ToArray();

    internal static string Label(string key)
    {
        if (string.IsNullOrEmpty(key)) return MobileSettingsCatalog.Text("模组按键", "Mod keybind");
        string[] parts = key.Split('/', 2);
        if (parts.Length != 2) return key;
        string localization = $"Mods.{parts[0]}.Keybinds.{parts[1]}.DisplayName";
        string name = Language.Exists(localization) ? Language.GetTextValue(localization) : parts[1];
        return parts[0] + ": " + name;
    }

    internal static void BeginFrame() { previous.Clear(); previous.UnionWith(current); current.Clear(); }
    internal static void Hold(string key)
    {
        if (!string.IsNullOrEmpty(key) && PlayerInput.Triggers.Current.KeyStatus.ContainsKey(key)) current.Add(key);
    }

    internal static bool IsHeld(string key) => !string.IsNullOrEmpty(key) && current.Contains(key);

    // Runs before Player.Update/ProcessTriggers, with native keyboard input intact.
    internal static void Apply()
    {
        foreach (string key in current.Concat(previous).Distinct()) {
            var triggers = PlayerInput.Triggers;
            if (!triggers.Current.KeyStatus.TryGetValue(key, out bool physical)) continue;
            bool down = physical || current.Contains(key);
            bool old = triggers.Old.KeyStatus.TryGetValue(key, out bool value) && value;
            triggers.Current.KeyStatus[key] = down;
            triggers.JustPressed.KeyStatus[key] = down && !old;
            triggers.JustReleased.KeyStatus[key] = !down && old;
        }
    }

    internal static void Reset() { current.Clear(); previous.Clear(); }
}
