using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;

namespace ArenaPvP
{
    [HarmonyPatch(typeof(EntityStates.Seeker.Reprieve), nameof(EntityStates.Seeker.Reprieve.OnEnter))]
    internal static class ArenaReprieveBarrier
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.PropertyGetter(typeof(HealthComponent), nameof(HealthComponent.fullHealth));
            var replacement = AccessTools.Method(typeof(ArenaReprieveBarrier), nameof(HealthBasis));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (!instruction.Calls(original)) { yield return instruction; continue; }
                count++;
                var call = new CodeInstruction(OpCodes.Call, replacement);
                call.labels.AddRange(instruction.labels); call.blocks.AddRange(instruction.blocks);
                yield return call;
            }
            if (count != 1) throw new InvalidOperationException("Native Reprieve barrier calculation changed; cannot install the shield conversion fix safely.");
        }
        internal static float HealthBasis(HealthComponent health)
        {
            if (!Plugin.Active) return health.fullHealth;
            return ReprieveBarrierRules.HealthBasis(true, health.fullHealth, health.fullShield);
        }
    }
}
