using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace MoonlightPeaksCoop
{
    // Tiny localhost control channel so tests do not depend on window focus or SendKeys.
    internal static class Ctl
    {
        private static readonly ConcurrentQueue<string> commands = new ConcurrentQueue<string>();
        private static TcpListener listener;
        private static Thread thread;
        private static volatile bool stopping;

        internal static int Port = 47111;
        internal static volatile string Status = "?";
        internal static volatile string LastMessage = "";

        internal static void Start()
        {
            if (listener != null) return;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, Port);
                listener.Start();
                thread = new Thread(Loop) { IsBackground = true };
                thread.Start();
                CoopPlugin.Log.LogInfo("control on 127.0.0.1:" + Port);
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogWarning("control: " + e.Message);
            }
        }

        internal static void Process()
        {
            while (commands.TryDequeue(out var cmd))
            {
                try
                {
                    switch (cmd)
                    {
                        case "host": SaveTransfer.StartHost(); Net.StartHost(CoopPlugin.Port); break;
                        case "join": SaveTransfer.Fetch(CoopPlugin.JoinAddress); Net.StartClient(CoopPlugin.JoinAddress, CoopPlugin.Port); break;
                        case "stop": Net.Stop(); SaveTransfer.StopHost(); break;
                        case "press":
                        {
                            var scr = Chicken.UI.UIScreen<IntroScreen>.Instance;
                            if (!scr) scr = UnityEngine.Object.FindAnyObjectByType<IntroScreen>();
                            if (scr) scr.Hide(false);
                            break;
                        }
                        case "cont": Probe.ClickButton("Continue"); break;
                        case "load": Probe.LoadLatestSave(); break;
                        case "spawn": CoopPlugin.SpawnRemoteDummy(); break;
                        case "despawn": CoopPlugin.RemoveRemoteDummy(); break;
                        case "menu": CoopPlugin.ShowMenu = true; break;
                        case "day": Net.SendTime(0, 0); break;
                        case "move":
                        {
                            var pv = PlayerView.Instance;
                            if (pv && pv.Character && pv.Mover) pv.Mover.Teleport(pv.Character.transform.position + new Vector3(0.6f, 0, 0), true, false);
                            break;
                        }
                        case "warp":
                        {
                            var app = AppGameStateMachine.Instance;
                            var room = AddressableLibrary<AssetLibrary>.Instance.PlayerPlot;
                            LastMessage = "warp app=" + (app && app.CurrentState ? app.CurrentState.GetType().Name : "null") + " room=" + (room ? room.AssetName : "null") + " cur=" + (GamePersistence.CurrentRoomAsset ? GamePersistence.CurrentRoomAsset.AssetName : "none");
                            CoopPlugin.SwitchTo(room);
                            LastMessage += " sent";
                            break;
                        }
                        case "home":
                        {
                            var rooms = AddressableLibrary<AssetLibrary>.Instance.PlayerHouseRooms;
                            LastMessage = "home rooms=" + rooms.Length + " first=" + (rooms.Length > 0 && rooms[0] ? rooms[0].AssetName : "null");
                            if (rooms.Length > 0) CoopPlugin.SwitchTo(rooms[0]);
                            break;
                        }
                        case "startday":
                        {
                            var node = System.Linq.Enumerable.FirstOrDefault(Director.Core.DirectorNode.Nodes, n => n is Director.Nodes.StartNewDayNode);
                            CoopPlugin.Log.LogInfo("startday node=" + (node != null));
                            if (node != null)
                            {
                                var subject = AddressableLibrary<AssetLibrary>.Instance.PlayerEntity;
                                var ctx = new Director.Core.ExecutionContext(node, subject, null, SerializedGuid.Empty);
                                EventBus.OnStartDirectorStateRequest.Dispatch(ctx);
                            }
                            break;
                        }
                        case "hold":
                            {
                                var all = Asset.GetAll<ItemAsset>();
                                ItemAsset pick = null;
                                foreach (var a in all)
                                {
                                    if (a == null || a.ToolAddon == null || a.ItemViewReference == null) continue;
                                    if (a.AssetName.IndexOf("Form", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                                    pick = a; break;
                                }
                                if (pick == null && all.Count > 0) pick = all[0];
                                var inv = Chicken.Utilities.MonoBehaviourSingleton<GameInventory>.Instance;
                                LastMessage = "inventory=" + (inv != null) + " pick=" + (pick ? pick.AssetName : "null") + " count=" + all.Count;
                                if (pick != null && GamePersistence.Instance != null && inv)
                                {
                                    var ok = inv.TryGrabItem(new ItemEntry(pick), true, false);
                                    LastMessage += " grabbed=" + ok + " persisted=" + (GamePersistence.Instance.Player.GrabbedItemEntry.ItemAsset ? GamePersistence.Instance.Player.GrabbedItemEntry.ItemAsset.AssetName : "no");
                                }
                            }
                            break;
                        case "unhold":
                            {
                                var inv = Chicken.Utilities.MonoBehaviourSingleton<GameInventory>.Instance;
                                if (inv) inv.TryGrabNone(false, true, true);
                            }
                            break;
                        case "rhold":
                            { var all = Asset.GetAll<ItemAsset>(); if (all.Count > 0 && CoopPlugin.RemotePlayer) CoopPlugin.ApplyRemoteHeld(CoopPlugin.RemotePlayer, all[0].SerializedGuid.ToGuid().ToString()); }
                            break;
                        case "coins":
                            { var m = CoopPlugin.CurrentMeta(); if (m != null) m.Coins += 10; }
                            break;
                        case "nextday":
                            if (GamePersistence.Instance != null) { GamePersistence.Instance.IncrementDay(); GamePersistence.Instance.Time.DayProgression = 0f; }
                            break;
                        case "applyworld":
                            CoopPlugin.ApplyGuestWorld(SaveTransfer.PendingWorld);
                            SaveTransfer.PendingWorld = null;
                            break;
                        case "quit": Application.Quit(); break;
                        default:
                            if (cmd.StartsWith("resolve/"))
                            {
                                var a = byte.Parse(cmd.Substring(8));
                                var s = DirectorSync.Resolve(a, "");
                                LastMessage = "resolve " + a + "=" + (s != null ? s.AssetName : "null");
                            }
                            else if (cmd == "refresolve")
                            {
                                var r = new Director.Core.DirectorVariableRef();
                                r.Scope = Director.Core.DirectorVariableRef.ScopeType.HardCoded;
                                r.HardCodedGuidValue = AddressableLibrary<AssetLibrary>.Instance.PlayerEntity.SerializedGuid;
                                DirectorSync.FromGuest = false;
                                var before = r.GetAssetValue() as IEntityAsset;
                                DirectorSync.FromGuest = true;
                                var after = r.GetAssetValue() as IEntityAsset;
                                DirectorSync.FromGuest = false;
                                LastMessage = "ref before=" + (before != null ? before.AssetName : "null") + " guest=" + (after != null ? after.AssetName : "null");
                            }
                            else if (cmd.StartsWith("applysave:"))
                            {
                                var guid = SerializedGuid.FromString(cmd.Substring(10));
                                var menu = UnityEngine.Object.FindAnyObjectByType<AppMenuStateMachine>();
                                if (menu) menu.LoadGame(guid, null);
                            }
                            break;
                    }
                }
                catch (Exception e) { LastMessage = "error " + cmd + ": " + e.Message; CoopPlugin.Log.LogWarning("ctl " + cmd + ": " + e.Message); }
            }
        }

        internal static void Enqueue(string cmd) => commands.Enqueue(cmd);

        private static void Loop()
        {
            while (!stopping && listener != null)
            {
                try
                {
                    using (var client = listener.AcceptTcpClient())
                    using (var stream = client.GetStream())
                    {
                        var buf = new byte[512];
                        var n = stream.Read(buf, 0, buf.Length);
                        var req = Encoding.ASCII.GetString(buf, 0, n);
                        var path = "/";
                        var first = req.IndexOf(' ');
                        if (first > 0)
                        {
                            var second = req.IndexOf(' ', first + 1);
                            if (second > first) path = req.Substring(first + 1, second - first - 1);
                        }
                        var cmd = path.Trim('/').ToLowerInvariant();
                        if (cmd.StartsWith("join/")) { CoopPlugin.JoinAddress = cmd.Substring(5); cmd = "join"; }
                        if (cmd.Length > 0) commands.Enqueue(cmd);
                        var text = cmd == "status" ? Status : "ok " + cmd;
                        var body = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: " + Encoding.ASCII.GetByteCount(text) + "\r\nConnection: close\r\n\r\n" + text);
                        stream.Write(body, 0, body.Length);
                    }
                }
                catch (SocketException) { if (stopping) break; }
                catch (Exception) { }
            }
        }
    }
}


















