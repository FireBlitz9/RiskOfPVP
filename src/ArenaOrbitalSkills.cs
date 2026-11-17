using HarmonyLib;
using RoR2.Skills;

namespace ArenaPvP
{
    // This native getter gates Captain's orbital strikes/beacons (and any
    // other skill using this SkillDef). Stock/cooldown/target tests stay native.
    [HarmonyPatch(typeof(CaptainOrbitalSkillDef), "get_isAvailable")]
    internal static class ArenaOrbitalSkills
    {
        private static bool Prefix(ref bool __result)
        { if (!Plugin.Active) return true; __result = true; return false; }
    }
}
