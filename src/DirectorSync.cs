using HarmonyLib;

namespace MoonlightPeaksCoop
{
    internal static class DirectorSync
    {
        internal static bool ApplyingFromHost;
        internal static bool FromGuest;
        internal static string LastSubjectName = "";
        internal static int LastActor;
        internal static string LastLoiterName = "";
        internal static float LastNodeAt;
        internal static int RoomSwitchCause;
        internal static bool ForceSwitchNode;
        internal static bool SuppressSend;

        internal static bool IsSwitchNode(Director.Core.ExecutionContext context) =>
            context != null && context.Node is Director.Nodes.SwitchRoomNode;

        internal static bool ShouldDefer()
        {
            if (ForceSwitchNode) return false;
            if (string.IsNullOrEmpty(CoopPlugin.PendingRoomGuid)) return false;
            if (CoopPlugin.PendingRoomCause == 2) return false;
            var target = Room.Exists && Room.Instance ? Room.Instance.RoomAsset : null;
            return !(target && target.SerializedGuid.ToGuid().ToString() == CoopPlugin.PendingRoomGuid);
        }

        internal static byte ActorFor(IEntityAsset asset, out string guid)
        {
            guid = "";
            if (asset == null) return 0;
            var player = AddressableLibrary<AssetLibrary>.Instance.PlayerEntity;
            if ((UnityEngine.Object)asset == (UnityEngine.Object)player)
                return Net.IsHost ? (byte)1 : (byte)2;
            if (CoopPlugin.GuestPlayerAsset != null && (UnityEngine.Object)asset == (UnityEngine.Object)CoopPlugin.GuestPlayerAsset)
                return 2;
            guid = asset.SerializedGuid.ToGuid().ToString();
            return 3;
        }

        internal static IEntityAsset Resolve(byte actor, string guid)
        {
            if (actor == 1) return AddressableLibrary<AssetLibrary>.Instance.PlayerEntity;
            if (actor == 2)
            {
                if (Net.IsHost && CoopPlugin.GuestPlayerAsset != null) return CoopPlugin.GuestPlayerAsset;
                return AddressableLibrary<AssetLibrary>.Instance.PlayerEntity;
            }
            if (actor == 3 && !string.IsNullOrEmpty(guid)) return Asset.Get<EntityAsset>(SerializedGuid.FromString(guid));
            return null;
        }

        internal static void Send(Director.Core.ExecutionContext context)
        {
            if (!Net.Running || context == null || context.Node == null) return;
            if (SuppressSend) return;
            LastNodeAt = UnityEngine.Time.unscaledTime;
            var actor = ActorFor(context.Subject, out var subjectGuid);
            var loiterActor = ActorFor(context.LoiterTarget, out var loiterGuid);
            Net.SendDirectorNode(context.Node.SerializedGuid.ToGuid().ToString(), subjectGuid, actor, loiterGuid, loiterActor);
        }
    }

    [HarmonyPatch(typeof(GameDirectorStateMachine), "GoToNode")]
    internal static class DirectorGoToPatch
    {
        public static bool Prefix(Director.Core.ExecutionContext context)
        {
            if (Net.Running && Net.IsHost && !DirectorSync.ApplyingFromHost)
            {
                DirectorSync.FromGuest = false;
                DirectorSync.LastNodeAt = UnityEngine.Time.unscaledTime;
                if (DirectorSync.ShouldDefer()) { CoopPlugin.DeferContext(context); return false; }
            }
            if (!Net.Running || Net.IsHost || DirectorSync.ApplyingFromHost) return true;
            DirectorSync.Send(context);
            return false;
        }
    }

    [HarmonyPatch(typeof(GameDirectorStateMachine), "GoToNextNode")]
    internal static class DirectorNextPatch
    {
        public static bool Prefix(GameDirectorStateMachine __instance)
        {
            if (!Net.Running || Net.IsHost || DirectorSync.ApplyingFromHost) return true;
            DirectorSync.Send(__instance.CurrentContext);
            return false;
        }

        public static void Postfix(GameDirectorStateMachine __instance)
        {
            if (!Net.Running || !Net.IsHost || DirectorSync.SuppressSend) return;
            DirectorSync.LastNodeAt = UnityEngine.Time.unscaledTime;
            DirectorSync.Send(__instance.CurrentContext);
        }
    }

    [HarmonyPatch(typeof(GameSwitchRoomState), "OnActivate")]
    internal static class RoomSwitchCausePatch
    {
        private static void Postfix(GameSwitchRoomState __instance)
        {
            if (!Net.Running || !Net.IsHost) return;
            var config = Traverse.Create(__instance).Field("config").GetValue<GameSwitchRoomState.Config>();
            var target = config != null ? config.TargetRoom : null;
            if (target != null && !string.IsNullOrEmpty(CoopPlugin.PendingRoomGuid) &&
                CoopPlugin.PendingRoomCause == 2 &&
                target.SerializedGuid.ToGuid().ToString() != CoopPlugin.PendingRoomGuid)
            {
                CoopPlugin.Log.LogInfo("room pending replaced by " + target.AssetName);
                CoopPlugin.NoteSwitchTarget(target);
            }
            DirectorSync.RoomSwitchCause = DirectorSync.IsSwitchNode(config != null ? config.ExecutionContext : null) ? 2 : 1;
        }
    }

    [HarmonyPatch(typeof(Director.Core.DirectorVariableRef), "GetAssetValue")]
    internal static class VariableRefGuestPatch
    {
        private static void Postfix(ref IAsset __result)
        {
            if (!DirectorSync.FromGuest || __result == null || CoopPlugin.GuestPlayerAsset == null) return;
            if (!(__result is IEntityAsset)) return;
            var player = AddressableLibrary<AssetLibrary>.Instance.PlayerEntity;
            if ((UnityEngine.Object)__result == (UnityEngine.Object)player) __result = CoopPlugin.GuestPlayerAsset;
        }
    }
}
