using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.Graphics.Effects;
using Terraria.ModLoader;
using ReLogic.Content;
using Terraria.UI;

namespace MobileJoystick;

// Sample the scene before UI drawing so the lens cannot magnify itself.
internal sealed class MobilePrecisionMagnifier : ModSystem
{
	private bool capturing, capturedScene, warned;
	private Texture2D fallback;
	private Color[] pixels;
	private Rectangle source;
	private double nextReadback;
	private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
	internal static bool IsLongPress(double start, double now) => now - start >= .28;

	public override void Load()
	{
		if (!Main.dedServ) On_Main.DrawInterface += CaptureBeforeInterface;
	}

	public override void Unload()
	{
		if (!Main.dedServ) On_Main.DrawInterface -= CaptureBeforeInterface;
		SetCapture(false);
		fallback?.Dispose();
		fallback = null;
		pixels = null;
	}

	public override void PostUpdateInput() => SetCapture(MobileWorldInteraction.ShowMagnifier);
	public override void OnWorldUnload() => SetCapture(false);

	private void SetCapture(bool enabled)
	{
		if (enabled == capturing) return;
		capturing = enabled;
		if (enabled) Filters.Scene.OnPostDraw += SceneCaptured;
		else { Filters.Scene.OnPostDraw -= SceneCaptured; capturedScene = false; }
	}
	private void SceneCaptured() => capturedScene = true;

	internal static Rectangle LensBounds(Vector2 finger, int width, int height)
	{
		int size = (int)MathHelper.Clamp(Math.Min(width / 1280f, height / 720f) * 140f, 96f, 220f);
		size = Math.Min(size, Math.Min(width, height) - 12);
		int y = (int)finger.Y - size - 38;
		if (y < 6) y = (int)finger.Y + 38;
		return new Rectangle(Math.Clamp((int)finger.X - size / 2, 6, Math.Max(6, width - size - 6)),
			Math.Clamp(y, 6, Math.Max(6, height - size - 6)), size, size);
	}

	internal static Rectangle SampleBounds(Vector2 point, int width, int height, int lensSize)
	{
		int size = Math.Max(1, Math.Min((int)(lensSize / 2.5f), Math.Min(width, height)));
		return new Rectangle(Math.Clamp((int)point.X - size / 2, 0, width - size),
			Math.Clamp((int)point.Y - size / 2, 0, height - size), size, size);
	}

	private void CaptureBeforeInterface(On_Main.orig_DrawInterface orig, Main self, GameTime time)
	{
		if (MobileWorldInteraction.ShowMagnifier && !capturedScene && Now >= nextReadback) {
			// Retro lighting renders directly to the backbuffer. Read only the
			// small lens region at 20 Hz; normal lighting stays entirely on GPU.
			var device = self.GraphicsDevice;
			int width = device.PresentationParameters.BackBufferWidth, height = device.PresentationParameters.BackBufferHeight;
			var lens = LensBounds(MobileWorldInteraction.PrecisionPosition, width, height);
			source = SampleBounds(MobileWorldInteraction.PrecisionPosition, width, height, lens.Width);
			try {
				if (fallback == null || fallback.IsDisposed || fallback.Width != source.Width || fallback.Height != source.Height) {
					fallback?.Dispose();
					fallback = new Texture2D(device, source.Width, source.Height);
					pixels = new Color[source.Width * source.Height];
				}
				device.GetBackBufferData(source, pixels, 0, pixels.Length);
				fallback.SetData(pixels);
			}
			catch (Exception error) when (error is InvalidOperationException or NotSupportedException) {
				if (!warned) { Mod.Logger.Warn("Touch magnifier capture unavailable: " + error.Message); warned = true; }
			}
			nextReadback = Now + .05;
		}
		orig(self, time);
		capturedScene = false;
	}

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		if (!MobileWorldInteraction.ShowMagnifier) return;
		int index = layers.FindIndex(layer => layer.Name == "Vanilla: Cursor");
		layers.Insert(index < 0 ? layers.Count : index, new LegacyGameInterfaceLayer("MobileJoystick: Precision Lens", DrawLens, InterfaceScaleType.None));
	}

	internal static bool UsesPrecisionGrid(Item item, bool smart) => MobileWorldInteraction.IsPrecisionItem(item) && !smart;

	// Scene touches and the aim stick share one marker policy. Only the image is
	// magnified; the marker retains a readable size inside the lens.
	internal static void DrawAimMarker(SpriteBatch batch, Vector2 point, float scale,
		Rectangle? lens = null, Rectangle? sampled = null)
	{
		Vector2 ratio = lens.HasValue ? lens.Value.Size() / sampled.Value.Size() : Vector2.One;
		Vector2 offset = lens.HasValue ? lens.Value.Location.ToVector2() - sampled.Value.Location.ToVector2() * ratio : Vector2.Zero;
		Vector2 marker = point * ratio + offset;
		if (!UsesPrecisionGrid(Main.LocalPlayer.HeldItem, MobileJoystickInput.SmartCursorEnabled)) {
			var texture = ModContent.Request<Texture2D>("MobileJoystick/Assets/MobileControls/AimReticle", AssetRequestMode.ImmediateLoad).Value;
			batch.Draw(texture, marker, null, Color.White, 0, texture.Size() * .5f,
				28f * scale / Math.Max(texture.Width, texture.Height), SpriteEffects.None, 0);
			return;
		}
		Vector2 world = MobileWorldInteraction.ToWorld(point);
		Point tile = world.ToTileCoordinates();
		Vector2 first = MobileWorldInteraction.ToScreen(new Vector2((tile.X - 2) * 16, (tile.Y - 2) * 16)) * ratio + offset;
		Vector2 last = MobileWorldInteraction.ToScreen(new Vector2((tile.X + 3) * 16, (tile.Y + 3) * 16)) * ratio + offset;
		var grid = new Rectangle((int)Math.Min(first.X, last.X), (int)Math.Min(first.Y, last.Y),
			(int)Math.Abs(last.X - first.X), (int)Math.Abs(last.Y - first.Y));
		for (int i = 0; i <= 5; i++) {
			Stroke(new Rectangle(grid.X + grid.Width * i / 5, grid.Y, 1, grid.Height), Color.White * .18f);
			Stroke(new Rectangle(grid.X, grid.Y + grid.Height * i / 5, grid.Width, 1), Color.White * .18f);
		}
		int length = Math.Max(4, (int)(10 * scale)), thickness = Math.Max(1, (int)(2 * scale));
		int x = (int)marker.X, y = (int)marker.Y;
		Stroke(new Rectangle(x - length, y - thickness / 2, length * 2 + 1, thickness), Color.White);
		Stroke(new Rectangle(x - thickness / 2, y - length, thickness, length * 2 + 1), Color.White);

		void Stroke(Rectangle bounds, Color color)
		{
			if (lens.HasValue) bounds = Rectangle.Intersect(bounds, lens.Value);
			if (bounds.Width > 0 && bounds.Height > 0) batch.Draw(TextureAssets.MagicPixel.Value, bounds, color);
		}
	}

	private bool DrawLens()
	{
		if (!MobileWorldInteraction.ShowMagnifier) return true;
		int width = PlayerInput.RealScreenWidth, height = PlayerInput.RealScreenHeight;
		Vector2 point = MobileWorldInteraction.PrecisionPosition;
		Rectangle lens = LensBounds(point, width, height);
		Texture2D texture = capturedScene ? Main.screenTarget : fallback;
		if (texture == null || texture.IsDisposed) return true;
		Rectangle sample = capturedScene ? SampleBounds(point, texture.Width, texture.Height, lens.Width)
			: new Rectangle(0, 0, texture.Width, texture.Height);
		var batch = Main.spriteBatch;
		batch.End();
		batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
		try {
			batch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(lens.X - 3, lens.Y - 3, lens.Width + 6, lens.Height + 6), Color.Black);
			batch.Draw(texture, lens, sample, Color.White);
			DrawAimMarker(batch, point, Math.Min(width / 1280f, height / 720f) * .5f, lens,
				capturedScene ? sample : source);
		}
		finally {
			batch.End();
			// GameInterfaceLayer owns the surrounding batch lifetime.
			batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
				DepthStencilState.None, RasterizerState.CullCounterClockwise);
		}
		return true;
	}
}
