using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace MoonlightPeaksCoop
{
    internal static class SaveTransfer
    {
        internal const int Offset = 10;

        private static TcpListener listener;
        internal static byte[] PendingWorld;
        private static Thread thread;

        internal static void StartHost()
        {
            StopHost();
            try
            {
                listener = new TcpListener(IPAddress.Any, CoopPlugin.Port + Offset);
                listener.Start();
                thread = new Thread(() =>
                {
                    while (listener != null)
                    {
                        try
                        {
                            using (var c = listener.AcceptTcpClient())
                            {
                                var s = c.GetStream();
                                var role = s.ReadByte();
                                if (role == 'G')
                                {
                                    var file = Probe.LatestSaveFile();
                                    if (file == null) continue;
                                    var bytes = File.ReadAllBytes(file);
                                    var len = BitConverter.GetBytes(bytes.Length);
                                    s.Write(len, 0, 4);
                                    s.Write(bytes, 0, bytes.Length);
                                    s.Flush();
                                    CoopPlugin.Log.LogInfo("sent save (" + bytes.Length + " bytes)");
                                }
                                else if (role == 'P')
                                {
                                    var lenBuf = new byte[4];
                                    ReadExact(s, lenBuf, 4);
                                    var len = BitConverter.ToInt32(lenBuf, 0);
                                    var data = new byte[len];
                                    ReadExact(s, data, len);
                                    PendingWorld = data;
                                    Ctl.Enqueue("applyworld");
                                }
                            }
                        }
                        catch (SocketException) { break; }
                        catch (Exception e) { CoopPlugin.Log.LogWarning("save host: " + e.Message); }
                    }
                }) { IsBackground = true };
                thread.Start();
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning("save host start: " + e.Message); }
        }

        internal static void StopHost()
        {
            try { listener?.Stop(); } catch { }
            listener = null;
        }

        internal static void Fetch(string ip)
        {
            new Thread(() =>
            {
                try
                {
                    using (var c = new TcpClient())
                    {
                        c.Connect(ip, CoopPlugin.Port + Offset);
                        var s = c.GetStream();
                        s.WriteByte((byte)'G');
                        var lenBuf = new byte[4];
                        ReadExact(s, lenBuf, 4);
                        var len = BitConverter.ToInt32(lenBuf, 0);
                        if (len <= 0 || len > 64 * 1024 * 1024) throw new InvalidDataException("bad save length " + len);
                        var data = new byte[len];
                        ReadExact(s, data, len);
                        var guid = SerializedGuid.NewGuid().ToGuid().ToString();
                        var root = Probe.LatestProfileRoot() ?? Probe.CreateProfileRoot();
                        var dir = Path.Combine(root, "Saves", guid);
                        Directory.CreateDirectory(dir);
                        File.WriteAllBytes(Path.Combine(dir, "GameData.json"), data);
                        CoopPlugin.Log.LogInfo("downloaded host save " + guid);
                        Ctl.Enqueue("applysave:" + guid);
                    }
                }
                catch (Exception e) { CoopPlugin.Log.LogWarning("save fetch: " + e.Message); }
            }) { IsBackground = true }.Start();
        }

        internal static void Push(byte[] data)
        {
            if (data == null || Net.Peer == null) return;
            new Thread(() =>
            {
                try
                {
                    using (var c = new TcpClient())
                    {
                        c.Connect(Net.Peer.Address.ToString(), CoopPlugin.Port + Offset);
                        var s = c.GetStream();
                        s.WriteByte((int)'P');
                        var len = BitConverter.GetBytes(data.Length);
                        s.Write(len, 0, 4);
                        s.Write(data, 0, data.Length);
                        s.Flush();
                    }
                }
                catch (Exception e) { CoopPlugin.Log.LogWarning("world push: " + e.Message); }
            }) { IsBackground = true }.Start();
        }

        private static void ReadExact(Stream s, byte[] buf, int count)
        {
            var off = 0;
            while (off < count)
            {
                var n = s.Read(buf, off, count - off);
                if (n <= 0) throw new EndOfStreamException();
                off += n;
            }
        }
    }
}


