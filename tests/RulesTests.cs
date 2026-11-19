using System;
using System.Collections.Generic;
using ArenaPvP;

internal static class RulesTests
{
    private static int assertions;
    private static void Check(bool condition, string name)
    { assertions++; if (!condition) throw new Exception("FAILED: " + name); }

    private static void ScrapperRewards()
    {
        // Synthetic catalog: 1/2/3 are normal tiers, 5/6/7/8 are distinct
        // void tiers, 30/31 are custom tiers; 999 is an unclassified pickup.
        Func<int, int> tierOf = p => p == 999 ? -1 : p / 100;
        int[] eligible = { 101, 102, 103, 104, 105, 201, 202, 301, 401, 501, 601, 701, 801, 3001, 3101, 999, 101 };
        Func<int, int> noRoll = n => { throw new Exception("Recycling a weighted essence must not roll a new rarity."); };
        var white = ScrapRewardRules.Choices(ItemSelection.Weighted, eligible, 1, 3, tierOf, noRoll);
        Check(white.Length == 5 && Array.IndexOf(white, 301) < 0 && Array.IndexOf(white, 999) < 0, "a recycled white essence offers only unique eligible white items, without a rarity roll");
        Check(ScrapRewardRules.Choices(ItemSelection.Command, eligible, 1, 3, tierOf, noRoll).Length == eligible.Length,
            "Command recycling retains its unrestricted item and equipment choices");
        Check(!ScrapRewardRules.PreservesTier(ItemSelection.Command), "Command is explicitly exempt from scrapper tier locking");
        foreach (ItemSelection mode in new[] { ItemSelection.Weighted, ItemSelection.Draft, ItemSelection.Random })
        {
            Check(ScrapRewardRules.PreservesTier(mode), "scrapper tier preservation is mandatory in " + mode);
            foreach (int tier in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 30, 31 })
            {
                var picks = ScrapRewardRules.Choices(mode, eligible, tier, 3, tierOf, n => n - 1);
                bool sameTier = picks.Length > 0;
                foreach (int pickup in picks) sameTier &= tierOf(pickup) == tier;
                Check(sameTier, mode + " keeps exact tier " + tier + ", including void subtiers and modded tiers");
            }
            Check(ScrapRewardRules.Choices(mode, eligible, 99, 3, tierOf, noRoll).Length == 0,
                mode + " refuses unavailable tiers instead of falling back to red, equipment or XP");
            Check(ScrapRewardRules.Choices(mode, eligible, -1, 3, tierOf, noRoll).Length == 0,
                mode + " cannot manufacture a reward for an unclassified pickup");
            Check(ScrapRewardRules.Choices(mode, new int[0], 1, 3, tierOf, noRoll).Length == 0,
                mode + " respects an empty or host-banned source-tier pool");
        }
        var draft = ScrapRewardRules.Choices(ItemSelection.Draft, eligible, 1, 3, tierOf, n => n - 1);
        Check(draft.Length == 3 && draft[0] != draft[1] && draft[1] != draft[2] && draft[0] != draft[2],
            "recycled drafts respect host draft size and never repeat a choice");
        Check(ScrapRewardRules.Choices(ItemSelection.Draft, eligible, 2, 12, tierOf, n => 0).Length == 2,
            "a small source-tier pool cannot fill a draft with higher-tier items");
        bool randomStaysWhite = true;
        for (int ticket = 0; ticket < 5; ticket++)
        {
            int chosen = ticket;
            var pick = ScrapRewardRules.Choices(ItemSelection.Random, eligible, 1, 3, tierOf, n => chosen);
            randomStaysWhite &= pick.Length == 1 && tierOf(pick[0]) == 1;
        }
        Check(randomStaysWhite, "every random recycling ticket returns exactly one same-tier item");
        Check(ArenaRules.ScrapBatchLimit(10, 0, false, ScrapRewardRules.Choices(ItemSelection.Weighted,
            eligible, 99, 3, tierOf, noRoll).Length > 0) == 0, "an unavailable exact tier prevents inventory removal at preflight");

        var bank = new RewardQueue();
        RewardOffer used;
        bank.Enqueue(new RewardOffer { Id = 1, Items = new[] { 301 } });
        bank.Enqueue(new RewardOffer { Id = 2, Items = white, ScrapTier = 1 });
        bank.Enqueue(new RewardOffer { Id = 3, Items = new[] { 201, 202 }, ScrapTier = 2 });
        Check(bank.First.ScrapTier == -1, "a previously banked normal reward stays separate from later recycled credit");
        Check(bank.TryConsume(1, 301, false, out used, tierOf) && used.ScrapTier == -1,
            "normal rewards keep their original rarity and queue order");
        Check(!bank.TryConsume(2, 301, false, out used, tierOf) && bank.Count == 2 && bank.First.ScrapTier == 1,
            "the host rejects a red claim against white credit without spending it");
        Check(!bank.TryConsume(2, 101, false, out used), "locked rewards fail closed if the tier validator is missing");
        Check(!bank.TryConsume(2, -1, false, out used, tierOf) && bank.First.Id == 2,
            "capped XP requests cannot consume recycled credit");
        Check(bank.TryConsume(2, -1, true, out used, tierOf) && used.ScrapTier == 1,
            "personal XP remains an allowed substitution under the host's level cap");
        Check(bank.First.ScrapTier == 2 && !bank.TryConsume(3, 101, true, out used, tierOf),
            "mixed-tier credits retain independent constraints after another credit is consumed");
        Check(bank.TryConsume(3, 202, false, out used, tierOf) && bank.Count == 0,
            "a matching-tier choice consumes exactly its own recycled reward");
        Check(!bank.TryConsume(3, 202, false, out used, tierOf), "recycled reward replay cannot grant another item");
        bank.Enqueue(new RewardOffer { Id = 4, Items = new[] { 101, 301, 999 }, ScrapTier = 1 });
        Check(!bank.TryConsume(4, 301, false, out used, tierOf) && !bank.TryConsume(4, 999, false, out used, tierOf),
            "exact-tier validation rejects off-tier entries even in an accidentally mixed offer");
        Check(bank.TryConsume(4, 101, false, out used, tierOf), "a valid same-tier claim still succeeds after rejected claims");
    }

    private static void DeferredAeroltMenu()
    {
        var menu = new DeferredMenuStartup();
        Check(!menu.Waiting && !menu.Request(true), "an unselected optional integration cannot start a menu");
        Check(menu.OnRunStarting(false, false) && !menu.Waiting, "ordinary modes retain native Aerolt startup");
        Check(menu.OnRunStarting(true, true) && !menu.Waiting, "already-ready Aerolt retains native startup and hotkeys");
        Check(!menu.OnRunStarting(true, false) && menu.Waiting, "Arena defers only cold optional menu startup");
        bool unsolicited = false;
        for (int i = 0; i < 10000; i++) unsolicited |= menu.Request(false) || menu.Complete(false);
        Check(!unsolicited && menu.Waiting, "spawning and arbitrarily many idle frames never request bootstrap");
        Check(!menu.OnRunStarting(true, false) && menu.Waiting, "replaying before first use still does no initialization");
        Check(menu.Request(true), "first deliberate menu keypress requests native bootstrap");
        Check(!menu.Request(false), "holding/releasing without another key-down never duplicates bootstrap");
        Check(!menu.OnRunStarting(true, false), "replay during native initialization never registers another callback");
        Check(!menu.OnRunStarting(false, false), "a run transition cannot duplicate our in-flight native request");
        Check(!menu.Complete(false) && menu.Waiting, "native readiness is never forced");
        Check(menu.Complete(true) && !menu.Waiting, "native completion opens the explicitly requested menu once");
        Check(!menu.Complete(true) && !menu.Request(true), "completion is idempotent and hands hotkeys back to Aerolt");
        Check(menu.OnRunStarting(true, true), "later ready runs remain entirely native");

        menu.OnRunStarting(true, false); menu.Request(true);
        Check(!menu.Request(true), "a second press can cancel opening without restarting initialization");
        Check(!menu.Complete(true) && !menu.Waiting, "cancelling an open request does not cancel or rewrite native initialization");
        menu.OnRunStarting(true, false); menu.Request(true); menu.Request(true);
        Check(!menu.Request(true) && menu.Complete(true), "a third press requeues opening without a second bootstrap");
        menu.OnRunStarting(true, false);
        Check(menu.OnRunStarting(false, false) && !menu.Waiting, "leaving Arena before first use restores ordinary native startup");
        menu.OnRunStarting(true, false);
        Check(!menu.Complete(true) && !menu.Waiting, "externally initialized Aerolt never opens without the user's request");
        menu.OnRunStarting(true, false); menu.Request(true);
        Check(menu.OnRunStarting(true, true) && menu.Complete(true), "readiness during a replay preserves the pending deliberate open");
        menu.OnRunStarting(true, false); menu.Request(true); menu.Reset();
        Check(!menu.Waiting && !menu.Complete(true) && !menu.Request(true), "failure/destruction clears only our own request state");
        Check(menu.OnRunStarting(false, false), "a cleared adapter does not block ordinary native callbacks");
    }

    private static void LateArrivals()
    {
        Check(CatchupRules.Credits(15, 0) == 15 && CatchupRules.Credits(15, 4) == 11, "late arrivals receive only their deficit against the least-equipped baseline");
        Check(CatchupRules.Credits(4, 9) == 0 && CatchupRules.Credits(0, 0) == 0, "existing items cannot produce negative credit or a free catch-up grant");
        Check(CatchupRules.Total(int.MaxValue, 10) == int.MaxValue, "large catch-up counts cannot overflow");
        Check(CatchupRules.Total(0, 15) == 15, "an unspent late arrival cannot lower the next arrival's baseline to zero");
        var bank = new RewardQueue(); int remaining = 100;
        for (uint i = 1; i <= RewardQueue.MaximumPending; i++)
            if (bank.Enqueue(new RewardOffer { Id = i, Items = new[] { 1 }, Catchup = true })) remaining--;
        Check(remaining == 88 && bank.Count == 12 && CatchupRules.Total(bank.Count, remaining) == 100, "catch-up credit beyond the normal bank is retained without rolling hundreds of offers at spawn");
        bank.Enqueue(new RewardOffer { Id = 1000, Items = new[] { 2 }, GatesRespawn = true });
        Check(bank.PendingCatchup == 11 && bank.ReturnedCatchup == 1, "a forced death choice returns displaced catch-up credit");
        remaining = CatchupRules.Total(remaining, bank.TakeReturnedCatchup());
        Check(remaining == 89 && bank.ReturnedCatchup == 0 && bank.TakeReturnedCatchup() == 0, "displaced credit is restored exactly once");
        RewardOffer used; bank.TryConsume(1000, 2, false, out used);
        if (bank.Enqueue(new RewardOffer { Id = 1001, Items = new[] { 1 }, Catchup = true })) remaining--;
        Check(bank.PendingCatchup + remaining == 100, "death reward consumption and refilling do not lose late-join credit");
        bank.Clear(); Check(bank.ReturnedCatchup == 0 && bank.PendingCatchup == 0, "new runs clear catch-up offer state");
        var roster = new BalancedTeams<uint>(); var actors = new List<uint> { 1, 2 }; roster.Balance(actors.ToArray());
        bool balanced = true;
        for (uint i = 0; i < 17; i++)
        {
            actors.Add(0xF0000000u + i); roster.Balance(actors.ToArray());
            int red = actors.FindAll(id => roster.Side(id) == 0).Count;
            balanced &= Math.Abs(red - (actors.Count - red)) <= 1;
        }
        Check(balanced, "multiple survivor bots use the same balanced roster as humans");
        for (uint i = 3; i < 34; i++)
        {
            actors.Add(i); roster.Balance(actors.ToArray()); int red = actors.FindAll(id => roster.Side(id) == 0).Count;
            balanced &= Math.Abs(red - (actors.Count - red)) <= 1;
        }
        Check(balanced, "batches of human late joins remain balanced with existing bots");
        actors.RemoveRange(0, 9); roster.Balance(actors.ToArray());
        int redNow = actors.FindAll(id => roster.Side(id) == 0).Count;
        Check(Math.Abs(redNow - (actors.Count - redNow)) <= 1, "departures rebalance remaining participants");
        uint reused = actors[0]; actors.RemoveAt(0); roster.Balance(actors.ToArray()); actors.Add(reused); roster.Balance(actors.ToArray());
        redNow = actors.FindAll(id => roster.Side(id) == 0).Count;
        Check(Math.Abs(redNow - (actors.Count - redNow)) <= 1, "reused bot slots cannot retain an unbalanced old side");
    }

    private static void BlacklistsAndCaps()
    {
        var list = new BlacklistSelection(" Bear , MissingCustom, Syringe=2,BEAR, , ModAbsent=7 ");
        Check(list.Contains("bear") && !list.CanGrant("Bear", 0), "legacy bans remain case-insensitive hard bans");
        Check(list.Limit("Syringe") == 2 && list.CanGrant("SYRINGE", 1) && !list.CanGrant("Syringe", 2), "saved limits allow only stacks below the maximum");
        Check(list.CanGrant("Unknown", int.MaxValue), "unlisted items remain unlimited");
        list.Adjust("Bear", 1);
        Check(!list.Contains("Bear") && list.Limit("Bear") == 1, "right-click switches a hard ban to a one-stack limit");
        list.Adjust("Bear", 1); Check(list.Limit("Bear") == 2, "repeated right-click increments the stack limit");
        list.Toggle("Bear"); Check(list.Contains("Bear"), "left-click hard-bans a limited item");
        list.Toggle("Bear"); Check(list.Limit("Bear") == -1, "left-click allows a banned item without retaining a hidden cap");
        list.Adjust("Bear", -1); Check(list.Limit("Bear") == -1, "decreasing an unlimited item keeps it unlimited");
        list.Adjust("Bear", 1); list.Adjust("Bear", -1); Check(list.Limit("Bear") == -1, "decreasing one removes the limit");
        list.Adjust("Bear", int.MaxValue); list.Adjust("Bear", 1); Check(list.Limit("Bear") == int.MaxValue, "right-click cannot overflow a large cap into a ban");
        string saved = list.Serialize(); var restored = new BlacklistSelection(saved);
        Check(restored.Contains("MissingCustom") && restored.Limit("ModAbsent") == 7, "editing visible items preserves absent custom mods' bans and limits");
        Check(restored.Serialize() == saved && restored.Limit("Syringe") == 2, "canonical serialization roundtrips limits and legacy names without item catalog indices");
        var voidCap = new BlacklistSelection("BearVoid=2");
        Check(voidCap.CanGrant("BearVoid", 1) && !voidCap.CanGrant("BearVoid", 1L + 1), "pending native corruption counts against the destination cap");
        Check(!voidCap.CanGrant("BearVoid", (long)int.MaxValue + int.MaxValue), "combined conversion stacks cannot overflow the cap comparison");
        var otherMode = new BlacklistSelection(saved); otherMode.Toggle("Syringe");
        Check(list.Limit("Syringe") == 2 && otherMode.Contains("Syringe"), "each mode's detached edits remain independent until applied");
        var bank = new RewardQueue(); RewardOffer used; int owned = 1;
        Func<int, bool> canGrant = p => p != 101 || list.CanGrant("Syringe", owned);
        bank.Enqueue(new RewardOffer { Id = 1, Items = new[] { 101 } });
        bank.Enqueue(new RewardOffer { Id = 2, Items = new[] { 101, 201 } });
        Check(bank.TryConsume(1, 101, false, out used, null, canGrant), "a queued claim below its cap can be consumed"); owned++;
        Check(!bank.TryConsume(2, 101, false, out used, null, canGrant) && bank.First.Id == 2, "an older queued offer cannot exceed a cap reached after it was created");
        owned--;
        Check(bank.TryConsume(2, 101, false, out used, null, canGrant), "scrapping a stack makes room for a retained capped reward");
        bank.Enqueue(new RewardOffer { Id = 3, Items = new[] { 101 }, ScrapTier = 1 }); owned = 2;
        Check(!bank.TryConsume(3, 101, false, out used, p => 1, canGrant), "recycled rewards cannot bypass held-stack limits");
        Check(bank.TryConsume(3, -1, true, out used, p => 1, canGrant), "enabled uncapped XP remains an alternative to capped recycled choices");
        bank.Enqueue(new RewardOffer { Id = 4, Items = new[] { 301 }, ScrapTier = 1 });
        Check(!bank.TryConsume(4, 301, true, out used, p => 3, p => true), "stack eligibility never bypasses exact-tier recycling validation");
    }

    private static void NativeItemExperience()
    {
        var pending = new LevelExperienceQueue(); var amounts = new System.Collections.Generic.List<ulong>();
        Func<uint, ulong> threshold = level => new ulong[] { 0, 0, 20, 47, 83, 130 }[level];
        pending.Record(1, 3, false, threshold);
        Check(pending.Count == 0, "items acquired later never earn historical level experience");
        pending.Record(1, 3, true, threshold);
        Check(pending.Count == 2, "each actual personal level records its native XP interval");
        pending.Drain(false, amounts.Add);
        Check(pending.Count == 2 && amounts.Count == 0, "XP earned while dead waits for a usable body");
        pending.Drain(true, amounts.Add);
        Check(amounts.Count == 2 && amounts[0] == 20 && amounts[1] == 27 && pending.Count == 0,
            "respawn delivers the original native per-level XP amounts once and in order");
        pending.Drain(true, amounts.Add); pending.Record(3, 3, true, threshold); pending.Record(3, 2, true, threshold);
        Check(pending.Count == 0 && amounts.Count == 2, "reapplying a level on respawn or correcting down grants no duplicate XP");
        pending.Record(3, 5, true, threshold); pending.Drain(true, amounts.Add);
        Check(amounts.Count == 4 && amounts[2] == 36 && amounts[3] == 47, "Random catch-up leveling uses individual native threshold differences");
        pending.Record(1, 3, true, threshold);
        try { pending.Drain(true, _ => { throw new InvalidOperationException("Native effect callback failed"); }); }
        catch (InvalidOperationException) { }
        Check(pending.Count == 1, "a partially applied native XP call cannot be retried after an effect exception");
        pending.Drain(true, amounts.Add);
        Check(amounts.Count == 5 && amounts[4] == 27, "remaining deferred XP survives without replaying the failed call");
        pending.Record(1, 3, true, _ => ulong.MaxValue);
        Check(pending.Count == 0, "a saturated native XP table produces no wrapped experience");
    }

    private static void EquipmentRewards()
    {
        // Catalog IDs are unrelated to item tiers; equipment has two explicit
        // source classes so recycling cannot trade ordinary gear for lunar gear.
        Func<int, int> tierOf = p => p >= 800 && p < 900 ? ScrapRewardRules.EquipmentTier
            : p >= 900 ? ScrapRewardRules.LunarEquipmentTier : p / 100;
        int[] catalog = { 101, 301, 801, 802, 803, 804, 901, 902, 801 };
        Func<int, int> noRoll = n => { throw new Exception("Weighted recycling must not reroll equipment rarity."); };
        var ordinary = ScrapRewardRules.Choices(ItemSelection.Weighted, catalog, ScrapRewardRules.EquipmentTier, 2, tierOf, noRoll);
        Check(ordinary.Length == 4, "weighted equipment recycling retains four unique ordinary choices without a rarity roll");
        var lunar = ScrapRewardRules.Choices(ItemSelection.Weighted, catalog, ScrapRewardRules.LunarEquipmentTier, 2, tierOf, noRoll);
        Check(lunar.Length == 2 && lunar[0] == 901 && lunar[1] == 902, "lunar equipment recycling has its own source class");
        foreach (ItemSelection mode in new[] { ItemSelection.Weighted, ItemSelection.Draft, ItemSelection.Random })
        {
            foreach (int source in new[] { ScrapRewardRules.EquipmentTier, ScrapRewardRules.LunarEquipmentTier })
            {
                var choices = ScrapRewardRules.Choices(mode, catalog, source, 2, tierOf, n => n - 1);
                bool valid = choices.Length == (mode == ItemSelection.Random ? 1 : mode == ItemSelection.Draft ? 2 : source == ScrapRewardRules.EquipmentTier ? 4 : 2);
                foreach (int choice in choices) valid &= tierOf(choice) == source;
                Check(valid, mode + " refunds only the scrapped equipment class " + source);
            }
            var missing = ScrapRewardRules.Choices(mode, new[] { 101, 301, 901 }, ScrapRewardRules.EquipmentTier, 2, tierOf, noRoll);
            Check(ArenaRules.ScrapBatchLimit(1, 0, mode == ItemSelection.Random, missing.Length > 0) == 0,
                mode + " blocks equipment removal if its replacement class is banned or unavailable");
        }
        var bank = new RewardQueue(); RewardOffer used;
        bank.Enqueue(new RewardOffer { Id = 1, Items = new[] { 801, 301, 901 }, ScrapTier = ScrapRewardRules.EquipmentTier });
        Check(!bank.TryConsume(1, 301, false, out used, tierOf) && !bank.TryConsume(1, 901, false, out used, tierOf),
            "forged red-item and lunar-equipment claims cannot spend ordinary-equipment credit");
        Check(!bank.TryConsume(1, -1, false, out used, tierOf) && bank.Count == 1, "capped XP cannot consume equipment credit");
        Check(bank.TryConsume(1, 801, false, out used, tierOf) && bank.Count == 0, "one valid equipment consumes exactly one credit");
        Check(!bank.TryConsume(1, 801, false, out used, tierOf), "an equipment reward cannot be replayed");
        for (uint id = 10; id < 10 + RewardQueue.MaximumPending; id++)
            bank.Enqueue(new RewardOffer { Id = id, Items = ordinary, ScrapTier = ScrapRewardRules.EquipmentTier });
        Check(ArenaRules.ScrapBatchLimit(1, bank.Count, false, true) == 0, "a full choice bank prevents losing scrapped equipment");
        Check(ArenaRules.ScrapBatchLimit(1, bank.Count, true, true) == 1, "automatic equipment recycling needs no bank slot");
        var equipmentOnly = new EssenceWeights(0, 0, 0, 0, 0, 100);
        Check(EssenceRollRules.Choose(new[] { EssenceKind.White, EssenceKind.Equipment }, equipmentOnly, n => 99) == EssenceKind.Equipment,
            "the editable equipment weight reaches the roll, including its upper boundary");
        Check(!EssenceRollRules.Choose(new[] { EssenceKind.White }, equipmentOnly, noRoll).HasValue,
            "disabled equipment cannot roll or fall back to an item when every other weight is zero");
        var retained = new EssenceWeights(60, 25, 10, 3, 2, 5);
        Check(EssenceRollRules.Choose(new[] { EssenceKind.White, EssenceKind.Green, EssenceKind.Red, EssenceKind.Void, EssenceKind.Lunar, EssenceKind.Equipment },
            retained, total => { Check(total == 105, "saved five-class weights plus new equipment weight normalize together"); return 104; }) == EssenceKind.Equipment,
            "existing saved percentages retain their values and include the new equipment class");
    }

    private static void NewModes()
    {
        Check(MinorPickupRules.CanCollect(false, -1, 1, 20, true, true), "neutral vanilla-player minor pickups accept an Arena player");
        Check(MinorPickupRules.CanCollect(true, 20, 1, 20, true, true), "antler orbs accept their owner's combat team");
        Check(!MinorPickupRules.CanCollect(true, 20, 1, 21, true, true), "opponents cannot steal owned antler orbs");
        Check(MinorPickupRules.CanCollect(false, -1, 20, 20, false, true), "team-specific healing retains allied collection");
        Check(!MinorPickupRules.CanCollect(false, -1, 20, 21, false, true), "team-specific pickups reject enemies");
        Check(!MinorPickupRules.CanCollect(false, -1, 1, 1, true, false), "non-Arena collectors are outside the compatibility override");
        Check((int)ItemSelection.Weighted == 3 && Enum.GetNames(typeof(ItemSelection)).Length == 4, "Weighted keeps protocol value 3 without a duplicate dropdown entry");
        Check(SelectionNames.Normalize("WeightedEssence") == "Weighted" && SelectionNames.Normalize("weightedessence") == "Weighted", "old saved WeightedEssence settings migrate");
        Check(SelectionNames.Normalize("Random") == "Random" && SelectionNames.Normalize("Command") == "Command", "other saved modes stay unchanged");
        Check(MapVotingRules.Preview(new[] { 0, 1, 2 }, new int[0], 0) == 0, "no mode votes preserves default FFA");
        Check(MapVotingRules.Preview(new[] { 0, 1, 2 }, new[] { 1, 2, 2 }, 0) == 2, "KOTH mode wins a majority");
        Check(MapVotingRules.Preview(new[] { 0, 1, 2 }, new[] { 0, 1 }, 1) == 1, "host default breaks mode ties deterministically");
        Check(MapVotingRules.Preview(new[] { 0, 1, 2 }, new[] { 99, 99, 1 }, 0) == 1, "unknown modes cannot win a forged ballot");
        for (int count = 1; count <= 32; count++)
        {
            var teams = new BalancedTeams<int>(); var ids = new int[count]; for (int i = 0; i < count; i++) ids[i] = i;
            teams.Balance(ids); int red = 0, blue = 0; var previous = new int[count];
            for (int i = 0; i < count; i++) { previous[i] = teams.Side(i); if (previous[i] == 0) red++; else if (previous[i] == 1) blue++; }
            Check(red + blue == count && Math.Abs(red - blue) <= 1, "balanced Red/Blue assignments with " + count + " participants");
            teams.Balance(ids); bool stable = true; for (int i = 0; i < count; i++) stable &= previous[i] == teams.Side(i);
            Check(stable, "unchanged roster does not reshuffle teams for " + count + " participants");
        }
        var roster = new BalancedTeams<string>(); roster.Balance(new[] { "a", "b", "c", "d", "e", "f" });
        roster.Balance(new[] { "a", "c", "e" }); int reds = 0;
        foreach (string id in new[] { "a", "c", "e" }) if (roster.Side(id) == 0) reds++;
        Check(reds == 1 || reds == 2, "departures rebalance remaining players without exceeding a one-player difference");
        roster.Clear(); Check(roster.Side("a") == -1, "team assignments reset between runs");
        roster.AssignPresent(new[] { "a", "b", "c", "d" }); int originalSide = roster.Side("a");
        roster.AssignPresent(new[] { "a", "c", "new" });
        Check(roster.Side("a") == originalSide && roster.Side("new") == 1 - originalSide, "late joins fill the smaller team without retagging existing players or their in-flight attacks");
        var withBots = new BalancedTeams<string>(); withBots.AssignPresent(new[] { "host" });
        withBots.AssignPresent(new[] { "host", "joiner" }, 0, 3);
        Check(withBots.Side("host") == 0 && withBots.Side("joiner") == 0, "late human joins include existing bot counts when choosing the smaller team");
        withBots.AssignPresent(new[] { "host", "joiner" }, 3, 0);
        Check(withBots.Side("host") == 0 && withBots.Side("joiner") == 0, "bot additions cannot retag existing humans or their live attacks");
        var hill = new HillScore();
        Check(hill.Tick(0, 0, 50, 10) == -1 && hill.Charge[0] == 0 && hill.Charge[1] == 0, "empty focus earns nothing");
        Check(hill.Tick(3, 1, 50, 10) == -1 && hill.Charge[0] == 0, "contested focus pauses even when outnumbered");
        Check(hill.Tick(1, 0, 5, 10) == -1 && hill.Charge[0] == 0.5f, "uncontested team gains focus charge");
        Check(hill.Tick(0, 1, 2, 10) == -1 && hill.Charge[0] == 0.5f && hill.Charge[1] == 0.2f, "both teams retain independent partial charge");
        Check(hill.Tick(1, 1, 5, 10) == -1 && hill.Charge[0] == 0.5f && hill.Charge[1] == 0.2f, "contesting never erases earned charge");
        Check(hill.Tick(5, 0, 5, 10) == 0 && hill.Captures[0] == 1 && hill.Charge[0] == 0 && hill.Charge[1] == 0.2f, "one capture resets only the scoring team and retains opposing partial charge");
        Check(Math.Abs(hill.Progress(0, 3) - 1f / 3) < 0.00001f, "one full focus means one third of a three-charge match");
        hill.Tick(1, 0, 5, 10);
        Check(hill.Progress(0, 3) == 0.5f && hill.Charge[0] == 0.5f, "halfway through second focus is 50 percent of a three-charge match");
        hill.Tick(1, 0, 5, 10); hill.Tick(1, 0, 10, 10);
        Check(hill.Captures[0] == 3 && hill.Progress(0, 3) == 1 && Math.Abs(hill.Progress(1, 3) - 0.2f / 3) < 0.00001f, "three captures complete the goal without erasing opposing progress");
        Check(HillScore.ContinuedTarget(3, 3, 1, 3) == 6 && hill.Captures[0] == 3, "KOTH Continue adds another goal without discarding scores");
        Check(HillScore.ContinuedTarget(int.MaxValue, int.MaxValue, 0, 50) == int.MaxValue, "continued capture goal cannot overflow");
        var single = new HillScore(); single.Tick(1, 0, 5, 10);
        Check(single.Progress(0, 1) == 0.5f, "one-charge goal matches the current focus percentage");
        var many = new HillScore(); many.Tick(9, 0, 5, 10);
        Check(many.Charge[0] == single.Charge[0], "additional teammates do not speed up charging");
        Check(ArenaRules.RandomLevel(59.9f, 60, 30, true) == 1 && ArenaRules.RandomLevel(60, 60, 30, true) == 2, "Random shared levels advance on the exact one-minute boundary");
        Check(ArenaRules.RandomLevel(120, 30, 30, true) == 5, "host controls Random's level interval");
        Check(ArenaRules.RandomLevel(99999, 60, 30, true) == 30, "Random automatic leveling respects the maximum");
        Check(ArenaRules.RandomLevel(99999, 60, 30, false) == 1, "Random automatic leveling can be disabled");
        Check(ArenaRules.RandomLevel(0, 60, 30, true) == 1, "warmup cannot grant a Random level");
        var guard = new BurstGuard(); bool fired;
        Check(guard.Clamp(0, -9999, 100, 100, 1.25f, out fired) == -9999 && !fired, "unarmed protection does not prevent ordinary low-health deaths");
        guard.Observe(0, 1);
        Check(guard.Clamp(0, -9999, 100, 100, 1.25f, out fired) == 10 && fired, "a lethal opposing hit is stopped before death at ten percent health");
        Check(guard.Protected(0.5) && guard.Clamp(0.5, -9999, 10, 100, 1.25f, out fired) == 10 && !fired, "same-burst proc hits cannot kill or repeatedly extend protection");
        guard.Observe(0.5, 1);
        Check(!guard.Protected(1.25) && guard.Clamp(1.25, -9999, 10, 100, 1.25f, out fired) == -9999, "sustained attacks after grace can kill and healing during grace cannot create permanent immunity");
        guard.Observe(2, 0.89f);
        Check(guard.Clamp(2, -9999, 89, 100, 1.25f, out fired) == -9999, "protection requires ninety percent recovery to rearm");
        guard.Observe(3, 0.9f);
        Check(guard.Clamp(3, 50, 90, 100, 1.25f, out fired) == 50 && !fired, "nonlethal hits remain ordinary damage");
        Check(guard.Clamp(12, -500, 50, 100, 1.25f, out fired) == 10 && fired, "a slow lethal DoT after the initial hit still triggers armed protection");
        Check(guard.Clamp(12.1, -500, 1, 100, 1.25f, out fired) == 1, "health floor never heals a victim injured by another source");
        guard.Reset();
        Check(!guard.Protected(0) && guard.Clamp(0, 0, 100, 100, 1, out fired) == 0, "respawn resets the protection state");
    }

    private static void RetainHillProgress()
    {
        var hill = new HillScore();
        hill.Tick(0, 1, 22, 100);
        hill.Tick(1, 0, 67, 100);
        float away = hill.Progress(1, 3), home = hill.Progress(0, 3);
        Check(hill.Tick(1, 0, 34, 100) == 0 && hill.Captures[0] == 1, "finishing a partially charged focus scores exactly once");
        Check(hill.Charge[1] == 0.22f && hill.Progress(1, 3) == away, "the reported 22 percent survives opposing capture and relocation");
        Check(hill.Progress(0, 3) > home, "scoring team total progress never decreases at relocation");
        hill.Tick(1, 0, 40, 100);
        Check(hill.Tick(0, 1, 79, 100) == 1 && hill.Captures[1] == 1 && hill.Charge[0] == 0.4f, "opposite-side capture also preserves earned partial charge");
        float saved = hill.Progress(0, 3);
        hill.Tick(1, 1, 999, 100); hill.Tick(0, 0, 999, 100);
        Check(hill.Progress(0, 3) == saved, "empty and contested focus cannot reset carried progress");
        hill.Tick(0, 1, 100, 100); hill.Tick(0, 1, 100, 100);
        Check(hill.Captures[1] == 3 && hill.Progress(0, 3) == saved && hill.Progress(1, 3) == 1, "successive opposing captures retain unfinished progress through match end");
        int target = HillScore.ContinuedTarget(3, hill.Captures[0], hill.Captures[1], 3);
        Check(target == 6 && hill.Charge[0] == 0.4f && hill.Captures[0] == 1, "Continue raises the target without losing carried partial charge or captures");
    }
    private sealed class TestNode
    {
        internal readonly System.Collections.Generic.List<TestNode> Children = new System.Collections.Generic.List<TestNode>();
        internal bool Alive = true, Protected, Mission;
        internal int Visits, Context;
    }
    private static void IncrementalCleanup()
    {
        var root = new TestNode();
        for (int i = 0; i < 10000; i++) root.Children.Add(new TestNode());
        var walk = new HierarchyWorkQueue<TestNode>(); int visits = 0, edges = 0;
        Func<TestNode, int, int> visit = (n, c) => { if (!n.Alive || n.Protected) return -1; n.Visits++; visits++; n.Context = n.Mission ? 1 : c; return n.Context; };
        Func<TestNode, int> count = n => n.Alive ? n.Children.Count : 0;
        Func<TestNode, int, TestNode> child = (n, i) => { edges++; return n.Children[i]; };
        walk.Begin(root); walk.Step(visit, count, child);
        Check(visits == 1 && edges == 0 && walk.Pending, "visiting a huge root cannot enqueue or scan its children in one step");
        for (int i = 0; i < 384; i++) walk.Step(visit, count, child);
        Check(visits < 385 && edges < 385 && walk.Pending, "a frame budget bounds work inside a single wide hierarchy");
        int steps = 0; while (walk.Pending && steps++ < 40000) walk.Step(visit, count, child);
        bool once = root.Visits == 1; foreach (var node in root.Children) once &= node.Visits == 1;
        Check(!walk.Pending && once && edges == 10000, "successive bounded frames eventually visit every scene node exactly once");
        root = new TestNode(); var tail = root;
        for (int i = 0; i < 10000; i++) { var next = new TestNode(); tail.Children.Add(next); tail = next; }
        walk.Begin(root); steps = 0; while (walk.Pending && steps++ < 40000) walk.Step(visit, count, child);
        Check(!walk.Pending && tail.Visits == 1, "deep scenes use an explicit stack without recursive stack overflow");
        var actor = new TestNode { Protected = true }; actor.Children.Add(new TestNode());
        var mission = new TestNode { Mission = true }; mission.Children.Add(new TestNode());
        root = new TestNode(); root.Children.Add(actor); root.Children.Add(mission); root.Children.Add(new TestNode());
        walk.Begin(root); while (walk.Pending) walk.Step(visit, count, child);
        Check(actor.Children[0].Visits == 0, "protected player and ability subtrees are pruned before traversal");
        Check(mission.Children[0].Context == 1 && root.Children[2].Context == 0, "mission state-machine suppression propagates only within its own subtree");
        walk.Begin(root); walk.Step(visit, count, child); walk.Clear();
        Check(!walk.Pending, "leaving Arena or changing scene clears unfinished work");
        root = new TestNode(); walk.Begin(root); walk.Step(visit, count, child); root.Alive = false; walk.Step(visit, count, child);
        Check(!walk.Pending, "destroyed scene objects retire safely between frames");
    }

    private static void ContinueVoting()
    {
        var ballot = new ContinueBallot<string>();
        Check(!ballot.Open && ballot.Required == 0 && !ballot.Passed, "empty closed ballot cannot resume a match");
        Check(!ballot.SetVote("host", 0, true), "votes before match end are rejected");
        ballot.Begin(new[] { "host" }); uint first = ballot.Round;
        Check(first != 0 && ballot.Required == 1 && !ballot.Passed, "a solo match waits for its one human vote");
        Check(ballot.SetVote("host", first, true) && ballot.Passed, "sole host can continue a multiplayer match against bots");
        Check(!ballot.SetVote("host", first, true) && ballot.VoteCount == 1, "duplicate votes do not count twice");
        ballot.Close(); Check(!ballot.Passed && !ballot.SetVote("host", first, true), "closing the ballot invalidates queued packets and clears votes");
        ballot.Begin(new[] { "host", "friend" }); uint second = ballot.Round;
        Check(second != first && ballot.VoteCount == 0 && ballot.Required == 2, "the next match end has a new round and no inherited votes");
        Check(!ballot.SetVote("host", first, true) && ballot.VoteCount == 0, "a delayed old-round vote cannot continue a later match");
        Check(!ballot.SetVote("outsider", second, true) && !ballot.CanVote("outsider"), "spectators, bots and unknown identities cannot vote unless the server admits them");
        Check(ballot.SetVote("friend", second, true) && !ballot.Passed, "one out of two is a tie, not a majority");
        Check(ballot.HasVoted("friend") && !ballot.HasVoted("host"), "each player sees their own accepted vote state");
        Check(ballot.SetVote("friend", second, false) && ballot.VoteCount == 0 && !ballot.Passed, "a player may withdraw support before passage");
        Check(!ballot.SetVote("friend", second, false), "duplicate withdrawal cannot reduce another player's votes");
        ballot.SetVote("host", second, true); ballot.SetVote("friend", second, true);
        Check(ballot.Passed && ballot.VoteCount == 2, "two out of two resumes the match");
        int[] expected = { 1, 2, 2, 3, 3, 4, 4, 5 };
        for (int count = 1; count <= expected.Length; count++)
        {
            var ids = new string[count]; for (int i = 0; i < count; i++) ids[i] = "player" + i;
            ballot.Begin(ids); Check(ballot.Required == expected[count - 1], "strict-majority threshold for " + count + " humans");
            for (int i = 0; i < expected[count - 1] - 1; i++) ballot.SetVote(ids[i], ballot.Round, true);
            Check(!ballot.Passed, "one vote below the threshold cannot resume with " + count + " humans");
            ballot.SetVote(ids[expected[count - 1] - 1], ballot.Round, true);
            Check(ballot.Passed, "the decisive majority vote passes with " + count + " humans");
        }
        ballot.Begin(new[] { "host", "a", "b", "c" }); ballot.SetVote("host", ballot.Round, true); ballot.SetVote("a", ballot.Round, true);
        Check(!ballot.Passed, "two out of four remains a tie");
        ballot.Refresh(new[] { "host", "a", "b" });
        Check(ballot.Required == 2 && ballot.Passed, "a non-voter leaving recalculates the connected-player majority");
        ballot.Refresh(new[] { "host", "b" });
        Check(ballot.VoteCount == 1 && !ballot.HasVoted("a") && !ballot.Passed, "a disconnected voter's vote is removed");
        ballot.Refresh(new[] { "host", "a", "b" });
        Check(!ballot.HasVoted("a") && ballot.VoteCount == 1, "reconnecting players do not recover a discarded vote");
        ballot.Refresh(new[] { "host", "host", "a" });
        Check(ballot.EligibleCount == 2 && ballot.Required == 2, "duplicate roster entries do not change the majority");
        ballot.Refresh(new string[0]); Check(!ballot.Passed && ballot.VoteCount == 0, "zero humans never auto-continue a bots-only match");
        ballot.Close(); ballot.Refresh(new[] { "host" });
        Check(!ballot.CanVote("host") && ballot.EligibleCount == 0, "membership refresh cannot reopen a closed ballot");
    }

    private static void ReplayAndLargeLobbies()
    {
        var ballot = new ContinueBallot<string>(); ballot.Begin(new[] { "host", "a", "b" });
        uint round = ballot.Round;
        ballot.SetVote("host", round, true); ballot.SetVote("a", round, true, true);
        Check(!ballot.Passed && !ballot.RestartPassed && ballot.VoteCount == 1 && ballot.RestartVoteCount == 1, "split Continue and Play Again votes do not combine into a majority");
        ballot.SetVote("host", round, true, true);
        Check(ballot.RestartPassed && !ballot.Passed && ballot.VoteCount == 0 && ballot.RestartVoteCount == 2, "switching to Play Again removes the old Continue vote and can form a replay majority");
        Check(!ballot.SetVote("host", round, true, true) && ballot.RestartVoteCount == 2, "duplicate Play Again packets cannot double count");
        Check(ballot.HasRestartVoted("host") && !ballot.HasVoted("host"), "each human has one mutually exclusive match-end vote");
        ballot.SetVote("host", round, false, true);
        Check(!ballot.RestartPassed && ballot.RestartVoteCount == 1, "Play Again support can be withdrawn");
        ballot.Refresh(new[] { "host", "b" });
        Check(ballot.RestartVoteCount == 0 && !ballot.RestartPassed, "departed replay voters cannot keep a stale majority");
        ballot.Close(); ballot.Begin(new[] { "host" });
        Check(!ballot.SetVote("host", round, true, true) && !ballot.RestartPassed, "old replay packets cannot affect a new ballot");
        ballot.SetVote("host", ballot.Round, true, true);
        Check(ballot.RestartPassed && !ballot.Passed, "a solo host can vote for a fresh match against bots");
        var ids = new string[512]; for (int i = 0; i < ids.Length; i++) ids[i] = "human" + i;
        var teams = new BalancedTeams<string>(); teams.Balance(ids);
        int red = 0, blue = 0; foreach (string id in ids) { if (teams.Side(id) == 0) red++; else if (teams.Side(id) == 1) blue++; }
        Check(red == 256 && blue == 256, "large team assignment has no four/16/64-player cap");
        var larger = new string[513]; Array.Copy(ids, larger, ids.Length); larger[512] = "late";
        teams.AssignPresent(larger);
        Check(teams.Side("late") >= 0 && teams.Side(ids[0]) == 0, "large-lobby late joins get a team without retagging existing players");
        ballot.Begin(ids); for (int i = 0; i < 256; i++) ballot.SetVote(ids[i], ballot.Round, true);
        Check(ballot.EligibleCount == 512 && ballot.Required == 257 && !ballot.Passed, "half a large lobby is still a tie");
        ballot.SetVote(ids[256], ballot.Round, true);
        Check(ballot.Passed && ballot.VoteCount == 257, "every admitted human counts toward a large-lobby majority");
    }
    private static void ReprieveShieldConversion()
    {
        Check(ReprieveBarrierRules.HealthBasis(true, 115, 0) * 0.2f == 23, "unshielded Arena Reprieve retains its ordinary entry barrier");
        Check(ReprieveBarrierRules.HealthBasis(false, 1, 172.5f) == 1, "vanilla gamemodes retain native health-only Reprieve scaling");
        Check(Math.Abs(ReprieveBarrierRules.HealthBasis(true, 1, 172.5f) * 0.2f - 34.7f) < 0.0001f, "Transcendence entry barrier is based on health plus converted shields");
        Check(Math.Abs(ReprieveBarrierRules.HealthBasis(true, 115, 9.2f) - 124.2f) < 0.0001f, "ordinary health/shield mixes use the same combined-health basis");
        foreach (float step in new[] { 0.02f, 1f / 60 })
        {
            float maximum = 173.5f, oldBarrier = 0.2f, newBarrier = ReprieveBarrierRules.HealthBasis(true, 1, 172.5f) * 0.2f;
            for (float elapsed = 0; elapsed < 0.5f; elapsed += step)
            {
                oldBarrier = Math.Max(0, oldBarrier - maximum / 30 * (0.5f + 2.5f * oldBarrier / maximum) * step);
                newBarrier = Math.Max(0, newBarrier - maximum / 30 * (0.5f + 2.5f * newBarrier / maximum) * step);
            }
            Check(oldBarrier == 0 && newBarrier > 0, "converted-health barrier survives native startup and the first vehicle checks at step " + step);
        }
    }

    private static void StartupSafety()
    {
        var trace = new LaunchTraceState();
        Check(trace.Observe(1, 0, true, false) == LaunchTraceChange.None && !trace.Active, "idle lobby time is never counted as launch time");
        Check(trace.Observe(1, 2, true, false) == LaunchTraceChange.None, "a launched/destroying controller alone does not invent a launch");
        Check(trace.Observe(1, 1, true, false) == LaunchTraceChange.Begin && trace.Active, "only an actual native Launching state starts the lobby timer");
        Check(trace.Observe(1, 1, true, false) == LaunchTraceChange.None, "repeated launching frames retain the same timer");
        Check(trace.Observe(0, 0, false, false) == LaunchTraceChange.Cancel && !trace.Active, "disconnect cancels the old timer instead of counting time at the title screen");
        Check(trace.Observe(2, 0, true, false) == LaunchTraceChange.None && !trace.Active, "rejoin/voting time cannot inherit an old launch duration");
        Check(trace.Observe(2, 1, true, false) == LaunchTraceChange.Begin, "a second launch starts a fresh timer");
        Check(!trace.EnterRun() && trace.Active, "run initialization retains a valid same-attempt launch baseline");
        Check(trace.Observe(0, 0, true, true) == LaunchTraceChange.None && trace.Active, "removing the pregame controller during the active run does not cancel scene timings");
        trace.Stop(); Check(trace.EnterRun() && trace.Active, "direct/replay Run creation establishes its own baseline");
        trace.Stop(); trace.Observe(2, 1, true, false);
        Check(trace.Observe(3, 1, true, false) == LaunchTraceChange.Begin, "a replaced pregame controller cannot reuse the previous lobby timer");
        Check(trace.Observe(3, 0, true, false) == LaunchTraceChange.Cancel, "cancelled launches stop timing when the lobby returns idle");
        var order = new System.Collections.Generic.List<int>(); int reports = 0;
        Action<int> handlers = x => order.Add(x); handlers += x => order.Add(x + 1);
        StartupCallbacks.Invoke(handlers, 5, (d, ms) => { reports++; Check(ms >= 0, "elapsed callback time is monotonic"); });
        Check(order.Count == 2 && order[0] == 5 && order[1] == 6 && reports == 2, "timing invokes every listener once in native multicast order");
        var expected = new InvalidOperationException("native callback failure");
        order.Clear(); handlers = x => order.Add(1); handlers += x => { order.Add(2); throw expected; }; handlers += x => order.Add(3);
        Exception caught = null;
        try { StartupCallbacks.Invoke(handlers, 0, (d, ms) => { }); } catch (Exception error) { caught = error; }
        Check(ReferenceEquals(caught, expected) && order.Count == 2, "timing preserves the original exception and native stop-on-exception behavior");
        order.Clear(); handlers = x => order.Add(x);
        StartupCallbacks.Invoke(handlers, 7, (d, ms) => { throw new Exception("observer failed"); });
        Check(order.Count == 1 && order[0] == 7, "a failed timing observer cannot break a valid callback");
        handlers = x => { throw expected; }; caught = null;
        try { StartupCallbacks.Invoke(handlers, 0, (d, ms) => { throw new Exception("observer failed"); }); } catch (Exception error) { caught = error; }
        Check(ReferenceEquals(caught, expected), "observer failure cannot mask the original game exception");
    }

    public static int Main()
    {
        try
        {
            NewModes();
            ContinueVoting();
            StartupSafety();
            ReplayAndLargeLobbies();
            ReprieveShieldConversion();
            IncrementalCleanup();
            RetainHillProgress();
            ScrapperRewards();
            EquipmentRewards();
            BlacklistsAndCaps();
            NativeItemExperience();
            DeferredAeroltMenu();
            LateArrivals();
            Check(ArenaRules.IsUserPresent(true, true, false), "the sole local host counts without a remote client connection");
            Check(ArenaRules.IsUserPresent(true, true, true), "a normally connected local host counts once");
            Check(ArenaRules.IsUserPresent(true, false, true), "a connected remote player is retained");
            Check(!ArenaRules.IsUserPresent(true, false, false), "disconnected remote players cannot keep a match ticking");
            Check(!ArenaRules.IsUserPresent(false, true, false), "a non-host local client cannot impersonate a listen server");
            Check(!ArenaRules.IsUserPresent(false, false, false), "an absent user is not a participant");
            int remainingPlayers = (ArenaRules.IsUserPresent(true, true, false) ? 1 : 0) + (ArenaRules.IsUserPresent(true, false, false) ? 1 : 0);
            Check(remainingPlayers == 1, "the host remains a valid participant after the last remote player leaves");
            BotBuilds();
            BotIntelligenceAndCounters();
            RewardArrivalAndVehicleCleanup();
            foreach (string section in new[] { SettingsLayout.General, SettingsLayout.Profile(ArenaMode.FFA), SettingsLayout.Profile(ArenaMode.Teams), SettingsLayout.Profile(ArenaMode.KOTH) })
                Check(ArenaRules.IsMatchSettingGroup(section), "match settings includes gameplay category " + section);
            Check(!ArenaRules.IsMatchSettingGroup(SettingsLayout.Controls), "local Controls tab never enters host gameplay snapshots");
            Check(!ArenaRules.IsMatchSettingGroup("06 Local controls"), "client preferences and keybinds never enter host settings snapshots");
            Check(!ArenaRules.IsMatchSettingGroup("Keyboard + Mouse") && !ArenaRules.IsMatchSettingGroup("Controls"), "general game controls are not match settings");
            Check(!ArenaRules.IsMatchSettingGroup(null) && !ArenaRules.IsMatchSettingGroup("08 Unknown"), "unknown settings categories are rejected rather than exposed");
            var closeGesture = new DoubleTapRelease();
            Check(!closeGesture.Update(true, true, 0), "holding W when a timed picker opens cannot dismiss it");
            bool dismissedWhileHeld = false;
            for (int frame = 1; frame <= 100; frame++) dismissedWhileHeld |= closeGesture.Update(true, true, frame / 60.0);
            Check(!dismissedWhileHeld, "continuous W across frames never counts as repeated taps");
            Check(!closeGesture.Update(true, false, 2), "release of W held before opening is ignored");
            Check(!closeGesture.Update(true, true, 2.1) && !closeGesture.Update(true, false, 2.2), "one fresh complete W tap keeps the menu open");
            Check(!closeGesture.Update(true, true, 2.3), "second W press alone does not close the menu");
            Check(closeGesture.Update(true, false, 2.4), "press release press release closes on the second release");
            Check(!closeGesture.Update(true, false, 3), "reopened picker starts with no inherited taps");
            Check(!closeGesture.Update(true, true, 3.1) && !closeGesture.Update(true, false, 3.2), "first tap is retained only for the double-tap window");
            Check(!closeGesture.Update(true, false, 4), "an isolated W tap expires without closing");
            Check(!closeGesture.Update(true, true, 4.1) && !closeGesture.Update(true, false, 4.2), "spaced movement presses cannot accumulate into a dismissal");
            Check(!closeGesture.Update(true, true, 4.3) && closeGesture.Update(true, false, 4.4), "two new quick complete taps still close after a timeout");
            closeGesture.Reset();
            Check(!closeGesture.Update(false, true, 5) && !closeGesture.Update(false, false, 5.1), "W input outside a reward menu does not count");
            Check(!closeGesture.Update(true, false, 5.2) && !closeGesture.Update(true, true, 5.3) && !closeGesture.Update(true, false, 5.4), "opening starts a fresh two-tap sequence");
            Check(!closeGesture.Update(false, false, 5.5), "closing the menu cancels a partial double tap");
            Check(!closeGesture.Update(true, false, 5.6) && !closeGesture.Update(true, true, 5.7) && !closeGesture.Update(true, false, 5.8), "a tap before closing cannot dismiss the reopened menu");
            closeGesture.Reset();
            Check(!closeGesture.Update(true, false, 6) && !closeGesture.Update(true, true, 6.1) && !closeGesture.Update(true, false, 6.2), "new reward resets the dismissal gesture");
            Check(!closeGesture.Update(true, true, 6.3) && !closeGesture.Update(true, true, 6.4), "holding the second press waits for its release");
            Check(closeGesture.Update(true, false, 6.5), "second complete tap closes after held frames");
            Check(ArenaRules.ScrapBatchLimit(10, 0, false, true) == 10, "an empty reward bank accepts a full scrapper batch");
            Check(ArenaRules.ScrapBatchLimit(10, 9, false, true) == 3, "scrap at most the items whose reward points fit");
            Check(ArenaRules.ScrapBatchLimit(10, 12, false, true) == 0, "a full reward bank consumes no items");
            Check(ArenaRules.ScrapBatchLimit(10, 0, false, false) == 0, "an impossible reward pool consumes no items");
            Check(ArenaRules.ScrapBatchLimit(10, 12, true, true) == 10, "automatic random rewards do not occupy bank space");
            Check(ArenaRules.ScrapBatchLimit(0, 0, false, true) == 0 && ArenaRules.ScrapBatchLimit(-10, 0, true, true) == 0, "nonpositive batches cannot create credits");
            Check(ArenaRules.LevelCap(0, 3, 60, 2, 30) == 3, "initial cap");
            Check(ArenaRules.LevelCap(59.999, 3, 60, 2, 30) == 3, "before boundary");
            Check(ArenaRules.LevelCap(60, 3, 60, 2, 30) == 5, "exact boundary");
            Check(ArenaRules.LevelCap(120, 3, 60, 2, 30) == 7, "multiple intervals");
            Check(ArenaRules.LevelCap(double.MaxValue, 3, 60, 2, 30) == 30, "large clock saturates");
            Check(ArenaRules.LevelCap(-100, 3, 60, 2, 30) == 3, "negative clock");
            Check(ArenaRules.LevelCap(60, 99, 60, 1, 10) == 10, "initial clamped to maximum");
            Check(ArenaRules.LevelCap(100, 3, 0, 1, 30) == 3, "invalid interval safe");
            Check(ArenaRules.CanRespawn(RespawnRule.Timer, true, false, 5, 5), "timer ignores choice");
            Check(!ArenaRules.CanRespawn(RespawnRule.Timer, false, true, 4, 5), "timer waits");
            Check(ArenaRules.CanRespawn(RespawnRule.AfterChoice, true, true, 1, 1), "choice and minimum cleanup complete");
            Check(!ArenaRules.CanRespawn(RespawnRule.AfterChoice, true, true, 0.5, 1), "choice respects minimum cleanup");
            Check(!ArenaRules.CanRespawn(RespawnRule.AfterChoice, true, false, 10, 5), "choice required");
            Check(!ArenaRules.CanRespawn(RespawnRule.TimerAndChoice, true, true, 4, 5), "both requires timer");
            Check(!ArenaRules.CanRespawn(RespawnRule.TimerAndChoice, true, false, 6, 5), "both requires choice");
            Check(ArenaRules.CanRespawn(RespawnRule.TimerAndChoice, true, true, 5, 5), "both complete");
            Check(ArenaRules.CanRespawn(RespawnRule.AfterChoice, false, false, 5, 5), "random/no-pool falls back to timer");
            var queue = new RewardQueue();
            queue.Enqueue(new RewardOffer { Id = 1, Items = new[] { 5, 9 } });
            RewardOffer used;
            Check(!queue.TryConsume(1, 1000, true, out used) && queue.Count == 1, "forged item rejected");
            Check(!queue.TryConsume(2, 5, true, out used) && queue.Count == 1, "stale offer rejected");
            Check(!queue.TryConsume(1, -1, false, out used) && queue.Count == 1, "capped XP does not spend reward");
            Check(queue.TryConsume(1, -1, true, out used) && queue.Count == 0, "XP spends one reward");
            Check(!queue.TryConsume(1, -1, true, out used), "XP replay rejected");
            for (uint i = 10; i < 22; i++) Check(queue.Enqueue(new RewardOffer { Id = i, Items = new[] { 5 } }), "fill queue " + i);
            Check(!queue.Enqueue(new RewardOffer { Id = 22, Items = new[] { 5 } }), "bounded queue");
            Check(queue.Enqueue(new RewardOffer { Id = 23, Items = new[] { 9 }, GatesRespawn = true }), "death reward gets space");
            Check(queue.First.Id == 23 && queue.Count == RewardQueue.MaximumPending, "death choice moves first without overflow");
            Check(queue.TryConsume(23, 9, false, out used) && used.GatesRespawn, "death gate is consumed");
            Check(queue.First.Id == 10, "earlier timer reward preserved");
            Check(!queue.TryConsume(23, 9, false, out used), "death reward replay rejected");
            queue.Clear(); Check(queue.Count == 0 && queue.First == null, "cleanup");
            Check(MapVotingRules.Select(new int[0], new[] { 1 }, n => 0) == -1, "no allowed maps");
            Check(MapVotingRules.Select(new[] { 1, 2, 3 }, new[] { 2, 2, 1 }, n => 0) == 2, "majority vote");
            Check(MapVotingRules.Select(new[] { 1, 2, 3 }, new[] { 2, 1, 99, 99 }, n => 0) == 1, "blacklisted or forged ballot ignored");
            Check(MapVotingRules.Select(new[] { 1, 2, 3 }, new[] { 2, 1 }, n => n - 1) == 2, "tie draws from tied leaders");
            Check(MapVotingRules.Select(new[] { 1, 2, 3 }, new int[0], n => n - 1) == 3, "no votes random allowed map");
            Check(MapVotingRules.Preview(new[] { 1, 2, 3 }, new int[0], 2) == 2, "preview uses host default before voting");
            Check(MapVotingRules.Preview(new[] { 1, 2, 3 }, new[] { 1, 1, 2 }, 3) == 1, "most votes override host default");
            Check(MapVotingRules.Preview(new[] { 1, 2, 3 }, new[] { 1, 2 }, 2) == 2, "tied default remains displayed");
            Check(MapVotingRules.Preview(new[] { 1, 2, 3 }, new[] { 1, 3 }, 2) == 1, "tie excluding default is stable");
            Check(MapVotingRules.Preview(new[] { 1, 2, 3 }, new int[0], 99) == 1, "unavailable default falls back to allowed map");
            Check(MapVotingRules.Preview(new int[0], new[] { 1 }, 1) == -1, "empty preview cannot select a banned map");
            var kinds = new[] { EssenceKind.White, EssenceKind.Green, EssenceKind.Red, EssenceKind.Void, EssenceKind.Lunar, EssenceKind.Equipment };
            var counts = new int[8];
            for (int ticket = 0; ticket < 100; ticket++)
            {
                int fixedTicket = ticket;
                var result = EssenceRollRules.Choose(kinds, total => { Check(total == 100, "full roll denominator"); return fixedTicket; });
                Check(result.HasValue, "every ticket selects an essence"); counts[(int)result.Value]++;
            }
            Check(counts[(int)EssenceKind.White] == 55 && counts[(int)EssenceKind.Green] == 25 && counts[(int)EssenceKind.Red] == 10
                && counts[(int)EssenceKind.Void] == 3 && counts[(int)EssenceKind.Lunar] == 2 && counts[(int)EssenceKind.Equipment] == 5, "exact 55/25/10/3/2/5 ticket distribution");
            Check(EssenceRollRules.Choose(new[] { EssenceKind.White, EssenceKind.White, EssenceKind.Lunar }, total => { Check(total == 57, "empty/disabled/duplicate categories do not bias denominator"); return 56; }) == EssenceKind.Lunar, "remaining categories renormalize");
            var customWeights = new EssenceWeights(10, 20, 30, 25, 15);
            var customCounts = new int[8];
            for (int ticket = 0; ticket < 100; ticket++)
            {
                int fixedTicket = ticket;
                var result = EssenceRollRules.Choose(kinds, customWeights, total =>
                { if (total != 100) throw new Exception("Custom percentages did not reach the roll."); return fixedTicket; });
                customCounts[(int)result.Value]++;
            }
            Check(customCounts[(int)EssenceKind.White] == 10 && customCounts[(int)EssenceKind.Green] == 20 && customCounts[(int)EssenceKind.Red] == 30
                && customCounts[(int)EssenceKind.Void] == 25 && customCounts[(int)EssenceKind.Lunar] == 15, "every class uses its edited percentage, including distinct void/lunar values");
            var greenOnly = new EssenceWeights(0, 100, 0, 0, 0);
            Check(EssenceRollRules.Choose(kinds, greenOnly, total => 0) == EssenceKind.Green
                && EssenceRollRules.Choose(kinds, greenOnly, total => total - 1) == EssenceKind.Green, "zero-chance classes never roll at either boundary");
            var equalWeights = new EssenceWeights(10, 10, 0, 0, 0);
            Check(EssenceRollRules.Choose(kinds, equalWeights, total => { Check(total == 20, "non-100 total uses normalized denominator"); return 9; }) == EssenceKind.White, "custom white boundary");
            Check(EssenceRollRules.Choose(kinds, equalWeights, total => 10) == EssenceKind.Green, "custom green boundary");
            Check(EssenceRollRules.Choose(new[] { EssenceKind.Red, EssenceKind.Lunar }, customWeights,
                total => { Check(total == 45, "disabled/empty classes are removed from custom total"); return 44; }) == EssenceKind.Lunar, "remaining custom probabilities renormalize");
            Check(!EssenceRollRules.Choose(kinds, new EssenceWeights(0, 0, 0, 0, 0), total => { throw new Exception("All-zero choices must not roll."); }).HasValue, "all zero produces no essence without a random-range error");
            Check(!EssenceRollRules.Choose(new[] { EssenceKind.White }, greenOnly, total => { throw new Exception("Zero-weight available classes must not roll."); }).HasValue, "only unavailable nonzero classes produces no essence");
            Check(!EssenceRollRules.Choose(new[] { EssenceKind.Boss }, total => { throw new Exception("empty pool should not roll"); }).HasValue, "unsupported/empty weighted pool has no offer");
            queue.Enqueue(new RewardOffer { Id = 90, Items = new[] { 10, 11 } });
            Check(!queue.TryConsume(90, 12, false, out used) && queue.First.Id == 90, "weighted offer rejects another essence without consuming or rerolling");
            Check(queue.TryConsume(90, 11, false, out used) && queue.Count == 0, "weighted offer accepts its original pickup once");
            Check(ArenaRules.ContinuedKillTarget(20, 20) == 40, "continue raises 20-kill target to 40");
            Check(ArenaRules.ContinuedKillTarget(40, 40) == 60, "continue can repeat to 60");
            Check(ArenaRules.ContinuedKillTarget(0, 7) == 27, "time-only match continues twenty beyond the leader");
            Check(ArenaRules.ContinuedKillTarget(20, 25) == 45, "overtaken target cannot immediately refinish");
            Check(ArenaRules.ContinuedKillTarget(int.MaxValue - 5, int.MaxValue - 5) == int.MaxValue, "continued target does not overflow");
            Check(ArenaRules.ContinuedDeadline(600, 600) == 1200, "expired time limit extends for another full duration");
            Check(ArenaRules.ContinuedDeadline(70, 0) == 0, "unlimited match stays unlimited");
            Check(ArenaRules.ContinuedDeadline(70, 600) == 670, "early kill win gets a full continuation interval");
            var teams = new TeamSlots<string>(2);
            Check(teams.GetOrAssign("host") == 0, "first player has a combat team");
            Check(teams.GetOrAssign("client") == 1, "opponents never share a team");
            Check(teams.GetOrAssign("host") == 0, "respawn/owned summons retain the owner's team slot");
            Check(teams.GetOrAssign("client") == 1, "reconnection retains its previous team slot");
            Check(teams.GetOrAssign("new player") == -1, "departed identities are not silently recycled");
            teams.Clear(); Check(teams.GetOrAssign("new player") == 0, "next run starts with fresh assignments");
            bool voidRewardsBlocked = true;
            foreach (ItemSelection mode in Enum.GetValues(typeof(ItemSelection)))
            foreach (RespawnRule respawnRule in Enum.GetValues(typeof(RespawnRule)))
            for (int flags = 0; flags < 8; flags++)
            foreach (int amount in new[] { 0, 1, 12 })
            {
                var plan = DeathRewardRules.Create(true, mode, respawnRule, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0, amount, amount);
                voidRewardsBlocked &= !plan.RequiresChoice && plan.VictimRewards == 0 && plan.KillerRewards == 0 && plan.CatchupRewards == 0;
            }
            Check(voidRewardsBlocked, "void farming cannot award death, kill, underdog or mandatory-choice rewards in any mode/toggle combination");
            var ordinaryDeath = DeathRewardRules.Create(false, ItemSelection.Weighted, RespawnRule.TimerAndChoice, true, true, false, 2, 4);
            Check(ordinaryDeath.VictimRewards == 2 && ordinaryDeath.KillerRewards == 4 && ordinaryDeath.CatchupRewards == 0 && ordinaryDeath.RequiresChoice, "death and kill amounts remain independent with no hidden double reward");
            var offDeath = DeathRewardRules.Create(false, ItemSelection.Command, RespawnRule.AfterChoice, false, true, true, 12, 3);
            Check(offDeath.VictimRewards == 0 && !offDeath.RequiresChoice && offDeath.CatchupRewards == 0 && offDeath.KillerRewards == 3, "death toggle off cannot be overridden by respawn or catchup options");
            var zeroDeath = DeathRewardRules.Create(false, ItemSelection.Draft, RespawnRule.AfterChoice, true, true, true, 0, 0);
            Check(zeroDeath.VictimRewards == 0 && zeroDeath.KillerRewards == 0 && zeroDeath.CatchupRewards == 0 && !zeroDeath.RequiresChoice, "zero amounts cannot create free choice-gated items");
            var bonusDeath = DeathRewardRules.Create(false, ItemSelection.Command, RespawnRule.Timer, true, false, true, 1, 12);
            Check(bonusDeath.VictimRewards == 1 && bonusDeath.CatchupRewards == 1 && bonusDeath.KillerRewards == 0 && !bonusDeath.RequiresChoice, "extra death reward is an explicit opt-in bonus only");
            Check(!ArenaRules.CanRespawn(RespawnRule.AfterChoice, zeroDeath.RequiresChoice, false, 4, 5)
                && ArenaRules.CanRespawn(RespawnRule.AfterChoice, zeroDeath.RequiresChoice, false, 5, 5), "void/disabled/zero-reward choices safely use the configured respawn timer");
            // Same generator/weights before and after continuation, exactly as
            // the existing ItemPool retained by ArenaMatch.ContinueGame.
            var uninterrupted = new Random(5792); var continued = new Random(5792);
            bool sequenceUnchanged = true; int reds = 0, other = 0;
            for (int i = 0; i < 500; i++)
            {
                var expected = EssenceRollRules.Choose(kinds, EssenceWeights.Default, total => uninterrupted.Next(total));
                var actual = EssenceRollRules.Choose(kinds, EssenceWeights.Default, total => continued.Next(total));
                sequenceUnchanged &= expected == actual;
                if (i >= 250) { if (actual == EssenceKind.Red) reds++; else other++; }
            }
            Check(sequenceUnchanged && reds > 0 && other > 0, "continued weighted sequence retains its stream and does not latch to red");
            Console.WriteLine("PASS: " + assertions + " arena rule assertions.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }

    private static void BotBuilds()
    {
        Check(SettingsLayout.Profile(ArenaMode.FFA) != SettingsLayout.Profile(ArenaMode.Teams)
            && SettingsLayout.Profile(ArenaMode.KOTH) != SettingsLayout.Profile(ArenaMode.Teams), "each mode has a distinct settings profile");
        string key = SettingsLayout.Key("Reward events", "Rewards per death");
        Check(SettingsLayout.Section(key) == "Reward events" && SettingsLayout.Title(key) == "Rewards per death", "subsection keys round-trip to concise UI labels");
        Check(SettingsLayout.Section("Default map") == "" && SettingsLayout.Title("Default map") == "Default map", "general settings retain their plain labels");
        var common = new[] {
            new BotBuildItem { Pickup = 1, Name = "Syringe" }, new BotBuildItem { Pickup = 2, Name = "CritGlasses" },
            new BotBuildItem { Pickup = 3, Name = "SecondarySkillMagazine" }, new BotBuildItem { Pickup = 4, Name = "Bear" }
        };
        Check(BotBuildRules.Choose(BotBuildKind.OnHit, common, 0, true) == 1, "on-hit build starts with attack speed");
        Check(BotBuildRules.Choose(BotBuildKind.Critical, common, 0, true) == 2, "critical build starts with crit");
        Check(BotBuildRules.Choose(BotBuildKind.SkillCaster, common, 0, true) == 3, "caster build prioritizes secondary charges");
        Check(BotBuildRules.Choose(BotBuildKind.Bulwark, common, 0, true) == 4, "bulwark build prioritizes defence");
        Check(BotBuildRules.ForBody("MageBody", 0, false) == BotBuildKind.SkillCaster, "Artificer uses a caster plan");
        Check(BotBuildRules.ForBody("HuntressBody", 0, false) == BotBuildKind.Critical, "Huntress uses a critical plan");
        Check(BotBuildRules.ForBody("CommandoBody", 2, false) == BotBuildKind.OnHit, "rapid-fire survivors keep an appropriate damage plan");
        Check(BotBuildRules.WantsLevel(4, 1, 3, true) && !BotBuildRules.WantsLevel(5, 2, 3, true), "bots spend each fifth choice reward on legal XP, without repeating it");
        Check(!BotBuildRules.WantsLevel(4, 3, 3, true) && !BotBuildRules.WantsLevel(4, 1, 3, false), "bots obey the host's XP switch and current level cap");
        Check(!BotBuildRules.WantsLevel(0, 1, 3, true), "bots develop starting items before taking levels");
        Check(BotBuildRules.ForBody("CommandoBody", 2, true) == BotBuildKind.Bulwark, "KOTH includes objective holders");
        Check(BotBuildRules.Choose(BotBuildKind.OnHit, new BotBuildItem[0], 0, true) == -1, "empty offer can fall back to legal XP");
        foreach (BotBuildKind build in Enum.GetValues(typeof(BotBuildKind)))
        {
            var onlyWhite = new[] { new BotBuildItem { Pickup = 18, Name = "ArmorPlate" }, new BotBuildItem { Pickup = 19, Name = "UnknownCustomWhite" } };
            int selected = BotBuildRules.Choose(build, onlyWhite, 30, false);
            Check(selected == 18 || selected == 19, build + " cannot choose a red absent from a weighted white offer");
            Check(BotBuildRules.Choose(build, new[] { new BotBuildItem { Pickup = 99, Name = "CustomVoidItem" } }, 4, false) == 99,
                build + " supports a custom-item-only offer");
            var banned = new[] { new BotBuildItem { Pickup = 8, Name = "Syringe", AiAllowed = false }, new BotBuildItem { Pickup = 9, Name = "Other" } };
            Check(BotBuildRules.Choose(build, banned, 4, false) == 9, build + " avoids native AI-blacklisted choices when alternatives exist");
        }
        var rare = new[] { new BotBuildItem { Pickup = 1, Name = "Syringe" }, new BotBuildItem { Pickup = 2, Name = "FireballsOnHit", Rare = true } };
        Check(BotBuildRules.Choose(BotBuildKind.OnHit, rare, 0, true) == 1, "Command builds develop common items before legendary items");
        Check(BotBuildRules.Choose(BotBuildKind.OnHit, rare, 0, false) == 2, "Weighted accepts its rolled rare item without Command pacing");
        common[0].Owned = 6;
        Check(BotBuildRules.Choose(BotBuildKind.OnHit, common, 20, true) != 1, "a completed attack-speed target yields to unmet build goals");
        var tied = new[] { new BotBuildItem { Pickup = 20, Name = "Custom" }, new BotBuildItem { Pickup = 12, Name = "OtherCustom" } };
        Check(BotBuildRules.Choose(BotBuildKind.OnHit, tied, 12, false) == 12, "equal build scores use a stable tie-break");
        Check(float.IsNegativeInfinity(BotBuildRules.TargetScore(1, .1f, true, true, true)), "protected opponents never outrank valid targets");
        Check(BotBuildRules.TargetScore(25, .1f, false, false, false) > BotBuildRules.TargetScore(25, 1, false, false, false), "bots prefer finishing a wounded opponent at equal range");
        Check(BotBuildRules.TargetScore(25, 1, true, false, false) > BotBuildRules.TargetScore(25, 1, false, false, false), "target stickiness avoids constant aim switching");
        Check(BotBuildRules.TargetScore(25, 1, false, true, false) > BotBuildRules.TargetScore(25, 1, false, false, false), "KOTH targets on the objective gain priority");
    }
    private static void BotIntelligenceAndCounters()
    {
        Check(BotTacticsRules.Tier(-20)==BotIntelligence.Low && BotTacticsRules.Tier(99)==BotIntelligence.Ultra,"intelligence clamps to its five supported settings");
        Check(BotTacticsRules.Name(1)=="Medium" && BotTacticsRules.Name(3)=="Extra High" && BotTacticsRules.Name(4)=="Ultra","slider levels have readable labels");
        Check(!BotTacticsRules.Adaptive(BotIntelligence.High) && BotTacticsRules.Adaptive(BotIntelligence.ExtraHigh) && BotTacticsRules.Adaptive(BotIntelligence.Ultra),"only the top two tiers replace fixed builds with adaptive scoring");
        foreach(ArenaMode mode in Enum.GetValues(typeof(ArenaMode)))
            Check(BotTacticsRules.ChaseHumans(true,mode)==(mode==ArenaMode.FFA),"human chase is FFA-only: "+mode);
        Check(!BotTacticsRules.ChaseHumans(false,ArenaMode.FFA),"disabled chase never alters targeting");
        foreach(BotIntelligence tier in Enum.GetValues(typeof(BotIntelligence)))
        {
            Check(!BotTacticsRules.CanFire(false,true,true,false,1,tier) && !BotTacticsRules.CanFire(true,false,true,false,1,tier),tier+" cannot fire at dead, absent or allied targets");
            Check(!BotTacticsRules.CanFire(true,true,false,false,1,tier) && !BotTacticsRules.CanFire(true,true,true,true,1,tier),tier+" does not shoot through walls or into spawn protection");
            Check(!BotTacticsRules.CanFire(true,true,true,false,-1,tier) && BotTacticsRules.CanFire(true,true,true,false,1,tier),tier+" must actually aim at its target before shooting");
        }
        Check(BotTacticsRules.AimDamping(BotIntelligence.Medium)==.18f && BotTacticsRules.AimSpeed(BotIntelligence.Medium)==10,"Medium retains the previous aim parameters");
        Check(BotTacticsRules.AimDamping(BotIntelligence.Ultra)<BotTacticsRules.AimDamping(BotIntelligence.ExtraHigh),"Ultra converges on its aim faster");
        Check(BotTacticsRules.TargetScore(BotIntelligence.High,5,1,false,false,false,1)>BotTacticsRules.TargetScore(BotIntelligence.High,6,.1f,true,true,false,10),"High seeks the closest hostile entity");
        bool held=true;
        for(int i=0;i<100;i++)held=BotTacticsRules.HeldSkill(true,held,true,false);
        Check(held && !BotTacticsRules.HeldSkill(false,held,true,false),"losing the enemy preserves native held skills until the native state releases them");
        Check(!BotTacticsRules.HeldSkill(true,false,true,false) && BotTacticsRules.HeldSkill(true,false,false,false),"missing targets block new offensive utilities while preserving defensive native actions");
        for(int x=-100;x<=100;x+=20)for(int z=-100;z<=100;z+=20)
        {
            float sx,sz;BotTacticsRules.ObjectiveOffset(x,z,25,out sx,out sz);
            Check(sx*sx+sz*sz<=25*25*.55f*.55f+.001f,"all tactical offsets stay within the KOTH capture area, including retreat directions");
        }
        Check(BotTacticsRules.StoppingDistance(30,80,1)>BotTacticsRules.StoppingDistance(7,80,1),"faster movement probes farther ahead for cliffs");
        var fixedContext=new BotBuildContext();fixedContext.Inventory["FireRing"]=2;fixedContext.Inventory["IceRing"]=2;
        var voidBand=new BotBuildItem { Pickup=1,Name="ElementalRingVoid",Corrupts=new[]{"FireRing","IceRing"} };
        Check(!BotBuildRules.FixedCompatible(BotBuildKind.SkillCaster,voidBand,fixedContext),"fixed caster plans never destroy their Runald/Kjaro setup with Singularity Band");
        fixedContext.Inventory.Clear();
        Check(!BotBuildRules.FixedCompatible(BotBuildKind.SkillCaster,voidBand,fixedContext),"a fixed caster cannot sabotage its planned bands before buying them either");
        var plasma=new BotBuildItem { Name="MissileVoid",Corrupts=new[]{"Missile"} };
        fixedContext.PreferVoid=true;
        Check(BotBuildRules.FixedCompatible(BotBuildKind.OnHit,plasma,fixedContext),"a void-route on-hit bot can acquire its planned Plasma Shrimp");
        Check(!BotBuildRules.FixedCompatible(BotBuildKind.OnHit,new BotBuildItem { Name="Missile",ConvertsTo="MissileVoid" },fixedContext),"that route avoids buying an AtG it will only corrupt later");
        fixedContext.Inventory["Missile"]=2;
        Check(!BotBuildRules.FixedCompatible(BotBuildKind.OnHit,plasma,fixedContext),"an unexpected existing ordinary proc build is preserved rather than discarded");
        var adaptive=new BotBuildContext();var hit=new BotBuildItem { Pickup=1,Name="Syringe",Traits=BotItemTraits.Damage };
        var blast=new BotBuildItem { Pickup=2,Name="Behemoth",Rare=true,Traits=BotItemTraits.Damage };
        Check(BotBuildRules.ChooseAdaptive(new[]{hit,blast},adaptive)==2,"adaptive builds are not artificially restricted to common items in Command");
        adaptive.Ultra=true;adaptive.EnemyInventory["BearVoid"]=1;
        Check(BotBuildRules.ChooseAdaptive(new[]{hit,blast},adaptive)==1,"Ultra builds multi-hit damage to break an opponent's Safer Spaces");
        adaptive.EnemyInventory.Clear();adaptive.EnemyInventory["ArmorPlate"]=4;
        Check(BotBuildRules.ChooseAdaptive(new[]{hit,blast},adaptive)==2,"Ultra prefers heavy hits over small repeated hits against flat damage reduction");
        adaptive.EnemyInventory.Clear();adaptive.EnemyInventory["Hoof"]=4;
        Check(BotBuildRules.ChooseAdaptive(new[]{new BotBuildItem { Pickup=3,Name="SlowOnHit" },blast},adaptive)==3,"Ultra can sacrifice raw damage to counter a mobility build");
        adaptive.EnemyInventory.Clear();adaptive.EnemyInventory["CritDamage"]=1;
        Check(BotBuildRules.ChooseAdaptive(new[]{new BotBuildItem { Pickup=4,Name="BearVoid" },blast},adaptive)==4,"Ultra responds to burst/crit equipment with protection");
        adaptive.EnemyInventory.Clear();adaptive.EnemyEquipment.Add("Cleanse");
        var bleed=new BotBuildItem { Pickup=5,Name="BleedOnHitVoid" };
        Check(BotBuildRules.ChooseAdaptive(new[]{hit,bleed},adaptive)==1,"Blast Shower in an opponent's equipment changes Ultra's damage choice");
        adaptive.EnemyEquipment.Clear();adaptive.Shield=.95f;
        Check(BotBuildRules.AdaptiveScore(new BotBuildItem { Name="IncreaseHealing" },adaptive)<BotBuildRules.AdaptiveScore(hit,adaptive),"full shield conversion does not attract useless health-healing upgrades");
        adaptive.Shield=0;adaptive.Crit=100;
        Check(BotBuildRules.AdaptiveScore(new BotBuildItem { Name="CritGlasses" },adaptive)<BotBuildRules.AdaptiveScore(hit,adaptive),"adaptive scoring respects a completed crit setup");
        adaptive.Inventory["FireRing"]=2;adaptive.Inventory["IceRing"]=2;
        Check(BotBuildRules.AdaptiveScore(voidBand,adaptive)<BotBuildRules.AdaptiveScore(blast,adaptive),"adaptive builds include the actual cost of corrupting owned rings");
        Check(BotBuildRules.ChooseAdaptive(new[]{new BotBuildItem { Pickup=77,Name="CustomDamage",Traits=BotItemTraits.Damage }},adaptive)==77,"adaptive scoring handles loaded custom catalog items without selecting unoffered content");
        Check(BotBuildRules.ChooseAdaptive(new[]{new BotBuildItem { Pickup=78,Name="Fireworks",AiAllowed=false }},adaptive)==-1,"AI-incompatible-only offers can be banked instead of wasting a bot's reward");
        Check(BotBuildRules.ChooseAdaptive(new[]{voidBand},adaptive)==-1,"an adaptive bot banks a destructive conversion when it is the only offered item");
        var credits=new RewardQueue();credits.Enqueue(new RewardOffer { Id=500,Items=new[]{1},Catchup=true });
        credits.Enqueue(new RewardOffer { Id=501,Items=new[]{2} });uint arrival=credits.ArrivalRevision;
        Check(!credits.DeferBotOffer(999) && credits.First.Id==500,"stale bot deferral cannot reorder a different offer");
        Check(credits.DeferBotOffer(500) && credits.First.Id==501 && credits.Count==2 && credits.PendingCatchup==1 && credits.ArrivalRevision==arrival,"bot deferral retains exact offers, catch-up credit and arrival identity while letting another class be chosen");
        RewardOffer consumed;
        Check(credits.TryConsume(501,2,false,out consumed) && credits.First.Id==500,"a usable bot reward is consumed without losing the earlier banked incompatible one");
        Check(!credits.DeferBotOffer(500) && credits.TryConsume(500,-1,true,out consumed) && credits.Count==0,"a previously banked bot reward can turn into XP when the host cap allows it");
    }
    private static void RewardArrivalAndVehicleCleanup()
    {
        var bank = new RewardQueue(); var menu = new RewardMenuArrival();
        Check(bank.Enqueue(new RewardOffer { Id = 1, Items = new[] { 101 } }), "first reward is banked");
        menu.Observe(bank.First.Id, bank.ArrivalRevision, false, false, true, false);
        Check(menu.TakeRequest(false), "the first banked reward opens automatically");
        menu.Dismiss();
        menu.Observe(bank.First.Id, bank.ArrivalRevision, false, false, true, false);
        Check(!menu.TakeRequest(false), "Back or double-W does not immediately reopen the same bank");
        uint revision = bank.ArrivalRevision;
        bank.Enqueue(new RewardOffer { Id = 2, Items = new[] { 201 } });
        menu.Observe(bank.First.Id, bank.ArrivalRevision, false, false, true, false);
        Check(bank.First.Id == 1 && bank.ArrivalRevision != revision && menu.TakeRequest(false), "a reward behind a dismissed head opens without changing or rerolling that head");
        bank.Enqueue(new RewardOffer { Id = 3 });
        menu.Observe(bank.First.Id, bank.ArrivalRevision, false, false, true, false);
        Check(!menu.TakeRequest(true) && menu.Requested, "automatic menu waits during a vehicle or critical ability without losing the arrival");
        menu.Observe(bank.First.Id, bank.ArrivalRevision, false, false, true, false);
        Check(menu.TakeRequest(false) && !menu.TakeRequest(false), "a deferred arrival opens exactly once when the ability ends");
        menu.Reset();
        menu.Observe(bank.First.Id, bank.ArrivalRevision, false, false, false, false);
        Check(!menu.TakeRequest(false), "an explicit local opt-out still keeps ordinary rewards manual");
        menu.Observe(bank.First.Id, bank.ArrivalRevision, false, false, true, false);
        Check(menu.TakeRequest(false), "enabling auto-open with banked rewards takes effect immediately");
        menu.Observe(bank.First.Id, bank.ArrivalRevision, true, false, false, false);
        Check(menu.TakeRequest(false), "an already-banked mandatory death choice opens when it becomes required");
        menu.Dismiss();
        menu.Observe(bank.First.Id, bank.ArrivalRevision, true, false, false, false);
        Check(!menu.TakeRequest(false), "dismissing a required choice does not create an input lock loop");
        menu.Observe(bank.First.Id, bank.ArrivalRevision + 1, false, false, true, true);
        Check(!menu.TakeRequest(false), "finished matches never reopen reward panels");
        menu.Observe(0, bank.ArrivalRevision + 2, true, true, true, false);
        Check(!menu.TakeRequest(false), "an empty bank never opens an impossible choice");
        bank.Clear();
        for (uint i = 0; i < RewardQueue.MaximumPending; i++) bank.Enqueue(new RewardOffer { Id = i + 1 });
        revision = bank.ArrivalRevision;
        Check(!bank.Enqueue(new RewardOffer { Id = 50 }) && bank.ArrivalRevision == revision, "a rejected full-bank reward cannot create a phantom arrival");
        bank.Enqueue(new RewardOffer { Id = 51, GatesRespawn = true });
        Check(bank.First.Id == 51 && bank.ArrivalRevision != revision, "a required death reward replacing a full-bank entry is a real arrival");
        Check(WorldCleanupRules.PreserveVehicle(true, true, false, false), "an empty prewarmed Enforcer dash actor survives body and scene cleanup");
        Check(WorldCleanupRules.PreserveVehicle(true, false, true, false), "a Seeker flight actor is retained before its passenger resolves on a peer");
        Check(WorldCleanupRules.PreserveVehicle(true, false, false, true), "an occupied player vehicle is retained");
        Check(!WorldCleanupRules.PreserveVehicle(true, false, false, false), "ordinary unoccupied world vehicles remain subject to cleanup");
        Check(!WorldCleanupRules.PreserveVehicle(false, true, true, true), "non-vehicle interactables and monsters gain no cleanup exemption");
    }
}
