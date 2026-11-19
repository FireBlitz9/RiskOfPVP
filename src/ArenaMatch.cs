using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using RoR2.Navigation;
using UnityEngine;
using UnityEngine.Networking;

namespace ArenaPvP
{
    internal sealed class ArenaPlayer
    {
        internal NetworkUser User;
        internal CharacterMaster BotMaster;
        internal int BotOrdinal = -1;
        internal bool BotRemoved;
        internal BotBuildKind BotBuild;
        internal int BotRewards;
        internal bool IsBot { get { return BotOrdinal >= 0; } }
        internal string Name;
        internal int Kills, Deaths, Level = 1;
        internal int Side = -1;
        internal readonly BurstGuard Burst = new BurstGuard();
        internal bool ClearDots;
        internal bool InitialGranted, Dead, ChoiceRequired, ChoiceComplete;
        internal bool LateEntrant;
        internal bool CatchupPrepared;
        internal int CatchupRemaining, CatchupTarget;
        internal int CatchupOutstanding { get { return CatchupRules.Total(CatchupRemaining, Rewards.PendingCatchup + Rewards.ReturnedCatchup); } }
        internal int PendingRewards { get { return CatchupRules.Total(Rewards.Count, CatchupRules.Total(CatchupRemaining, Rewards.ReturnedCatchup)); } }
        internal float RespawnAt, ProtectionUntil, LastHitTime = -100f, LastRequest = -100f;
        internal uint LastAttacker;
        internal int LastBody;
        internal readonly RewardQueue Rewards = new RewardQueue();
        internal readonly LevelExperienceQueue BeadExperience = new LevelExperienceQueue();
        internal uint UserId { get { return IsBot ? 0xF0000000u + (uint)BotOrdinal : User ? User.netId.Value : 0; } }
        internal CharacterMaster Master { get { return IsBot ? BotMaster : User ? User.master : null; } }
        internal string SurvivorKey { get { var master = Master; return master && master.bodyPrefab ? master.bodyPrefab.name : ""; } }
        internal bool Connected { get { return IsBot ? !BotRemoved && BotMaster : ArenaNetwork.IsUserPresent(User) && User.isParticipating; } }
    }

    internal sealed class ArenaMatch : MonoBehaviour
    {
        internal static ArenaMatch Instance;
        internal MatchSettings Rules;
        internal readonly Dictionary<NetworkUserId, ArenaPlayer> Players = new Dictionary<NetworkUserId, ArenaPlayer>();
        internal readonly List<ArenaPlayer> Bots = new List<ArenaPlayer>();
        internal IEnumerable<ArenaPlayer> AllPlayers { get { return Players.Values.Concat(Bots); } }
        internal float Clock, Elapsed, WarmupRemaining, NextReward, TimeLimit;
        internal bool Finished, ReturningToLobby, Restarting;
        internal string Result = "";
        internal readonly int[] TeamKills = new int[2];
        internal readonly ArenaHill Hill = new ArenaHill();
        internal readonly ContinueBallot<NetworkUserId> ContinueVotes = new ContinueBallot<NetworkUserId>();
        private readonly BalancedTeams<uint> teamRoster = new BalancedTeams<uint>();
        private readonly HashSet<uint> balancedMembers = new HashSet<uint>();
        private int nextCatchup;
        internal IEnumerable<Vector3> SpawnPositions { get { return spawnPositions; } }
        internal int Cap { get { return Rules == null ? 1 : Rules.Selection == ItemSelection.Random
            ? ArenaRules.RandomLevel(Elapsed, Rules.RandomLevelSeconds, Rules.MaximumLevel, Rules.RandomAutoLevel)
            : ArenaRules.LevelCap(Elapsed, Rules.InitialCap, Rules.CapInterval, Rules.CapStep, Rules.MaximumLevel); } }
        private ItemPool pool;
        private uint offerSequence = 0x52410000;
        private float nextSnapshot;
        private bool stageStarted;
        private bool warnedEmptyPool;
        private uint itemPolicyRevision;
        private readonly List<Vector3> spawnPositions = new List<Vector3>();

        private void Awake() { Instance = this; Stage.onServerStageBegin += OnStage; }
        private void OnDestroy() { Stage.onServerStageBegin -= OnStage; Instance = null; }

        internal void Begin(MatchSettings settings)
        {
            var timing = System.Diagnostics.Stopwatch.StartNew();
            End(); Rules = settings; pool = new ItemPool(Run.instance, settings);
            if (settings.Mode != ArenaMode.FFA)
            {
                var rng = new System.Random(unchecked((int)Run.instance.seed ^ 0x5445414D));
                teamRoster.Balance(NetworkUser.readOnlyInstancesList.Where(u => ArenaNetwork.IsUserPresent(u) && u.isParticipating)
                    .OrderBy(u => rng.Next()).Select(u => u.netId.Value).ToArray());
            }
            WarmupRemaining = settings.WarmupSeconds; NextReward = settings.ItemInterval; TimeLimit = settings.MatchMinutes * 60f;
            if (TeamManager.instance) TeamManager.instance.SetTeamLevel(TeamIndex.Player, 1);
            EnsurePlayers();
            Plugin.Log("Arena started. Host mode=" + settings.Selection + "; weights W/G/R/V/L/E/B/S=" + settings.WeightedEssences.Describe()
                + "; start/death/kill/timer amounts=" + settings.StartingRewards + "/" + settings.DeathRewards + "/" + settings.KillRewards + "/" + settings.TimedRewards + ".");
            Plugin.Log("Arena " + settings.Mode + " host initialization: " + timing.Elapsed.TotalMilliseconds.ToString("0.0") + " ms.");
        }

        internal void End()
        {
            foreach (var bot in Bots) if (NetworkServer.active && bot.BotMaster) ArenaBots.Despawn(bot, false);
            Bots.Clear(); if (ArenaBots.Instance) ArenaBots.Instance.Reset();
            Players.Clear(); spawnPositions.Clear(); Rules = null; pool = null;
            balancedMembers.Clear(); teamRoster.Clear(); nextCatchup = 0;
            ContinueVotes.Close();
            Restarting = false;
            Clock = Elapsed = nextSnapshot = TimeLimit = 0; Finished = ReturningToLobby = stageStarted = warnedEmptyPool = false; itemPolicyRevision = 0;
            Result = "";
            teamRoster.Clear(); TeamKills[0] = TeamKills[1] = 0; Hill.Reset();
        }

        private void OnStage(Stage stage)
        {
            if (!Plugin.Active || !NetworkServer.active || Rules == null) return;
            stageStarted = true; BuildSpawnPositions();
        }

        private void BuildSpawnPositions()
        {
            spawnPositions.Clear();
            var graph = SceneInfo.instance ? SceneInfo.instance.groundNodes : null;
            if (graph != null)
            {
                var nodes = graph.GetActiveNodesForHullMaskWithFlagConditions(HullMask.Golem, NodeFlags.None, NodeFlags.NoCharacterSpawn);
                // Sampling graph nodes avoids respawning everybody at the original pod cluster.
                int stride = Math.Max(1, nodes.Count / 160);
                for (int i = 0; i < nodes.Count; i += stride)
                {
                    Vector3 point;
                    if (!graph.GetNodePosition(nodes[i], out point)) continue;
                    if (PlayerSpawnInhibitor.readOnlyInstancesList.Any(x => x && x.IsInhibiting(graph, nodes[i]))) continue;
                    spawnPositions.Add(point);
                }
            }
            if (spawnPositions.Count == 0)
                foreach (var point in SpawnPoint.readOnlyInstancesList) if (point) spawnPositions.Add(point.transform.position);
        }

        private void EnsurePlayers()
        {
            foreach (var user in NetworkUser.readOnlyInstancesList)
            {
                if (!ArenaNetwork.IsUserPresent(user) || !user.isParticipating || !user.master) continue;
                ArenaPlayer player;
                if (!Players.TryGetValue(user.id, out player))
                {
                    player = new ArenaPlayer { LateEntrant = stageStarted && WarmupRemaining <= 0 }; Players.Add(user.id, player);
                }
                player.User = user;
                player.Name = user.userName;
            }
            RefreshTeamAssignments();
            foreach (var player in AllPlayers.Where(p => p.Connected).ToArray())
            { ArenaTeams.Ensure(player.Master); EnsureParticipant(player); }
        }

        internal void RefreshTeamAssignments()
        {
            if (!Plugin.Active || !NetworkServer.active || Rules == null) return;
            var active = AllPlayers.Where(p => p.Connected).OrderBy(p => p.UserId).ToArray();
            if (Rules.Mode == ArenaMode.FFA) { foreach (var p in active) p.Side = -1; return; }
            if (balancedMembers.Count != active.Length || active.Any(p => !balancedMembers.Contains(p.UserId)))
            {
                teamRoster.Balance(active.Select(p => p.UserId).ToArray());
                balancedMembers.Clear(); foreach (var p in active) balancedMembers.Add(p.UserId);
            }
            foreach (var p in active) { p.Side = teamRoster.Side(p.UserId); ArenaTeams.Ensure(p.Master); }
        }

        private static int HeldItems(ArenaPlayer player)
        {
            var inventory = player.Master ? player.Master.inventory : null;
            if (!inventory) return 0;
            long count = 0;
            foreach (var index in inventory.itemAcquisitionOrder)
            {
                var def = ItemCatalog.GetItemDef(index);
                if (def && !def.hidden && def.tier != ItemTier.NoTier && def != RoR2Content.Items.LevelBonus && def != RoR2Content.Items.UseAmbientLevel)
                    count += inventory.GetItemCountPermanent(index);
            }
            for (uint slot = 0; slot < inventory.equipmentSlotCount; slot++)
                for (uint set = 0; set < inventory.equipmentSetCount; set++)
                { var location = new EquipmentLocation { slot = slot, set = set }; if (inventory.GetEquipment(in location).equipmentIndex != EquipmentIndex.None) count++; }
            return (int)Math.Min(int.MaxValue, count);
        }

        private int LeastEstablishedItems(ArenaPlayer joining)
        {
            var others = AllPlayers.Where(p => p != joining && p.Connected && p.InitialGranted && p.Master).ToArray();
            return others.Length == 0 ? 0 : others.Min(p => CatchupRules.Total(HeldItems(p), p.CatchupOutstanding));
        }

        internal void RegisterDropInUser(NetworkUser user)
        {
            if (!Plugin.Active || !NetworkServer.active || Rules == null || !ArenaNetwork.IsUserPresent(user) || !user.master) return;
            EnsurePlayers(); ArenaPlayer player;
            if (Players.TryGetValue(user.id, out player)) PrepareCatchup(player);
            ArenaNetwork.SendSnapshots();
        }

        private void PrepareCatchup(ArenaPlayer player)
        {
            if (!player.LateEntrant || player.CatchupPrepared) return;
            // DropIn finishes native survivor loadout items after SpawnBody;
            // settle the deficit once before issuing any Arena credit.
            player.CatchupRemaining = CatchupRules.Credits(player.CatchupTarget, HeldItems(player));
            player.CatchupPrepared = true;
        }

        private void PumpCatchup()
        {
            var pending = AllPlayers.Where(p => p.Connected && (p.CatchupRemaining > 0 || p.Rewards.ReturnedCatchup > 0)).ToArray();
            if (pending.Length == 0 || Finished) return;
            var budget = System.Diagnostics.Stopwatch.StartNew();
            int remaining = pending.Length;
            while (remaining-- > 0)
            {
                nextCatchup %= pending.Length; var player = pending[nextCatchup++];
                PrepareCatchup(player);
                player.CatchupRemaining = CatchupRules.Total(player.CatchupRemaining, player.Rewards.TakeReturnedCatchup());
                if (player.CatchupRemaining > 0 && player.Master && player.Master.GetBody()
                    && (Rules.Selection == ItemSelection.Random || player.IsBot || player.Rewards.Count < RewardQueue.MaximumPending))
                    if (GiveReward(player, false, "late join", true)) player.CatchupRemaining--;
                if (budget.Elapsed.TotalMilliseconds >= 2) break;
            }
        }

        internal void EnsureParticipant(ArenaPlayer player)
        {
                if (!player.Master) return;
                player.Master.destroyOnBodyDeath = false;
                player.Master.preventGameOver = true;
                if (!player.InitialGranted)
                {
                    int least = player.LateEntrant ? LeastEstablishedItems(player) : 0;
                    player.InitialGranted = true;
                    if (Rules.Selection == ItemSelection.Random) player.Level = Cap;
                    ApplyLevel(player);
                    if (player.LateEntrant)
                    {
                        player.CatchupTarget = least;
                        player.CatchupRemaining = CatchupRules.Credits(least, HeldItems(player));
                        Plugin.Log("Arena late arrival " + SafeName(player.Name) + ": " + player.CatchupRemaining + " catch-up rewards (lowest item count " + least + ").");
                    }
                    else if (Rules.OnStart) for (int i = 0; i < Rules.StartingRewards; i++) GiveReward(player, false, "start");
                }
                var body = player.Master.GetBody();
                if (body && body.healthComponent.alive && player.LastBody != body.GetInstanceID()) BodySpawned(body);
                if (player.InitialGranted && player.LastBody != 0 && !player.Dead && (!body || !body.healthComponent || !body.healthComponent.alive))
                {
                    // Some out-of-bounds/body-destruction paths bypass the global
                    // damage report. Recover once, without generating farmable rewards.
                    player.Dead = true; player.Deaths++; player.ChoiceRequired = false; player.ChoiceComplete = true;
                    player.RespawnAt = Clock + Rules.RespawnSeconds;
                    Plugin.Log("Arena recovered a missing/dead body for " + SafeName(player.Name) + "; timed respawn queued without rewards.");
                }
                if (Rules.Selection == ItemSelection.Random && player.Level != Cap) { player.Level = Cap; ApplyLevel(player); }
                if (body && (player.ClearDots || player.Burst.Protected(Clock) || player.Burst.Until > Clock - Time.fixedDeltaTime))
                { player.ClearDots = false; DotController.RemoveAllDots(body.gameObject); }
        }

        private void FixedUpdate()
        {
            if (!Plugin.Active || !NetworkServer.active || Rules == null) return;
            RefreshItemRules();
            EnsurePlayers();
            if (Finished) RefreshContinueVotes();
            if (!stageStarted && Stage.instance && SceneInfo.instance && AllPlayers.Any(p => p.Master && p.Master.GetBody()))
            { stageStarted = true; BuildSpawnPositions(); }
            if (!stageStarted || !AllPlayers.Any(p => p.Connected)) return;
            if (ArenaBots.Instance) ArenaBots.Instance.Tick(this);
            PumpCatchup();
            Clock += Time.fixedDeltaTime;
            if (!Finished)
            {
                if (WarmupRemaining > 0) WarmupRemaining = Math.Max(0f, WarmupRemaining - Time.fixedDeltaTime);
                else
                {
                    Elapsed += Time.fixedDeltaTime;
                    if (Rules.Mode == ArenaMode.KOTH) Hill.Tick(this, Time.fixedDeltaTime, true);
                    if (Rules.OnTimer && Elapsed >= NextReward)
                    {
                        NextReward = Elapsed + Rules.ItemInterval;
                        foreach (var p in AllPlayers.Where(p => p.Connected))
                            for (int i = 0; i < Rules.TimedRewards; i++) GiveReward(p, false, "timer");
                    }
                    if (TimeLimit > 0 && Elapsed >= TimeLimit) Finish();
                }
                if (WarmupRemaining > 0 && Rules.Mode == ArenaMode.KOTH) Hill.Tick(this, 0f, false);
            }
            foreach (var player in AllPlayers.Where(p => p.Connected && p.Dead).ToArray())
            {
                if (Clock >= nextSnapshot && player.ChoiceRequired && !player.ChoiceComplete && player.Rewards.First != null
                    && AvailableChoices(player, player.Rewards.First.Items).Length == 0 && !(Rules.AllowLevel && player.Level < Cap))
                    player.ChoiceRequired = false;
                if (Finished || ArenaRules.CanRespawn(Rules.Respawn, player.ChoiceRequired, player.ChoiceComplete, Clock, player.RespawnAt))
                    Respawn(player);
            }
            if (Clock >= nextSnapshot)
            { nextSnapshot = Clock + 0.25f; ArenaNetwork.SendSnapshots(); }
        }

        internal ArenaPlayer Find(CharacterMaster master)
        {
            if (!master) return null;
            var bot = Bots.FirstOrDefault(p => p.BotMaster == master); if (bot != null) return bot;
            var controller = master.playerCharacterMasterController;
            if (!controller || !controller.networkUser) return null;
            ArenaPlayer p;
            return Players.TryGetValue(controller.networkUser.id, out p) ? p : null;
        }

        internal int SideFor(NetworkUser user) { return user && Rules != null && Rules.Mode != ArenaMode.FFA ? teamRoster.Side(user.netId.Value) : -1; }
        internal bool AreOpponents(ArenaPlayer a, ArenaPlayer b)
        { return a != null && b != null && a != b && (Rules.Mode == ArenaMode.FFA || a.Side >= 0 && b.Side >= 0 && a.Side != b.Side); }

        internal static bool IsPlayerOrOwned(CharacterMaster master)
        { return PlayerOwner(master); }

        internal static CharacterMaster PlayerOwner(CharacterMaster master)
        {
            for (int depth = 0; master && depth < 8; depth++)
            {
                if (master.playerCharacterMasterController || Instance && Instance.Bots.Any(p => !p.BotRemoved && p.BotMaster == master)) return master;
                master = master.minionOwnership ? master.minionOwnership.ownerMaster : null;
            }
            return null;
        }

        internal void BodySpawned(CharacterBody body)
        {
            if (Rules == null) return;
            if (body.master && (body.master.playerCharacterMasterController || Bots.Any(p => p.BotMaster == body.master)))
            { body.master.destroyOnBodyDeath = false; body.master.preventGameOver = true; }
            var player = Find(body.master);
            if (player == null) { EnsurePlayers(); player = Find(body.master); }
            if (player == null || player.LastBody == body.GetInstanceID()) return;
            player.LastBody = body.GetInstanceID(); player.Dead = false;
            player.Burst.Reset(); player.ClearDots = false;
            player.ChoiceRequired = false; player.ChoiceComplete = false;
            player.ProtectionUntil = Clock + Rules.ProtectionSeconds;
            player.LastAttacker = 0; player.LastHitTime = -100f;
            ApplyLevel(player);
            ArenaBeadExperience.Flush(player);
        }

        internal void PlayerDied(DamageReport report)
        {
            if (Rules == null) return;
            ArenaPlayer victim = Find(report.victimMaster);
            if (victim == null || victim.Dead || victim.BotRemoved) return;
            if (Finished || WarmupRemaining > 0)
            {
                victim.Dead = true; victim.ChoiceRequired = false;
                victim.RespawnAt = Clock + 1f;
                AnnounceDeath(victim, null);
                return;
            }
            victim.Dead = true; victim.Deaths++;
            victim.RespawnAt = Clock + Rules.RespawnSeconds;
            victim.ChoiceComplete = false;
            bool voidDeath = (ulong)(report.damageInfo.damageType & (DamageType.OutOfBounds | DamageType.VoidDeath)) != 0;
            var killer = Find(PlayerOwner(report.attackerMaster));
            if (killer == null && Clock - victim.LastHitTime <= 10f)
                killer = AllPlayers.FirstOrDefault(p => p.UserId == victim.LastAttacker && p.Connected);
            if (!AreOpponents(killer, victim)) killer = null;
            if (killer != null)
            {
                killer.Kills++;
                if (killer.Side >= 0) TeamKills[killer.Side]++;
                var killerBody = killer.Master ? killer.Master.GetBody() : null;
                if (killerBody && killerBody.healthComponent.alive && Rules.HealOnKill > 0)
                    killerBody.healthComponent.Heal(killerBody.healthComponent.fullHealth * Rules.HealOnKill, default(ProcChainMask));
            }
            var plan = DeathRewardRules.Create(voidDeath, Rules.Selection, Rules.Respawn, Rules.OnDeath, Rules.OnKill,
                Rules.Catchup && AllPlayers.Max(p => p.Kills) - victim.Kills >= 3, Rules.DeathRewards, Rules.KillRewards);
            victim.ChoiceRequired = plan.RequiresChoice;
            if (Rules.Respawn == RespawnRule.AfterChoice && victim.ChoiceRequired) victim.RespawnAt = Clock + 1f;
            if (killer != null) for (int i = 0; i < plan.KillerRewards; i++) GiveReward(killer, false, "kill");
            AnnounceDeath(victim, killer);
            for (int i = 0; i < plan.VictimRewards; i++)
                if (!GiveReward(victim, i == 0 && victim.ChoiceRequired, "death") && i == 0) victim.ChoiceRequired = false;
            for (int i = 0; i < plan.CatchupRewards; i++) GiveReward(victim, false, "underdog");
            if (killer != null && Rules.Mode != ArenaMode.KOTH && Rules.KillLimit > 0
                && (Rules.Mode == ArenaMode.Teams ? TeamKills[killer.Side] : killer.Kills) >= Rules.KillLimit) Finish();
            ArenaNetwork.SendSnapshots();
        }

        private static void AnnounceDeath(ArenaPlayer victim, ArenaPlayer killer)
        {
            Chat.SendBroadcastChat(new Chat.SimpleChatMessage {
                baseToken = killer == null ? "ARENA_PVP_DIED" : "ARENA_PVP_DEFEATED",
                paramTokens = killer == null ? new[] { SafeName(victim.Name) } : new[] { SafeName(victim.Name), SafeName(killer.Name) }
            });
        }

        internal bool GiveReward(ArenaPlayer player, bool gatesRespawn, string source = "event", bool catchup = false)
        {
            if (!player.Master || pool == null || Finished) return false;
            RefreshItemRules();
            if (Rules.Selection == ItemSelection.Random)
            {
                int item = pool.Pick(inventory: player.Master.inventory, survivor: player.SurvivorKey);
                if (item < 0) { WarnEmpty(); return false; }
                return GrantPickup(player, item);
            }
            int[] choices = Rules.Selection == ItemSelection.Command ? pool.All
                : Rules.Selection == ItemSelection.Weighted ? pool.Weighted(player.Master.inventory, player.SurvivorKey) : pool.DraftByEssence(Rules.DraftSize, player.Master.inventory, player.SurvivorKey);
            var available = pool.Available(choices, player.Master.inventory, player.SurvivorKey);
            if (available.Length == 0 && !(Rules.AllowLevel && player.Level < Cap)) { WarnEmpty(); return false; }
            if (player.IsBot)
            {
                // Roll on the host now; score/grant later inside the common bot
                // budget. A timer burst cannot run every adaptive build at once.
                return player.Rewards.Enqueue(new RewardOffer { Id=++offerSequence,Items=choices,GatesRespawn=gatesRespawn,Catchup=catchup });
            }
            uint offerId = ++offerSequence;
            bool queued = player.Rewards.Enqueue(new RewardOffer { Id = offerId, Items = choices, GatesRespawn = gatesRespawn, Catchup = catchup });
            if (queued && Rules.Selection == ItemSelection.Weighted)
                Plugin.Log("Weighted reward " + offerId + ": " + SafeName(player.Name) + ", source=" + source
                    + ", essence=" + (choices.Length > 0 ? ItemPool.Category(choices[0]).ToString() : "XP only")
                    + ", configured W/G/R/V/L/E/B/S=" + Rules.WeightedEssences.Describe() + ", banked=" + player.Rewards.Count + ".");
            return queued;
        }

        internal bool SpendBotReward(ArenaPlayer player)
        {
            if(!Plugin.Active || !NetworkServer.active || Finished || Rules==null || pool==null || !player.IsBot || !player.Connected || !player.Master)return false;
            var offer=player.Rewards.First;if(offer==null)return false;
            var choices=AvailableChoices(player,offer.Items);
            bool mayLevel=Rules.AllowLevel && player.Level<Cap;
            int item=BotBuildRules.WantsLevel(player.BotRewards,player.Level,Cap,mayLevel) ? -1 : ArenaBotBuilds.Choose(player,choices,Rules.Selection);
            if(item<0 && !mayLevel)
            {
                // Keep the credit, let other banked classes be considered, and
                // never trap a dead bot behind an impossible AI choice.
                if(player.Dead && player.ChoiceRequired)player.ChoiceComplete=true;
                offer.GatesRespawn=false;player.Rewards.DeferBotOffer(offer.Id);return false;
            }
            RewardOffer used;
            if(!player.Rewards.TryConsume(offer.Id,item,mayLevel,out used,ItemPool.ExactTier,id=>pool.CanGrant(id,player.Master.inventory,-1,player.SurvivorKey)))return false;
            if(item<0) { player.Level++;player.BotRewards++;ApplyLevel(player); }
            else GrantPickup(player,item);
            if(used.GatesRespawn)player.ChoiceComplete=true;
            return true;
        }

        internal bool GiveScrapReward(ArenaPlayer player, int tier)
        {
            if (ScrapCapacity(player, tier, 1) == 0) return false;
            if (!ScrapRewardRules.PreservesTier(Rules.Selection)) return GiveReward(player, false, "scrapper");
            int[] choices = pool.ScrapChoices(Rules.Selection, tier, Rules.DraftSize, player.Master.inventory, player.SurvivorKey);
            if (choices.Length == 0) return false;
            if (Rules.Selection == ItemSelection.Random)
            { return GrantPickup(player, choices[0]); }
            return player.Rewards.Enqueue(new RewardOffer { Id = ++offerSequence, Items = choices, ScrapTier = (int)tier });
        }

        internal int ScrapCapacity(ArenaPlayer player, int tier, int requested, int releasing = -1)
        {
            if (!Plugin.Active || !NetworkServer.active || Rules == null || !Rules.EnableScrapper || pool == null || Finished
                || player == null || !player.Connected || player.Dead || !player.Master) return 0;
            RefreshItemRules();
            return ArenaRules.ScrapBatchLimit(requested, player.Rewards.Count, Rules.Selection == ItemSelection.Random,
                pool.CanScrap(Rules.Selection, tier, Rules.AllowLevel && player.Level < Cap, player.Master.inventory, releasing, player.SurvivorKey));
        }

        private void WarnEmpty()
        {
            if (warnedEmptyPool) return;
            warnedEmptyPool = true;
            Plugin.Warn("No valid reward is available with this pool/cap. Reward skipped and any required death choice waived to avoid trapping a dead player.");
        }

        internal bool Choose(NetworkUser user, uint offerId, int selection)
        {
            if (!user || Rules == null || Finished) return false;
            RefreshItemRules();
            ArenaPlayer p;
            if (!Players.TryGetValue(user.id, out p) || !p.Connected || !p.Master || Clock - p.LastRequest < 0.08f) return false;
            p.LastRequest = Clock;
            bool mayLevel = Rules.AllowLevel && p.Level < Cap;
            RewardOffer used;
            if (!p.Rewards.TryConsume(offerId, selection, mayLevel, out used, ItemPool.ExactTier, pickup => pool.CanGrant(pickup, p.Master.inventory, -1, p.SurvivorKey))) return false;
            if (selection == -1) { p.Level++; ApplyLevel(p); }
            else GrantPickup(p, selection);
            if (used.GatesRespawn) p.ChoiceComplete = true;
            ArenaNetwork.SendSnapshots();
            return true;
        }

        private void ApplyLevel(ArenaPlayer player)
        {
            if (!player.Master || !player.Master.inventory) return;
            var inventory = player.Master.inventory;
            // Native per-inventory levels: replicated by RoR2 and preserved through respawns.
            int current = inventory.GetItemCountPermanent(RoR2Content.Items.LevelBonus);
            int desired = Math.Max(0, Math.Min(player.Level, Cap) - 1);
            if (current < desired)
            {
                inventory.GiveItemPermanent(RoR2Content.Items.LevelBonus, desired - current);
                ArenaBeadExperience.LevelsGained(player, current + 1, desired + 1);
            }
            else if (current > desired) inventory.RemoveItemPermanent(RoR2Content.Items.LevelBonus, current - desired);
        }

        internal void RefreshItemRules()
        {
            if (!Plugin.Active || !NetworkServer.active || Rules == null || pool == null) return;
            if (itemPolicyRevision == Plugin.Settings.MatchRevision) return;
            itemPolicyRevision = Plugin.Settings.MatchRevision;
            Rules.RefreshCombat(Plugin.Settings);
            if (!Rules.AntiOneShot) foreach (var player in AllPlayers) player.Burst.Reset();
            if (Rules.ProtectionSeconds <= 0f) foreach (var player in AllPlayers) player.ProtectionUntil = 0f;
            string combined = Plugin.Settings.GlobalBlacklist.Value;
            if (!pool.RefreshRestrictions(combined, Plugin.Settings.SurvivorBlacklists.Value)) return;
            Rules.GlobalBlacklist = combined; Rules.SurvivorBlacklists = Plugin.Settings.SurvivorBlacklists.Value;
            Plugin.Log("Applied " + Rules.Mode + " host item/equipment restrictions to active rewards.");
        }
        internal int[] AvailableChoices(ArenaPlayer player, int[] choices)
        { RefreshItemRules(); return player.Master && pool != null ? pool.Available(choices, player.Master.inventory, player.SurvivorKey) : new int[0]; }

        private bool GrantPickup(ArenaPlayer player, int pickup)
        {
            var definition = PickupCatalog.GetPickupDef(new PickupIndex(pickup));
            if (definition == null || !player.Master || !player.Master.inventory || !pool.CanGrant(pickup, player.Master.inventory, -1, player.SurvivorKey)) return false;
            var inventory = player.Master.inventory;
            if (definition.itemIndex != ItemIndex.None) inventory.GiveItemPermanent(definition.itemIndex);
            else if (definition.equipmentIndex != EquipmentIndex.None)
            {
                ArenaEquipment.EnsureCapacity(player.Master);
                var location = ArenaEquipment.GrantLocation(inventory);
                inventory.SetEquipmentIndex(definition.equipmentIndex, in location);
            }
            GenericPickupController.SendPickupMessage(player.Master, new UniquePickup(new PickupIndex(pickup)));
            if (player.IsBot) player.BotRewards++;
            return true;
        }

        private void Respawn(ArenaPlayer player)
        {
            if (!player.Master || !SceneInfo.instance) return;
            var existing = player.Master.GetBody();
            if (existing && existing.healthComponent.alive) { player.Dead = false; return; }
            Vector3 best = player.Master.CalculateSafeRespawnPosition();
            float bestDistance = -1f;
            var living = AllPlayers.Where(p => AreOpponents(player, p) && p.Connected && p.Master)
                .Select(p => p.Master.GetBody()).Where(b => b && b.healthComponent.alive).ToArray();
            foreach (var candidate in spawnPositions)
            {
                float closest = living.Length == 0 ? UnityEngine.Random.value * 100f
                    : living.Min(b => (b.footPosition - candidate).sqrMagnitude);
                if (closest > bestDistance) { bestDistance = closest; best = candidate; }
            }
            var spawned = player.Master.Respawn(best, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f), true);
            if (spawned) BodySpawned(spawned);
            else player.RespawnAt = Clock + 1f;
        }

        internal void Finish()
        {
            if (Finished) return;
            Finished = true;
            var connected = AllPlayers.Where(p => p.Connected).ToArray();
            int best = connected.Length == 0 ? 0 : connected.Max(p => p.Kills);
            var winners = connected.Where(p => p.Kills == best).Select(p => SafeName(p.Name));
            Result = string.Join(" & ", winners.ToArray()) + " won with " + best + " kills!";
            if (Rules.Mode != ArenaMode.FFA)
            {
                float red = Rules.Mode == ArenaMode.Teams ? TeamKills[0] : Hill.Score.Captures[0] + Hill.Score.Charge[0];
                float blue = Rules.Mode == ArenaMode.Teams ? TeamKills[1] : Hill.Score.Captures[1] + Hill.Score.Charge[1];
                Result = Mathf.Abs(red - blue) < 0.0001f ? "Red and Blue tied!" : (red > blue ? "Red" : "Blue") + " team won!";
            }
            foreach (var p in AllPlayers) p.ChoiceRequired = false;
            ContinueVotes.Begin(EligibleContinueVoters());
            Chat.AddMessage("<color=#44ded1>ARENA</color> " + Result);
            ArenaNetwork.SendSnapshots();
        }

        private IEnumerable<NetworkUserId> EligibleContinueVoters()
        { return Players.Where(pair => pair.Value.Connected).Select(pair => pair.Key); }

        private void RefreshContinueVotes()
        {
            if (!Finished || ReturningToLobby) return;
            ContinueVotes.Refresh(EligibleContinueVoters());
            ApplyEndVote();
        }

        internal bool VoteContinue(NetworkUser user, uint round, bool support, bool playAgain = false)
        {
            if (!Plugin.Active || !NetworkServer.active || !Finished || ReturningToLobby || Rules == null
                || !ArenaNetwork.IsUserPresent(user)) return false;
            ArenaPlayer player;
            if (!Players.TryGetValue(user.id, out player) || player.User != user || !player.Connected) return false;
            ContinueVotes.Refresh(EligibleContinueVoters());
            if (!ContinueVotes.SetVote(user.id, round, support, playAgain)) return false;
            if (!ApplyEndVote()) ArenaNetwork.SendSnapshots();
            return true;
        }

        private bool ApplyEndVote()
        { return ContinueVotes.RestartPassed ? PlayAgain() : ContinueGame(); }

        private bool PlayAgain()
        {
            if (!Plugin.Active || !NetworkServer.active || !Finished || ReturningToLobby || Rules == null || !ContinueVotes.RestartPassed
                || !ArenaReplay.Instance || !ArenaReplay.Instance.Prepare(Run.instance, Rules.Mode)) return false;
            ReturningToLobby = Restarting = true; ContinueVotes.Close(); ArenaNetwork.SendSnapshots();
            NetworkSession.instance.EndRun(); return true;
        }

        private bool ContinueGame()
        {
            if (!Plugin.Active || !NetworkServer.active || !Finished || ReturningToLobby || Rules == null || !ContinueVotes.Passed) return false;
            int leader = Rules.Mode == ArenaMode.Teams ? TeamKills.Max() : !AllPlayers.Any() ? 0 : AllPlayers.Max(p => p.Kills);
            if (Rules.Mode == ArenaMode.KOTH) Hill.Target = HillScore.ContinuedTarget(Hill.Target, Hill.Score.Captures[0], Hill.Score.Captures[1], Rules.FocusCaptures);
            else Rules.KillLimit = ArenaRules.ContinuedKillTarget(Rules.KillLimit, leader);
            TimeLimit = ArenaRules.ContinuedDeadline(Elapsed, Rules.MatchMinutes * 60f);
            Finished = false; Result = "";
            ContinueVotes.Close();
            foreach (var player in AllPlayers)
            {
                player.ChoiceRequired = false; player.ChoiceComplete = true;
                if (player.Dead) player.RespawnAt = Clock;
                player.ProtectionUntil = Clock + Rules.ProtectionSeconds;
            }
            Chat.AddMessage("<color=#44ded1>RiskOfArena</color> Continuing: first to " + (Rules.Mode == ArenaMode.KOTH ? Hill.Target + " full focus charges." : Rules.KillLimit + " kills."));
            ArenaNetwork.SendSnapshots(); return true;
        }

        internal bool BackToLobby(NetworkUser user)
        {
            if (!Plugin.Active || !ArenaNetwork.IsHostUser(user) || !Finished || ReturningToLobby) return false;
            ReturningToLobby = true;
            ContinueVotes.Close(); ArenaNetwork.SendSnapshots();
            // Native Run.OnDestroy calls HandlePostRunDestination, which sends
            // the entire connected session to character selection ("lobby").
            // This bypasses the Arena-only SceneExitController progression guard.
            if (NetworkSession.instance) NetworkSession.instance.EndRun();
            else if (Run.instance) Destroy(Run.instance.gameObject);
            return true;
        }

        internal static string SafeName(string name)
        { return (name ?? "Survivor").Replace("<", "").Replace(">", "").Replace("\n", " ").Replace("\r", " "); }
    }
}
