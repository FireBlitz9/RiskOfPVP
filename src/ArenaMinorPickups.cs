using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using UnityEngine;

namespace ArenaPvP
{
    // These native pickups hard-code a single eligible TeamFilter. Adapt only
    // that filter for this trigger, then let native collection/attraction run.
    [HarmonyPatch]
    internal static class ArenaMinorPickups
    {
        internal struct FilterState { internal TeamFilter Filter; internal TeamIndex Original; }
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var type in new[] { typeof(MoneyPickup), typeof(HealthPickup), typeof(ElusiveAntlersPickup), typeof(LowHealthOrbPickup), typeof(AmmoPickup), typeof(BuffPickup) })
                yield return AccessTools.Method(type, "OnTriggerStay");
            yield return AccessTools.Method(typeof(GravitatePickup), "StartGravitate");
        }
        private static bool Prefix(Component __instance, Collider other, out FilterState __state)
        {
            __state = default(FilterState);
            if (!Plugin.Active || !other) return true;
            var body = other.GetComponent<CharacterBody>();
            if (!body || !body.master || !ArenaMatch.IsPlayerOrOwned(body.master)) return true;
            TeamFilter filter = null;
            var money = __instance as MoneyPickup; if (money) filter = money.teamFilter;
            var health = __instance as HealthPickup; if (health) filter = health.teamFilter;
            var antler = __instance as ElusiveAntlersPickup; if (antler) filter = antler.teamFilter;
            var orb = __instance as LowHealthOrbPickup; if (orb) filter = orb.teamFilter;
            var magnet = __instance as GravitatePickup; if (magnet) filter = magnet.teamFilter;
            var ammo = __instance as AmmoPickup; if (ammo) filter = ammo.teamFilter;
            var buff = __instance as BuffPickup; if (buff) filter = buff.teamFilter;
            if (!filter) return true;
            if (!antler) antler = __instance.transform.root.GetComponentInChildren<ElusiveAntlersPickup>();
            bool owned = antler && antler.ownerBody && antler.ownerBody.teamComponent;
            var ownTeam = owned ? antler.ownerBody.teamComponent.teamIndex : TeamIndex.None;
            var team = body.teamComponent.teamIndex;
            if (!MinorPickupRules.CanCollect(owned, (int)ownTeam, (int)filter.teamIndex, (int)team, filter.teamIndex == TeamIndex.Player, ArenaTeams.IsArenaTeam(team))) return false;
            __state = new FilterState { Filter = filter, Original = filter.teamIndex };
            filter.teamIndex = team;
            return true;
        }
        private static Exception Finalizer(Exception __exception, FilterState __state)
        { if (__state.Filter) __state.Filter.teamIndex = __state.Original; return __exception; }
    }
}
