using System;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ArenaPvP
{
    [HarmonyPatch(typeof(GlobalEventManager), "OnPlayerCharacterDeath")]
    internal static class OneArenaDeathMessage
    {
        // ArenaMatch broadcasts one authoritative notice, with PvP attribution.
        // Leave native death procs, scoring and non-Arena announcements intact.
        private static bool Prefix() { return !Plugin.Active; }
    }

    [HarmonyPatch(typeof(RoR2.UI.Nameplate), "LateUpdate")]
    internal static class ArenaNametagVisibility
    {
        private static void Postfix(RoR2.UI.Nameplate __instance, CharacterBody ___body)
        {
            if (!Plugin.Active || !ArenaTeams.IsCombatantBody(___body)) return;
            if (!Plugin.Settings.ShowNametags.Value && __instance.label) __instance.label.enabled = false;
            int side = ArenaTeams.SideOf(___body.master);
            if (side >= 0)
            {
                var color = ArenaTeams.SideColor(side);
                if (__instance.label) __instance.label.color = color;
                if (__instance.criticallyHurtSpriteRenderer) __instance.criticallyHurtSpriteRenderer.color = color;
                if (__instance.coloredSprites != null) foreach (var sprite in __instance.coloredSprites) if (sprite) sprite.color = color;
            }
            if (Plugin.Settings.ShowOpponentArrows.Value) return;
            if (__instance.aliveObject) __instance.aliveObject.SetActive(false);
            if (__instance.deadObject) __instance.deadObject.SetActive(false);
            if (__instance.criticallyHurtSpriteRenderer) __instance.criticallyHurtSpriteRenderer.enabled = false;
            if (__instance.coloredSprites != null) foreach (var sprite in __instance.coloredSprites) if (sprite) sprite.enabled = false;
        }
    }

    [HarmonyPatch(typeof(TeamComponent), "SetupIndicator")]
    internal static class ArenaPlayerPositionIndicators
    {
        // Pick the native player indicator for human combat teams as well.
        // Only presentation changes; actual combat-team membership stays intact.
        private static bool Prefix(TeamComponent __instance, ref GameObject ___defaultIndicatorPrefab, out GameObject __state)
        {
            __state = ___defaultIndicatorPrefab;
            if (!Plugin.Active || !ArenaTeams.IsCombatantBody(__instance.body)) return true;
            var prefab = ArenaIndicatorAssets.PlayerIndicator;
            if (!prefab) return ArenaIndicatorAssets.Failed; // Await async; use native fallback after a load failure.
            ___defaultIndicatorPrefab = prefab;
            return true;
        }
        private static void Postfix(ref GameObject ___defaultIndicatorPrefab, GameObject __state)
        { ___defaultIndicatorPrefab = __state; }
    }

    [HarmonyPatch(typeof(Run), nameof(Run.BeginGameOver))]
    internal static class PreventTeamWipe
    { private static bool Prefix() { return !Plugin.Active; } }

    [HarmonyPatch(typeof(CombatDirector), "FixedUpdate")]
    internal static class StopCombatDirectors
    { private static bool Prefix() { return !Plugin.Active; } }

    [HarmonyPatch(typeof(DirectorCore), nameof(DirectorCore.TrySpawnObject))]
    internal static class StopEnemySpawnRequests
    {
        private static bool Prefix(DirectorSpawnRequest directorSpawnRequest, ref GameObject __result)
        {
            if (!Plugin.Active) return true;
            if (directorSpawnRequest.spawnCard is InteractableSpawnCard)
            {
                if (ArenaScrapper.Spawning && directorSpawnRequest.spawnCard == ArenaScrapper.SpawnCard) return true;
                __result = null; return false;
            }
            if (!(directorSpawnRequest.spawnCard is CharacterSpawnCard)) return true;
            var summoner = directorSpawnRequest.summonerBodyObject
                ? directorSpawnRequest.summonerBodyObject.GetComponent<CharacterBody>() : null;
            if (summoner && ArenaMatch.IsPlayerOrOwned(summoner.master)) return true;
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(TeleporterInteraction), nameof(TeleporterInteraction.OnInteractionBegin))]
    internal static class DisableTeleporter
    { private static bool Prefix() { return !Plugin.Active; } }

    [HarmonyPatch(typeof(PurchaseInteraction), nameof(PurchaseInteraction.OnInteractionBegin))]
    internal static class DisablePurchases
    { private static bool Prefix() { return !Plugin.Active; } }

    [HarmonyPatch(typeof(TeamManager), nameof(TeamManager.GiveTeamExperience))]
    internal static class DisableSharedExperience
    { private static bool Prefix(TeamIndex teamIndex) { return !Plugin.Active || !(teamIndex == TeamIndex.Player || ArenaTeams.IsArenaTeam(teamIndex)); } }

    [HarmonyPatch(typeof(CharacterMaster), "TryReviveOnBodyDeath")]
    internal static class ReplaceNativeRevives
    {
        private static bool Prefix(CharacterMaster __instance, ref bool __result)
        {
            if (!Plugin.Active || !__instance.playerCharacterMasterController) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(TeamManager), nameof(TeamManager.SetTeamLevel))]
    internal static class KeepBaseTeamLevelAtOne
    {
        private static void Prefix(TeamIndex teamIndex, ref uint newLevel)
        { if (Plugin.Active && (teamIndex == TeamIndex.Player || ArenaTeams.IsArenaTeam(teamIndex))) newLevel = 1; }
    }

    [HarmonyPatch(typeof(HealthComponent), "TakeDamageProcess")]
    internal static class ArenaDamage
    {
        private static System.Collections.Generic.IEnumerable<CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<CodeInstruction> instructions)
        { return ArenaBurstProtection.ProtectHealthWrites(instructions); }
        private static bool Prefix(HealthComponent __instance, DamageInfo damageInfo, out float __state)
        {
            __state = __instance.combinedHealth;
            var match = ArenaMatch.Instance;
            if (!Plugin.Active || !NetworkServer.active || !match || match.Rules == null || !__instance.body) return true;
            var victim = match.Find(ArenaMatch.PlayerOwner(__instance.body.master));
            if (victim == null) return true;
            if (match.Finished || match.WarmupRemaining > 0 || victim.ProtectionUntil > match.Clock)
            { damageInfo.rejected = true; return false; }
            if (match.Rules.NoFallDamage && (ulong)(damageInfo.damageType & DamageType.FallDamage) != 0)
            { damageInfo.rejected = true; return false; }
            if (!ArenaBurstProtection.Before(__instance, damageInfo)) return false;
            var attackerBody = damageInfo.attacker ? damageInfo.attacker.GetComponent<CharacterBody>() : null;
            var attacker = attackerBody ? match.Find(ArenaMatch.PlayerOwner(attackerBody.master)) : null;
            if (attacker != null)
            {
                if (attacker == victim) return true; // Preserve intentional native health costs; team checks already reject friendly attacks.
                if (!match.AreOpponents(attacker, victim)) { damageInfo.rejected = true; return false; }
                attacker.ProtectionUntil = match.Clock;
                damageInfo.damage *= match.Rules.DamageScale;
            }
            return true;
        }

        private static void Postfix(HealthComponent __instance, DamageInfo damageInfo, float __state)
        {
            var match = ArenaMatch.Instance;
            if (!Plugin.Active || !NetworkServer.active || !match || match.Rules == null || damageInfo.rejected
                || !__instance.body || __instance.combinedHealth >= __state) return;
            var victim = match.Find(ArenaMatch.PlayerOwner(__instance.body.master));
            var body = damageInfo.attacker ? damageInfo.attacker.GetComponent<CharacterBody>() : null;
            var attacker = body ? match.Find(ArenaMatch.PlayerOwner(body.master)) : null;
            if (match.AreOpponents(victim, attacker))
            { victim.LastAttacker = attacker.UserId; victim.LastHitTime = match.Clock; }
        }
    }

    [HarmonyPatch(typeof(PlayerCharacterMasterController), "PollInputToButtonBuffers")]
    internal static class BlockSkillsWhileChoosing
    {
        private static readonly System.Reflection.MethodInfo suppress = AccessTools.Method(typeof(PlayerCharacterMasterController), "SuppressAllButtonsUntilReleased");
        private static void Postfix(PlayerCharacterMasterController __instance)
        {
            // Keep native polling alive so releases during a menu are observed.
            // Then suppress actions, in the same order used by native UI focus.
            if (!Plugin.Active || !ArenaUI.Instance || !ArenaUI.Instance.IsOpenFor(__instance.networkUser)) return;
            if (suppress != null) suppress.Invoke(__instance, null);
        }
    }
}
