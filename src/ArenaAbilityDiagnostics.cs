using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ArenaPvP
{
    // Observe early native exits. Never reject an ejection, change input,
    // replenish barrier, change attack filtering or force an ability state.
    internal static class ArenaAbilityDiagnostics
    {
        private static readonly System.Reflection.FieldInfo age = AccessTools.Field(typeof(SojournVehicleBase), "age");
        private static readonly System.Reflection.FieldInfo overlap = AccessTools.Field(typeof(SojournVehicleBase), "overlapAttack");
        private static readonly HashSet<int> logged = new HashSet<int>();
        private static Run run;
        internal static void EarlyExit(VehicleSeat seat, Collision collision = null)
        {
            try
            {
                var flight = seat ? seat.GetComponent<SojournVehicleBase>() : null;
                var body = seat ? seat.currentPassengerBody : null;
                if (!flight || !body || age == null) return;
                float seconds = (float)age.GetValue(flight);
                if (seconds > 1.5f) return;
                if (run != Run.instance) { logged.Clear(); run = Run.instance; }
                if (logged.Count >= 256) logged.Clear();
                if (!logged.Add(seat.GetInstanceID())) return;
                var attack = overlap == null ? null : overlap.GetValue(flight) as OverlapAttack;
                var hit = attack != null && attack.lastHitObject ? attack.lastHitObject.GetComponentInParent<CharacterBody>() : null;
                var health = body.healthComponent;
                string caller = string.Join(" > ", new StackTrace(false).GetFrames().Take(12).Select(frame => {
                    var method = frame.GetMethod(); return (method.DeclaringType == null ? "" : method.DeclaringType.Name + ".") + method.Name;
                }).ToArray());
                Plugin.Log("Ability early exit: " + flight.GetType().Name + "; role=" + (NetworkServer.active ? "server" : "client")
                    + "; age=" + seconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
                    + "; barrier=" + (health ? health.barrier + "/" + health.fullBarrier + "; fullHealth=" + health.fullHealth : "missing")
                    + "; utilityHeld=" + (seat.currentPassengerInputBank != null && seat.currentPassengerInputBank.skill3.down)
                    + "; bodyTeam=" + (body.teamComponent ? ((int)body.teamComponent.teamIndex).ToString() : "none")
                    + "; attackTeam=" + (attack == null ? "none" : ((int)attack.teamIndex).ToString())
                    + "; lastHit=" + (hit ? hit.name + "/team=" + (hit.teamComponent ? ((int)hit.teamComponent.teamIndex).ToString() : "none") : "none")
                    + "; collision=" + (collision != null && collision.collider ? collision.collider.name + "/layer=" + collision.collider.gameObject.layer : "none")
                    + "; nativePath=" + caller);
            }
            catch (Exception error) { Plugin.Warn("Ability exit trace unavailable: " + error.Message); }
        }
    }
    [HarmonyPatch(typeof(VehicleSeat), nameof(VehicleSeat.CallCmdEjectPassenger))]
    internal static class TraceArenaVehicleEjectRequest
    {
        private static void Prefix(VehicleSeat __instance)
        { if (Plugin.Active) ArenaAbilityDiagnostics.EarlyExit(__instance); }
    }
    [HarmonyPatch(typeof(VehicleSeat), "SetPassengerInternal")]
    internal static class TraceArenaVehicleRemoval
    {
        private static void Prefix(VehicleSeat __instance, GameObject newPassengerBodyObject)
        { if (Plugin.Active && !newPassengerBodyObject) ArenaAbilityDiagnostics.EarlyExit(__instance); }
    }
    [HarmonyPatch(typeof(SojournVehicleBase), "OnCollisionEnter")]
    internal static class TraceArenaFlightCollision
    {
        private static void Prefix(SojournVehicleBase __instance, Collision collision)
        {
            if (Plugin.Active && __instance.detonateOnCollision && Util.HasEffectiveAuthority(__instance.GetComponent<NetworkIdentity>()))
                ArenaAbilityDiagnostics.EarlyExit(__instance.GetComponent<VehicleSeat>(), collision);
        }
    }
}
