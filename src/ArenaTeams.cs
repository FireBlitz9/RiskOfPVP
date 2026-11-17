using System;
using System.Collections.Generic;
using HarmonyLib;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ArenaPvP
{
    // One genuine combat team per player ownership tree. Native enemy masks,
    // hit filters, buff wards and AI can distinguish allies without FFA hacks.
    internal sealed class ArenaTeams : MonoBehaviour
    {
        private static readonly List<TeamIndex> slots = new List<TeamIndex>();
        private static readonly Dictionary<CharacterMaster, TeamIndex> originals = new Dictionary<CharacterMaster, TeamIndex>();
        private static TeamSlots<NetworkUserId> roster;
        private static Run boundRun;
        private static bool warnedCapacity;
        internal static int BotCapacity { get { return Math.Min(16, slots.Count / 4); } }

        internal static void Register()
        {
            var player = TeamCatalog.GetTeamDef(TeamIndex.Player);
            for (int i = 0; i < Math.Min(64, TeamsAPI.ModdedTeamCountLimit); i++)
            {
                var team = TeamsAPI.RegisterTeam(new TeamDef
                {
                    nameToken = "ARENA_PVP_NAME", softCharacterLimit = int.MaxValue,
                    friendlyFireScaling = 0, levelUpEffect = player.levelUpEffect, levelUpSound = player.levelUpSound
                }, new TeamsAPI.TeamBehavior("RiskOfArenaPlayer" + i, TeamsAPI.TeamClassification.Player));
                if (team == TeamIndex.None) break;
                slots.Add(team);
            }
            if (slots.Count == 0) throw new InvalidOperationException("No custom combat-team slots are available.");
            roster = new TeamSlots<NetworkUserId>(slots.Count - BotCapacity);
            Plugin.Log("RiskOfArena registered " + slots.Count + " isolated player teams.");
        }

        internal static bool IsArenaTeam(TeamIndex team) { return slots.Contains(team); }
        internal static void ForRun(Run run)
        {
            if (boundRun == run) return;
            Restore(); boundRun = run;
        }
        internal static void Ensure(CharacterMaster master)
        {
            if (!Plugin.Active || !NetworkServer.active || !master || roster == null) return;
            ForRun(Run.instance);
            var owner = ArenaMatch.PlayerOwner(master);
            var match = ArenaMatch.Instance;
            var participant = match ? match.Find(owner) : null;
            var user = owner && owner.playerCharacterMasterController ? owner.playerCharacterMasterController.networkUser : null;
            if (!user && (participant == null || !participant.IsBot)) return;
            int index = match && match.Rules != null && match.Rules.Mode != ArenaMode.FFA ? participant != null ? participant.Side : match.SideFor(user)
                : participant != null && participant.IsBot ? slots.Count - 1 - participant.BotOrdinal : roster.GetOrAssign(user.id);
            if (index < 0)
            {
                if (!warnedCapacity) { Plugin.Warn("Arena player-team capacity reached. Return to the lobby before adding more new player identities."); warnedCapacity = true; }
                return;
            }
            if (!originals.ContainsKey(master)) originals.Add(master, master.teamIndex);
            TeamIndex team = slots[index]; if (master.teamIndex != team) master.teamIndex = team;
            var body = master.GetBody(); if (body && body.teamComponent && body.teamComponent.teamIndex != team) body.teamComponent.teamIndex = team;
            // Zero is the pre-Start value. R2API attaches its replication helper
            // in TeamManager.Start, which then initializes team XP/level itself.
            // Writing here earlier produces unsynchronizable dirty-bit errors.
            if (TeamManager.instance && TeamManager.instance.GetTeamLevel(team) > 1) TeamManager.instance.SetTeamLevel(team, 1);
        }
        private void FixedUpdate()
        {
            if (!Plugin.Active || !NetworkServer.active) return;
            foreach (var master in CharacterMaster.readOnlyInstancesList) if (master && ArenaMatch.IsPlayerOrOwned(master)) Ensure(master);
        }
        internal static int SideOf(CharacterMaster master)
        {
            if (!Plugin.Active || !master) return -1;
            var view = ArenaUI.Instance ? ArenaUI.Instance.CurrentMatch : null;
            bool teams = NetworkServer.active && ArenaMatch.Instance && ArenaMatch.Instance.Rules != null
                ? ArenaMatch.Instance.Rules.Mode != ArenaMode.FFA : view != null && view.Mode != (int)ArenaMode.FFA;
            if (!teams) return -1;
            int side = slots.IndexOf(master.teamIndex); return side == 0 || side == 1 ? side : -1;
        }
        internal static Color SideColor(int side) { return side == 0 ? new Color(0.9f, 0.22f, 0.28f) : new Color(0.22f, 0.57f, 0.95f); }
        internal static bool IsCombatantBody(CharacterBody body)
        { return body && body.master && (body.master.playerCharacterMasterController || IsArenaTeam(body.master.teamIndex)
            && (!body.master.minionOwnership || !body.master.minionOwnership.ownerMaster)); }
        internal static void Restore()
        {
            if (NetworkServer.active)
                foreach (var entry in originals)
                    if (entry.Key)
                    {
                        entry.Key.teamIndex = entry.Value;
                        var body = entry.Key.GetBody(); if (body && body.teamComponent) body.teamComponent.teamIndex = entry.Value;
                    }
            originals.Clear(); if (roster != null) roster.Clear(); boundRun = null; warnedCapacity = false;
        }
        private void OnDestroy() { Restore(); }
    }

    [HarmonyPatch(typeof(MinionOwnership), nameof(MinionOwnership.SetOwner))]
    internal static class InheritArenaOwnerTeam
    {
        private static void Postfix(MinionOwnership __instance)
        { if (Plugin.Active && NetworkServer.active) ArenaTeams.Ensure(__instance.GetComponent<CharacterMaster>()); }
    }
}
