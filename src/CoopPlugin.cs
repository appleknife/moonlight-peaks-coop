using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Chicken.Utilities;
using HarmonyLib;
using UnityEngine;

namespace MoonlightPeaksCoop
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class CoopPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "moonlightpeaks.coop";
        public const string PluginName = "Moonlight Peaks Co-op";
        public const string PluginVersion = "0.1.4";

        internal static CoopPlugin Instance;
        internal static ManualLogSource Log;
        internal static string Profile = "default";
        internal static string IsolatedSaveRoot;

        internal static PlayerView LocalPlayer;
        internal static PlayerView RemotePlayer;
        internal static EntityAsset GuestPlayerAsset;
        internal static bool ShowMenu;
        internal static bool DebugProbe = false;
        internal static string JoinAddress = "127.0.0.1";
        internal const int Port = 47110;
        internal static float NextStateSend;

        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            ParseProfile();
            harmony = new Harmony(PluginGuid);
            foreach (var a in Environment.GetCommandLineArgs()) if (a.StartsWith("--coop-ctl=", StringComparison.OrdinalIgnoreCase)) Ctl.Port = int.Parse(a.Substring(11));
            Ctl.Start();
            harmony.PatchAll();
            var logFile = Path.Combine(IsolatedSaveRoot, "coop-" + Ctl.Port + ".log");
            Log.LogEvent += (s, e) =>
            {
                try { File.AppendAllText(logFile, DateTime.Now.ToString("HH:mm:ss.fff") + " " + e.Level + " " + e.Data + Environment.NewLine); } catch { }
            };
            Application.logMessageReceived += (msg, stack, type) =>
            {
                if (type == LogType.Log) return;
                try { File.AppendAllText(logFile, DateTime.Now.ToString("HH:mm:ss.fff") + " UNITY " + type + " " + msg + " | " + stack + Environment.NewLine); } catch { }
            };
            Log.LogInfo("Moonlight Peaks Co-op loaded. profile=" + Profile + " log=" + logFile);
        }

        private void Update()
        {
            try
            {
                if (Rewired.ReInput.isReady)
                {
                    var kb = Rewired.ReInput.controllers.Keyboard;
                    if (kb != null)
                    {
                        if (kb.GetKeyDown(KeyCode.F8)) ShowMenu = !ShowMenu;
                        if (kb.GetKeyDown(KeyCode.F1)) Net.StartHost(Port);
                        if (kb.GetKeyDown(KeyCode.F2)) Net.StartClient(JoinAddress, Port);
                        if (kb.GetKeyDown(KeyCode.F3)) Net.Stop();
                        if (kb.GetKeyDown(KeyCode.F5)) Net.SendTime(CachedDay, CachedHour);
                        if (kb.GetKeyDown(KeyCode.F9)) SpawnRemoteDummy();
                        if (kb.GetKeyDown(KeyCode.F6)) Probe.LoadLatestSave();
                        if (kb.GetKeyDown(KeyCode.F7)) Probe.ClickButton("Continue");
                    }
                }
                Net.Process();
                Ctl.Process();
                try { SendStateIfNeeded(); } catch (Exception e) { Log.LogWarning("send state: " + e.Message); }
                try { TickLocalHeld(); } catch (Exception e) { Log.LogWarning("local held: " + e.Message); }
                try { Probe.Tick(); } catch (Exception e) { Log.LogWarning("probe: " + (e.StackTrace ?? "").Replace(Environment.NewLine, " | ")); }
            }
            catch (Exception e)
            {
                Log.LogWarning("update: " + e.Message);
            }
        }

        private void OnGUI()
        {
            if (!ShowMenu) return;
            GUI.Box(new Rect(10, 10, 340, 225), "Moonlight Peaks Co-op " + PluginVersion);
            GUI.Label(new Rect(22, 40, 300, 20), "Profile: " + Profile);
            GUI.Label(new Rect(22, 60, 300, 20), "Local player: " + (LocalPlayer ? "yes" : "no"));
            GUI.Label(new Rect(22, 80, 300, 20), "Remote: " + (RemotePlayer ? "yes" : "no") + "   net=" + (Net.Running ? (Net.IsHost ? "host" : "guest") : "off"));
            GUI.Label(new Rect(22, 105, 60, 20), "Address");
            JoinAddress = GUI.TextField(new Rect(82, 103, 140, 22), JoinAddress ?? "");
            if (GUI.Button(new Rect(228, 103, 90, 24), "Join")) Net.StartClient(JoinAddress, Port);
            if (GUI.Button(new Rect(22, 135, 120, 30), "Host")) Net.StartHost(Port);
            if (GUI.Button(new Rect(150, 135, 120, 30), "Disconnect")) Net.Stop();
            if (GUI.Button(new Rect(22, 173, 120, 26), "Spawn ghost")) SpawnRemoteDummy();
            if (GUI.Button(new Rect(150, 173, 120, 26), "Remove ghost")) RemoveRemoteDummy();
        }

        private static int CachedDay => GamePersistence.Instance != null && GamePersistence.Instance.Time != null ? GamePersistence.Instance.Time.TotalDay : 0;
        private static float CachedHour => GamePersistence.Instance != null && GamePersistence.Instance.Time != null ? GamePersistence.Instance.Time.DayProgression : 0f;

        private static float nextTimeSend;
        private static float nextRoomSend;
        internal static int LastCoins = -1;
        private static Vector3 restoreLocal;
        private static float restoreUntil;
        private static float nextCoinSend;
        private static string lastRoomGuid;

        private static void SendStateIfNeeded()
        {
            if (!Net.Running || !PlayerView.Instance || !PlayerView.Instance.Character) return;
            if (GamePersistence.Instance == null || GamePersistence.Instance.Player == null) return;
            var now = Time.unscaledTime;
            if (Net.IsHost && now >= nextTimeSend)
            {
                nextTimeSend = now + 1f;
                Net.SendTime(CachedDay, CachedHour);
            }
            var room = GamePersistence.CurrentRoomAsset;
            if (Net.IsHost && room)
            {
                var g = room.SerializedGuid.ToGuid().ToString();
                if (g != lastRoomGuid || now >= nextRoomSend) { lastRoomGuid = g; nextRoomSend = now + 2f; Net.SendRoom(g, (byte)(DirectorSync.RoomSwitchCause == 2 ? 2 : 1)); }
            }
            if (now >= nextCoinSend)
            {
                nextCoinSend = now + 1f;
                var meta = CurrentMeta();
                if (meta != null && meta.Coins != LastCoins) { LastCoins = meta.Coins; Net.SendCoins(meta.Coins); }
            }
            if (now < NextStateSend) return;
            NextStateSend = now + 0.05f;
            var pv = PlayerView.Instance;
            var state = 0f;
            var current = pv.StateMachine ? pv.StateMachine.CurrentState as BasePlayerState : null;
            if (current != null) state = (float)(int)current.StateName;
            var speed = pv.Mover ? pv.Mover.SmoothVelocity.magnitude : 0f;
            var entry = GamePersistence.Instance != null ? GamePersistence.Instance.Player.GrabbedItemEntry : default;
            var held = entry.ItemAsset;
            var hg = held ? held.SerializedGuid.ToGuid().ToString() : "";
            if (hg.Length > 0 && PlayerStateCount % 60 == 0) Log.LogInfo("sending held " + hg);
            Net.SendPlayerState(pv.Character.transform.position, pv.Character.transform.rotation, speed, state, hg, (int)entry.ItemQualityLevel, entry.Metadata, entry.CustomName);
        }

        internal static bool TryRestoreLocal(out Vector3 pos)
        {
            if (Time.unscaledTime < restoreUntil) { pos = restoreLocal; return true; }
            pos = Vector3.zero;
            return false;
        }

        internal static int MetaCount()
        {
            var f = typeof(PersistenceService).GetField("GameMetaPersistences", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var list = f != null ? f.GetValue(null) as System.Collections.IEnumerable : null;
            if (list == null) return -1;
            int n = 0; foreach (var _ in list) n++; return n;
        }

        internal static GameMetaPersistence CurrentMeta()
        {
            if (GamePersistence.Instance == null) return null;
            var f = typeof(PersistenceService).GetField("GameMetaPersistences", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var list = f != null ? f.GetValue(null) as System.Collections.IEnumerable : null;
            if (list == null) return null;
            foreach (GameMetaPersistence m in list) if (m.Guid.Equals(GamePersistence.Instance.Guid)) return m;
            return null;
        }

        internal static void OnCoins(int coins)
        {
            var meta = CurrentMeta();
            if (meta != null) meta.Coins = coins;
            LastCoins = coins;
        }

        internal static void OnPeerJoined()
        {
            Log.LogInfo("peer joined");
            if (!RemotePlayer) SpawnRemoteDummy();
        }

        internal static void OnPeerLeft()
        {
            Log.LogInfo("peer left");
            RemoveRemoteDummy();
        }

        internal static int PlayerStateCount;

        internal static void OnPlayerState(float x, float y, float z, float rx, float ry, float rz, float rw, float speed, float state, string heldGuid, int heldQuality, string heldMetadata, string heldCustomName)
        {
            if (!RemotePlayer) SpawnRemoteDummy();
            if (!RemotePlayer) return;
            PlayerStateCount++;
            var puppet = RemotePlayer.GetComponent<CoopPuppet>();
            if (puppet) puppet.Apply(x, y, z, rx, ry, rz, rw, speed, state);
            else if (PlayerStateCount % 60 == 0) Log.LogWarning("no puppet on remote");
            if (puppet) puppet.ApplyHeld(heldGuid, heldQuality, heldMetadata, heldCustomName);
        }

        private static float nextWorldApply;

        internal static void ApplyGuestWorld(byte[] data)
        {
            if (data == null || GamePersistence.Instance == null) return;
            if (Time.unscaledTime < nextWorldApply) return;
            if (AppGameStateMachine.Instance && AppGameStateMachine.Instance.CurrentState is GameSwitchRoomState) return;
            nextWorldApply = Time.unscaledTime + 6f;
            if (LocalPlayer && LocalPlayer.Character) { restoreLocal = LocalPlayer.Character.transform.position; restoreUntil = Time.unscaledTime + 20f; }
            try
            {
                var guest = SerializationUtility.DeserializeData<GamePersistence>(data);
                if (guest == null) return;
                var host = GamePersistence.Instance;
                var skip = new System.Collections.Generic.HashSet<string> { "Player", "NpcRelationships", "Time", "Settings", "Camera", "Mods", "currentRoom", "saveBlockers", "Version", "BuildVersion", "IsNewGame", "SaveGameExecutionContext" };
                foreach (var f in typeof(GamePersistence).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                {
                    if (skip.Contains(f.Name)) continue;
                    try { f.SetValue(host, f.GetValue(guest)); } catch { }
                }
                var room = guest.CurrentRoom != null ? Asset.Get<RoomAsset>(guest.CurrentRoom.Guid) : GamePersistence.CurrentRoomAsset;
                if (room && AppGameStateMachine.Instance && !(AppGameStateMachine.Instance.CurrentState is GameSwitchRoomState))
                    AppGameStateMachine.Instance.GotoState<GameSwitchRoomState>(new GameSwitchRoomState.Config(room, false, false, false, false, false));
                Log.LogInfo("applied guest world (" + data.Length + " bytes)");
            }
            catch (Exception e) { Log.LogWarning("apply guest world: " + e.Message); }
        }
        internal static void OnDirectorNode(string nodeGuid, string subjectGuid, byte actor, string loiterGuid, byte loiterActor)
        {
            try
            {
                Log.LogInfo("director recv " + nodeGuid + " actor=" + actor);
                if (string.IsNullOrEmpty(nodeGuid)) return;
                var node = Director.Core.DirectorNode.Get(SerializedGuid.FromString(nodeGuid));
                if (node == null) return;
                if (DirectorSync.ShouldDefer())
                {
                    DeferNode(nodeGuid, subjectGuid, actor, loiterGuid, loiterActor);
                    Ctl.LastMessage = "deferred " + nodeGuid;
                    return;
                }
                var subject = DirectorSync.Resolve(actor, subjectGuid);
                var loiter = DirectorSync.Resolve(loiterActor, loiterGuid);
                DirectorSync.LastActor = actor;
                DirectorSync.LastSubjectName = subject != null ? subject.AssetName : "null";
                DirectorSync.LastLoiterName = loiter != null ? loiter.AssetName : "";
                DirectorSync.FromGuest = true;
                var context = new Director.Core.ExecutionContext(node, subject, loiter, SerializedGuid.Empty);
                var was = DirectorSync.ApplyingFromHost;
                DirectorSync.ApplyingFromHost = true;
                try { EventBus.OnStartDirectorStateRequest.Dispatch(context); }
                finally { DirectorSync.ApplyingFromHost = was; }
            }
            catch (Exception e) { Log.LogWarning("director node: " + e.Message); }
        }
        internal static void ApplyRemoteHeld(PlayerView view, string guidStr)
        {
            if (view == null) return;
            var puppet = view.GetComponent<CoopPuppet>();
            if (puppet) puppet.ApplyHeld(guidStr, 0, null, null);
        }

        private static readonly System.Reflection.MethodInfo HandleGrabbed = typeof(PlayerEquipment).GetMethod("HandleGrabbedItemChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        private static PlayerView localHeldView;
        private static ItemEntry localHeldPrev;
        private static float nextLocalHeldCheck;

        internal static void TickLocalHeld()
        {
            var v = LocalPlayer;
            if (!v || !v.Equipment) return;
            if (Time.unscaledTime < nextLocalHeldCheck) return;
            nextLocalHeldCheck = Time.unscaledTime + 1f;
            if (v != localHeldView) { localHeldView = v; localHeldPrev = default; }
            if (v.Equipment.GrabbedItemView) return;
            if (GamePersistence.Instance == null || GamePersistence.Instance.Player == null) return;
            var entry = GamePersistence.Instance.Player.GrabbedItemEntry;
            if (!entry.ItemAsset) return;
            ApplyEntry(v.Equipment, entry, ref localHeldPrev);
        }

        internal static void ApplyEntry(PlayerEquipment equipment, ItemEntry entry, ref ItemEntry previous)
        {
            try
            {
                var info = new GrabbedItemChangeInfo();
                info.PreviousItemEntry = previous;
                info.ItemEntry = entry;
                info.Instant = true;
                previous = entry;
                HandleGrabbed.Invoke(equipment, new object[] { info });
            }
            catch (Exception e) { Log.LogWarning("local held: " + e.Message); }
        }

        internal static string PendingRoomGuid;
        internal static float PendingRoomSince;
        internal static int PendingRoomCause;

        private static void SetPending(string guid, int cause)
        {
            PendingRoomGuid = guid;
            PendingRoomSince = Time.unscaledTime;
            PendingRoomCause = cause;
            Probe.ResetRoomDebounce();
        }

        internal static void NoteSwitchTarget(RoomAsset target)
        {
            if (!target) return;
            SetPending(target.SerializedGuid.ToGuid().ToString(), 2);
        }

        internal static void ClearPending()
        {
            PendingRoomGuid = null;
            ResumeDeferredNode();
        }

        private static bool hasDeferred;
        private static string dNode, dSubject, dLoiter;
        private static byte dActor, dLoiterActor;

        private static void DeferNode(string nodeGuid, string subjectGuid, byte actor, string loiterGuid, byte loiterActor)
        {
            hasDeferred = true;
            dNode = nodeGuid; dSubject = subjectGuid; dActor = actor; dLoiter = loiterGuid; dLoiterActor = loiterActor;
        }

        internal static void DeferContext(Director.Core.ExecutionContext context)
        {
            if (context == null || context.Node == null) return;
            var actor = DirectorSync.ActorFor(context.Subject, out var subjectGuid);
            var loiterActor = DirectorSync.ActorFor(context.LoiterTarget, out var loiterGuid);
            DeferNode(context.Node.SerializedGuid.ToGuid().ToString(), subjectGuid, actor, loiterGuid, loiterActor);
        }

        internal static void ResumeDeferredNode()
        {
            if (!hasDeferred) return;
            hasDeferred = false;
            OnDirectorNode(dNode, dSubject, dActor, dLoiter, dLoiterActor);
        }

        internal static void SwitchTo(RoomAsset room)
        {
            if (!room) return;
            var app = AppGameStateMachine.Instance;
            if (!app) return;
            SetPending(room.SerializedGuid.ToGuid().ToString(), 0);
            if (app.CurrentState is GameSwitchRoomState) return;
            if (app.CurrentState is GameDirectorStateMachine) { ForceSwitchViaDirector(room); return; }
            app.GotoState<GameSwitchRoomState>(new GameSwitchRoomState.Config(room, false, false, true, false, false));
        }

        internal static void OnRoomState(string guidStr, byte cause) => SwitchRoom(guidStr, false, cause);
        internal static void ForceRoom(string guidStr) => SwitchRoom(guidStr, true, PendingRoomCause == 0 ? 1 : PendingRoomCause, true);

        private static void SetPrivate(object obj, string field, object value) =>
            obj.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(obj, value);

        private static void ForceSwitchViaDirector(RoomAsset room)
        {
            var app = AppGameStateMachine.Instance;
            if (!app || !room) return;
            var node = new Director.Nodes.SwitchRoomNode();
            SetPrivate(node, "<FadeToBlack>k__BackingField", false);
            SetPrivate(node, "<FadeFromBlack>k__BackingField", false);
            SetPrivate(node, "<ShowLoadingWidget>k__BackingField", false);
            SetPrivate(node, "<MarkRoomEntered>k__BackingField", false);
            SetPrivate(node, "<BlockDayTimeProgression>k__BackingField", true);
            var target = new Director.Core.DirectorVariableRef((Director.Core.VariableType)2);
            target.Scope = Director.Core.DirectorVariableRef.ScopeType.HardCoded;
            target.HardCodedVariableType = (Director.Core.VariableType)2;
            target.HardCodedGuidValue = room.SerializedGuid;
            SetPrivate(node, "<Target>k__BackingField", target);
            var context = new Director.Core.ExecutionContext(node, AddressableLibrary<AssetLibrary>.Instance.PlayerEntity, null, SerializedGuid.Empty);
            DirectorSync.ForceSwitchNode = true;
            DirectorSync.SuppressSend = true;
            try { EventBus.OnStartDirectorStateRequest.Dispatch(context); }
            catch (Exception e) { Log.LogWarning("director switch: " + e); }
            finally { DirectorSync.ForceSwitchNode = false; DirectorSync.SuppressSend = false; }
        }

        private static void SwitchRoom(string guidStr, bool force, int cause, bool replace = false)
        {
            if (GamePersistence.Instance == null) return;
            var guid = SerializedGuid.FromString(guidStr);
            var current = GamePersistence.CurrentRoomAsset;
            var app = AppGameStateMachine.Instance;
            Ctl.LastMessage = "roomrecv " + guidStr + " cause=" + cause + " cur=" + (current ? current.AssetName : "none") + " app=" + (app && app.CurrentState ? app.CurrentState.GetType().Name : "null");
            if (guidStr != PendingRoomGuid || cause != PendingRoomCause) SetPending(guidStr, cause);
            if (!force && current != null && current.SerializedGuid.Equals(guid)) return;
            if (!app) return;
            if (cause == 2 && !force && Time.unscaledTime - DirectorSync.LastNodeAt < 4f)
            {
                Ctl.LastMessage += " wait=director";
                return;
            }
            if (app.CurrentState is GameSwitchRoomState && !replace) { Ctl.LastMessage += " skip=already-switching"; return; }
            var room = Asset.Load<RoomAsset>(guid, true, false);
            if (!room) { Log.LogWarning("room asset not found: " + guidStr); return; }
            if (Net.IsHost && (replace || app.CurrentState is GameDirectorStateMachine))
            {
                Log.LogInfo("director switch to " + room.AssetName + " cause=" + cause);
                ForceSwitchViaDirector(room);
                return;
            }
            var config = new GameSwitchRoomState.Config(room, false, false, true, false, false);
            Log.LogInfo("switching to room " + room.AssetName + " cause=" + cause);
            try { app.GotoState<GameSwitchRoomState>(config); }
            catch (Exception e) { Log.LogWarning("switch goto: " + e); }
            Log.LogInfo("switch issued; app state now " + (app.CurrentState ? app.CurrentState.GetType().Name : "null") + " room=" + (Room.Exists && Room.Instance && Room.Instance.RoomAsset ? Room.Instance.RoomAsset.AssetName : "none"));
        }

        internal static void OnTimeState(int day, float progression)
        {
            if (GamePersistence.Instance == null) return;
            GamePersistence.Instance.Time.TotalDay = day;
            GamePersistence.Instance.Time.DayProgression = progression;
        }
        internal static PlayerView SpawnLocalPlayer(Vector3 pos)
        {
            var prefab = AddressableLibrary<AssetLibrary>.Instance.PlayerView;
            var template = prefab ? prefab.gameObject : (RemotePlayer ? RemotePlayer.gameObject : null);
            if (!template) return null;
            EntityDuplicatePatch.SpawningPuppet = true;
            GameObject go;
            try { go = UnityEngine.Object.Instantiate(template, pos, Quaternion.identity); }
            finally { EntityDuplicatePatch.SpawningPuppet = false; }
            go.name = "CoopLocalPlayer";
            var view = go.GetComponent<PlayerView>();
            if (!view) return null;
            var oldPuppet = go.GetComponent<CoopPuppet>();
            if (oldPuppet) UnityEngine.Object.Destroy(oldPuppet);
            if (view.Mover) view.Mover.enabled = true;
            if (view.Input) view.Input.enabled = true;
            if (view.Interactor) view.Interactor.enabled = true;
            if (view.ToolInteractor) view.ToolInteractor.enabled = true;
            if (view.StateMachine) view.StateMachine.enabled = true;
            if (view.Mover && view.Mover.Controller) view.Mover.Controller.enabled = true;
            var id = go.AddComponent<CoopIdentity>();
            id.IsLocal = true;
            go.SetActive(true);
            var persistence = new EntityCharacterPersistence();
            persistence.Guid = SerializedGuid.NewGuid();
            typeof(EntityCharacter).GetProperty("Persistence").SetValue(view.Character, persistence, null);
            LocalPlayer = view;
            SetLocalSingleton(view);
            localHeldView = null;
            nextLocalHeldCheck = 0f;
            TickLocalHeld();
            Log.LogInfo("spawned local player at " + pos);
            return view;
        }

        internal static void SpawnRemoteDummy()
        {
            if (RemotePlayer || !PlayerView.Exists) return;
            LocalPlayer = PlayerView.Instance;
            if (LocalPlayer && !LocalPlayer.GetComponent<CoopIdentity>()) LocalPlayer.gameObject.AddComponent<CoopIdentity>().IsLocal = true;
            var prefab = AddressableLibrary<AssetLibrary>.Instance.PlayerView;
            if (!prefab)
            {
                Log.LogWarning("Player prefab not available yet");
                return;
            }
            EntityDuplicatePatch.SpawningPuppet = true;
            GameObject go;
            try
            {
                go = UnityEngine.Object.Instantiate(prefab.gameObject,
                    LocalPlayer.transform.position + new Vector3(1.2f, 0f, 0f),
                    LocalPlayer.transform.rotation);
            }
            finally
            {
                EntityDuplicatePatch.SpawningPuppet = false;
            }
            go.name = "CoopRemoteDummy";
            var view = go.GetComponent<PlayerView>();
            go.AddComponent<CoopIdentity>();
            if (!view)
            {
                Log.LogWarning("Spawned prefab has no PlayerView");
                UnityEngine.Object.Destroy(go);
                return;
            }
            
            go.SetActive(true);
            // Keep the singleton pointing at our own player on both machines.
            SetLocalSingleton(LocalPlayer);
            if (Net.IsHost)
            {
                if (GuestPlayerAsset == null)
                {
                    var orig = AddressableLibrary<AssetLibrary>.Instance.PlayerEntity;
                    GuestPlayerAsset = UnityEngine.Object.Instantiate(orig);
                    GuestPlayerAsset.name = "CoopGuestPlayer";
                    GuestPlayerAsset.SerializedGuid = SerializedGuid.NewGuid();
                }
                if (view.Character)
                {
                    typeof(EntityBehaviour<EntityCharacter>).GetProperty("IsRegistered").SetValue(view.Character, false, null);
                    typeof(EntityBehaviour<EntityCharacter>).GetField("entity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(view.Character, GuestPlayerAsset);
                    EntityDuplicatePatch.SpawningPuppet = true;
                    try { view.Character.RegisterToDictionary(); } finally { EntityDuplicatePatch.SpawningPuppet = false; }
                }
            }
            if (view.Input) view.Input.enabled = false;
            var persistence = new EntityCharacterPersistence();
            persistence.Guid = SerializedGuid.NewGuid();
            typeof(EntityCharacter).GetProperty("Persistence").SetValue(view.Character, persistence, null);
            var puppet = go.AddComponent<CoopPuppet>();
            puppet.Setup(view);
            RemotePlayer = view;
            SetLocalSingleton(LocalPlayer);
            Log.LogInfo("Spawned remote dummy; singletonLocal=" + (PlayerView.Instance == LocalPlayer) + " localChar=" + (LocalPlayer.Character ? "yes" : "no") + " remoteChar=" + (view.Character ? "yes" : "no"));
            Log.LogInfo("LOCALVIEW " + Describe(LocalPlayer));
            Log.LogInfo("REMOTEVIEW " + Describe(view));
        }

        internal static void RemoveRemoteDummy()
        {
            if (!RemotePlayer) return;
            UnityEngine.Object.Destroy(RemotePlayer.gameObject);
            RemotePlayer = null;
            if (LocalPlayer) SetLocalSingleton(LocalPlayer);
        }

        private void PatchDuplicateGuard()
        {
            var prefix = new HarmonyMethod(typeof(EntityDuplicatePatch).GetMethod("Prefix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic));
            int patched = 0;
            foreach (var t in typeof(EntityCharacter).Assembly.GetTypes())
            {
                for (var b = t.BaseType; b != null; b = b.BaseType)
                {
                    if (!b.IsGenericType || b.GetGenericTypeDefinition() != typeof(EntityBehaviour<>)) continue;
                    var m = AccessTools.Method(b, "AddEntityToDictionary");
                    if (m != null) { harmony.Patch(m, prefix: prefix); patched++; }
                    break;
                }
            }
            Log.LogInfo("duplicate guard patched " + patched + " types");
        }

        private static void CopyDir(string from, string to)
        {
            try
            {
                Directory.CreateDirectory(to);
                foreach (var f in Directory.GetFiles(from))
                    try { CopyFile(f, Path.Combine(to, Path.GetFileName(f))); } catch { }
                foreach (var d in Directory.GetDirectories(from))
                    try { CopyDir(d, Path.Combine(to, Path.GetFileName(d))); } catch { }
            }
            catch { }
        }

        private static void CopyFile(string from, string to)
        {
            if (File.Exists(to)) return;
            File.Copy(from, to);
        }

        internal static string Describe(PlayerView v)
        {
            if (!v) return "null";
            var parts = new System.Text.StringBuilder();
            var t = v.transform;
            int guard = 0;
            while (t != null && guard++ < 6) { parts.Insert(0, t.name + "/"); t = t.parent; }
            parts.Append(" children=").Append(v.transform.childCount);
            for (int i = 0; i < v.transform.childCount; i++)
            {
                var c = v.transform.GetChild(i);
                parts.Append(" [").Append(c.name).Append(":").Append(c.GetComponents<Component>().Length).Append("]");
            }
            parts.Append(" scene=").Append(v.gameObject.scene.name);
            return parts.ToString();
        }

        internal static void SetLocalSingleton(PlayerView view)
        {
            var p = typeof(MonoBehaviourSingleton<PlayerView>).GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (p != null && p.CanWrite) p.SetValue(null, view, null);
        }

        private static void ParseProfile()
        {
            var args = Environment.GetCommandLineArgs();
            foreach (var a in args)
            {
                if (a.StartsWith("--coop-profile=", StringComparison.OrdinalIgnoreCase))
                    Profile = a.Substring("--coop-profile=".Length);
            }
            var safe = Profile;
            foreach (var c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            IsolatedSaveRoot = Path.Combine(Paths.BepInExRootPath, "coop-saves", safe);
            Directory.CreateDirectory(IsolatedSaveRoot);
            try
            {
                var original = Application.persistentDataPath;
                var leaf = Path.GetFileName(original.TrimEnd('\\', '/'));
                var dest = Path.Combine(IsolatedSaveRoot, leaf);
                if (Directory.Exists(original)) CopyDir(original, dest);
            }
            catch (Exception e) { Log.LogWarning("save seed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(EntityAsset), "get_IsPlayer")]
    internal static class GuestIsPlayerPatch
    {
        private static void Postfix(EntityAsset __instance, ref bool __result)
        {
            if (!__result && __instance == CoopPlugin.GuestPlayerAsset) __result = true;
        }
    }

    [HarmonyPatch(typeof(Application), "persistentDataPath", MethodType.Getter)]
    internal static class PersistentDataPathPatch
    {
        private static void Postfix(ref string __result)
        {
            try
            {
                var leaf = Path.GetFileName(__result.TrimEnd('\\', '/'));
                var root = CoopPlugin.IsolatedSaveRoot;
                if (!string.IsNullOrEmpty(root))
                    __result = Path.Combine(root, leaf);
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogWarning("persistentDataPath patch failed: " + e);
            }
        }
    }
}

































































