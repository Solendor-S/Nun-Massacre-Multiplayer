using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace NunMP.Net
{
    public enum Msg : byte
    {
        Hello = 1, Welcome, Reject, Roster, Ping, Start, Bye, Pref,  // Pref: client -> host lobby choice (nun skin)
        // >= Player: gameplay. Client->host packets of relayed types get forwarded to the other clients.
        Player = 10, Nun, Event, Voice,
    }

    public class Peer
    {
        public ulong Id;
        public byte Slot;
        public string Name;
        public float LastHeard;
    }

    // Star topology: clients talk to the host, which relays player/event packets; voice goes peer to peer. Slot 0 = host.
    public static class Session
    {
        public const string Version = "0.6.4";
        public const int MaxPlayers = 8;
        const float Timeout = 8f;

        public static Transport T;
        public static byte MySlot;
        public static bool Connected;           // client: welcomed. host: always once hosting
        public static string Status = "offline";
        public static string Mode = "coop";
        public static readonly Dictionary<byte, Peer> Peers = new Dictionary<byte, Peer>(); // everyone incl. self
        public static bool Active => T != null;
        public static bool IsHost => T != null && T.IsHost;

        static readonly Dictionary<Msg, List<Action<byte, BinaryReader>>> handlers = new Dictionary<Msg, List<Action<byte, BinaryReader>>>();
        static float nextPing;
        static string myName = "player";

        public static string GameHash { get; } = Sha1(Path.Combine(Application.dataPath, "Managed", "Assembly-CSharp.dll"));
        static string Sha1(string f) { using (var s = SHA1.Create()) return BitConverter.ToString(s.ComputeHash(File.ReadAllBytes(f))).Replace("-", "").Substring(0, 12); }

        public static void On(Msg m, Action<byte, BinaryReader> h)
        {
            if (!handlers.TryGetValue(m, out var l)) handlers[m] = l = new List<Action<byte, BinaryReader>>();
            l.Add(h);
        }

        public static void StartHost(Transport t, string name)
        {
            Stop();
            T = t; myName = name; MySlot = 0; Connected = true;
            Peers[0] = new Peer { Id = t.SelfId, Slot = 0, Name = name, LastHeard = Time.unscaledTime };
            Status = "hosting via " + t.Describe;
            Plugin.Log.LogInfo(Status);
        }

        public static void StartClient(Transport t, string name)
        {
            Stop();
            T = t; myName = name; Connected = false;
            Status = "connecting via " + t.Describe;
            Raw(T.HostId, Msg.Hello, w => { w.Write(Version); w.Write(GameHash); w.Write(name); }, true);
            nextPing = Time.unscaledTime + 1f;
            Plugin.Log.LogInfo(Status);
        }

        public static void Stop()
        {
            if (T != null)
            {
                Broadcast(Msg.Bye, _ => { }, true);
                T.Shutdown();
            }
            T = null; Connected = false; Peers.Clear(); Status = "offline";
        }

        // Host: to every client. Client: to host (host relays gameplay types).
        public static void Broadcast(Msg m, Action<BinaryWriter> body, bool reliable)
        {
            if (T == null) return;
            if (IsHost) { foreach (var p in Peers.Values) if (p.Slot != 0) Raw(p.Id, m, body, reliable, 0); }
            else if (Connected || m == Msg.Bye) Raw(T.HostId, m, body, reliable, MySlot);
        }

        // Straight to one peer, no host hop (voice: clients send to each other directly, Steam relays it).
        public static void SendTo(Peer p, Msg m, Action<BinaryWriter> body, bool reliable)
        {
            if (T != null && Connected && p.Slot != MySlot) Raw(p.Id, m, body, reliable, MySlot);
        }

        static void Raw(ulong to, Msg m, Action<BinaryWriter> body, bool reliable, byte slot = 0)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write((byte)m); w.Write(slot);
            body(w);
            T.Send(to, ms.GetBuffer(), (int)ms.Length, reliable);
        }

        public static void Tick()
        {
            if (T == null) return;
            T.Poll(Receive);
            var now = Time.unscaledTime;
            if (now >= nextPing) { nextPing = now + 1f; Broadcast(Msg.Ping, _ => { }, false); }
            if (IsHost)
            {
                foreach (var p in Peers.Values.Where(p => p.Slot != 0 && now - p.LastHeard > Timeout).ToList()) Drop(p, "timed out");
            }
            else if (Connected && Peers.TryGetValue(0, out var h) && now - h.LastHeard > Timeout) { Plugin.Log.LogWarning("host timed out"); Stop(); Status = "host timed out"; }
        }

        static void Receive(ulong from, byte[] d, int len)
        {
            if (len < 2) return;
            var m = (Msg)d[0];
            var r = new BinaryReader(new MemoryStream(d, 2, len - 2));
            if (IsHost)
            {
                var p = Peers.Values.FirstOrDefault(x => x.Id == from);
                if (m == Msg.Hello) { HostHello(from, r); return; }
                if (p == null) return;
                p.LastHeard = Time.unscaledTime;
                if (m == Msg.Bye) { Drop(p, "left"); return; }
                if (m == Msg.Ping) return;
                if (m == Msg.Player || m == Msg.Event || m == Msg.Nun)
                {   // relay with the authoritative sender slot (voice isn't relayed: peers send it to each other)
                    d[1] = p.Slot;
                    foreach (var o in Peers.Values) if (o.Slot != 0 && o.Slot != p.Slot) T.Send(o.Id, d, len, m == Msg.Event);
                }
                Dispatch(m, p.Slot, r);
                return;
            }
            if (from != T.HostId)
            {   // peer-to-peer voice: the sender's slot comes from the roster, not from the packet
                var src = m == Msg.Voice ? Peers.Values.FirstOrDefault(x => x.Id == from) : null;
                if (src != null) Dispatch(m, src.Slot, r);
                return;
            }
            if (Peers.TryGetValue(0, out var host)) host.LastHeard = Time.unscaledTime;
            switch (m)
            {
                case Msg.Welcome:
                    MySlot = r.ReadByte(); Connected = true; Status = $"connected as slot {MySlot}";
                    Plugin.Log.LogInfo(Status);
                    return;
                case Msg.Reject: Status = "rejected: " + r.ReadString(); Plugin.Log.LogWarning(Status); T.Shutdown(); T = null; return;
                case Msg.Roster:
                    Peers.Clear();
                    int n = r.ReadByte();
                    for (int i = 0; i < n; i++) { var p = new Peer { Slot = r.ReadByte(), Id = r.ReadUInt64(), Name = r.ReadString(), LastHeard = Time.unscaledTime }; Peers[p.Slot] = p; }
                    Mode = r.ReadString();
                    Sync.Rules.Read(r);
                    return;
                case Msg.Bye: Stop(); Status = "host closed the session"; return;
            }
            Dispatch(m, d[1], r);
        }

        // Several subsystems may listen to one message type; each gets its own reader from the payload start.
        static void Dispatch(Msg m, byte slot, BinaryReader r)
        {
            if (!handlers.TryGetValue(m, out var hs)) return;
            var ms = (MemoryStream)r.BaseStream;
            var start = ms.Position;
            foreach (var h in hs)
            {
                ms.Position = start;
                try { h(slot, r); } catch (Exception e) { Plugin.Log.LogError($"handler {m}: {e}"); }
            }
        }

        static void HostHello(ulong from, BinaryReader r)
        {
            string ver = r.ReadString(), hash = r.ReadString(), name = r.ReadString();
            string why = ver != Version ? $"mod version {ver} != host {Version}"
                : hash != GameHash ? "different game build (Assembly-CSharp mismatch)"
                : Peers.Count >= MaxPlayers ? "lobby full"
                : GameStarted ? "game already started" : null;
            var existing = Peers.Values.FirstOrDefault(x => x.Id == from);
            if (why != null && existing == null) { Raw(from, Msg.Reject, w => w.Write(why), true); return; }
            var p = existing ?? new Peer { Id = from, Slot = (byte)Enumerable.Range(1, MaxPlayers).First(s => !Peers.ContainsKey((byte)s)), Name = name };
            p.LastHeard = Time.unscaledTime;
            Peers[p.Slot] = p;
            Raw(from, Msg.Welcome, w => w.Write(p.Slot), true);
            SendRoster();
            Plugin.Log.LogInfo($"{name} joined as slot {p.Slot}");
        }

        static void Drop(Peer p, string why)
        {
            Peers.Remove(p.Slot);
            Plugin.Log.LogInfo($"{p.Name} {why}");
            PeerLeft?.Invoke(p.Slot);
            SendRoster();
        }

        public static Action<byte> PeerLeft;
        public static bool GameStarted;

        public static void SendPref(byte value) { if (T != null && !IsHost && Connected) Raw(T.HostId, Msg.Pref, w => w.Write(value), true, MySlot); }

        public static void SendRoster() => Broadcast(Msg.Roster, w =>
        {
            w.Write((byte)Peers.Count);
            foreach (var p in Peers.Values) { w.Write(p.Slot); w.Write(p.Id); w.Write(p.Name); }
            w.Write(Mode);
            Sync.Rules.Write(w);
        }, true);
    }
}
