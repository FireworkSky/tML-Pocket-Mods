using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace MobileJoystick;

public enum MobileCustomControlKind
{
	Joystick,
	Button,
	RadialMenu
}

public enum MobileJoystickRole
{
	Move,
	Aim,
	Grapple,
	Unbound
}

public sealed class MobileCustomControl
{
	public string Id { get; set; } = string.Empty;
	public MobileCustomControlKind Kind { get; set; }
	public MobileJoystickRole JoystickRole { get; set; }
	public MobileControlAction Action { get; set; } = MobileControlAction.Unbound;
	public string ModKeybind { get; set; } = string.Empty;
	public List<string> RadialModKeybinds { get; set; } = new();
	public string TextureName { get; set; } = string.Empty;
	public float X { get; set; }
	public float Y { get; set; }
	public float Size { get; set; } = 1f;
	public bool Multitouch { get; set; } = true;
	public List<MobileControlAction> RadialActions { get; set; } = new();
}

public sealed class MobileCustomPreset
{
	public string Name { get; set; } = string.Empty;
	public List<MobileCustomControl> Controls { get; set; } = new();
}

internal static class MobileCustomControls
{
	public const int PresetCount = 4;
	public const float MinimumSize = 0.35f;
	public const float MaximumSize = 2f;

	public static List<MobileCustomPreset> CreateDefaults(MobileJoystickConfig config)
	{
		MobileControlLayoutMode originalMode = config.ControlLayout;
		MobileControlLayout layout;
		try {
			config.ControlLayout = MobileControlLayoutMode.Simple;
			layout = MobileJoystickLayout.CreateControlsLayout(config, 1920, 1080);
		}
		finally {
			config.ControlLayout = originalMode;
		}

		List<MobileCustomControl> controls = new() {
			CreateJoystick("move", MobileJoystickRole.Move, layout.JoystickCenter, layout.JoystickRadius, layout.Scale, 1920, 1080),
			CreateJoystick("aim", MobileJoystickRole.Aim, layout.AttackCenter, layout.AttackRadius, layout.Scale, 1920, 1080)
		};
		foreach (MobileControlButton button in layout.Buttons) {
			controls.Add(new MobileCustomControl {
				Id = "button-" + button.Action,
				Kind = MobileCustomControlKind.Button,
				Action = button.Action,
				TextureName = button.TextureName,
				X = button.Center.X / 1920f,
				Y = button.Center.Y / 1080f,
				Size = button.Radius / (24f * layout.Scale)
			});
		}

		List<MobileCustomPreset> presets = new(PresetCount);
		for (int i = 0; i < PresetCount; i++) {
			presets.Add(new MobileCustomPreset {
				Name = "Custom " + (i + 1),
				Controls = controls.Select(CloneControl).ToList()
			});
		}
		return presets;
	}

	public static void EnsureInitialized(MobileJoystickConfig config)
	{
		if (config == null) return;
		List<MobileCustomPreset> defaults = CreateDefaults(config);
		List<MobileCustomPreset> existing = config.CustomPresets ?? new List<MobileCustomPreset>();
		List<MobileCustomPreset> presets = new(PresetCount);
		for (int i = 0; i < PresetCount; i++) {
			MobileCustomPreset preset = i < existing.Count ? Clone(existing[i]) : null;
			if (preset == null) {
				preset = Clone(defaults[i]);
			}
			else {
				if (string.IsNullOrWhiteSpace(preset.Name)) preset.Name = defaults[i].Name;
				Validate(preset);
			}
			presets.Add(preset);
		}
		config.CustomPresets = presets;
		config.ActiveCustomPresetIndex = Math.Clamp(config.ActiveCustomPresetIndex, 0, PresetCount - 1);
	}

	public static MobileCustomPreset Clone(MobileCustomPreset preset)
	{
		if (preset == null) return null;
		return new MobileCustomPreset {
			Name = preset.Name ?? string.Empty,
			Controls = preset.Controls?.Select(CloneControl).ToList() ?? new List<MobileCustomControl>()
		};
	}

	public static void Validate(MobileCustomPreset preset)
	{
		if (preset == null) return;
		if (string.IsNullOrWhiteSpace(preset.Name)) preset.Name = "Custom 1";
		preset.Name = preset.Name.Trim();
		if (preset.Controls == null) preset.Controls = new List<MobileCustomControl>();

		HashSet<string> ids = new(StringComparer.Ordinal);
		List<MobileCustomControl> valid = new();
		foreach (MobileCustomControl control in preset.Controls) {
			if (control == null || !Enum.IsDefined(control.Kind)) continue;
			if (control.Kind == MobileCustomControlKind.Joystick) {
				if (!Enum.IsDefined(control.JoystickRole)) continue;
			}
			else if (control.Kind == MobileCustomControlKind.Button && !Enum.IsDefined(control.Action)) {
				continue;
			}

			if (string.IsNullOrWhiteSpace(control.Id) || !ids.Add(control.Id)) control.Id = "control-" + Guid.NewGuid().ToString("N");
			control.X = ClampFinite(control.X, 0f, 1f, 0.5f);
			control.Y = ClampFinite(control.Y, 0f, 1f, 0.5f);
			control.Size = ClampFinite(control.Size, MinimumSize, MaximumSize, 1f);
			control.TextureName ??= string.Empty;
			control.ModKeybind ??= string.Empty;
			if (control.Kind == MobileCustomControlKind.RadialMenu) {
				control.RadialActions ??= new List<MobileControlAction>();
				control.RadialActions = control.RadialActions.Take(4)
					.Select(action => Enum.IsDefined(action) && action is not (MobileControlAction.QuickPotions or MobileControlAction.Zoom)
						? action : MobileControlAction.Unbound).ToList();
				control.RadialModKeybinds = (control.RadialModKeybinds ?? new()).Take(4).Select(key => key ?? string.Empty).ToList();
				while (control.RadialModKeybinds.Count < 4) control.RadialModKeybinds.Add(string.Empty);
				while (control.RadialActions.Count < 4) control.RadialActions.Add(MobileControlAction.Unbound);
			}
			else {
				control.RadialActions = new List<MobileControlAction>();
			}
			valid.Add(control);
		}
		preset.Controls = valid;
	}

	private static MobileCustomControl CreateJoystick(string id, MobileJoystickRole role, Vector2 center, float radius, float scale, int width, int height)
		=> new() {
			Id = "joystick-" + id,
			Kind = MobileCustomControlKind.Joystick,
			JoystickRole = role,
			TextureName = role == MobileJoystickRole.Move ? "JoystickKnob" : "AttackAimUse",
			X = center.X / width,
			Y = center.Y / height,
			Size = radius / (66f * scale)
		};

	private static MobileCustomControl CloneControl(MobileCustomControl control)
	{
		if (control == null) return null;
		return new MobileCustomControl {
			Id = control.Id,
			Kind = control.Kind,
			JoystickRole = control.JoystickRole,
			Action = control.Action,
			ModKeybind = control.ModKeybind,
			RadialModKeybinds = control.RadialModKeybinds?.ToList() ?? new(),
			TextureName = control.TextureName,
			X = control.X,
			Y = control.Y,
			Size = control.Size,
			Multitouch = control.Multitouch,
			RadialActions = control.RadialActions?.ToList() ?? new List<MobileControlAction>()
		};
	}

	private static float ClampFinite(float value, float min, float max, float fallback)
		=> float.IsFinite(value) ? MathHelper.Clamp(value, min, max) : fallback;
}

internal sealed class MobileControlEditorHistory
{
	private sealed class Snapshot
	{
		public MobileCustomPreset Preset { get; init; }
		public Dictionary<int, MobileCustomPreset> Copies { get; init; }
	}

	private readonly List<Snapshot> snapshots = new();
	private int cursor;

	public MobileControlEditorHistory(MobileCustomPreset initial)
	{
		snapshots.Add(new Snapshot { Preset = MobileCustomControls.Clone(initial) ?? new MobileCustomPreset(), Copies = new() });
	}

	public bool CanUndo => cursor > 0;
	public bool CanRedo => cursor < snapshots.Count - 1;
	public MobileCustomPreset Current => MobileCustomControls.Clone(snapshots[cursor].Preset);
	public Dictionary<int, MobileCustomPreset> CurrentCopies => CloneCopies(snapshots[cursor].Copies);

	public void Record(MobileCustomPreset preset) => Record(preset, null);

	public void Record(MobileCustomPreset preset, IReadOnlyDictionary<int, MobileCustomPreset> copies)
	{
		while (snapshots.Count > cursor + 1) snapshots.RemoveAt(snapshots.Count - 1);
		snapshots.Add(new Snapshot {
			Preset = MobileCustomControls.Clone(preset) ?? new MobileCustomPreset(),
			Copies = CloneCopies(copies)
		});
		cursor = snapshots.Count - 1;
	}

	public MobileCustomPreset Undo()
	{
		if (CanUndo) cursor--;
		return Current;
	}

	public MobileCustomPreset Redo()
	{
		if (CanRedo) cursor++;
		return Current;
	}

	private static Dictionary<int, MobileCustomPreset> CloneCopies(IReadOnlyDictionary<int, MobileCustomPreset> copies)
	{
		Dictionary<int, MobileCustomPreset> result = new();
		if (copies == null) return result;
		foreach (KeyValuePair<int, MobileCustomPreset> pair in copies)
			result[pair.Key] = MobileCustomControls.Clone(pair.Value);
		return result;
	}
}
