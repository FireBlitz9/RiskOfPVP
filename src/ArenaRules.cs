using System;
using System.Collections.Generic;

namespace ArenaPvP
{
    public enum ItemSelection { Random, Draft, Command, Weighted }
    public enum EssenceKind { White, Green, Red, Lunar, Void, Equipment, Boss, Expansion }
    public enum RespawnRule { Timer, AfterChoice, TimerAndChoice }

    internal static class CatchupRules
    {
        internal static int Credits(int leastItems, int held)
        { return (int)Math.Max(0, Math.Min(int.MaxValue, (long)leastItems - Math.Max(0, held))); }
        internal static int Total(int held, int outstanding)
        { return (int)Math.Min(int.MaxValue, (long)Math.Max(0, held) + Math.Max(0, outstanding)); }
    }

    internal sealed class ChoiceListVersion
    {
        private int[] rendered;
        internal bool Update(int[] choices)
        {
            choices = choices ?? new int[0];
            bool same = rendered != null && rendered.Length == choices.Length;
            if (same) for (int i = 0; i < choices.Length; i++) if (rendered[i] != choices[i]) { same = false; break; }
            if (same) return false;
            rendered = (int[])choices.Clone(); return true;
        }
    }

    internal sealed class LevelExperienceQueue
    {
        private readonly Queue<ulong> pending = new Queue<ulong>();
        internal int Count { get { return pending.Count; } }
        internal void Clear() { pending.Clear(); }
        internal void Record(int previous, int current, bool heldItem, Func<uint, ulong> threshold)
        {
            if (!heldItem) return;
            for (int level = Math.Max(1, previous); level < current; level++)
            {
                ulong before = threshold((uint)level), after = threshold((uint)level + 1);
                if (after > before) pending.Enqueue(after - before);
            }
        }
        internal void Drain(bool bodyReady, Action<ulong> award)
        {
            if (!bodyReady) return;
            // Consume before the native call: a partially applied native callback
            // must never be retried and duplicate experience after an exception.
            while (pending.Count > 0) award(pending.Dequeue());
        }
    }

    internal static class ReprieveBarrierRules
    {
        // Native AddBarrier still clamps the grant. Change only the entry basis;
        // keep the serialized coefficient, Chakra contribution and decay native.
        internal static float HealthBasis(bool arena, float health, float shield)
        { return arena ? health + shield : health; }
    }

    // Server owns membership and round IDs. A closed/old ballot cannot resume
    // the next match; disconnects remove both eligibility and any stored vote.
    internal sealed class ContinueBallot<T>
    {
        private readonly HashSet<T> eligible = new HashSet<T>();
        private readonly HashSet<T> votes = new HashSet<T>();
        private readonly HashSet<T> restartVotes = new HashSet<T>();
        internal uint Round { get; private set; }
        internal bool Open { get; private set; }
        internal int EligibleCount { get { return eligible.Count; } }
        internal int VoteCount { get { return votes.Count; } }
        internal int RestartVoteCount { get { return restartVotes.Count; } }
        internal int Required { get { return eligible.Count == 0 ? 0 : eligible.Count / 2 + 1; } }
        internal bool Passed { get { return Open && eligible.Count > 0 && votes.Count >= Required; } }
        internal bool RestartPassed { get { return Open && eligible.Count > 0 && restartVotes.Count >= Required; } }
        internal void Begin(IEnumerable<T> players)
        {
            Close(); unchecked { Round++; if (Round == 0) Round++; }
            Open = true; Refresh(players);
        }
        internal void Refresh(IEnumerable<T> players)
        {
            eligible.Clear();
            if (Open) foreach (T player in players) eligible.Add(player);
            votes.RemoveWhere(v => !eligible.Contains(v));
            restartVotes.RemoveWhere(v => !eligible.Contains(v));
        }
        internal bool CanVote(T player) { return Open && eligible.Contains(player); }
        internal bool HasVoted(T player) { return Open && votes.Contains(player); }
        internal bool HasRestartVoted(T player) { return Open && restartVotes.Contains(player); }
        internal bool SetVote(T player, uint round, bool support, bool restart = false)
        {
            if (!Open || round != Round || !eligible.Contains(player)) return false;
            var target = restart ? restartVotes : votes;
            var other = restart ? votes : restartVotes;
            bool changed = support ? target.Add(player) : target.Remove(player);
            if (support) changed = other.Remove(player) || changed;
            return changed;
        }
        internal void Close() { Open = false; eligible.Clear(); votes.Clear(); restartVotes.Clear(); }
    }
    public enum ArenaMode { FFA, Teams, KOTH }
    public static class WorldCleanupRules
    {
        // Non-interactive seats are internal movement actors, including parked
        // masterless dash bodies. They are not world loot or director enemies.
        public static bool PreserveVehicle(bool seat, bool internalActor, bool flightActor, bool playerPassenger)
        { return seat && (internalActor || flightActor || playerPassenger); }
    }
    public sealed class RewardMenuArrival
    {
        private uint offer, revision;
        private bool waiting, enabled;
        public bool Requested { get; private set; }
        public void Reset() { offer = revision = 0; waiting = enabled = Requested = false; }
        public void Dismiss() { Requested = false; }
        public void Observe(uint currentOffer, uint arrivalRevision, bool required, bool warmup, bool autoOpen, bool finished)
        {
            bool headChanged = currentOffer != offer, arrived = arrivalRevision != revision;
            if (finished || currentOffer == 0) Requested = false;
            else if (required && (!waiting || headChanged) || (autoOpen || warmup) && (headChanged || arrived || autoOpen && !enabled)) Requested = true;
            offer = currentOffer; revision = arrivalRevision; waiting = required; enabled = autoOpen;
        }
        public bool TakeRequest(bool modalBlocked)
        { if (!Requested || modalBlocked) return false; Requested = false; return true; }
    }
    public static class SettingsLayout
    {
        public const string General = "General", Controls = "Controls";
        public static string Profile(ArenaMode mode)
        {
            switch (mode) { case ArenaMode.FFA: return "FFA"; case ArenaMode.Teams: return "Teams"; case ArenaMode.KOTH: return "KOTH"; default: throw new ArgumentOutOfRangeException("mode"); }
        }
        public static string LegacyProfile(ArenaMode mode)
        { return mode == ArenaMode.FFA ? "02 FFA" : mode == ArenaMode.Teams ? "03 Teams" : "04 KOTH"; }
        public static string Key(string section, string name) { return section + " / " + name; }
        public static string Section(string key) { int at = key.IndexOf(" / ", StringComparison.Ordinal); return at < 0 ? "" : key.Substring(0, at); }
        public static string Title(string key) { int at = key.IndexOf(" / ", StringComparison.Ordinal); return at < 0 ? key : key.Substring(at + 3); }
    }
    public static class SelectionNames
    { public static string Normalize(string name) { return string.Equals(name, "WeightedEssence", StringComparison.OrdinalIgnoreCase) ? "Weighted" : name; } }
    public sealed class BlacklistSelection
    {
        private readonly Dictionary<string, int> entries = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public BlacklistSelection(string saved)
        {
            foreach (string part in (saved ?? "").Split(','))
            {
                string name = part.Trim(); if (name.Length == 0) continue;
                int split = name.LastIndexOf('='), limit;
                if (split > 0 && int.TryParse(name.Substring(split + 1), out limit) && limit > 0)
                    entries[name.Substring(0, split).Trim()] = limit;
                else entries[name] = 0; // Retain unknown/temporarily absent mod names.
            }
        }
        public int Limit(string name) { int value; return entries.TryGetValue(name, out value) ? value : -1; }
        public IEnumerable<KeyValuePair<string, int>> Entries { get { return entries; } }
        public void Set(string name, int limit)
        { if (limit < 0) entries.Remove(name); else entries[name] = limit; }
        public bool Contains(string name) { return Limit(name) == 0; }
        public void Toggle(string name) { if (Contains(name)) entries.Remove(name); else entries[name] = 0; }
        public void Adjust(string name, int change)
        {
            int current = Limit(name);
            long next = (long)Math.Max(0, current) + change;
            if (next <= 0) entries.Remove(name); else entries[name] = (int)Math.Min(int.MaxValue, next);
        }
        public bool CanGrant(string name, long owned)
        { int limit = Limit(name); return limit < 0 || Math.Max(0, owned) < limit; }
        public string Serialize()
        {
            var sorted = new List<string>(entries.Keys); sorted.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", sorted.ConvertAll(name => entries[name] > 0 ? name + "=" + entries[name] : name).ToArray());
        }
    }
    public static class MinorPickupRules
    {
        public static bool CanCollect(bool owned, int ownerTeam, int filterTeam, int collectorTeam, bool vanillaPlayerFilter, bool arenaCollector)
        { return arenaCollector && (owned ? ownerTeam == collectorTeam : vanillaPlayerFilter || filterTeam == collectorTeam); }
    }

    public sealed class BurstGuard
    {
        private bool armed;
        public double Until { get; private set; }
        public bool Protected(double now) { return now < Until; }
        public void Reset() { armed = false; Until = 0; }
        public void Observe(double now, float fraction) { if (!Protected(now) && fraction >= 0.9f) armed = true; }
        public float Clamp(double now, float proposed, float current, float maximum, float seconds, out bool triggered)
        {
            triggered = false;
            if (!armed && !Protected(now)) return proposed;
            float floor = Math.Min(current, Math.Max(1, maximum * 0.1f));
            if (proposed >= floor) return proposed;
            if (armed) { armed = false; Until = now + seconds; triggered = true; }
            return floor;
        }
    }

    public sealed class BalancedTeams<T>
    {
        private readonly Dictionary<T, int> teams = new Dictionary<T, int>();
        public int Side(T id) { int side; return teams.TryGetValue(id, out side) ? side : -1; }
        public void Clear() { teams.Clear(); }
        public void Balance(T[] present)
        {
            AssignPresent(present);
            int red = 0, blue = 0;
            foreach (T id in present) { int side = Side(id); if (side == 0) red++; else if (side == 1) blue++; }
            for (int i = present.Length - 1; i >= 0 && Math.Abs(red - blue) > 1; i--)
            {
                int larger = red > blue ? 0 : 1;
                if (Side(present[i]) != larger) continue;
                teams[present[i]] = 1 - larger;
                if (larger == 0) { red--; blue++; } else { blue--; red++; }
            }
        }
        public void AssignPresent(T[] present, int redOffset = 0, int blueOffset = 0)
        {
            int red = redOffset, blue = blueOffset;
            foreach (T id in present) { int side = Side(id); if (side == 0) red++; else if (side == 1) blue++; }
            foreach (T id in present) if (Side(id) < 0)
            { int side = red <= blue ? 0 : 1; teams[id] = side; if (side == 0) red++; else blue++; }
        }
    }

    public sealed class HillScore
    {
        public readonly int[] Captures = new int[2];
        public readonly float[] Charge = new float[2];
        public int Tick(int red, int blue, float delta, float seconds)
        {
            if (red > 0 && blue > 0 || red <= 0 && blue <= 0 || delta <= 0 || seconds <= 0) return -1;
            int side = red > 0 ? 0 : 1;
            Charge[side] = Math.Min(1f, Charge[side] + delta / seconds);
            if (Charge[side] < 1f) return -1;
            // A relocation must not erase the other team's earned progress.
            Captures[side]++; Charge[side] = 0;
            return side;
        }
        public float Progress(int side, int target)
        { return side < 0 || side > 1 || target <= 0 ? 0 : Math.Max(0, Math.Min(1, (Captures[side] + Charge[side]) / target)); }
        public static int ContinuedTarget(int target, int red, int blue, int increment)
        { return (int)Math.Min(int.MaxValue, (long)Math.Max(target, Math.Max(red, blue)) + Math.Max(1, increment)); }
    }

    // Complete two fresh press/release cycles while the picker is open.
    // A key held before opening is ignored until it is released.
    internal sealed class DoubleTapRelease
    {
        private const double WindowSeconds = 0.6;
        private bool active, wasHeld, awaitingRelease, firstTap;
        private double firstReleasedAt;

        internal bool Update(bool menuOpen, bool held, double now)
        {
            if (!menuOpen) { Reset(); return false; }
            if (!active) { active = true; wasHeld = held; return false; }
            if (firstTap && now - firstReleasedAt > WindowSeconds) firstTap = false;
            bool pressed = held && !wasHeld, released = !held && wasHeld;
            wasHeld = held;
            if (pressed) awaitingRelease = true;
            if (!released || !awaitingRelease) return false;
            awaitingRelease = false;
            if (firstTap) { Reset(); return true; }
            firstTap = true; firstReleasedAt = now;
            return false;
        }

        internal void Reset()
        { active = wasHeld = awaitingRelease = firstTap = false; firstReleasedAt = 0; }
    }

    public struct DeathRewardPlan
    {
        public int VictimRewards, KillerRewards, CatchupRewards;
        public bool RequiresChoice;
    }
    public static class DeathRewardRules
    {
        public static DeathRewardPlan Create(bool voidDeath, ItemSelection selection, RespawnRule respawn,
            bool onDeath, bool onKill, bool catchup, int deathAmount, int killAmount)
        {
            // This applies to every source of extra rewards from the death,
            // including the mandatory choice used by choice-gated respawns.
            if (voidDeath) return new DeathRewardPlan();
            int victimRewards = onDeath ? Math.Max(0, deathAmount) : 0;
            bool choice = victimRewards > 0 && selection != ItemSelection.Random && respawn != RespawnRule.Timer;
            return new DeathRewardPlan { RequiresChoice = choice,
                VictimRewards = victimRewards,
                KillerRewards = onKill ? Math.Max(0, killAmount) : 0, CatchupRewards = onDeath && victimRewards > 0 && catchup ? 1 : 0 };
        }
    }

    // Slots remain reserved through death/disconnect. Old projectiles cannot
    // become allied to an unrelated late joiner by recycling a departed slot.
    public sealed class TeamSlots<T>
    {
        private readonly Dictionary<T, int> assigned = new Dictionary<T, int>();
        private readonly int capacity;
        public TeamSlots(int capacity) { this.capacity = Math.Max(0, capacity); }
        public int GetOrAssign(T player)
        {
            int value; if (assigned.TryGetValue(player, out value)) return value;
            if (assigned.Count >= capacity) return -1;
            value = assigned.Count; assigned.Add(player, value); return value;
        }
        public void Clear() { assigned.Clear(); }
    }

    // Immutable run settings: editing sliders cannot change a queued offer or
    // another reward within the current run's captured host rules.
    public sealed class EssenceWeights
    {
        public static readonly EssenceWeights Default = new EssenceWeights(55, 25, 10, 3, 2, 5);
        private readonly int white, green, red, @void, lunar, equipment, boss, special;
        public EssenceWeights(int white, int green, int red, int @void, int lunar, int equipment = 0, int boss = 0, int special = 0)
        {
            this.white = Clamp(white); this.green = Clamp(green); this.red = Clamp(red);
            this.@void = Clamp(@void); this.lunar = Clamp(lunar);
            this.equipment = Clamp(equipment); this.boss = Clamp(boss); this.special = Clamp(special);
        }
        private static int Clamp(int value) { return Math.Max(0, Math.Min(100, value)); }
        public string Describe() { return white + "/" + green + "/" + red + "/" + @void + "/" + lunar + "/" + equipment + "/" + boss + "/" + special; }
        public int Weight(EssenceKind kind)
        {
            switch (kind)
            {
                case EssenceKind.White: return white;
                case EssenceKind.Green: return green;
                case EssenceKind.Red: return red;
                case EssenceKind.Void: return @void;
                case EssenceKind.Lunar: return lunar;
                case EssenceKind.Equipment: return equipment;
                case EssenceKind.Boss: return boss;
                case EssenceKind.Expansion: return special;
                default: return 0;
            }
        }
    }
    public static class EssenceRollRules
    {
        public static EssenceKind? Choose(EssenceKind[] available, Func<int, int> roll)
        { return Choose(available, EssenceWeights.Default, roll); }
        public static EssenceKind? Choose(EssenceKind[] available, EssenceWeights weights, Func<int, int> roll)
        {
            var order = new[] { EssenceKind.White, EssenceKind.Green, EssenceKind.Red, EssenceKind.Void, EssenceKind.Lunar, EssenceKind.Equipment, EssenceKind.Boss, EssenceKind.Expansion };
            int total = 0; foreach (var kind in order) if (Array.IndexOf(available, kind) >= 0) total += weights.Weight(kind);
            if (total == 0) return null;
            int ticket = roll(total);
            if (ticket < 0 || ticket >= total) throw new ArgumentOutOfRangeException("roll");
            foreach (var kind in order) if (Array.IndexOf(available, kind) >= 0)
            { ticket -= weights.Weight(kind); if (ticket < 0) return kind; }
            return null;
        }
    }

    public static class ScrapRewardRules
    {
        public const int EquipmentTier = int.MaxValue, LunarEquipmentTier = int.MaxValue - 1;
        public static bool PreservesTier(ItemSelection mode) { return mode != ItemSelection.Command; }

        public static int[] Choices(ItemSelection mode, int[] eligible, int sourceTier, int draftSize,
            Func<int, int> tierOf, Func<int, int> roll)
        {
            if (!PreservesTier(mode)) return (int[])eligible.Clone();
            if (sourceTier < 0) return new int[0];
            var candidates = new List<int>();
            var seen = new HashSet<int>();
            foreach (int pickup in eligible)
                if (tierOf(pickup) == sourceTier && seen.Add(pickup)) candidates.Add(pickup);
            // Recycling preserves an existing tier; fresh-drop weights never apply.
            if (mode == ItemSelection.Weighted) return candidates.ToArray();
            int count = mode == ItemSelection.Random ? 1 : Math.Max(0, draftSize);
            var choices = new List<int>();
            while (choices.Count < count && candidates.Count > 0)
            {
                int index = roll(candidates.Count);
                if (index < 0 || index >= candidates.Count) throw new ArgumentOutOfRangeException("roll");
                choices.Add(candidates[index]);
                candidates.RemoveAt(index);
            }
            return choices.ToArray();
        }
    }

    public static class MapVotingRules
    {
        public static int Preview(int[] eligible, int[] ballots, int defaultMap)
        {
            if (eligible.Length == 0) return -1;
            var order = (int[])eligible.Clone();
            int index = Array.IndexOf(order, defaultMap);
            if (index > 0) { int first = order[0]; order[0] = order[index]; order[index] = first; }
            // A stable tie-break keeps the cover shown in the lobby equal to the launched map.
            // With no votes, all maps tie at zero and the host's allowed default wins.
            return Select(order, ballots, n => 0);
        }

        public static int Select(int[] eligible, int[] ballots, Func<int, int> chooseTie)
        {
            if (eligible.Length == 0) return -1;
            var counts = new Dictionary<int, int>();
            foreach (int map in eligible) counts[map] = 0;
            foreach (int ballot in ballots) if (counts.ContainsKey(ballot)) counts[ballot]++;
            int best = -1;
            var leaders = new List<int>();
            foreach (int map in eligible)
            {
                if (counts[map] > best) { best = counts[map]; leaders.Clear(); }
                if (counts[map] == best) leaders.Add(map);
            }
            return leaders[chooseTie(leaders.Count)];
        }
    }

    // Kept independent of Unity so the rules can be exercised without launching RoR2.
    public static class ArenaRules
    {
        public static int RandomLevel(float elapsed, float interval, int maximum, bool enabled)
        { return enabled ? LevelCap(elapsed, 1, interval, 1, maximum) : 1; }
        public static bool IsUserPresent(bool serverActive, bool isLocalUser, bool hasClientConnection)
        { return hasClientConnection || serverActive && isLocalUser; }

        public static bool IsMatchSettingGroup(string section)
        { return section == SettingsLayout.General || section == "FFA" || section == "Teams" || section == "KOTH"; }

        public static int ScrapBatchLimit(int requested, int pending, bool automatic, bool rewardAvailable)
        {
            if (!rewardAvailable) return 0;
            int available = automatic ? Math.Max(0, requested) : Math.Max(0, RewardQueue.MaximumPending - Math.Max(0, pending));
            return Math.Min(Math.Max(0, requested), available);
        }

        public static int ContinuedKillTarget(int currentTarget, int leaderKills)
        { return (int)Math.Min(int.MaxValue, (long)Math.Max(0, Math.Max(currentTarget, leaderKills)) + 20L); }
        public static float ContinuedDeadline(float elapsed, float duration)
        { return duration > 0 ? Math.Max(0, elapsed) + duration : 0; }

        public static int LevelCap(double elapsed, int initial, double interval, int step, int maximum)
        {
            initial = Math.Max(1, Math.Min(initial, maximum));
            if (interval <= 0 || double.IsNaN(elapsed)) return initial;
            double growth = Math.Floor(Math.Max(0, elapsed) / interval) * Math.Max(0, step);
            return (int)Math.Min(maximum, initial + growth);
        }

        public static bool CanRespawn(RespawnRule rule, bool choiceRequired, bool choiceComplete,
            double now, double readyAt)
        {
            bool timerDone = now >= readyAt;
            if (!choiceRequired || rule == RespawnRule.Timer) return timerDone;
            // The caller supplies a one-second body-cleanup deadline for AfterChoice.
            return choiceComplete && timerDone;
        }
    }

    public sealed class RewardOffer
    {
        public uint Id;
        public int[] Items = new int[0];
        public bool GatesRespawn;
        public bool Catchup;
        // Exact ItemTier, including individual void/custom tiers. -1 is unrestricted.
        public int ScrapTier = -1;
    }

    public sealed class RewardQueue
    {
        public const int MaximumPending = 12;
        private readonly List<RewardOffer> offers = new List<RewardOffer>();
        public int Count { get { return offers.Count; } }
        public RewardOffer First { get { return Count == 0 ? null : offers[0]; } }
        public uint ArrivalRevision { get; private set; }
        public int ReturnedCatchup { get; private set; }
        public int PendingCatchup { get { return offers.FindAll(o => o.Catchup).Count; } }
        public int TakeReturnedCatchup() { int value = ReturnedCatchup; ReturnedCatchup = 0; return value; }
        public void Clear() { offers.Clear(); ArrivalRevision = 0; ReturnedCatchup = 0; }

        public bool Enqueue(RewardOffer offer)
        {
            if (offer.GatesRespawn)
            {
                // A death choice goes first, even if timer rewards already fill the queue.
                if (Count >= MaximumPending)
                { if (offers[Count - 1].Catchup) ReturnedCatchup++; offers.RemoveAt(Count - 1); }
                offers.Insert(0, offer);
                AdvanceArrival();
                return true;
            }
            if (Count >= MaximumPending) return false;
            offers.Add(offer);
            AdvanceArrival();
            return true;
        }
        private void AdvanceArrival() { ArrivalRevision++; if (ArrivalRevision == 0) ArrivalRevision = 1; }

        public bool TryConsume(uint id, int selection, bool mayLevel, out RewardOffer consumed, Func<int, int> tierOf = null, Func<int, bool> canGrant = null)
        {
            consumed = null;
            RewardOffer first = First;
            if (first == null || first.Id != id) return false;
            bool valid = selection == -1 ? mayLevel : Array.IndexOf(first.Items, selection) >= 0;
            if (valid && selection != -1 && canGrant != null) valid = canGrant(selection);
            if (valid && selection != -1 && first.ScrapTier >= 0)
                valid = tierOf != null && tierOf(selection) == first.ScrapTier;
            if (!valid) return false;
            consumed = first;
            offers.RemoveAt(0);
            return true;
        }
    }
}
