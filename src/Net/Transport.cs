using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Steamworks;

namespace NunMP.Net
{
    // Raw packet pipe. Peer ids: SteamID64 for Steam, local UDP port for loopback.
    public abstract class Transport
    {
        public ulong SelfId, HostId;
        public bool IsHost => SelfId == HostId;
        public abstract string Describe { get; }
        public abstract void Send(ulong to, byte[] buf, int len, bool reliable);
        public abstract void Poll(Action<ulong, byte[], int> onMsg);
        public virtual void Shutdown() { }
    }

    // Dev-only: N game windows on one PC. ponytail: raw UDP, no reliability; loopback doesn't drop.
    public class UdpTransport : Transport
    {
        public const int HostPort = 27960;
        readonly UdpClient udp;
        readonly byte[] buf = new byte[64 * 1024];

        public UdpTransport(bool host)
        {
            udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, host ? HostPort : 0));
            udp.Client.IOControl(-1744830452, new byte[] { 0 }, null); // SIO_UDP_CONNRESET off: ICMP from a closed peer would throw
            SelfId = (ulong)((IPEndPoint)udp.Client.LocalEndPoint).Port;
            HostId = HostPort;
        }

        public override string Describe => $"UDP loopback :{SelfId}";

        public override void Send(ulong to, byte[] b, int len, bool reliable)
        {
            try { udp.Send(b, len, new IPEndPoint(IPAddress.Loopback, (int)to)); } catch (SocketException) { }
        }

        public override void Poll(Action<ulong, byte[], int> onMsg)
        {
            while (udp.Available > 0)
            {
                var ep = new IPEndPoint(IPAddress.Any, 0);
                byte[] d;
                try { d = udp.Receive(ref ep); } catch (SocketException) { continue; }
                onMsg((ulong)ep.Port, d, d.Length);
            }
        }

        public override void Shutdown() => udp.Close();
    }

    public class SteamTransport : Transport
    {
        readonly IntPtr[] msgs = new IntPtr[64];
        readonly Func<ulong, bool> allowed;
        Callback<SteamNetworkingMessagesSessionRequest_t> onRequest;

        // allowed = live lobby-membership check; sessions/packets from anyone else are ignored.
        public SteamTransport(ulong host, Func<ulong, bool> allowed)
        {
            this.allowed = allowed;
            SelfId = SteamUser.GetSteamID().m_SteamID;
            HostId = host;
            onRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(r =>
            {
                var id = r.m_identityRemote;
                if (allowed(id.GetSteamID64())) SteamNetworkingMessages.AcceptSessionWithUser(ref id);
            });
        }

        public override string Describe => $"Steam {SteamFriends.GetPersonaName()}";

        public override void Send(ulong to, byte[] b, int len, bool reliable)
        {
            var id = new SteamNetworkingIdentity();
            id.SetSteamID64(to);
            var h = GCHandle.Alloc(b, GCHandleType.Pinned);
            try
            {
                SteamNetworkingMessages.SendMessageToUser(ref id, h.AddrOfPinnedObject(), (uint)len,
                    (reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_Unreliable) | 32 /*AutoRestartBrokenSession*/, 0);
            }
            finally { h.Free(); }
        }

        public override void Poll(Action<ulong, byte[], int> onMsg)
        {
            int n;
            while ((n = SteamNetworkingMessages.ReceiveMessagesOnChannel(0, msgs, msgs.Length)) > 0)
                for (int i = 0; i < n; i++)
                {
                    var m = SteamNetworkingMessage_t.FromIntPtr(msgs[i]);
                    var d = new byte[m.m_cbSize];
                    Marshal.Copy(m.m_pData, d, 0, m.m_cbSize);
                    var from = m.m_identityPeer.GetSteamID64();
                    SteamNetworkingMessage_t.Release(msgs[i]);
                    if (allowed(from)) onMsg(from, d, d.Length);
                }
        }

        public override void Shutdown()
        {
            onRequest?.Dispose();
            onRequest = null;
        }
    }
}
