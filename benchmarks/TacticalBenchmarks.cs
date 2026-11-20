using System;
using System.Diagnostics;
using System.Globalization;
using ArenaPvP;

internal static class TacticalBenchmarks
{
    private const float Step = .2f;
    private const float PreferredRange = 24;
#if ULTRA_V1
    // V1 had a preferred movement distance but no engagement-state type.
    private enum BotEngagement { Press, Trade, Hold, Recover }
#else
    private static readonly BotKit Kit = new BotKit { Range = PreferredRange, Reach = 46, OnHit = .85f, SkillUse = .6f };
#endif

    private struct Route
    {
        internal int Id, FirstHop, LegacySlot;
        internal float Travel, Range, HillDistance, AllyDistance, Sector;
        internal bool HasShot, FirstHopExposed, CoveredExit, Connected;
    }

    private sealed class Controller
    {
#if ULTRA_V3
        private readonly BotRoutePlanner routes = new BotRoutePlanner();
        private readonly BotFightTiming timing = new BotFightTiming();
        private readonly BotCastWindow casts = new BotCastWindow();
#elif ULTRA_V1
        private readonly int identity;
        private float preferred = PreferredRange;
#endif
        internal Controller(int identity = 0)
        {
#if ULTRA_V1
            this.identity = identity;
#endif
        }

        internal BotEngagement Feeling(float now, float health, float enemyHealth, float range, float trade,
            float pressure, int enemies, int allies, bool hill, bool ready, bool visible, int body = 1)
        {
#if ULTRA_V3
            return timing.Decide(now, health, enemyHealth, range, Kit, trade, pressure, enemies, allies,
                hill, ready, true, visible, casts.IsOpen(body, now)).Engagement;
#elif ULTRA_V1
            preferred = health < .3f ? 32f : PreferredRange;
            return hill ? BotEngagement.Hold : !visible ? BotEngagement.Press
                : health < .3f ? BotEngagement.Recover : BotEngagement.Trade;
#else
            return visible ? BotAdaptation.Engage(health, enemyHealth, range, Kit, trade, pressure, enemies, allies, hill, ready)
                : BotEngagement.Press;
#endif
        }

        internal void ObserveCast(int body, float now, int stock, int token = 0)
        {
#if ULTRA_V3
            casts.Observe(body, 1, 7, now, 8, stock, token);
#endif
        }

        internal bool Reserve(float now, float health, float pressure, bool hill, bool ready)
        {
#if ULTRA_V3
            return timing.Decide(now, health, .7f, PreferredRange, Kit, 0, pressure, 1, 0, hill,
                ready, true, true, false).ReserveEscape;
#else
            return false;
#endif
        }

        internal Route Choose(Route[] options, BotEngagement mode, float spacing, float hillRadius, float now,
            float remaining, bool visible, bool holding, bool canMove = true)
        {
            float best = float.NegativeInfinity;
            Route selected = options[0]; // Shared safe start anchor.
#if ULTRA_V3
            routes.Progress(now, remaining, canMove);
            foreach (var r in options)
            {
                if (!r.Connected || holding && r.HillDistance > hillRadius) continue;
                var route = new BotRouteOption { Node = r.Id, FirstHop = r.FirstHop, Travel = r.Travel,
                    Range = r.Range, HillDistance = r.HillDistance, AllyDistance = r.AllyDistance, Sector = r.Sector,
                    HasShot = r.HasShot, FirstHopExposed = r.FirstHopExposed, HasCoverExit = r.CoveredExit };
                float score = routes.Score(route, mode, spacing, hillRadius, now);
                if (score <= best) continue;
                best = score;
                selected = r;
            }
            routes.SetGoal(new BotRouteOption { Node = selected.Id, FirstHop = selected.FirstHop }, now, selected.Travel);
#elif ULTRA_V1
            // V1 projected a single point at its preferred spacing plus a
            // sinusoidal sidestep. Approximate that projection with the nearest
            // scripted ground sample. Hidden targets use their pursuit sample.
            if (!visible && hillRadius <= 0) return options[1];
            float lateral = (float)Math.Sin(now * .65f + identity * 2.4f) * 7f;
            if (hillRadius > 0) BotTacticsRules.ObjectiveOffset(0, lateral, hillRadius, out _, out lateral);
            foreach (var r in options)
            {
                if (r.LegacySlot < 0) continue;
                float side = r.LegacySlot == 0 ? 0 : r.LegacySlot == 1 ? 10 : -10;
                float rangeError = r.Range - preferred, sideError = side - lateral;
                float score = -rangeError * rangeError - sideError * sideError;
                if (score <= best) continue;
                best = score;
                selected = r;
            }
            if (hillRadius > 0 && selected.HillDistance > hillRadius) return options[0];
#else
            if (!visible && hillRadius <= 0) return options[1]; // Known target, native path toward that position.
            foreach (var r in options)
            {
                if (r.LegacySlot < 0) continue;
                // v1.3.2 FightGoal's three projected options and unchanged scores.
                float score = -r.Travel * .3f - Math.Abs(r.Range - spacing) * .45f;
                score += (mode == BotEngagement.Recover ? !r.HasShot : r.HasShot) ? 8 : 0;
                if (mode == BotEngagement.Hold && hillRadius == 0 && r.LegacySlot != 0) score += 2;
                if (score <= best) continue;
                best = score;
                selected = r;
            }
#endif
            return selected;
        }
    }

    private static Route Option(int id, int legacy, float travel, float range, bool shot, bool exposed, bool covered = false)
    {
        return new Route { Id = id, FirstHop = id, LegacySlot = legacy, Travel = travel, Range = range,
            HasShot = shot, FirstHopExposed = exposed, CoveredExit = covered, Connected = true, AllyDistance = 10 };
    }

    private static void Result(string group, int seed, bool ok, float metric)
    { Console.WriteLine("CASE\t{0}\t{1}\t{2}\t{3:R}", group, seed, ok ? 1 : 0, metric); }

    private static void Corner(int seed)
    {
        const int deadline = 16;
        var brain = new Controller();
        bool hard = seed >= 32;
        float length = 8 + seed % 5;
        float speed = 1.9f + seed % 3 * .1f;
        float detour = length + (hard ? 7 : 4);
        int closedUntil = seed % 4 == 0 ? 3 : hard && seed % 3 == 0 ? 10 : 18 + seed % 4;
        int alternateClosedUntil = hard ? 5 + seed % 9 : 0;
        // Every deadline must be physically achievable even when the two
        // corridors reopen late. A planner may still choose the wrong one.
        if (closedUntil + Math.Ceiling(length / speed) > deadline)
            alternateClosedUntil = Math.Min(alternateClosedUntil, deadline - (int)Math.Ceiling(detour / speed));
        // The world applies the same collision to both policies. Route 1 looks
        // connected on the graph but its first step is physically blocked.
        var options = new[]
        {
            Option(0, -1, 0, 45, false, false),
            Option(1, 0, length, PreferredRange, true, false),
            Option(2, seed % 5 == 0 ? 1 : -1, detour, PreferredRange + 2, true, false, true),
            Option(3, 2, length + 2, PreferredRange + 11, false, true),
        };
        options[2].FirstHop = 4;
        float remaining = length;
        int direction = -1, blocked = 0, completed = 0;
        for (int tick = 0; tick < deadline; tick++)
        {
            bool partialSight = hard && tick % 5 == 2;
            var option = brain.Choose(options, BotEngagement.Press, PreferredRange, 0, tick * Step, remaining, partialSight, false);
            if (direction != option.Id) { direction = option.Id; remaining = option.Travel; }
            if (direction == 1 && tick < closedUntil) { blocked++; continue; }
            if (direction == 2 && tick < alternateClosedUntil) { blocked++; continue; }
            if (direction == 0 || direction == 3) { blocked++; continue; }
            remaining -= speed;
            if (remaining <= 0) { completed = tick + 1; break; }
        }
        Result("Corner chase", seed, completed > 0, blocked * Step);
    }

    private static void Retreat(int seed)
    {
        var brain = new Controller();
        float distance = 21 + seed % 7;
        float HP = .34f + (seed % 5) * .027f;
        var options = new[]
        {
            Option(0, -1, 0, distance, true, true),
            Option(1, 0, 6, distance + 7, true, true),
            Option(2, 1, 8 + seed % 3, distance + 13, false, false, true),
            Option(3, -1, 6 + seed % 4, distance + 10, false, false, true),
        };
        var retreat = brain.Feeling(0, HP, .8f, distance, -.4f, .7f, 1, 0, false, false, true);
        var first = brain.Choose(options, retreat, 36, 0, 0, 0, true, false);
        int exposure = first.HasShot ? 1 : 0, resets = 0;
        bool crossfire = seed >= 32 && seed % 3 != 0;
        bool fleeingTarget = seed >= 32 && seed % 4 == 0;
        // The first corner breaks sight. Some enemies briefly reappear before
        // the bot recovers, so one hidden frame is not enough to pass.
        for (int tick = 1; tick <= 5; tick++)
        {
            bool visible = tick == 3 && seed % 3 == 0 || fleeingTarget && tick >= 3;
            float foe = fleeingTarget && tick >= 3 ? .12f : .8f;
            var mode = brain.Feeling(tick * Step, HP + tick * .035f, foe, distance,
                visible ? -.4f : 0, visible ? .7f : 0, 1, 0, false, tick >= 4, visible);
            // The second lane is briefly covered by another opponent. The
            // shorter exit opens again one step earlier than the long exit.
            if (crossfire) { options[2].HasShot = tick <= 2; options[3].HasShot = tick == 1; }
            var option = brain.Choose(options, mode, 36, 0, tick * Step, 0, visible, false);
            if (option.HasShot) exposure++;
            if (mode == BotEngagement.Recover && !option.HasShot) resets++;
        }
        Result("Retreat under pressure", seed, resets >= (crossfire ? 3 : 4) && exposure <= (crossfire ? 2 : 1), exposure * Step);
    }

    private static void Hill(int seed)
    {
        int count = 2 + seed % 3;
        const float radius = 12;
        var brains = new Controller[count];
        var occupancy = new int[count];
        var next = new int[count];
        var movingTo = new int[count];
        var travel = new int[count];
        bool hard = seed >= 32;
        for (int i = 0; i < count; i++)
        {
            brains[i] = new Controller(i);
            occupancy[i] = hard ? 1 + (seed + i * 2) % 6 : 0;
            movingTo[i] = occupancy[i];
        }
        for (int step = 0; step < 9; step++)
        {
            int offset = (seed + (step >= 4 ? 1 : 0)) % 6;
            for (int i = 0; i < count; i++)
            {
                int preferred = (i * 6 / count + offset) % 6;
                var options = new Route[7];
                options[0] = Option(0, 0, occupancy[i] == 0 ? 0 : 6, PreferredRange + 1, true, false);
                options[0].AllyDistance = 0;
                for (int lane = 0; lane < 6; lane++)
                {
                    // After relocation, one old angle no longer covers the
                    // hill. The opponent's two approaches share a lane.
                    int sharedLane = (offset + 1) % 6;
                    int legacy = lane == sharedLane ? 1
                        : lane == (hard ? preferred : (sharedLane + 5) % 6) && (hard || i % 2 == 1) ? 2 : -1;
                    var r = Option(lane + 1, legacy, occupancy[i] == lane + 1 ? 0 : 5 + ((seed + lane + i) % 3),
                        PreferredRange + (lane == sharedLane ? 0 : 1.5f), lane != (offset + 4) % 6, true, lane == sharedLane);
                    r.HillDistance = 7 + (lane == (offset + 5) % 6 ? 1 : 0);
                    if (hard && step >= 4 && lane == (offset + seed % 3) % 6) r.HillDistance = radius + 2;
                    r.Sector = (float)Math.Cos((lane - preferred) * Math.PI / 3);
                    for (int p = 0; p < count; p++)
                        if (p != i && occupancy[p] == lane + 1) r.AllyDistance = 0;
                    options[lane + 1] = r;
                }
                int chosen = brains[i].Choose(options, BotEngagement.Hold, PreferredRange, radius, step * Step, 0, true, true).Id;
                if (movingTo[i] != chosen) { movingTo[i] = chosen; travel[i] = hard ? 1 + (i + seed) % 3 : 1; }
                next[i] = travel[i] == 0 || --travel[i] == 0 ? movingTo[i] : occupancy[i];
            }
            Array.Copy(next, occupancy, count);
        }
        int occupied = 0, blockedLane = ((seed + 1) % 6 + 4) % 6 + 1;
        int outside = hard ? ((seed + 1) % 6 + seed % 3) % 6 + 1 : -1;
        for (int lane = 1; lane <= 6; lane++)
            if (lane != blockedLane && lane != outside && Array.IndexOf(occupancy, lane) >= 0) occupied++;
        Result("Contested hill lanes", seed, occupied >= Math.Min(3, count), occupied);
    }

    private static void Cooldowns(int seed)
    {
        var brain = new Controller();
        int stock = seed % 5 == 0 ? 2 : 1;
        float at = 2 + seed % 7 * .15f;
        brain.ObserveCast(1, at, stock, 17);
        brain.ObserveCast(1, at + .55f, stock, 17); // The same cast seen through a second peek.
        const float health = .7f;
        bool noEarlyPress = brain.Feeling(at + .25f, health, .78f, PreferredRange, 0, .15f, 1, 0, false, true, true) != BotEngagement.Press;
        bool usesWindow = brain.Feeling(at + .8f, health, .78f, PreferredRange, 0, .15f, 1, 0, false, true, true) == BotEngagement.Press;
        bool ignoresAfterRespawn = brain.Feeling(at + 1, health, .78f, PreferredRange, 0, .15f, 1, 0, false, true, true, 2) != BotEngagement.Press;
        bool noLatePress = brain.Feeling(at + 3, health, .78f, PreferredRange, 0, .15f, 1, 0, false, true, true) != BotEngagement.Press;
        bool holdsEscape = brain.Reserve(at + 3.2f, .38f, .26f, true, false);
        int checks = (noEarlyPress ? 1 : 0) + (usesWindow == (stock == 1) ? 1 : 0) + (ignoresAfterRespawn ? 1 : 0)
            + (noLatePress ? 1 : 0) + (holdsEscape ? 1 : 0);
        int expected = 5;
        if (seed >= 32)
        {
            expected += 3;
            var second = new Controller();
            second.ObserveCast(1, at, 1, 17);
            second.ObserveCast(1, at + 1.2f, 1, 18);
            if (second.Feeling(at + 1.75f, health, .78f, PreferredRange, 0, .15f, 1, 0, false, true, true) != BotEngagement.Press) checks++;
            if (brain.Feeling(at + .8f, .48f, .78f, PreferredRange, -.28f, .42f, 3, 0, false, true, true) != BotEngagement.Press) checks++;
            if (brain.Feeling(at + .8f, health, .78f, PreferredRange, 0, .15f, 1, 0, false, false, true) != BotEngagement.Press) checks++;
        }
        Result("Combat timing", seed, checks == expected, (float)checks / expected);
    }

    private sealed class BuildObservations
    {
#if ULTRA_V1
        private float speed = 7f;
#else
        private readonly BotOpponent opponent = new BotOpponent();
#endif

        internal void See(float now, float movement, float strafe)
        {
#if ULTRA_V1
            speed = movement;
#else
            opponent.See(1, now, movement, .10f, strafe, .65f);
#endif
        }

        internal void Hit(float now, float damage)
        {
#if !ULTRA_V1
            opponent.Damage(now, damage, true);
#endif
        }

        internal BotBuildContext At(float now)
        {
#if ULTRA_V1
            // V1 read the current build and body stats. The test opponent has
            // constant damage/regen stats and changes behavior during combat.
            return new BotBuildContext { Ultra = true, EnemyMoveSpeed = speed, EnemyHealing = .01f };
#else
            var c = new BotBuildContext { Ultra = true, AttackAffinity = .9f, EnemyMoveSpeed = opponent.Speed,
                EnemyStrafe = opponent.Strafe * opponent.Familiarity(now), EnemyHealing = opponent.Sustain * opponent.Familiarity(now),
                EnemyDamage = 1 + opponent.Pressure(now) };
#if ULTRA_V3
            float weight = opponent.Trade(now) < -.15f ? 1f : .85f;
            c.MobilityEvidence = opponent.MobilityEvidence(now) * weight;
            c.BurstEvidence = opponent.BurstEvidence(now) * weight;
#endif
            return c;
#endif
        }
    }

    private static void Counters(int seed)
    {
        var observed = new BuildObservations();
        var choices = new[] { new BotBuildItem { Pickup = 1, Name = "Syringe" }, new BotBuildItem { Pickup = 2, Name = "SlowOnHit" },
            new BotBuildItem { Pickup = 3, Name = "ShieldBooster" } };
        bool initial = BotBuildRules.ChooseAdaptive(choices, observed.At(0)) == 1;
        float delta = .23f + seed % 4 * .02f;
        float sidestep = seed >= 32 && seed % 3 == 0 ? .70f : .61f + (seed % 3) * .01f;
        for (int i = 0; i < 60; i++)
            observed.See(i * delta, 8, sidestep - (seed >= 32 && i % 5 <= 1 ? .27f : 0));
        float at = 59 * delta;
        bool caughtKite = BotBuildRules.ChooseAdaptive(choices, observed.At(at)) == 2;
        for (int i = 1; i <= 10; i++)
        {
            observed.See(at + i * delta, 4, .03f);
            observed.Hit(at + i * delta, .22f + seed % 3 * .02f);
        }
        at += 10 * delta + (seed >= 32 ? 6 + seed % 11 : 10);
        bool caughtBurst = BotBuildRules.ChooseAdaptive(choices, observed.At(at)) == 3;
        bool forgot = BotBuildRules.ChooseAdaptive(choices, observed.At(at + 65)) == 1;
        int checks = (initial ? 1 : 0) + (caughtKite ? 1 : 0) + (caughtBurst ? 1 : 0) + (forgot ? 1 : 0);
        Result("Counter-build over time", seed, checks == 4, checks);
    }

    private static int Batch(Controller[] brains, Route[] options, int ticks)
    {
        int checksum = 0;
        for (int tick = 0; tick < ticks; tick++)
        {
            float now = tick * Step;
            for (int i = 0; i < brains.Length; i++)
            {
                var controller = brains[i];
                bool visible = (tick + i) % 4 != 1;
                bool attackReady = (tick + i) % 3 != 1;
                if ((tick + i) % 17 == 0 && visible) controller.ObserveCast(1, now, 1);
                var mode = controller.Feeling(now, .5f + (tick % 6) * .05f, .75f, 22, -.13f, .38f,
                    1 + (tick + i) % 2, i % 2, false, attackReady, visible);
                checksum += controller.Choose(options, mode, 24, 0, now, 5, visible, false).Id;
            }
        }
        return checksum;
    }

    private static void Performance(int count)
    {
        var options = new Route[12];
        for (int i = 0; i < options.Length; i++)
            options[i] = Option(i, i < 3 ? i : -1, 3 + i * 2, 19 + i, i % 3 != 1, i % 2 == 1, i % 2 == 0);
        var controllers = new Controller[count];
        for (int i = 0; i < count; i++) controllers[i] = new Controller(i);
        const int ticks = 1800;
        Batch(controllers, options, ticks);
        var timer = new Stopwatch();
        for (int run = 0; run < 7; run++)
        {
            for (int i = 0; i < count; i++) controllers[i] = new Controller(i);
            timer.Restart();
            int checksum = Batch(controllers, options, ticks);
            timer.Stop();
            Console.WriteLine("PERF\t{0}\t{1}\t{2:R}\t{3}\t{4}", count, run, timer.Elapsed.TotalMilliseconds, ticks, checksum);
        }
    }

    public static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        for (int seed = 0; seed < 48; seed++)
        {
            Corner(seed);
            Retreat(seed);
            Hill(seed);
            Cooldowns(seed);
            Counters(seed);
        }
        int order = int.Parse(args[0], CultureInfo.InvariantCulture);
        int[] counts = { 1, 8, 24 };
        for (int i = 0; i < counts.Length; i++) Performance(counts[(i + order) % counts.Length]);
    }
}
