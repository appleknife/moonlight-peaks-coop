using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using Chicken.Utilities;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace MoonlightPeaksCoop
{
    internal static class Probe
    {
        private static int frames;
        internal static bool GuestInitialized;
        private static readonly Dictionary<string, float> lastPos = new Dictionary<string, float>();

        internal static void Tick()
        {
            frames++;
            var menuSm = UnityEngine.Object.FindAnyObjectByType<AppMenuStateMachine>();
            var lpos = CoopPlugin.LocalPlayer && CoopPlugin.LocalPlayer.Character ? CoopPlugin.LocalPlayer.Character.transform.position : Vector3.zero;
            var rpos = CoopPlugin.RemotePlayer && CoopPlugin.RemotePlayer.Character ? CoopPlugin.RemotePlayer.Character.transform.position : Vector3.zero;
            Ctl.Status = "lp=" + lpos.ToString("F2") + " rp=" + rpos.ToString("F2") + " dist=" + Vector3.Distance(lpos, rpos).ToString("F2") + " rpg=" + (CoopPlugin.RemotePlayer && CoopPlugin.RemotePlayer.Character && CoopPlugin.RemotePlayer.Character.Entity != null ? CoopPlugin.RemotePlayer.Character.Entity.SerializedGuid.ToGuid().ToString().Substring(0,8) : "no") + " gas=" + (CoopPlugin.GuestPlayerAsset != null) + " ph=" + (GamePersistence.Instance != null && GamePersistence.Instance.Player != null && GamePersistence.Instance.Player.GrabbedItemEntry.ItemAsset != null ? GamePersistence.Instance.Player.GrabbedItemEntry.ItemAsset.AssetName : "no") + " rheld=" + (CoopPlugin.RemotePlayer && CoopPlugin.RemotePlayer.Equipment && CoopPlugin.RemotePlayer.Equipment.GrabbedItemView ? CoopPlugin.RemotePlayer.Equipment.GrabbedItemView.name : "no") + " lheld=" + (CoopPlugin.LocalPlayer && CoopPlugin.LocalPlayer.Equipment && CoopPlugin.LocalPlayer.Equipment.GrabbedItemView ? CoopPlugin.LocalPlayer.Equipment.GrabbedItemView.name : "no") + " leq=" + (CoopPlugin.LocalPlayer && CoopPlugin.LocalPlayer.Equipment ? "yes" : "no") + " req=" + (CoopPlugin.RemotePlayer && CoopPlugin.RemotePlayer.Equipment ? "yes" : "no") + " pr=" + (PersistRoom ? PersistRoom.AssetName : "none") + " pend=" + (CoopPlugin.PendingRoomGuid ?? "") + " pc=" + CoopPlugin.PendingRoomCause + " lna=" + (Time.unscaledTime - DirectorSync.LastNodeAt).ToString("F1") + " coins=" + (CoopPlugin.CurrentMeta() != null ? CoopPlugin.CurrentMeta().Coins.ToString() : "?") + " metas=" + CoopPlugin.MetaCount() + " g=" + (GamePersistence.Instance != null ? GamePersistence.Instance.Guid.ToString() : "?") + " gi=" + GuestInitialized + " rels=" + (GamePersistence.Instance != null && GamePersistence.Instance.NpcRelationships != null ? GamePersistence.Instance.NpcRelationships.Count.ToString() : "?") + " day=" + (GamePersistence.Instance != null && GamePersistence.Instance.Time != null ? GamePersistence.Instance.Time.TotalDay + "@" + GamePersistence.Instance.Time.DayProgression.ToString("F2") : "n/a") + " room=" + (Room.Exists && Room.Instance && Room.Instance.RoomAsset ? Room.Instance.RoomAsset.AssetName : "none") + " menu=" + (menuSm && menuSm.CurrentState ? menuSm.CurrentState.GetType().Name : "null") + " app=" + (AppGameStateMachine.Instance && AppGameStateMachine.Instance.CurrentState ? AppGameStateMachine.Instance.CurrentState.GetType().Name : "null") + " player=" + (PlayerView.Instance ? "yes" : "no") + " remote=" + (CoopPlugin.RemotePlayer ? "yes" : "no") + " lpname=" + (CoopPlugin.LocalPlayer ? CoopPlugin.LocalPlayer.name : "null") + " instname=" + (PlayerView.Instance ? PlayerView.Instance.name : "null") + " ids=" + UnityEngine.Object.FindObjectsByType<CoopIdentity>(FindObjectsSortMode.None).Length + " ract=" + (CoopPlugin.RemotePlayer ? CoopPlugin.RemotePlayer.gameObject.activeInHierarchy.ToString() : "n/a") + " rpar=" + (CoopPlugin.RemotePlayer && CoopPlugin.RemotePlayer.transform.parent ? CoopPlugin.RemotePlayer.transform.parent.name + ":" + CoopPlugin.RemotePlayer.transform.parent.gameObject.activeInHierarchy : "none") + " lpar=" + (CoopPlugin.LocalPlayer && CoopPlugin.LocalPlayer.transform.parent ? CoopPlugin.LocalPlayer.transform.parent.name : "none") + " lact=" + (CoopPlugin.LocalPlayer ? CoopPlugin.LocalPlayer.gameObject.activeInHierarchy.ToString() : "n/a") + " dn=" + Net.DirectorCount + " dact=" + DirectorSync.LastActor + " dsub=" + DirectorSync.LastSubjectName + " dloit=" + DirectorSync.LastLoiterName + " ps=" + CoopPlugin.PlayerStateCount + " msg=" + Ctl.LastMessage + " pup=" + (CoopPlugin.RemotePlayer && CoopPlugin.RemotePlayer.GetComponent<CoopPuppet>() ? "yes" : "no") + " sent=" + Net.SentCount + " recv=" + Net.RecvCount + " peer=" + (Net.Peer != null ? Net.Peer.ToString() : "null") + " net=" + (Net.Running ? (Net.IsHost ? "host" : "guest") : "off");
            var inst = PlayerView.Instance;
            var local = FindLocal();
            if (inst && inst != CoopPlugin.RemotePlayer && inst.Character && inst != local)
            {
                var id = inst.GetComponent<CoopIdentity>();
                if (!id) id = inst.gameObject.AddComponent<CoopIdentity>();
                if (!id.IsLocal)
                {
                    ClearLocalMarkers(inst);
                    id.IsLocal = true;
                    local = inst;
                }
            }
            if (local)
            {
                CoopPlugin.LocalPlayer = local;
                if (PlayerView.Instance != local) CoopPlugin.SetLocalSingleton(local);
            }
            if (frames % 60 == 0) EnsureLocalPlayer();
            if (frames % 15 == 0 && Net.Running) MaybeApplyPendingRoom();
            if (frames % 180 == 0 && Net.Running && (Net.IsHost || GuestInitialized) && GamePersistence.Instance != null && PlayerView.Instance && PlayerView.Instance.Character) PushWorldIfChanged();
            if (!GuestInitialized && Net.Running && !Net.IsHost && GamePersistence.Instance != null && PlayerView.Instance && PlayerView.Instance.Character) GuestInit();
            if (frames % 120 != 0) return;
            if (!CoopPlugin.DebugProbe) return;
            try
            {
                var sb = new StringBuilder();
                var app = AppGameStateMachine.Instance;
                sb.Append("appState=").Append(app && app.CurrentState ? app.CurrentState.GetType().Name : "null");
                sb.Append(" player=").Append(PlayerView.Exists ? "yes" : "no");
                sb.Append(" room=").Append(Room.Exists && Room.Instance && Room.Instance.RoomAsset ? Room.Instance.RoomAsset.name : "none");
                CoopPlugin.Log.LogInfo(sb.ToString());

                var buttons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
                foreach (var b in buttons)
                {
                    if (!b.isActiveAndEnabled || !b.gameObject.activeInHierarchy) continue;
                    var label = b.GetComponentInChildren<TMP_Text>();
                    CoopPlugin.Log.LogInfo("BTN '" + (label ? label.text : b.name) + "' path=" + Path(b.transform));
                }

                if (CoopPlugin.RemotePlayer)
                {
                    var rp = CoopPlugin.RemotePlayer;
                    CoopPlugin.Log.LogInfo("REMOTE exists char=" + (rp.Character ? "yes" : "no") + " mover=" + (rp.Mover ? "yes" : "no") + " input=" + (rp.Input ? "yes" : "no") + " pos=" + (rp.Character ? rp.Character.transform.position.ToString("F2") : "n/a"));
                }

                var views = UnityEngine.Object.FindObjectsByType<PlayerView>(FindObjectsSortMode.None);
                CoopPlugin.Log.LogInfo("SINGLETON local=" + (PlayerView.Instance == CoopPlugin.LocalPlayer) + " instanceChar=" + (PlayerView.Instance && PlayerView.Instance.Character ? "yes" : "no") + " local=" + (CoopPlugin.LocalPlayer ? CoopPlugin.LocalPlayer.name : "null") + " localChildren=" + (CoopPlugin.LocalPlayer ? ChildNames(CoopPlugin.LocalPlayer.transform) : "n/a") + " views=" + views.Length + " names=" + string.Join(",", views.Select(v => v.name)));
                if (PlayerView.Exists && PlayerView.Instance)
                {
                    var t = PlayerView.Instance.Character ? PlayerView.Instance.Character.transform : PlayerView.Instance.transform;
                    CoopPlugin.Log.LogInfo("LOCALPOS " + t.position.x.ToString("F2") + "," + t.position.y.ToString("F2") + "," + t.position.z.ToString("F2"));
                }
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogWarning("probe: " + e.Message);
            }
        }

        private static void GuestInit()
        {
            GuestInitialized = true;
            var g = GamePersistence.Instance;
            g.Player = new PlayerPersistence();
            typeof(GamePersistence).GetField("NpcRelationships").SetValue(g, new Persistence.Core.GuidPersistenceList<NpcRelationshipPersistence>());
            CoopPlugin.Log.LogInfo("guest player data reset");
        }

        private static RoomAsset PersistRoom { get { if (GamePersistence.Instance == null) return null; try { return GamePersistence.CurrentRoomAsset; } catch { return null; } } }

        private static float nextRoomAttempt;
        private static float mismatchSince;
        private static float switchSince;
        internal static void ResetRoomDebounce() => nextRoomAttempt = 0f;

        private static bool SafeForRoomSwitch(int cause, out string why)
        {
            why = "";
            var app = AppGameStateMachine.Instance;
            if (app && app.CurrentState is GameSwitchRoomState) { why = "switch"; return false; }
            var nodeAge = Time.unscaledTime - DirectorSync.LastNodeAt;
            if (cause == 1)
            {
                if (nodeAge < 0.4f) { why = "node"; return false; }
                return true;
            }
            if (Cutscene.IsInCutscene) { why = "cutscene"; return false; }
            if (app && app.CurrentState is GameDirectorStateMachine) { why = "director"; return false; }
            if (nodeAge < 1f) { why = "node"; return false; }
            return true;
        }

        private static void MaybeApplyPendingRoom()
        {
            var pending = CoopPlugin.PendingRoomGuid;
            if (!string.IsNullOrEmpty(pending))
            {
                var guid = SerializedGuid.FromString(pending);
                var target = Room.Exists && Room.Instance ? Room.Instance.RoomAsset : null;
                if (target && target.SerializedGuid.Equals(guid)) { CoopPlugin.ClearPending(); return; }
                var age = Time.unscaledTime - CoopPlugin.PendingRoomSince;
                if (age > 30f) { CoopPlugin.ClearPending(); return; }
                var cause = CoopPlugin.PendingRoomCause;
                if (cause == 2 && age < 4f) return;
                if (age < 0.4f) return;
                var app = AppGameStateMachine.Instance;
                if (app && app.CurrentState is GameSwitchRoomState)
                {
                    if (switchSince == 0f) switchSince = Time.unscaledTime;
                }
                else switchSince = 0f;
                var stuck = switchSince != 0f && Time.unscaledTime - switchSince > 3f;
                var safe = SafeForRoomSwitch(cause, out var why);
                if (!safe && !stuck) return;
                if (Time.unscaledTime < nextRoomAttempt) return;
                nextRoomAttempt = Time.unscaledTime + 4f;
                CoopPlugin.Log.LogWarning("room apply " + pending + " cause=" + cause + " why=" + (stuck ? "stuck" : why));
                CoopPlugin.ForceRoom(pending);
                return;
            }
            if (!Net.Running || GamePersistence.Instance == null) return;
            var current = PersistRoom;
            var instance = Room.Exists && Room.Instance ? Room.Instance.RoomAsset : null;
            if (current == null || instance == null || current.SerializedGuid.Equals(instance.SerializedGuid)) { mismatchSince = 0f; return; }
            if (mismatchSince == 0f) mismatchSince = Time.unscaledTime;
            if (Time.unscaledTime - mismatchSince < 3f) return;
            if (!SafeForRoomSwitch(1, out _)) return;
            mismatchSince = 0f;
            CoopPlugin.Log.LogWarning("room mismatch repair " + current.AssetName);
            CoopPlugin.SwitchTo(current);
        }

        private static void EnsureLocalPlayer()
        {
            var existing = FindLocal();
            if (existing && existing.Character) return;
            if (AppGameStateMachine.Instance && AppGameStateMachine.Instance.CurrentState is GameSwitchRoomState) return;
            if (!Room.Exists || !Room.Instance) return;
            var pos = Room.Instance.PlayerSpawnFallback ? Room.Instance.PlayerSpawnFallback.position : Vector3.zero;
            Vector3 restore; if (CoopPlugin.TryRestoreLocal(out restore)) pos = restore;
            if (pos == Vector3.zero && CoopPlugin.RemotePlayer && CoopPlugin.RemotePlayer.Character)
                pos = CoopPlugin.RemotePlayer.Character.transform.position + new Vector3(1.2f, 0, 0);
            CoopPlugin.Log.LogInfo("ensure local spawn at " + pos);
            CoopPlugin.SpawnLocalPlayer(pos);
        }

        private static int lastWorldHash;
        private static void PushWorldIfChanged()
        {
            try
            {
                var room = GamePersistence.Instance.CurrentRoom;
                if (room == null) return;
                var sig = room.GridObjects.Count + "|" + room.Growables.Count + "|" + room.HarvestStatusGuidPersistences.Count + "|" +
                    room.Pickups.Count + "|" + room.FarmTiles.Count + "|" + room.Waterables.Count + "|" + room.TreeShakes.Count;
                foreach (var h in room.HarvestStatusGuidPersistences) sig += h.Guid + ":" + h.CanHarvest + ":" + h.DayLastHarvested + ";";
                foreach (var g in room.Growables) sig += g.Guid + ":" + g.GrowStageGuid + ":" + g.TimesHarvested + ":" + g.HasBeenReplaced + ";";
                foreach (var w in room.Waterables) sig += w.Guid + ":" + w.WateredAmount + ":" + w.DaysWateredLogs.Count + ";";
                var hash = sig.GetHashCode();
                if (hash == lastWorldHash) return;
                lastWorldHash = hash;
                SaveTransfer.Push(SerializationUtility.SerializeData(GamePersistence.Instance));
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning("world serialize: " + e.Message); }
        }

        internal static void ClearLocalMarkers(PlayerView except)
        {
            foreach (var id in UnityEngine.Object.FindObjectsByType<CoopIdentity>(FindObjectsSortMode.None))
                if (id.GetComponent<PlayerView>() != except) id.IsLocal = false;
        }

        internal static PlayerView FindLocal()
        {
            foreach (var id in UnityEngine.Object.FindObjectsByType<CoopIdentity>(FindObjectsSortMode.None))
                if (id.IsLocal) return id.GetComponent<PlayerView>();
            return null;
        }

        private static string ChildNames(Transform t)
        {
            var parts = new List<string>();
            for (int i = 0; i < t.childCount; i++) parts.Add(t.GetChild(i).name);
            return string.Join(",", parts);
        }

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            while (t != null && parts.Count < 6) { parts.Insert(0, t.name); t = t.parent; }
            return string.Join("/", parts);
        }

        internal static void LoadLatestSave()
        {
            try
            {
                var m = UnityEngine.Object.FindAnyObjectByType<AppMenuStateMachine>();
                if (!m) { CoopPlugin.Log.LogWarning("AppMenuStateMachine not found"); return; }
                var root = LatestProfileRoot();
                var dir = root != null ? System.IO.Path.Combine(root, "Saves") : null;
                var saves = dir != null && System.IO.Directory.Exists(dir) ? System.IO.Directory.GetDirectories(dir) : null;
                var save = saves != null && saves.Length > 0 ? saves[0] : null;
                if (save == null) { CoopPlugin.Log.LogWarning("No save folder in " + dir); return; }
                var guid = SerializedGuid.FromString(System.IO.Path.GetFileName(save));
                CoopPlugin.Log.LogInfo("LoadGame " + System.IO.Path.GetFileName(save));
                m.LoadGame(guid, null);
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning("LoadLatestSave: " + e); }
        }

        internal static string LatestSaveFile()
        {
            var root = LatestProfileRoot();
            if (root == null) return null;
            var saves = System.IO.Path.Combine(root, "Saves");
            if (!System.IO.Directory.Exists(saves)) return null;
            foreach (var d in System.IO.Directory.GetDirectories(saves))
            {
                var f = System.IO.Path.Combine(d, "GameData.json");
                if (System.IO.File.Exists(f)) return f;
            }
            return null;
        }

        internal static string CreateProfileRoot()
        {
            var root = System.IO.Path.Combine(CoopPlugin.IsolatedSaveRoot, "Moonlight Peaks");
            var dirs = System.IO.Directory.GetDirectories(root);
            if (dirs.Length > 0) return dirs[0];
            var d = System.IO.Path.Combine(root, "guest");
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(d, "Saves"));
            return d;
        }

        internal static string LatestProfileRoot()
        {
            var root = System.IO.Path.Combine(CoopPlugin.IsolatedSaveRoot, "Moonlight Peaks");
            if (!System.IO.Directory.Exists(root)) return null;
            foreach (var d in System.IO.Directory.GetDirectories(root))
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d, "Saves"))) return d;
            return null;
        }

        internal static void ClickButton(string contains)
        {
            var buttons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
            foreach (var b in buttons)
            {
                if (!b.isActiveAndEnabled || !b.gameObject.activeInHierarchy) continue;
                var label = b.GetComponentInChildren<TMP_Text>();
                var text = label ? label.text : b.name;
                if (text.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    CoopPlugin.Log.LogInfo("Clicking button " + text + " interactable=" + b.interactable + " selected=" + (EventSystem.current && EventSystem.current.currentSelectedGameObject == b.gameObject));
                    var es = EventSystem.current;
                    if (es)
                    {
                        var ped = new PointerEventData(es);
                        es.SetSelectedGameObject(b.gameObject);
                        ExecuteEvents.Execute(b.gameObject, ped, ExecuteEvents.pointerClickHandler);
                        ExecuteEvents.Execute(b.gameObject, ped, ExecuteEvents.submitHandler);
                    }
                    b.onClick.Invoke();
                    return;
                }
            }
            CoopPlugin.Log.LogWarning("Button not found: " + contains);
        }
    }
}










































