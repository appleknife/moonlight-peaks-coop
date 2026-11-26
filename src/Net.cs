using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace MoonlightPeaksCoop
{
    internal static class Net
    {
        internal const byte Hello = 1;
        internal const byte Welcome = 2;
        internal const byte Bye = 3;
        internal const byte PlayerState = 4;
        internal const byte TimeState = 5;
        internal const byte RoomState = 6;
        internal const byte DirectorNode = 7;
        internal const byte Coins = 8;

        private const ushort Magic = 0xC0DE;

        internal static bool IsHost;
        internal static bool Running;
        internal static IPEndPoint Peer;
        internal static int SentCount;
        internal static int RecvCount;
        internal static int DirectorCount;

        private static UdpClient client;
        private static Thread receiver;
        private static volatile bool stopping;
        private static readonly object sendLock = new object();
        private static readonly ConcurrentQueue<byte[]> inbox = new ConcurrentQueue<byte[]>();

        internal static void StartHost(int port)
        {
            Stop();
            client = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            client.Client.ReceiveBufferSize = 1 << 20;
            IsHost = true;
            Running = true;
            StartReceiver();
            CoopPlugin.Log.LogInfo("hosting on UDP " + port);
        }

        internal static void StartClient(string ip, int port)
        {
            Stop();
            client = new UdpClient();
            Peer = new IPEndPoint(IPAddress.Parse(ip), port);
            Running = true;
            StartReceiver();
            var ms = new MemoryStream();
            using (var w = MakeWriter(ms)) w.Write(Hello);
            SendRaw(ms.ToArray());
            CoopPlugin.Log.LogInfo("joining " + Peer);
        }

        internal static void Stop()
        {
            stopping = true;
            try { if (client != null) client.Close(); } catch { }
            if (receiver != null && receiver.IsAlive) receiver.Join(500);
            client = null;
            receiver = null;
            Peer = null;
            Running = false;
            IsHost = false;
            stopping = false;
            while (inbox.TryDequeue(out _)) { }
        }

        private static void StartReceiver()
        {
            receiver = new Thread(ReceiveLoop) { IsBackground = true };
            receiver.Start();
        }

        private static void ReceiveLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (!stopping && client != null)
            {
                try
                {
                    var data = client.Receive(ref any);
                    if (data == null || data.Length < 4) continue;
                    var remote = new IPEndPoint(any.Address, any.Port);
                    if (!IsHost)
                    {
                        if (Peer != null && (remote.Port != Peer.Port || !remote.Address.Equals(Peer.Address))) continue;
                    }
                    else
                    {
                        Peer = remote;
                    }
                    Interlocked.Increment(ref RecvCount);
                    inbox.Enqueue(data);
                }
                catch (SocketException)
                {
                    if (stopping) break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception e)
                {
                    CoopPlugin.Log.LogWarning("recv: " + e.Message);
                }
            }
        }

        internal static void Process()
        {
            while (inbox.TryDequeue(out var data))
            {
                try
                {
                    using (var r = MakeReader(new MemoryStream(data)))
                    {
                        var type = r.ReadByte();
                        switch (type)
                        {
                            case Hello:
                                if (!IsHost) break;
                                SendSimple(Welcome);
                                CoopPlugin.OnPeerJoined();
                                break;
                            case Welcome:
                                if (IsHost) break;
                                CoopPlugin.OnPeerJoined();
                                break;
                            case Bye:
                                CoopPlugin.OnPeerLeft();
                                break;
                            case PlayerState:
                                CoopPlugin.OnPlayerState(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadString(), r.ReadInt32(), r.ReadString(), r.ReadString());
                                break;
                            case Coins:
                                CoopPlugin.OnCoins(r.ReadInt32());
                                break;
                            case DirectorNode:
                                Interlocked.Increment(ref DirectorCount);
                                CoopPlugin.OnDirectorNode(r.ReadString(), r.ReadString(), r.ReadByte(), r.ReadString(), r.ReadByte());
                                break;
                            case RoomState:
                                CoopPlugin.OnRoomState(r.ReadString(), r.ReadByte());
                                break;
                            case TimeState:
                                CoopPlugin.OnTimeState(r.ReadInt32(), r.ReadSingle());
                                break;
                        }
                    }
                }
                catch (Exception e)
                {
                    CoopPlugin.Log.LogWarning("packet: " + e.Message);
                }
            }
        }

        internal static void SendPlayerState(Vector3 pos, Quaternion rot, float speed, float state, string heldGuid, int heldQuality, string heldMetadata, string heldCustomName)
        {
            if (!Running || Peer == null) return;
            var ms = new MemoryStream();
            using (var w = MakeWriter(ms))
            {
                w.Write(PlayerState);
                w.Write(pos.x); w.Write(pos.y); w.Write(pos.z);
                w.Write(rot.x); w.Write(rot.y); w.Write(rot.z); w.Write(rot.w);
                w.Write(speed);
                w.Write(state);
                w.Write(heldGuid ?? "");
                w.Write(heldQuality);
                w.Write(heldMetadata ?? "");
                w.Write(heldCustomName ?? "");
            }
            SendRaw(ms.ToArray());
        }

        internal static void SendTime(int totalDay, float hour)
        {
            if (!Running || Peer == null) return;
            var ms = new MemoryStream();
            using (var w = MakeWriter(ms))
            {
                w.Write(TimeState);
                w.Write(totalDay);
                w.Write(hour);
            }
            SendRaw(ms.ToArray());
        }

        internal static void SendRoom(string guid, byte cause)
        {
            if (!Running || Peer == null) return;
            var ms = new MemoryStream();
            using (var w = MakeWriter(ms)) { w.Write(RoomState); w.Write(guid); w.Write(cause); }
            SendRaw(ms.ToArray());
        }

        internal static void SendDirectorNode(string nodeGuid, string subjectGuid, byte actor, string loiterGuid, byte loiterActor)
        {
            if (!Running || Peer == null) return;
            var ms = new MemoryStream();
            using (var w = MakeWriter(ms)) { w.Write(DirectorNode); w.Write(nodeGuid ?? ""); w.Write(subjectGuid ?? ""); w.Write(actor); w.Write(loiterGuid ?? ""); w.Write(loiterActor); }
            SendRaw(ms.ToArray());
        }

        internal static void SendCoins(int coins)
        {
            if (!Running || Peer == null) return;
            var ms = new MemoryStream();
            using (var w = MakeWriter(ms)) { w.Write(Coins); w.Write(coins); }
            SendRaw(ms.ToArray());
        }

        internal static void SendSimple(byte type)
        {
            if (!Running || Peer == null) return;
            var ms = new MemoryStream();
            using (var w = MakeWriter(ms)) w.Write(type);
            SendRaw(ms.ToArray());
        }

        private static void SendRaw(byte[] data)
        {
            var c = client;
            var p = Peer;
            if (c == null || p == null) return;
            Interlocked.Increment(ref SentCount);
            lock (sendLock)
            {
                try { c.Send(data, data.Length, p); }
                catch (Exception e) { CoopPlugin.Log.LogWarning("send: " + e.Message); }
            }
        }

        private static BinaryWriter MakeWriter(Stream s)
        {
            var w = new BinaryWriter(s, System.Text.Encoding.UTF8, true);
            w.Write(Magic);
            return w;
        }

        private static BinaryReader MakeReader(Stream s)
        {
            var r = new BinaryReader(s, System.Text.Encoding.UTF8, true);
            var magic = r.ReadUInt16();
            if (magic != Magic) throw new InvalidDataException("bad magic");
            return r;
        }
    }
}









