using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Steamworks;

namespace NunMP.Net
{
    // Steam init (AppID 480 "Spacewar") + friends-only lobby used only as a rendezvous / member list.
    public static class SteamLobby
    {
        public static bool Ready;
        public static string Error = "";
        public static CSteamID Lobby = CSteamID.Nil;
        public static readonly HashSet<ulong> Members = new HashSet<ulong>();
        public static Action<ulong> OnEntered; // arg = lobby owner (host)

        static CallResult<LobbyCreated_t> created;
        static CallResult<LobbyEnter_t> entered;
        static Callback<LobbyChatUpdate_t> chat;
        static Callback<GameLobbyJoinRequested_t> joinReq;

        [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string path);

        public static void Init(string pluginDir)
        {
            if (Ready) return;
            try
            {
                Environment.SetEnvironmentVariable("SteamAppId", "480");
                Environment.SetEnvironmentVariable("SteamGameId", "480");
                LoadLibrary(Path.Combine(pluginDir, "steam_api64.dll")); // so DllImport("steam_api64") resolves from our folder
                if (!SteamAPI.IsSteamRunning()) { Error = "Steam is not running"; return; }
                var r = SteamAPI.InitEx(out var msg);
                if (r != ESteamAPIInitResult.k_ESteamAPIInitResult_OK) { Error = $"SteamAPI init: {r} {msg}"; return; }
                SteamNetworkingUtils.InitRelayNetworkAccess();
                created = CallResult<LobbyCreated_t>.Create(OnCreated);
                entered = CallResult<LobbyEnter_t>.Create(OnEnter);
                chat = Callback<LobbyChatUpdate_t>.Create(_ => RefreshMembers());
                joinReq = Callback<GameLobbyJoinRequested_t>.Create(j => Join(j.m_steamIDLobby.m_SteamID));
                Ready = true;
                Plugin.Log.LogInfo("Steam ready as " + SteamFriends.GetPersonaName());
            }
            catch (Exception e) { Error = "Steam init failed: " + e.Message; Plugin.Log.LogError(e); }
        }

        public static int Ticks; public static SteamAPICall_t LastCall;
        public static void Tick() { if (Ready) { SteamAPI.RunCallbacks(); Ticks++; } }
        public static string Debug => $"ticks={Ticks} call={LastCall.m_SteamAPICall} done={(LastCall.m_SteamAPICall != 0 && SteamUtils.IsAPICallCompleted(LastCall, out var f) ? "yes failed=" + f + " reason=" + SteamUtils.GetAPICallFailureReason(LastCall) : "no")}";

        public static void Create(string mode)
        {
            pendingMode = mode;
            var call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, Session.MaxPlayers);
            Plugin.Log.LogInfo($"CreateLobby call={call.m_SteamAPICall} loggedOn={SteamUser.BLoggedOn()}");
            LastCall = call;
            created.Set(call);
        }
        static string pendingMode;

        static void OnCreated(LobbyCreated_t r, bool fail)
        {
            if (fail || r.m_eResult != EResult.k_EResultOK) { Error = "lobby create failed " + r.m_eResult; return; }
            var id = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(id, "nunmp", Session.Version);
            SteamMatchmaking.SetLobbyData(id, "mode", pendingMode);
            // The creator gets LobbyEnter_t only as a broadcast callback, not on our CallResult: enter here.
            Plugin.Log.LogInfo("lobby created " + id.m_SteamID);
            Entered(id);
        }

        public static void Join(ulong lobby) => entered.Set(SteamMatchmaking.JoinLobby(new CSteamID(lobby)));

        static void OnEnter(LobbyEnter_t r, bool fail)
        {
            if (fail || r.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            { Error = "lobby join failed " + r.m_EChatRoomEnterResponse; return; }
            Entered(new CSteamID(r.m_ulSteamIDLobby));
        }

        static void Entered(CSteamID id)
        {
            Lobby = id;
            RefreshMembers();
            SteamFriends.SetRichPresence("status", "Nun Massacre co-op");
            OnEntered?.Invoke(SteamMatchmaking.GetLobbyOwner(Lobby).m_SteamID);
        }

        static void RefreshMembers()
        {
            Members.Clear();
            if (Lobby == CSteamID.Nil) return;
            int n = SteamMatchmaking.GetNumLobbyMembers(Lobby);
            for (int i = 0; i < n; i++) Members.Add(SteamMatchmaking.GetLobbyMemberByIndex(Lobby, i).m_SteamID);
        }

        public static bool IsMember(ulong id)
        {
            if (Lobby == CSteamID.Nil) return false;
            int n = SteamMatchmaking.GetNumLobbyMembers(Lobby);
            for (int i = 0; i < n; i++) if (SteamMatchmaking.GetLobbyMemberByIndex(Lobby, i).m_SteamID == id) return true;
            return false;
        }

        public static void Leave()
        {
            if (Lobby != CSteamID.Nil) SteamMatchmaking.LeaveLobby(Lobby);
            Lobby = CSteamID.Nil;
            Members.Clear();
            if (Ready) SteamFriends.ClearRichPresence();
        }

        // Friends currently in a NunMP lobby (they show as playing Spacewar).
        public static List<(string name, ulong lobby)> FriendLobbies()
        {
            var list = new List<(string, ulong)>();
            if (!Ready) return list;
            int n = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < n; i++)
            {
                var f = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                if (SteamFriends.GetFriendGamePlayed(f, out var g) && g.m_gameID.AppID().m_AppId == 480 && g.m_steamIDLobby.IsValid())
                    list.Add((SteamFriends.GetFriendPersonaName(f), g.m_steamIDLobby.m_SteamID));
            }
            return list;
        }

        public static string Name(ulong id) => Ready ? SteamFriends.GetFriendPersonaName(new CSteamID(id)) : id.ToString();
    }
}
