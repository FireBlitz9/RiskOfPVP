using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using RoR2.CharacterAI;
using UnityEngine;
using UnityEngine.Networking;

namespace ArenaPvP
{
    internal sealed class ArenaBots : MonoBehaviour
    {
        internal static ArenaBots Instance;
        private bool initialSpawned;
        private int startingRemaining;
        private float nextRoster, nextSpawn;
        private int nextBrain;
        private readonly System.Diagnostics.Stopwatch decisionBudget = new System.Diagnostics.Stopwatch();
        private readonly List<ArenaBotBrain> brains = new List<ArenaBotBrain>();
        private readonly List<BotTarget> targets = new List<BotTarget>();
        private readonly Dictionary<CharacterMaster,ArenaPlayer> owners = new Dictionary<CharacterMaster,ArenaPlayer>();
        private int choice;
        private sealed class BotSurvivor
        {
            internal string BodyName, DisplayNameToken, SortName;
            internal GameObject MasterPrefab;
        }
        private BotSurvivor[] survivors;
        internal string Status = "";
        private void Awake() { Instance = this; }
        private void OnDestroy() { Reset(); Instance = null; }
        internal void Reset() { initialSpawned = false; startingRemaining = 0; nextRoster = nextSpawn = 0; nextBrain = 0; brains.Clear(); targets.Clear(); owners.Clear(); ArenaBotControl.Clear(); ArenaBotBuilds.Reset(); Status = ""; }
        private BotSurvivor[] Survivors
        {
            get
            {
                if (survivors == null)
                {
                    var result = new List<BotSurvivor>();
                    for (int i = 0; i < SurvivorCatalog.survivorCount; i++)
                    {
                        var def = SurvivorCatalog.GetSurvivorDef((SurvivorIndex)i);
                        if (!def || !def.bodyPrefab) continue;
                        var body = def.bodyPrefab.GetComponent<CharacterBody>();
                        AddSurvivor(result, body, def.displayNameToken, def.cachedName);
                    }
                    // Heretic is a playable transformation outside character select.
                    var heretic = BodyCatalog.FindBodyPrefab("HereticBody");
                    if (heretic && !result.Any(s => s.BodyName == heretic.name))
                        AddSurvivor(result, heretic.GetComponent<CharacterBody>(), "HERETIC_BODY_NAME", "Heretic");
                    survivors = result.OrderBy(s => s.BodyName == "CommandoBody" ? 0 : 1).ThenBy(s => s.SortName).ToArray();
                }
                return survivors;
            }
        }
        private static void AddSurvivor(List<BotSurvivor> result, CharacterBody body, string token, string name)
        {
            if (!body) return;
            var prefab = MasterCatalog.GetMasterPrefab(MasterCatalog.FindAiMasterIndexForBody(body.bodyIndex));
            if (!prefab || !prefab.GetComponent<BaseAI>()) return;
            result.Add(new BotSurvivor { BodyName = body.gameObject.name, DisplayNameToken = token, SortName = name, MasterPrefab = prefab });
        }

        internal string SelectionName { get { return Survivors.Length == 0 ? "No registered survivor AI" : Language.GetString(Survivors[choice % Survivors.Length].DisplayNameToken); } }
        internal void NextSurvivor() { if (Survivors.Length > 0) choice = (choice + 1) % Survivors.Length; }
        internal void Tick(ArenaMatch match)
        {
            if (!NetworkServer.active || !Plugin.Active || match.Rules == null) return;
            if (!initialSpawned)
            {
                initialSpawned = true;
                startingRemaining = Math.Min(ArenaTeams.BotCapacity, match.Rules.StartingBots);
            }
            if (startingRemaining > 0 && !match.Finished && Time.unscaledTime >= nextSpawn)
            {
                nextSpawn = Time.unscaledTime + 0.15f;
                if (Add(true)) startingRemaining--; else startingRemaining = 0;
            }
            if (match.Finished || brains.Count == 0) return;
            float now = Time.unscaledTime;
            if (now >= nextRoster)
            {
                nextRoster = now + 0.25f; targets.Clear(); owners.Clear(); brains.RemoveAll(b => !b);
                foreach (var player in match.AllPlayers)
                {
                    if(player.Connected && player.Master) owners[player.Master]=player;
                    var body = player.Connected && !player.Dead && player.Master ? player.Master.GetBody() : null;
                    if (body && body.healthComponent && body.healthComponent.alive) targets.Add(new BotTarget { Player = player, Body = body });
                }
                if(match.Rules.BotIntelligence>=BotIntelligence.High)
                foreach(var master in CharacterMaster.readOnlyInstancesList)
                {
                    if(!master || owners.ContainsKey(master))continue;
                    var root=master.minionOwnership ? master.minionOwnership.ownerMaster : null;
                    ArenaPlayer owner=null;
                    for(int depth=0;root && depth<8;depth++)
                    { if(owners.TryGetValue(root,out owner))break;root=root.minionOwnership ? root.minionOwnership.ownerMaster : null; }
                    var body=owner!=null ? master.GetBody() : null;
                    if(body && body.healthComponent && body.healthComponent.alive && !ArenaWorldObjects.PreserveVehicle(body))
                        targets.Add(new BotTarget { Player=owner,Body=body,Summon=true });
                }
            }
            if (brains.Count == 0) return;
            // Round-robin fairness. Check between decisions: one native physics query
            // cannot be preempted, so this is a soft budget, not a frame-time promise.
            decisionBudget.Restart();
            int remaining = brains.Count;
            while (remaining-- > 0)
            {
                nextBrain %= brains.Count;
                var brain = brains[nextBrain++];
                if (brain)
                {
                    if(now>=brain.NextThink)brain.Think(match,targets,now);
                    var player=brain.Participant;
                    if(player!=null && player.Rewards.First!=null && (now>=brain.NextChoice || player.Rewards.ArrivalRevision!=brain.ChoiceArrival))
                    { brain.ChoiceArrival=player.Rewards.ArrivalRevision;brain.NextChoice=now+(match.SpendBotReward(player) ? .05f : .25f); }
                }
                if (decisionBudget.Elapsed.TotalMilliseconds >= 2) break;
            }
        }
        internal bool Add(bool starting = false)
        {
            var match = ArenaMatch.Instance;
            if (!Plugin.Active || !NetworkServer.active || !match || match.Rules == null || !SceneInfo.instance) return false;
            if (match.Finished) { Status = "Continue the match before spawning more bots."; return false; }
            if (Survivors.Length == 0) { Status = "No survivor has a registered AI master."; return false; }
            var bot = match.Bots.FirstOrDefault(p => p.BotRemoved);
            bool fresh = bot == null;
            if (fresh && match.Bots.Count >= ArenaTeams.BotCapacity) { Status = "Bot limit reached (" + ArenaTeams.BotCapacity + ")."; return false; }
            var def = Survivors[choice % Survivors.Length];
            GameObject staging = null, instance = null;
            try
            {
                if (fresh)
                {
                    bot = new ArenaPlayer { BotOrdinal = match.Bots.Count };
                    match.Bots.Add(bot);
                }
                // Register before activation/SpawnBody, so world cleanup and the
                // global body-start callback recognize this as a participant.
                staging = new GameObject("Arena bot staging"); staging.SetActive(false);
                instance = Instantiate(def.MasterPrefab, staging.transform, false);
                bot.BotMaster = instance.GetComponent<CharacterMaster>(); bot.BotRemoved = false; bot.Dead = false; bot.LastBody = 0;
                bot.InitialGranted = false; bot.Rewards.Clear(); bot.Level = match.Rules.Selection == ItemSelection.Random ? match.Cap : 1;
                bot.LateEntrant = !starting && match.Elapsed > 0; bot.CatchupRemaining = bot.CatchupTarget = 0; bot.CatchupPrepared = false; bot.BeadExperience.Clear();
                bot.BotBuild = BotBuildRules.ForBody(def.BodyName, bot.BotOrdinal, match.Rules.Mode == ArenaMode.KOTH); bot.BotRewards = 0;
                bot.BotMemory = new BotMemory();
                bot.Name = "Bot " + (bot.BotOrdinal + 1) + " - " + Language.GetString(def.DisplayNameToken);
                var brain = instance.AddComponent<ArenaBotBrain>();
                instance.transform.SetParent(null, true); instance.SetActive(true); Destroy(staging); staging = null;
                match.RefreshTeamAssignments(); NetworkServer.Spawn(instance); ArenaTeams.Ensure(bot.BotMaster);
                var points = match.SpawnPositions.ToArray();
                Vector3 position = points.Length > 0 ? points[UnityEngine.Random.Range(0, points.Length)] : bot.Master.CalculateSafeRespawnPosition();
                bot.BotMaster.Respawn(position, Quaternion.identity, true);
                brain.Initialize(bot, def.BodyName); brains.Add(brain);
                foreach (var item in new[] { RoR2Content.Items.UseAmbientLevel, RoR2Content.Items.InvadingDoppelganger })
                { int count = bot.Master.inventory.GetItemCountPermanent(item); if (count > 0) bot.Master.inventory.RemoveItemPermanent(item, count); }
                match.EnsureParticipant(bot); Status = "Spawned " + bot.Name + " (" + BotTacticsRules.Name((int)match.Rules.BotIntelligence) + ", "
                    + (BotTacticsRules.Adaptive(match.Rules.BotIntelligence) ? "adaptive build" : BotBuildRules.Name(bot.BotBuild)) + ").";
                Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = "ARENA_PVP_BOT_SPAWNED", paramTokens = new[] { ArenaMatch.SafeName(bot.Name) } });
                ArenaNetwork.SendSnapshots(); return true;
            }
            catch (Exception error)
            {
                if (bot != null) { bot.BotRemoved = true; bot.BotMaster = null; }
                match.RefreshTeamAssignments();
                if (instance) NetworkServer.Destroy(instance); Status = "Could not spawn this survivor AI. Try another survivor.";
                Plugin.Warn("Arena bot spawn failed: " + error); return false;
            }
            finally { if (staging) Destroy(staging); }
        }
        internal bool Remove()
        {
            var match = ArenaMatch.Instance;
            if (!Plugin.Active || !NetworkServer.active || !match || match.Rules == null) return false;
            var bot = match.Bots.LastOrDefault(p => p.Connected); if (bot == null) { Status = "No bots to remove."; return false; }
            Despawn(bot);
            Status = "Removed " + bot.Name + "."; ArenaNetwork.SendSnapshots(); return true;
        }
        internal static void Despawn(ArenaPlayer bot, bool rebalance = true)
        {
            var master = bot.BotMaster;
            var owned = new HashSet<CharacterMaster>(CharacterMaster.readOnlyInstancesList.Where(m => m && master && ArenaMatch.PlayerOwner(m) == master));
            bot.BotRemoved = true;
            foreach (var projectile in FindObjectsOfType<RoR2.Projectile.ProjectileController>())
            {
                var owner = projectile.owner ? projectile.owner.GetComponent<CharacterBody>() : null;
                if (owner && owned.Contains(owner.master))
                { if (projectile.GetComponent<NetworkIdentity>()) NetworkServer.Destroy(projectile.gameObject); else Destroy(projectile.gameObject); }
            }
            foreach (var member in owned) if (member) NetworkServer.Destroy(member.gameObject);
            bot.BotMaster = null;
            if (rebalance && ArenaMatch.Instance) ArenaMatch.Instance.RefreshTeamAssignments();
        }
    }
}
