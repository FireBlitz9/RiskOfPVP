using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using UnityEngine.Networking;

namespace ArenaPvP
{
    internal static class ArenaBeadExperience
    {
        internal static void LevelsGained(ArenaPlayer player, int previous, int current)
        {
            if (!Plugin.Active || !NetworkServer.active || !player.Master || !player.Master.inventory) return;
            player.BeadExperience.Record(previous, current,
                player.Master.inventory.GetItemCountEffective(DLC2Content.Items.ExtraStatsOnLevelUp) > 0, TeamManager.GetExperienceForLevel);
            Flush(player);
        }
        internal static void Flush(ArenaPlayer player)
        {
            if (!Plugin.Active || !NetworkServer.active || !player.Master || !TeamManager.instance) return;
            var body = player.Master.GetBody();
            player.BeadExperience.Drain(body && body.healthComponent && body.healthComponent.alive, player.Master.TrackBeadExperience);
        }
    }

    // Native Beads stores its reward normally on removal, but four application
    // clauses still compare against the single vanilla Player enum value.
    [HarmonyPatch(typeof(CharacterBody), nameof(CharacterBody.RecalculateStats))]
    internal static class ArenaBeadStats
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var getter = AccessTools.PropertyGetter(typeof(TeamComponent), nameof(TeamComponent.teamIndex));
            var replacement = AccessTools.Method(typeof(ArenaBeadStats), nameof(TeamForBeadStats));
            var fields = new HashSet<string> { "beadAppliedHealth", "beadAppliedShield", "beadAppliedRegen", "beadAppliedDamage" };
            int count = 0;
            for (int i = 0; i + 2 < code.Count; i++)
            {
                if (!code[i].Calls(getter) || code[i + 1].opcode != OpCodes.Ldc_I4_1 || code[i + 2].opcode.FlowControl != FlowControl.Cond_Branch) continue;
                bool beadClause = false;
                for (int j = i + 3; j < Math.Min(code.Count, i + 30); j++)
                {
                    if (code[j].Calls(getter)) break;
                    var field = code[j].operand as System.Reflection.FieldInfo;
                    if (field != null && field.DeclaringType == typeof(Inventory) && fields.Contains(field.Name))
                    { fields.Remove(field.Name); beadClause = true; break; }
                }
                if (!beadClause) continue;
                code[i].opcode = OpCodes.Call; code[i].operand = replacement; count++;
            }
            if (count != 4) throw new InvalidOperationException("Native Prayer Beads stat clauses changed; expected four isolated team checks.");
            return code;
        }
        internal static TeamIndex TeamForBeadStats(TeamComponent component)
        {
            TeamIndex original = component.teamIndex;
            if (!Plugin.Active || !ArenaTeams.IsArenaTeam(original)) return original;
            var body = component.GetComponent<CharacterBody>();
            return body && (ArenaTeams.IsCombatantBody(body) || ArenaMatch.PlayerOwner(body.master)) ? TeamIndex.Player : original;
        }
    }
}
