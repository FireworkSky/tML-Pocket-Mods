using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Newtonsoft.Json;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Social;
using Terraria.Net;

namespace MobileJoystick;

internal sealed partial class MobileMultiplayerSystem : ModSystem
{
	private const int MobileMenuMode = 12012;
	private enum Page { Browser, Connect, HostOptions }
	private static readonly MethodInfo StartNativeHost = typeof(Main).GetMethod("OnSubmitServerPassword", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
	private ILHook hostArgumentsHook;
	private bool hostArgumentsPatched, launchingHost;
	private static bool session, choosingPlayer;
	private Page page;
	private bool localExpanded = true, remoteExpanded = true, remember, inviteOnly;
	private int maximumPlayers = 8;
	private string address = "", port = "7777", password = "", hostPassword = "", notice = "";
	private bool down, previousDown, waitForRelease = true, escapeDown;
	private int touchId = -1, pressAction = -1, wheel;
	private Vector2 pointer, pressPosition, lastPointer;
	private bool dragging;
	private float scroll;
	private MobileMultiplayerLayout layout;
	private Action<string> saveEdit;
	private string editText = "";
	private int editingField = -1;
	private bool secretEdit;
	private readonly List<SavedEndpoint> endpoints = new();
	private static MobileMultiplayerSystem Instance => ModContent.GetInstance<MobileMultiplayerSystem>();
	internal static bool Active => !Main.dedServ && Main.gameMenu && Main.menuMode == MobileMenuMode && Enabled;
	internal static bool HostWorldSelection => session && Main.menuMultiplayer && Main.menuServer;
	private static bool Enabled => MobileJoystickConfig.Instance?.EnableMobileControls == true && !PlayerInput.UsingGamepadUI;
	private static string T(string zh, string en) => MobileSettingsCatalog.Text(zh, en);
	private string EndpointPath => Path.Combine(Main.SavePath, "ModConfigs", "MobileJoystick_Servers.json");

	public override void Load()
	{
		if (Main.dedServ) return;
		On_Main.DrawMenu += DrawMenu;
		// Preserve the native launcher and inject its documented player limit only
		// for launches requested by this view.
		if (StartNativeHost != null) hostArgumentsHook = new ILHook(StartNativeHost, il => {
			var cursor = new ILCursor(il);
			if (!cursor.TryGotoNext(MoveType.After, i => i.MatchLdstr("-autoshutdown -password \""))) return;
			cursor.EmitDelegate<Func<string, string>>(prefix => launchingHost ? $"-maxplayers {maximumPlayers} " + prefix : prefix);
			hostArgumentsPatched = true;
		});
	}
	public override void Unload()
	{
		if (Main.dedServ) return;
		FinishEdit(false);
		On_Main.DrawMenu -= DrawMenu;
		hostArgumentsHook?.Dispose();
		session = choosingPlayer = false;
	}
	internal static void Open()
	{
		session = choosingPlayer = true;
		Main.LoadPlayers(); Main.menuMultiplayer = true; Main.menuServer = false;
		Main.ClearPendingPlayerSelectCallbacks(); Main.menuMode = 1;
		Instance.CancelPress(); Instance.LoadEndpoints();
	}
	internal static void ShowBrowser()
	{
		session = true; choosingPlayer = false;
		Main.menuMultiplayer = true; Main.menuServer = false;
		Instance.page = Page.Browser; Instance.notice = "";
		Main.menuMode = MobileMenuMode; Instance.CancelPress();
	}
	private void ShowHostWorlds()
	{
		Main.menuMultiplayer = Main.menuServer = true; Main.menuMode = 6; CancelPress();
	}
	public override void OnWorldUnload() { FinishEdit(false); session = choosingPlayer = false; CancelPress(); }
	private void CancelPress()
	{
		down = previousDown = dragging = false; touchId = pressAction = -1; waitForRelease = true;
	}
	public override void PostUpdateInput()
	{
		if (!Enabled) {
			FinishEdit(false);
			if (Main.menuMode == MobileMenuMode) Main.menuMode = page == Page.HostOptions ? 889 : 12;
			session = choosingPlayer = false; CancelPress(); return;
		}
		if (!Main.gameMenu || Main.menuMode == 0) { FinishEdit(false); session = choosingPlayer = false; CancelPress(); return; }
		if (choosingPlayer && Main.menuMode == 13) ShowBrowser();
		if (Main.menuMode == 12) { if (session) ShowBrowser(); else Open(); }
		if (session && Main.menuServer && Main.menuMode is 889 or 30) {
			page = Page.HostOptions; hostPassword = Netplay.ServerPassword ?? "";
			inviteOnly = !Main.MenuServerMode.HasFlag(ServerMode.FriendsCanJoin);
			Main.menuMode = MobileMenuMode; CancelPress();
		}
		if (!Active) { FinishEdit(false); CancelPress(); return; }
		layout = new(PlayerInput.RealScreenWidth, PlayerInput.RealScreenHeight);
		if (!Main.hasFocus) { CancelPress(); return; }
		bool escape = Keyboard.GetState().IsKeyDown(Keys.Escape);
		if (escape && !escapeDown) { escapeDown = true; Back(); return; }
		escapeDown = escape; ReadPointer();
	}
	private void ReadPointer()
	{
		MouseState mouse = Mouse.GetState();
		if (touchId < 0) pointer = new(mouse.X, mouse.Y);
		down = mouse.LeftButton == ButtonState.Pressed;
		bool cancelled = false;
		TouchCollection touches = MobileTouchInput.GetState();
		if (touchId < 0) foreach (TouchLocation touch in touches)
			if (touch.State == TouchLocationState.Pressed) { touchId = touch.Id; break; }
		if (touchId >= 0) {
			bool found = false; down = false;
			foreach (TouchLocation touch in touches) {
				if (touch.Id != touchId) continue;
				found = true;
				int width = TouchPanel.DisplayWidth > 0 ? TouchPanel.DisplayWidth : PlayerInput.RealScreenWidth;
				int height = TouchPanel.DisplayHeight > 0 ? TouchPanel.DisplayHeight : PlayerInput.RealScreenHeight;
				pointer = new(touch.Position.X * PlayerInput.RealScreenWidth / width, touch.Position.Y * PlayerInput.RealScreenHeight / height);
				cancelled = touch.State == TouchLocationState.Invalid;
				down = !cancelled && touch.State != TouchLocationState.Released; break;
			}
			cancelled |= !found; if (!down) touchId = -1;
		}
		int delta = mouse.ScrollWheelValue - wheel; wheel = mouse.ScrollWheelValue;
		if (waitForRelease) { if (!down) waitForRelease = false; return; }
		if (cancelled) { CancelPress(); return; }
		if (page == Page.Browser && delta != 0 && EndpointArea.Contains(pointer.ToPoint())) ClampScroll(scroll - delta / 120f * 44);
		if (down && !previousDown) { pressPosition = lastPointer = pointer; pressAction = Hit(pointer); dragging = false; }
		if (down && pressAction == 30) SetPlayerCount(pointer.X);
		if (down && page == Page.Browser && pressAction >= 100) {
			if (Vector2.DistanceSquared(pointer, pressPosition) > 64 * layout.Scale * layout.Scale) dragging = true;
			if (dragging) ClampScroll(scroll + (lastPointer.Y - pointer.Y) / layout.Scale);
		}
		lastPointer = pointer;
		if (!down && previousDown && !dragging && pressAction >= 0 && Hit(pointer) == pressAction) Execute(pressAction);
		previousDown = down;
	}
	private void ClampScroll(float value) => scroll = Math.Clamp(value, 0, Math.Max(0, endpoints.Count * 44 - EndpointArea.Height / layout.Scale));
	private void SetPlayerCount(float x) => maximumPlayers = Math.Clamp(1 + (int)Math.Round(7 * (x - layout.Slider.Left) / layout.Slider.Width), 1, 8);
	private Rectangle EndpointBounds(int index) => layout.At(379, (localExpanded ? 348 : 288) + index * 44 - scroll, 522, 42);
	private Rectangle EndpointArea => layout.At(379, localExpanded ? 348 : 288, 522, localExpanded ? 122 : 182);
	private int Hit(Vector2 point)
	{
		Point p = point.ToPoint();
		if (page == Page.Browser) {
			if (layout.Back.Contains(p)) return 0;
			if (layout.Add.Contains(p)) return 1;
			if (layout.HostTab.Contains(p)) return 3;
			if (layout.LocalRow.Contains(p)) return 4;
			if (layout.RemoteHeader(localExpanded).Contains(p)) return 5;
			if (remoteExpanded && EndpointArea.Contains(p)) for (int i = 0; i < endpoints.Count; i++) if (EndpointArea.Contains(EndpointBounds(i)) && EndpointBounds(i).Contains(p)) return 100 + i;
		} else if (page == Page.Connect) {
			if (layout.FormBack.Contains(p)) return 0;
			if (layout.FormJoin.Contains(p)) return 20;
			for (int i = 0; i < 3; i++) if (layout.Field(i).Contains(p)) return 10 + i;
			if (layout.Toggle(layout.Field(3), false).Contains(p) || layout.Toggle(layout.Field(3), true).Contains(p)) return 13;
		} else {
			if (layout.HostBack.Contains(p)) return 0;
			if (layout.HostStart.Contains(p)) return 21;
			for (int i = 0; i < 4; i++) if (layout.HostOption(i).Contains(p)) return 30 + i;
		}
		return -1;
	}
	private void Execute(int action)
	{
		FinishEdit(true);
		SoundEngine.PlaySound(SoundID.MenuTick);
		switch (action) {
			case 0: Back(); break;
			case 1: page = Page.Connect; notice = ""; scroll = 0; break;
			case 3: ShowHostWorlds(); break;
			case 4: localExpanded = !localExpanded; scroll = 0; break;
			case 5: remoteExpanded = !remoteExpanded; scroll = 0; break;
			case 10: Edit(0, address, v => address = v.Trim()); break;
			case 11: Edit(1, port, v => port = v.Trim()); break;
			case 12: Edit(2, password, v => password = v, true); break;
			case 13: remember = layout.Toggle(layout.Field(3), true).Contains(pointer.ToPoint()); break;
			case 20: Join(); break;
			case 21: StartHost(); break;
			case 31:
				if (SocialAPI.Network != null) {
					if (layout.Toggle(layout.HostOption(1), true).Contains(pointer.ToPoint())) inviteOnly = true;
					else if (layout.Toggle(layout.HostOption(1), false).Contains(pointer.ToPoint())) inviteOnly = false;
				}
				else notice = T("仅限邀请需要启用 Steam 联机。", "Invite-only requires Steam networking.");
				break;
			case 32: notice = T("当前 tModLoader 不支持手机版跨平台会话。", "Mobile cross-platform sessions are unavailable in tModLoader."); break;
			case 33: Edit(3, hostPassword, v => hostPassword = v, true); break;
			default:
				if (action >= 100 && action - 100 < endpoints.Count) {
					SavedEndpoint endpoint = endpoints[action - 100];
					address = endpoint.Address; port = endpoint.Port.ToString(); password = ""; remember = true;
					page = Page.Connect; notice = "";
				}
				break;
		}
		CancelPress();
	}
	private void Back()
	{
		if (saveEdit != null) { FinishEdit(false); CancelPress(); return; }
		if (page == Page.HostOptions) ShowHostWorlds();
		else if (page == Page.Connect) ShowBrowser();
		else { choosingPlayer = true; Main.menuServer = false; Main.menuMode = 1; Main.LoadPlayers(); }
		CancelPress(); SoundEngine.PlaySound(SoundID.MenuClose);
	}
	private void Edit(int field, string initial, Action<string> save, bool secret = false)
	{
		Main.clrInput();
		editingField = field; editText = initial; saveEdit = save; secretEdit = secret;
		MobileTextInputSystem.Focus(this);
		PlayerInput.WritingText = true;
	}
	private void FinishEdit(bool save)
	{
		if (saveEdit == null) return;
		if (save) { saveEdit(editText); notice = ""; }
		saveEdit = null; editingField = -1; editText = "";
		MobileTextInputSystem.Blur(this);
		PlayerInput.WritingText = false;
	}
	private void UpdateTextInput()
	{
		if (saveEdit == null || !Main.hasFocus) return;
		PlayerInput.WritingText = true;
		Main.instance.HandleIME();
		editText = Main.GetInputText(editText);
		if (editText.Length > 255) editText = editText[..255];
		if (Main.inputTextEnter) FinishEdit(true);
		else if (Main.inputTextEscape) FinishEdit(false);
	}
	private void Join()
	{
		if (string.IsNullOrWhiteSpace(address) || Uri.CheckHostName(address.Trim()) == UriHostNameType.Unknown) { notice = T("请输入有效的 IP 地址或主机名。", "Enter a valid IP address or host name."); return; }
		if (!ushort.TryParse(port, out ushort number) || number == 0) { notice = T("端口必须为 1 到 65535。", "Port must be between 1 and 65535."); return; }
		address = address.Trim();
		if (remember) {
			endpoints.RemoveAll(e => e.Address.Equals(address, StringComparison.OrdinalIgnoreCase) && e.Port == number);
			endpoints.Insert(0, new SavedEndpoint { Address = address, Port = number });
			if (endpoints.Count > 20) endpoints.RemoveRange(20, endpoints.Count - 20);
			try { Directory.CreateDirectory(Path.GetDirectoryName(EndpointPath)); File.WriteAllText(EndpointPath, JsonConvert.SerializeObject(endpoints)); }
			catch (IOException e) { Mod.Logger.Warn("Could not save multiplayer address: " + e.Message); }
		}
		Main.getIP = address; Main.getPort = port;
		Netplay.ListenPort = number; Netplay.ServerPassword = password; Main.autoPass = password.Length > 0;
		Main.menuMode = 14; Main.statusText = Terraria.Localization.Language.GetTextValue("Net.ConnectingTo", address);
		Netplay.SetRemoteIPAsync(address, Main.StartClientGameplay);
	}
	private void StartHost()
	{
		if (StartNativeHost == null || !hostArgumentsPatched) { notice = T("无法调用当前版本的主机启动接口。", "The host launcher is unavailable in this version."); return; }
		Main.MenuServerMode = SocialAPI.Network == null ? ServerMode.None : ServerMode.Lobby | (inviteOnly ? ServerMode.None : ServerMode.FriendsCanJoin);
		Netplay.ServerPassword = hostPassword; launchingHost = true;
		try { StartNativeHost.Invoke(Main.instance, null); }
		catch (TargetInvocationException e) { Mod.Logger.Error("Mobile host launch failed", e.InnerException ?? e); notice = T("启动失败，请查看日志。", "Host launch failed. See the log."); Main.menuMode = MobileMenuMode; }
		finally { launchingHost = false; }
	}
	private void LoadEndpoints()
	{
		endpoints.Clear();
		try {
			if (File.Exists(EndpointPath)) endpoints.AddRange((JsonConvert.DeserializeObject<List<SavedEndpoint>>(File.ReadAllText(EndpointPath)) ?? new())
				.Where(e => e != null && !string.IsNullOrWhiteSpace(e.Address) && e.Port is > 0 and <= 65535).Take(20));
		} catch (Exception e) when (e is IOException or JsonException) { Mod.Logger.Warn("Could not read multiplayer addresses: " + e.Message); }
	}
	private sealed class SavedEndpoint { public string Address { get; set; } public int Port { get; set; } }
}
