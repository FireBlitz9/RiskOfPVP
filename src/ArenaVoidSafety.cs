using System;
using System.Linq;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ArenaPvP
{
    internal static class ArenaVoidSafety
    {
        [ThreadStatic] internal static CharacterBody ReturningBody;
        internal static bool Enabled
        {
            get
            {
                if (!Plugin.Active) return false;
                if (NetworkServer.active) return ArenaMatch.Instance && ArenaMatch.Instance.Rules != null && ArenaMatch.Instance.Rules.VoidProtection;
                var run = Run.instance;
                var view = ArenaNetwork.Views.Values.FirstOrDefault(v => run && v.RunId == run.netId.Value);
                return view != null && view.VoidProtection;
            }
        }
        internal static bool Participant(CharacterBody body)
        { return body && body.master && (body.master.playerCharacterMasterController || body.teamComponent && ArenaTeams.IsArenaTeam(body.teamComponent.teamIndex)); }
        internal static void Track(CharacterBody body)
        { if (Participant(body) && !body.GetComponent<ArenaSafeGround>()) body.gameObject.AddComponent<ArenaSafeGround>(); }
    }
    internal sealed class ArenaSafeGround : MonoBehaviour
    {
        private CharacterBody body;
        private float nextSample;
        private bool known;
        private Vector3 last;
        private int scene;
        private void Awake() { body = GetComponent<CharacterBody>(); }
        private void FixedUpdate()
        {
            if (!Plugin.Active) { Destroy(this); return; }
            if (!body || Time.unscaledTime < nextSample || !Util.HasEffectiveAuthority(body.gameObject)) return;
            nextSample = Time.unscaledTime + .2f;
            if (!body.characterMotor || !body.characterMotor.isGrounded || body.currentVehicle) return;
            RaycastHit hit;
            if (!Physics.Raycast(body.footPosition + Vector3.up * .7f, Vector3.down, out hit, 1.8f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)
                || hit.normal.y < .45f) return;
            last = hit.point; scene = gameObject.scene.handle; known = true;
        }
        internal bool TryPosition(out Vector3 point)
        { point = last; return known && scene == gameObject.scene.handle; }
    }
    [HarmonyPatch(typeof(MapZone), "CharacterEligibleForTeleport")]
    internal static class ArenaVoidEligibility
    {
        private static void Postfix(CharacterBody characterBody, ref bool __result)
        {
            if (Plugin.Active && ArenaVoidSafety.Enabled && ArenaVoidSafety.Participant(characterBody)) __result = true;
        }
    }
    [HarmonyPatch(typeof(MapZone), "TeleportBody")]
    internal static class ArenaVoidTeleportScope
    {
        private static void Prefix(MapZone __instance, CharacterBody characterBody, out CharacterBody __state)
        {
            __state = ArenaVoidSafety.ReturningBody;
            ArenaVoidSafety.ReturningBody = Plugin.Active && ArenaVoidSafety.Enabled && __instance.zoneType == MapZone.ZoneType.OutOfBounds
                && ArenaVoidSafety.Participant(characterBody) ? characterBody : null;
        }
        private static Exception Finalizer(CharacterBody __state, Exception __exception)
        { ArenaVoidSafety.ReturningBody = __state; return __exception; }
    }
    [HarmonyPatch(typeof(Run), nameof(Run.FindSafeTeleportPosition), new[] { typeof(CharacterBody), typeof(Transform), typeof(float), typeof(float) })]
    internal static class ArenaLastSafeVoidPosition
    {
        private static bool Prefix(CharacterBody characterBody, Transform targetDestination, ref Vector3 __result)
        {
            if (!Plugin.Active || !ArenaVoidSafety.Enabled || !characterBody || characterBody != ArenaVoidSafety.ReturningBody || targetDestination) return true;
            var tracker = characterBody.GetComponent<ArenaSafeGround>();
            return !tracker || !tracker.TryPosition(out __result);
        }
    }
}
