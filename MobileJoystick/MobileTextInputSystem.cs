using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace MobileJoystick;

// WritingText routes characters, but FNA must also start the platform input method.
public sealed class MobileTextInputSystem : ModSystem
{
    private static object owner;
    private static bool started;

    internal static void Focus(object field, Rectangle bounds = default)
    {
        owner = field;
        PlayerInput.WritingText = true;
        if (bounds.Width > 0) TextInputEXT.SetInputRectangle(bounds);
        TextInputEXT.StartTextInput();
        started = true;
    }

    internal static void Blur(object field)
    {
        if (owner != field) return;
        owner = null;
        Stop();
    }

    private static void Stop()
    {
        if (started) TextInputEXT.StopTextInput();
        started = false;
    }

    public override void PostUpdateInput()
    {
        if (Main.dedServ) return;
        if (MobileJoystickConfig.Instance?.EnableMobileControls != true) { owner = null; Stop(); return; }
        bool native = Main.drawingPlayerChat || Main.editSign || Main.editChest
            || Main.gameMenu && Main.MenuUI?.CurrentState is UIVirtualKeyboard
            || Main.inFancyUI && Main.InGameUI?.CurrentState is UIVirtualKeyboard;
        if (Main.hasFocus && (owner != null || native)) {
            if (!started || !TextInputEXT.IsTextInputActive()) TextInputEXT.StartTextInput();
            started = true;
            PlayerInput.WritingText = true;
        }
        else Stop();
    }

    public override void OnWorldUnload() { owner = null; Stop(); }
    public override void Unload() { if (!Main.dedServ) { owner = null; Stop(); } }
}
