using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ArenaPvP
{
    internal static class ArenaBurstProtection
    {
        private static CharacterMaster Owner(GameObject source)
        {
            for (int i = 0; source && i < 8; i++)
            {
                var body = source.GetComponent<CharacterBody>(); if (body) return ArenaMatch.PlayerOwner(body.master);
                var master = source.GetComponent<CharacterMaster>(); if (master) return ArenaMatch.PlayerOwner(master);
                var projectile = source.GetComponent<RoR2.Projectile.ProjectileController>();
                source = projectile ? projectile.owner : null;
            }
            return null;
        }
        internal static ArenaPlayer Victim(HealthComponent health, DamageInfo info)
        {
            var match = ArenaMatch.Instance;
            if (!Plugin.Active || !NetworkServer.active || !match || match.Rules == null || !match.Rules.AntiOneShot
                || !health.body || !health.body.master
                || (ulong)(info.damageType & DamageType.OutOfBounds) != 0) return null;
            var victim = match.Find(health.body.master); if (victim == null || victim.Dead || victim.Master != health.body.master) return null;
            var master = Owner(info.attacker) ?? Owner(info.inflictor);
            var attacker = match.Find(master);
            if (master) return match.AreOpponents(victim, attacker) ? victim : null;
            // Native DoTs can outlive their attacker's body. Keep their original
            // recorded PvP attribution, without protecting environmental hits.
            if (info.dotIndex != DotController.DotIndex.None && match.Clock - victim.LastHitTime <= 10)
            {
                foreach (var other in match.AllPlayers)
                    if (other.UserId == victim.LastAttacker && match.AreOpponents(victim, other)) return victim;
            }
            return null;
        }
        internal static bool Before(HealthComponent health, DamageInfo info)
        {
            var victim = Victim(health, info); if (victim == null) return true;
            double now = ArenaMatch.Instance.Clock;
            if (victim.Burst.Protected(now)) { info.rejected = true; return false; }
            victim.Burst.Observe(now, health.fullCombinedHealth > 0 ? health.combinedHealth / health.fullCombinedHealth : 0);
            return true;
        }
        // Replaces health assignments inside native TakeDamageProcess only,
        // after armor/barrier/shield calculations and before death/proc handling.
        internal static void SetHealth(HealthComponent health, float proposed, DamageInfo info)
        {
            var victim = Victim(health, info);
            if (victim != null)
            {
                bool triggered;
                proposed = victim.Burst.Clamp(ArenaMatch.Instance.Clock, proposed, health.health, health.fullHealth, ArenaMatch.Instance.Rules.AntiBurstSeconds, out triggered);
                if (triggered) victim.ClearDots = true; // Clear outside the DoT evaluator's iteration.
            }
            health.Networkhealth = proposed;
        }
        internal static IEnumerable<CodeInstruction> ProtectHealthWrites(IEnumerable<CodeInstruction> instructions)
        {
            var setter = AccessTools.PropertySetter(typeof(HealthComponent), "Networkhealth");
            var replacement = AccessTools.Method(typeof(ArenaBurstProtection), nameof(SetHealth));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(setter))
                {
                    count++;
                    var argument = new CodeInstruction(OpCodes.Ldarg_1);
                    argument.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    argument.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return argument;
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                }
                else yield return instruction;
            }
            if (count < 2) throw new InvalidOperationException("Native damage health/execute writes changed; cannot install Arena anti-one-shot protection safely.");
        }
    }
}
