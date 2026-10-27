using System.Linq;
using NunMP.Net;
using NunMP.Sync;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NunMP.UI
{
    // IMGUI lobby: auto-shows on the main menu, F2 toggles anywhere.
    public class LobbyMenu : MonoBehaviour
    {
        static readonly (string id, string label, bool ready)[] Modes =
        {
            ("coop", "Co-op Survival (2-8 vs the Nun)", true),
            ("nunplayer", "Play as Nun (1 nun vs the rest)", true),
        };

        bool show;
        public static bool Open, TestToggle;
        string code = "";
        Rect win = new Rect(20, 60, 420, 10);
        float nextFriends;
        System.Collections.Generic.List<(string name, ulong lobby)> friends = new System.Collections.Generic.List<(string, ulong)>();
        bool cursorForced;

        void Start()
        {
            SceneManager.sceneLoaded += (s, _) => show = s.name == "Nun Bootup";
            SteamLobby.OnEntered = owner =>
            {
                var self = Steamworks.SteamUser.GetSteamID().m_SteamID;
                var t = new SteamTransport(owner, SteamLobby.IsMember);
                if (owner == self) Session.StartHost(t, MyName); else Session.StartClient(t, MyName);
            };
        }

        static string MyName => SteamLobby.Ready ? Steamworks.SteamFriends.GetPersonaName() : "Player" + System.Diagnostics.Process.GetCurrentProcess().Id % 1000;

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2) || TestToggle) { show = !show; TestToggle = false; }
            Open = show;
            if (show && Time.unscaledTime > nextFriends) { nextFriends = Time.unscaledTime + 3f; friends = SteamLobby.FriendLobbies(); }
            // The game re-locks the cursor every frame for mouse look: switch its lock off through its own event,
            // and stop the camera turning while the panel is open (restored on close).
            bool inGame = SceneManager.GetActiveScene().name == GameFlow.GameScene;
            var fpc = PlayerSync.Local ? PlayerSync.Local.firstPersonController : null;
            if (show && inGame && !cursorForced)
            {
                cursorForced = true;
                GrannyClone.MessagesGranny.MouseLockToggleEvent?.Invoke(false);
                if (fpc) { lookWas = fpc.canMouseLook; fpc.canMouseLook = false; }
            }
            else if (!show && cursorForced)
            {
                cursorForced = false;
                if (inGame && !NunMode.IAmNun && !LivesSync.Spectating)
                {
                    GrannyClone.MessagesGranny.MouseLockToggleEvent?.Invoke(true);
                    if (fpc) fpc.canMouseLook = lookWas;
                    Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
                }
            }
            if (show) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
        bool lookWas = true;

        // OnGUI runs after everyone's Update/LateUpdate: last word on the cursor while the panel is up.
        void LateUpdate() { if (show) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } }

        void OnGUI()
        {
            if (!show) return;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            win = GUILayout.Window(0x4E554E, win, Draw, "Nun Massacre Multiplayer " + Session.Version + "   (F2 hide)");
        }

        void Draw(int _)
        {
            GUILayout.Label("Status: " + Session.Status);
            if (!SteamLobby.Ready) GUILayout.Label("Steam: " + (SteamLobby.Error == "" ? "starting..." : SteamLobby.Error));

            GUILayout.Space(6);
            GUILayout.Label("GAMEMODE");
            foreach (var m in Modes)
            {
                GUI.enabled = m.ready && (!Session.Active || Session.IsHost) && !Session.GameStarted;
                if (GUILayout.Toggle(Session.Mode == m.id, m.label) && Session.Mode != m.id) { Session.Mode = m.id; if (Session.IsHost) Session.SendRoster(); }
            }
            GUI.enabled = true;

            GUILayout.Space(6);
            if (Session.Mode == NunMode.Id && Session.Active)
            {
                GUILayout.Label("WHO IS THE NUN");
                GUI.enabled = Session.IsHost && !Session.GameStarted;
                GUILayout.BeginHorizontal();
                if (GUILayout.Toggle(Rules.NunSlot == 255, "Random") && Rules.NunSlot != 255) { Rules.NunSlot = 255; Session.SendRoster(); }
                foreach (var p in Session.Peers.Values.OrderBy(p => p.Slot))
                    if (GUILayout.Toggle(Rules.NunSlot == p.Slot, p.Name) && Rules.NunSlot != p.Slot) { Rules.NunSlot = p.Slot; Session.SendRoster(); }
                GUILayout.EndHorizontal();
                GUI.enabled = true;
                // the nun player chooses her look
                bool iPick = Rules.NunSlot == Session.MySlot || (Session.IsHost && Rules.NunSlot == 255);
                GUILayout.Label(iPick ? "YOUR NUN" : "NUN LOOK (the nun player picks)");
                GUI.enabled = iPick && !Session.GameStarted;
                GUILayout.BeginHorizontal();
                foreach (var sk in Rules.Skins)
                    if (GUILayout.Toggle(Rules.NunSkin == (byte)sk.id, sk.label) && Rules.NunSkin != (byte)sk.id)
                    {
                        var b = (byte)sk.id;
                        if (Session.IsHost) { Rules.NunSkin = b; Session.SendRoster(); }
                        else Session.SendPref(b);
                    }
                GUILayout.EndHorizontal();
                GUI.enabled = true;
            }
            var before = Rules.Describe;
            Rules.DrawEditor((!Session.Active || Session.IsHost) && !Session.GameStarted);
            if (Session.IsHost && Rules.Describe != before) Session.SendRoster();

            GUILayout.Space(6);
            GUILayout.Label("VOICE");
            GUILayout.BeginHorizontal();
            var ptt = GUILayout.Toggle(Voice.PushToTalk, "Push-to-talk (hold V)");
            var open = GUILayout.Toggle(!Voice.PushToTalk, "Open mic");
            GUILayout.EndHorizontal();
            if (ptt != Voice.PushToTalk || open == Voice.PushToTalk) { Voice.PushToTalk = !Voice.PushToTalk; Voice.SaveSettings(); }
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Voice volume {Voice.Volume * 100:0}%", GUILayout.Width(150));
            var vol = GUILayout.HorizontalSlider(Voice.Volume, 0f, 3f);
            GUILayout.EndHorizontal();
            if (Mathf.Abs(vol - Voice.Volume) > 0.001f) { Voice.Volume = vol; Voice.SaveSettings(); }
            GUILayout.BeginHorizontal();
            var direct = GUILayout.Toggle(DirectMic.UseDirect, "Mic: direct (louder)");
            var steam = GUILayout.Toggle(!DirectMic.UseDirect, "Mic: Steam");
            if (direct != DirectMic.UseDirect || steam == DirectMic.UseDirect) { DirectMic.UseDirect = !DirectMic.UseDirect; DirectMic.Save(); }
            GUILayout.EndHorizontal();
            if (DirectMic.UseDirect)
            {
                var auto = GUILayout.Toggle(DirectMic.AutoBoost, $"Auto boost (now {DirectMic.AutoGain:0}x) - adjusts to your mic while you talk");
                if (auto != DirectMic.AutoBoost) { DirectMic.AutoBoost = auto; DirectMic.Save(); }
                if (!DirectMic.AutoBoost)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"Mic boost {DirectMic.Boost:0.0}x", GUILayout.Width(150));
                    var boost = GUILayout.HorizontalSlider(DirectMic.Boost, 1f, 40f);
                    GUILayout.EndHorizontal();
                    if (Mathf.Abs(boost - DirectMic.Boost) > 0.01f) { DirectMic.Boost = boost; DirectMic.Save(); }
                }
                if (Microphone.devices.Length > 1 && GUILayout.Button("Mic device: " + DirectMic.Device + "  (click to change)"))
                { DirectMic.DeviceIndex = (DirectMic.DeviceIndex + 1) % Microphone.devices.Length; DirectMic.Save(); DirectMic.Stop(); }
            }
            if (GameFlow.InGame) GUILayout.Label("Voice stats: " + Voice.ShortStats);

            GUILayout.Space(6);
            GUILayout.Label("SOUND");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Nun: 'they saw you' cue {NunPlayer.NoticeVolume * 100:0}%", GUILayout.Width(210));
            var nv = GUILayout.HorizontalSlider(NunPlayer.NoticeVolume, 0f, 1f);
            if (GUILayout.Button("Test", GUILayout.Width(50))) NunPlayer.PlayNotice();
            GUILayout.EndHorizontal();
            if (Mathf.Abs(nv - NunPlayer.NoticeVolume) > 0.001f) { NunPlayer.NoticeVolume = nv; PlayerPrefs.SetFloat("nunmp_noticevol", nv); }
            GUILayout.BeginHorizontal();
            Voice.MicTest = GUILayout.Toggle(Voice.MicTest, "Test mic", GUILayout.Width(80));
            int bars = Mathf.Clamp(Mathf.RoundToInt(Voice.MyLevel * 120), 0, 30);
            GUILayout.Label((Voice.MicTest || Voice.Transmitting ? "[" + new string('|', bars) + new string('.', 30 - bars) + "] " : "") + Voice.Status);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            if (!Session.Active)
            {
                GUI.enabled = SteamLobby.Ready;
                if (GUILayout.Button("Host game (Steam, friends only)")) SteamLobby.Create(Session.Mode);
                GUILayout.Label("Friends hosting:");
                if (friends.Count == 0) GUILayout.Label("   (none found)");
                foreach (var f in friends) if (GUILayout.Button("Join " + f.name)) SteamLobby.Join(f.lobby);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Lobby code", GUILayout.Width(80));
                code = GUILayout.TextField(code);
                if (GUILayout.Button("Join", GUILayout.Width(60)) && ulong.TryParse(code.Trim(), out var id)) SteamLobby.Join(id);
                GUILayout.EndHorizontal();
                GUI.enabled = true;
                GUILayout.Space(6);
                GUILayout.Label("Dev (same PC):");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Host local")) Session.StartHost(new UdpTransport(true), MyName);
                if (GUILayout.Button("Join local")) Session.StartClient(new UdpTransport(false), MyName);
                GUILayout.EndHorizontal();
            }
            else
            {
                if (SteamLobby.Lobby != Steamworks.CSteamID.Nil)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Lobby code: " + SteamLobby.Lobby.m_SteamID);
                    if (GUILayout.Button("Copy", GUILayout.Width(60))) GUIUtility.systemCopyBuffer = SteamLobby.Lobby.m_SteamID.ToString();
                    GUILayout.EndHorizontal();
                }
                GUILayout.Label($"Players ({Session.Peers.Count}/{Session.MaxPlayers}):");
                foreach (var p in Session.Peers.Values.OrderBy(p => p.Slot))
                    GUILayout.Label($"   {(p.Slot == 0 ? "[host] " : "")}{p.Name}{(p.Slot == Session.MySlot ? " (you)" : "")}");
                if (Session.IsHost && !Session.GameStarted)
                {
                    GUI.enabled = SceneManager.GetActiveScene().name == "Nun Bootup";
                    if (GUILayout.Button("START GAME (uses your Play settings)")) GameFlow.HostStart();
                    GUI.enabled = true;
                }
                else if (!Session.GameStarted) GUILayout.Label("Waiting for host to start...");
                if (GUILayout.Button("Leave")) { Session.Stop(); SteamLobby.Leave(); }
            }
            GUI.DragWindow();
        }
    }
}
