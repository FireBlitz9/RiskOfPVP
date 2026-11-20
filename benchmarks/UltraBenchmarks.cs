using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using ArenaPvP;
using RoR2;
using RoR2.Items;
using UnityEngine;

internal static class UltraBenchmarks
{
    private static ArenaPlayer bot, opponent;
    private static ArenaMatch match;
    private static readonly Func<long> Allocated = (Func<long>)Delegate.CreateDelegate(
        typeof(Func<long>), typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes));

    private static void Check(string group, string label, bool passed)
    { Console.WriteLine("CHECK\t{0}\t{1}\t{2}", group, label, passed ? 1 : 0); }

    private static void Catalog()
    {
        string[] items = { "Syringe", "Behemoth", "BearVoid", "ArmorPlate", "SlowOnHit", "BleedOnHitVoid",
            "HalfAttackSpeedHalfCooldowns", "WyrmOnHit", "ShieldBooster", "PersonalShield", "MissileVoid", "CustomUtility" };
        ItemCatalog.Definitions = Enumerable.Range(0, 600)
            .Select(i => new ItemDef(i < items.Length ? items[i] : "Synthetic" + i, ItemTier.Tier1, i)).ToArray();
        ItemCatalog.Definitions[11].Tags.Add(ItemTag.Utility);
        for (int i = items.Length; i < 600; i++)
            ItemCatalog.Definitions[i].Tags.Add(i % 3 == 0 ? ItemTag.Damage : i % 3 == 1 ? ItemTag.Healing : ItemTag.Utility);
        EquipmentCatalog.Definitions = new[] { new EquipmentDef("Fruit", 0), new EquipmentDef("Cleanse", 1) };
        ContagiousItemManager.transformationInfos = new TransformationInfo[0];
    }

    private static void Roster()
    {
        ArenaBotBuilds.Reset();
        Time.unscaledTime = 10;
        bot = new ArenaPlayer { IsBot = true, Master = new CharacterMaster(), UserId = 1, Side = 0 };
        opponent = new ArenaPlayer { Master = new CharacterMaster(), UserId = 2, Side = 1 };
        opponent.Master.Body.corePosition = new Vector3(10, 0, 0);
        match = new ArenaMatch { Rules = new MatchSettings { Mode = ArenaMode.FFA, BotIntelligence = BotIntelligence.Ultra } };
        match.AllPlayers.Add(bot);
        match.AllPlayers.Add(opponent);
        ArenaMatch.Instance = match;
    }

    private static int Choose(params int[] ids) { return ArenaBotBuilds.Choose(bot, ids, ItemSelection.Command); }

    private static bool Fire(bool alive = true, bool hostile = true, bool visible = true, bool shielded = false, float aim = 1)
    { return BotTacticsRules.CanFire(alive, hostile, visible, shielded, aim, BotIntelligence.Ultra); }

    private static BotMotionIntent Motion(bool visible = true, bool threat = false, bool projectile = false,
        bool grounded = true, bool activeSkill = false, bool safe = true)
    {
        var state = new BotMotionState();
        state.Initialize(0, 0);
        return state.Update(1, BotIntelligence.Ultra, 0, true, grounded, visible || projectile, threat,
            activeSkill, safe, false, false, false
#if UPGRADED
            , reactive: true, projectile: projectile
#endif
        );
    }

    private static void Combat()
    {
        const string group = "Combat guardrails";
        Check(group, "Fire at an aligned visible opponent", Fire());
        Check(group, "Hold fire when the target is hidden", !Fire(visible: false));
        Check(group, "Hold fire against an ally", !Fire(hostile: false));
        Check(group, "Hold fire against a dead target", !Fire(alive: false));
        Check(group, "Respect spawn protection", !Fire(shielded: true));
        Check(group, "Wait for aim to align", !Fire(aim: .80f));
        Check(group, "Keep extra movement off during protected skill recovery", !Motion(activeSkill: true, threat: true).Sprint);
        Check(group, "Avoid extra jumps without ground contact", !Motion(grounded: false, threat: true, projectile: true).Jump);
        Check(group, "Suppress extra movement on unsafe ground", !Motion(safe: false, threat: true).Sprint);
        BotTacticsRules.ObjectiveOffset(45, 0, 10, out float x, out float z);
        Check(group, "Keep a steering goal inside the hill", x * x + z * z <= 100);
        const string adaptation = "Threat response";
        var calm = Motion();
        Check(adaptation, "Keep normal movement with no cue to dodge", calm.DodgeSide == 0);
        Check(adaptation, "Stay grounded when no projectile threatens", !calm.Jump);
        var windup = Motion(threat: true);
        Check(adaptation, "Dodge a threatening windup", windup.DodgeSide != 0);
        Check(adaptation, "Avoid spending a jump on the windup alone", !windup.Jump);
        var shot = Motion(threat: true, projectile: true);
        Check(adaptation, "Try a grounded jump for an incoming projectile", shot.Jump);
        Check(adaptation, "Sprint during quiet travel", Motion(visible: false).Sprint);
    }

    private static void Builds()
    {
        const string group = "Build choices";
        Roster();
        opponent.Master.inventory.Counts[(ItemIndex)2] = 1;
        Check(group, "Prefer rapid hits against Safer Spaces", Choose(0, 1) == 0);
        opponent.Master.inventory.Counts.Clear();
        opponent.Master.inventory.Counts[(ItemIndex)3] = 4;
        Time.unscaledTime += .6f;
        Check(group, "Switch to a heavy-hit choice against flat damage reduction", Choose(0, 1) == 1);
        opponent.Master.inventory.Counts.Clear();
        opponent.Master.inventory.Equipment[0] = (EquipmentIndex)1;
        Time.unscaledTime += .6f;
        Check(group, "Avoid relying on a debuff against Blast Shower", Choose(0, 5) == 0);
        match.Rules.Mode = ArenaMode.Teams;
        opponent.Side = bot.Side;
        Check(group, "Ignore a teammate's Blast Shower when building", Choose(0, 5) == 5);
        Check(group, "Consider an unfamiliar legal catalog item", Choose(11) == 11);
        var settings = new MatchSettings { GlobalBlacklist = "Syringe" };
        var pool = new ItemPool(new Run(), settings);
        Check(group, "Choose within the host-filtered offer", Choose(pool.Available(new[] { 0, 1 }, bot.Master.inventory)) == 1);
        Roster();
        bot.Master.bodyPrefab.name = "MageBody";
#if UPGRADED
        bot.BotKit = new BotKit { Range = 25, Reach = 40, OnHit = .3f, SkillUse = 1 };
#endif
        Check(group, "Prefer a DLC cooldown trade for a skill-heavy kit", Choose(6, 7) == 6);
        bot.Master.bodyPrefab.name = "CommandoBody";
#if UPGRADED
        bot.BotKit = new BotKit { Range = 25, Reach = 40, OnHit = 1, SkillUse = .42f };
#endif
        Check(group, "Prefer a DLC on-hit item for a rapid attacker", Choose(6, 7) == 7);
        int choice;
#if UPGRADED
        choice = ArenaBotBuilds.Choose(bot, new[] { 11 }, ItemSelection.Command, mayLevel: true);
#else
        choice = BotBuildRules.WantsLevel(0, 1, 5, true) ? -1 : Choose(11);
#endif
        Check(group, "Take an available early level over weak filler", choice == -1);
        Roster();
        bool picksDamageFirst = Choose(0, 4) == 0;
#if UPGRADED
        var observed = bot.BotMemory.For(opponent.UserId);
        observed.See(17, Time.unscaledTime, 10, .1f, .9f, 1);
#endif
        Check(group, "Switch from attack speed to a slow after seeing repeated strafing", picksDamageFirst && Choose(0, 4) == 4);
        Roster();
        picksDamageFirst = Choose(0, 8) == 0;
#if UPGRADED
        bot.BotMemory.For(opponent.UserId).Damage(Time.unscaledTime, .55f, received: true);
#endif
        Check(group, "Switch to a shield against a high-burst opponent", picksDamageFirst && Choose(0, 8) == 8);
    }

    private static void TimeWorkload(int size, int offset)
    {
        Roster();
        var offers = Enumerable.Range(0, 12).Select(k =>
            Enumerable.Range(0, size).Select(i => (i + k * (600 / 12)) % 600).ToArray()).ToArray();
        int repetitions = 900;
        for (int i = 0; i < repetitions; i++) Choose(offers[(offset + i) % offers.Length]);
        var watch = new Stopwatch();
        for (int batch = 0; batch < 7; batch++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long start = Allocated();
            int checksum = 0;
            watch.Restart();
            for (int i = 0; i < repetitions; i++) checksum += Choose(offers[(batch + offset + i) % offers.Length]);
            watch.Stop();
            long bytes = Allocated() - start;
            Console.WriteLine("PERF\t{0}\t{1}\t{2:R}\t{3}\t{4}\t{5}", size, batch,
                watch.Elapsed.TotalMilliseconds, bytes, repetitions, checksum);
        }
    }

    public static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Catalog();
        Combat();
        Builds();
        int order = int.Parse(args[0], CultureInfo.InvariantCulture);
        int[] sizes = { 24, 96, 600 };
        for (int i = 0; i < sizes.Length; i++) TimeWorkload(sizes[(i + order) % sizes.Length], order);
        Console.WriteLine("RUNTIME\t" + Environment.Version);
    }
}
