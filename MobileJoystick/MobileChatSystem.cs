using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI;
using Terraria.GameContent.UI.Chat;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI.Chat;

namespace MobileJoystick;

// Native chat still receives and submits messages. This only replaces its view.
public sealed class MobileChatSystem : ModSystem
{
	private sealed class Message
	{
		internal string Text;
		internal Color Color;
		internal double Time;
		internal int Width;
		internal List<TextSnippet[]> Lines;
	}
	private readonly List<Message> messages = new();
	private static MobileChatSystem instance;
	private bool open, emotes, pointerWasDown, waitForRelease, escapeWasDown;
	private int pointerId = int.MinValue, offset, category, emoteRow;
	private Vector2 lastPointer;
	private Vector2 pressPosition;
	private int pendingEmote = -1;
	private float scrollDrag;
	private int wheel;
	private MobileChatLayout layout;
	private List<int>[] groups;
	private RasterizerState historyClip;
	internal static bool IsOpen => instance?.open == true;
	internal static bool Enabled => !Main.dedServ && !Main.gameMenu && MobileJoystickConfig.Instance?.EnableMobileControls == true
		&& Main.myPlayer >= 0 && Main.myPlayer < Main.maxPlayers;
	private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

	public override void Load()
	{
		if (Main.dedServ) return;
		instance = this;
		On_Main.DrawPlayerChat += DrawChat;
		On_RemadeChatMonitor.AddNewMessage += ReceiveMessage;
		On_RemadeChatMonitor.Clear += ClearMessages;
	}
	public override void Unload()
	{
		if (!Main.dedServ) {
			On_Main.DrawPlayerChat -= DrawChat;
			On_RemadeChatMonitor.AddNewMessage -= ReceiveMessage;
			On_RemadeChatMonitor.Clear -= ClearMessages;
		}
		historyClip?.Dispose(); historyClip = null;
		messages.Clear(); instance = null;
	}
	public override void OnWorldUnload() { Close(); messages.Clear(); }
	internal static void Toggle() { if (instance == null) return; if (instance.open) instance.Close(); else instance.Open(); }
	internal static void Hide() => instance?.Close();
	private void Open()
	{
		Main.mapFullscreen = false;
		Main.ClosePlayerChat();
		open = true; emotes = false; offset = 0;
		waitForRelease = true; pointerId = int.MinValue;
		wheel = Mouse.GetState().ScrollWheelValue;
		SoundEngine.PlaySound(SoundID.MenuOpen);
	}
	private void Close()
	{
		if (open && !Main.dedServ) { Main.ClosePlayerChat(); MobileTextInputSystem.Blur(this); }
		open = emotes = pointerWasDown = false;
		pointerId = int.MinValue; offset = 0;
	}
	private void ReceiveMessage(On_RemadeChatMonitor.orig_AddNewMessage orig, RemadeChatMonitor self, string text, Color color, int width)
	{
		orig(self, text, color, width);
		messages.Insert(0, new Message { Text = text, Color = color, Time = Now });
		if (messages.Count > 500) messages.RemoveAt(messages.Count - 1);
		if (open && offset > 0) {
			int limit = Math.Max(1, (int)(layout.History.Width / Math.Max(.01f, .85f * layout.Scale)));
			offset += Wrap(text, color, limit).Count;
		}
	}
	private void ClearMessages(On_RemadeChatMonitor.orig_Clear orig, RemadeChatMonitor self)
	{
		orig(self); messages.Clear(); offset = 0;
	}

	// Called after the shared touch sample, including while gameplay is blocked.
	internal static void UpdateInput() => instance?.ReadInput();
	private void ReadInput()
	{
		if (!Enabled || Main.ingameOptionsWindow || Main.inFancyUI || Main.LocalPlayer.dead) { Close(); return; }
		if (Main.drawingPlayerChat && !open) {
			open = true; Main.mapFullscreen = false; offset = 0;
			wheel = Mouse.GetState().ScrollWheelValue;
		}
		if (open && Main.mapFullscreen) { Close(); return; }
		if (!Main.drawingPlayerChat) MobileTextInputSystem.Blur(this);
		bool escape = Keyboard.GetState().IsKeyDown(Keys.Escape);
		if (open && escape && !escapeWasDown) {
			if (emotes) emotes = false; else Close();
			// Prevent the same Escape opening the pause menu.
			Main.blockKey = Keys.Escape.ToString();
		}
		escapeWasDown = escape;
		if (!open) return;
		layout = new(PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight);
		if (!Main.hasFocus) { pointerId = int.MinValue; pointerWasDown = false; waitForRelease = true; return; }
		MouseState mouse = Mouse.GetState();
		Vector2 pointer = new(mouse.X, mouse.Y);
		bool down = MobileJoystickConfig.Instance.EnableMouseFallback && mouse.LeftButton == ButtonState.Pressed;
		bool released = !down && pointerWasDown;
		if (MobileJoystickConfig.Instance.EnableTouchInput) {
			foreach (var contact in MobileJoystickInput.ActiveContacts) {
				if (contact.Id < 0 || pointerId != int.MinValue && pointerId != contact.Id
					|| pointerId == int.MinValue && contact.State != TouchLocationState.Pressed) continue;
				pointerId = contact.Id; pointer = contact.Position;
				down = !contact.IsReleased; released = contact.State == TouchLocationState.Released;
				if (contact.IsReleased) pointerId = int.MinValue;
				break;
			}
			if (pointerId != int.MinValue) {
				bool found = false;
				foreach (var c in MobileJoystickInput.ActiveContacts) if (c.Id == pointerId) found = true;
				if (!found) { pointerId = int.MinValue; down = released = false; }
			}
		}
		bool pressed = down && !pointerWasDown;
		if (waitForRelease) { if (!down) waitForRelease = false; pressed = released = false; }
		if (pressed) {
			lastPointer = pressPosition = pointer; scrollDrag = 0; pendingEmote = -1;
			if (emotes) ReadEmotePress(pointer);
			else if (layout.Emote.Contains(pointer.ToPoint())) {
				Main.ClosePlayerChat(); MobileTextInputSystem.Blur(this); emotes = true; category = emoteRow = 0; offset = 0; RefreshGroups();
			}
			else if (layout.Input.Contains(pointer.ToPoint())) {
				if (!Main.drawingPlayerChat) { Main.OpenPlayerChat(); Main.chatText = string.Empty; }
				MobileTextInputSystem.Focus(this, layout.Input);
			}
			else for (int i = 0; i < 3; i++) {
				if (!MobileJoystickLayout.GetToolbarButton(MobileJoystickConfig.Instance, i, PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight).Contains(pointer.ToPoint())) continue;
				Close();
				if (i == 1) { Main.mapStyle = Main.mapStyle == 1 ? 0 : 1; SoundEngine.PlaySound(SoundID.MenuTick); }
				if (i == 2) Main.LocalPlayer.TryOpeningFullscreenMap();
				break;
			}
		}
		if (open && !emotes && down && !pressed && layout.History.Contains(pointer.ToPoint())) {
			scrollDrag += pointer.Y - lastPointer.Y;
			int lines = (int)(scrollDrag / Math.Max(1, 20 * layout.Scale));
			if (lines != 0) { offset = Math.Max(0, offset + lines); scrollDrag -= lines * 20 * layout.Scale; }
		}
		if (emotes && down && !pressed && layout.EmotesPanel.Contains(pointer.ToPoint())) {
			scrollDrag += lastPointer.Y - pointer.Y;
			int rows = (int)(scrollDrag / Math.Max(1, 54 * layout.Scale));
			if (rows != 0) { emoteRow += rows; scrollDrag -= rows * 54 * layout.Scale; }
		}
		int wheelChange = (mouse.ScrollWheelValue - wheel) / 120;
		if (wheelChange != 0 && layout.Panel.Contains(pointer.ToPoint()) && !emotes) offset = Math.Max(0, offset + wheelChange * 3);
		if (emotes && layout.EmotesPanel.Contains(pointer.ToPoint())) emoteRow -= wheelChange;
		if (emotes) emoteRow = Math.Clamp(emoteRow, 0, Math.Max(0, (groups[category].Count + 9) / 10 - 3));
		if (released && emotes && pendingEmote >= 0 && Vector2.DistanceSquared(pointer, pressPosition) < 64 * layout.Scale * layout.Scale) {
			EmoteBubble.MakeLocalPlayerEmote(pendingEmote); emotes = false; pendingEmote = -1;
		}
		wheel = mouse.ScrollWheelValue;
		pointerWasDown = down; lastPointer = pointer;
		Main.LocalPlayer.mouseInterface = true;
	}

	private void DrawChat(On_Main.orig_DrawPlayerChat orig, Main self)
	{
		if (!Enabled || Main.ingameOptionsWindow || Main.inFancyUI || MobileMenuSystem.EditingCustomControls) { orig(self); return; }
		layout = new(PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight);
		Begin();
		try {
			if (open) {
				MobileHudArtwork.Nine("ChatPanel", layout.Panel, Color.White, layout.Scale);
				Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(layout.Panel.Left + 2, layout.Input.Top - 2,
					layout.Panel.Width - 4, Math.Max(1, (int)(2 * layout.Scale))), Color.Black * .5f);
				MobileHudArtwork.Icon("Emote", layout.Emote, Color.White);
				string value = Main.drawingPlayerChat ? Main.chatText + ((int)(Now * 2) % 2 == 0 ? "|" : "") : T("说点啥话……", "Say something...");
				DrawInputText(value);
			}
			DrawHistory();
			if (emotes) DrawEmotes();
		}
		finally {
			Main.spriteBatch.End();
			Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
				DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
		}
		if (Main.drawingPlayerChat) { PlayerInput.WritingText = true; Main.instance.HandleIME(); Main.instance.DrawWindowsIMEPanel(layout.Input.TopLeft()); }
	}

	private void DrawInputText(string value)
	{
		float scale = .9f * layout.Scale;
		while (FontAssets.MouseText.Value.MeasureString(value).X * scale > layout.Input.Width && value.Length > 1) value = value[1..];
		Utils.DrawBorderString(Main.spriteBatch, value, new(layout.Input.X, layout.Input.Center.Y - 10 * layout.Scale),
			Main.drawingPlayerChat ? Color.White : new Color(190, 190, 190), scale);
	}

	private void DrawHistory()
	{
		float scale = .85f * layout.Scale;
		int limit = Math.Max(1, (int)(layout.History.Width / scale));
		var lines = new List<TextSnippet[]>();
		foreach (Message message in messages) {
			if (!open && Now - message.Time > 10) break;
			if (message.Lines == null || message.Width != limit) { message.Width = limit; message.Lines = Wrap(message.Text, message.Color, limit); }
			for (int i = message.Lines.Count - 1; i >= 0; i--) lines.Add(message.Lines[i]);
		}
		float lineHeight = 21 * layout.Scale;
		int visible = Math.Max(1, (int)(layout.History.Height / lineHeight));
		offset = Math.Clamp(offset, 0, Math.Max(0, lines.Count - visible));
		Rectangle savedScissor = Main.instance.GraphicsDevice.ScissorRectangle;
		Main.spriteBatch.End();
		Main.instance.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(layout.History, Main.instance.GraphicsDevice.Viewport.Bounds);
		historyClip ??= new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None };
		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, historyClip);
		try {
			for (int i = 0; i < visible && i + offset < lines.Count; i++) {
				TextSnippet[] snippets = lines[i + offset];
				ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value, snippets,
					new(layout.History.X, layout.History.Bottom - lineHeight * (i + 1)), 0f, Vector2.Zero, new(scale), out int hovered);
				if (open && hovered >= 0) {
					snippets[hovered].OnHover();
					if (Main.mouseLeft && Main.mouseLeftRelease) snippets[hovered].OnClick();
				}
			}
		}
		finally { Main.spriteBatch.End(); Main.instance.GraphicsDevice.ScissorRectangle = savedScissor; Begin(false); }
	}

	internal static List<TextSnippet[]> Wrap(string text, Color color, int width)
	{
		var lines = new List<TextSnippet[]>();
		var line = new List<TextSnippet>(); float x = 0;
		void Next() { lines.Add(line.ToArray()); line.Clear(); x = 0; }
		foreach (TextSnippet snippet in ChatManager.ParseMessage(text, color)) {
			if (snippet.UniqueDraw(true, out Vector2 size, Main.spriteBatch)) {
				if (x > 0 && x + size.X > width) Next(); line.Add(snippet); x += size.X; continue;
			}
			var elements = StringInfo.GetTextElementEnumerator(snippet.Text);
			string segment = "";
			while (elements.MoveNext()) {
				string element = elements.GetTextElement();
				if (element == "\r") continue;
				float w = FontAssets.MouseText.Value.MeasureString(element).X * snippet.Scale;
				if (element == "\n" || x > 0 && x + w > width) {
					if (segment.Length > 0) line.Add(snippet.CopyMorph(segment)); segment = ""; Next();
					if (element == "\n") continue;
				}
				segment += element; x += w;
			}
			if (segment.Length > 0) line.Add(snippet.CopyMorph(segment));
		}
		if (line.Count > 0) Next();
		return lines;
	}

	private static void Begin(bool end = true)
	{
		if (end) Main.spriteBatch.End();
		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise);
	}
	private static string T(string chinese, string english) => Language.ActiveCulture.Name == "zh-Hans" ? chinese : english;

	private void RefreshGroups() => groups = new[] { UIEmotesMenu.GetEmotesGeneral(), UIEmotesMenu.GetEmotesRPS(), UIEmotesMenu.GetEmotesItems(),
		UIEmotesMenu.GetEmotesBiomesAndEvents(), UIEmotesMenu.GetEmotesTownNPCs(), UIEmotesMenu.GetEmotesCritters(), UIEmotesMenu.GetEmotesBosses() };
	private void ReadEmotePress(Vector2 pointer)
	{
		if (layout.EmotesClose.Contains(pointer.ToPoint())) { emotes = false; return; }
		for (int i = 0; i < 7; i++) if (layout.Category(i).Contains(pointer.ToPoint())) { category = i; emoteRow = 0; return; }
		if (groups == null) RefreshGroups();
		for (int i = 0; i + emoteRow * 10 < groups[category].Count && i < 30; i++) if (layout.EmoteCell(i).Contains(pointer.ToPoint())) {
			pendingEmote = groups[category][i + emoteRow * 10]; return;
		}
	}
	private void DrawEmotes()
	{
		if (groups == null) RefreshGroups();
		MobileHudArtwork.Nine("EmotePanel", layout.EmotesPanel, Color.White * .92f, layout.Scale);
		MobileHudArtwork.Nine("EmoteHeader", layout.EmotesTitle, Color.White, layout.Scale);
		Utils.DrawBorderString(Main.spriteBatch, T("表情", "Emotes"), layout.EmotesTitle.Center.ToVector2(), Color.White, 1.25f * layout.Scale, .5f, .5f);
		for (int i = 0; i < 7; i++) {
			Rectangle bounds = layout.Category(i);
			MobileHudArtwork.Nine(i == category ? "EmoteHeader" : "Toolbar", bounds, Color.White, layout.Scale * .6f);
			MobileHudArtwork.Icon("Category" + i, bounds, Color.White, .8f);
		}
		for (int i = 0; i + emoteRow * 10 < groups[category].Count && i < 30; i++) {
			Rectangle bounds = layout.EmoteCell(i);
			MobileHudArtwork.Nine("EmoteSlot", bounds, Color.White * .9f, layout.Scale * .6f);
			DrawEmote(groups[category][i + emoteRow * 10], bounds);
		}
		MobileHudArtwork.Nine("EmoteHeader", layout.EmotesClose, Color.White, layout.Scale * .6f);
		Rectangle close = layout.EmotesClose; close.Width = (int)(44 * layout.Scale);
		MobileHudArtwork.Icon("Close", close, Color.White);
		Utils.DrawBorderString(Main.spriteBatch, T("关闭", "Close"), layout.EmotesClose.Center.ToVector2() + new Vector2(12 * layout.Scale, 0), Color.White, 1.05f * layout.Scale, .5f, .5f);
	}
	private static void DrawEmote(int id, Rectangle bounds)
	{
		int frameIndex = (int)(Now * 6) % 2;
		Texture2D bubbles = TextureAssets.Extra[ExtrasID.EmoteBubble].Value;
		Texture2D texture = id < EmoteID.Count ? bubbles : ModContent.Request<Texture2D>(EmoteBubbleLoader.GetEmoteBubble(id).Texture).Value;
		Rectangle frame = id < EmoteID.Count ? texture.Frame(8, EmoteBubble.EMOTE_SHEET_VERTICAL_FRAMES, id % 4 * 2 + frameIndex, id / 4 + 1)
			: EmoteBubbleLoader.GetFrameInEmoteMenu(id, frameIndex, frameIndex * 10) ?? texture.Frame(2, 1, frameIndex);
		float scale = Math.Min(bounds.Width * .82f / frame.Width, bounds.Height * .82f / frame.Height);
		Vector2 center = bounds.Center.ToVector2();
		Main.spriteBatch.Draw(bubbles, center, bubbles.Frame(8, 39, 1, 0), Color.White, 0f, frame.Size() / 2, scale, SpriteEffects.None, 0);
		Main.spriteBatch.Draw(texture, center, frame, Color.White, 0f, frame.Size() / 2, scale, SpriteEffects.None, 0);
	}
}
