using HarmonyLib;

namespace MoonlightPeaksCoop
{
    // EntityBehaviour registers one component per entity asset GUID and destroys the previous
    // one. A second player uses the same @Player asset, so while a puppet is being spawned we
    // keep the existing registration (and its body) alive.
    internal static class EntityDuplicatePatch
    {
        internal static bool SpawningPuppet;

        internal static bool Prefix(ref bool __result)
        {
            if (!SpawningPuppet) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(EntityBehaviour), "get_allowDuplicateEntityAsset")]
    internal static class EntityDuplicatePatchBase
    {
        private static bool Prefix(ref bool __result) => EntityDuplicatePatch.Prefix(ref __result);
    }

    [HarmonyPatch(typeof(EntityLocation), "get_allowDuplicateEntityAsset")]
    internal static class EntityDuplicatePatchLocation
    {
        private static bool Prefix(ref bool __result) => EntityDuplicatePatch.Prefix(ref __result);
    }
}
