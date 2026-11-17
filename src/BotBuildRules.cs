using System;

namespace ArenaPvP
{
    public enum BotBuildKind { OnHit, Critical, SkillCaster, Bulwark }
    public sealed class BotBuildItem
    {
        public int Pickup, Owned;
        public string Name;
        public bool Equipment, Rare, AiAllowed = true;
    }
    public sealed class BotBuildGoal
    {
        public readonly string Item;
        public readonly int Count, After;
        public readonly float Weight;
        public BotBuildGoal(string item, int count, float weight, int after = 0)
        { Item = item; Count = count; Weight = weight; After = after; }
    }
    // Saved build plans spend ordinary rewards, never grant a full free loadout.
    public static class BotBuildRules
    {
        private static BotBuildGoal G(string item, int count, float weight, int after = 0) { return new BotBuildGoal(item, count, weight, after); }
        public static readonly BotBuildGoal[] Shared = {
            G("Hoof", 2, 105), G("Bear", 2, 100), G("Medkit", 1, 94), G("SprintBonus", 1, 48),
            G("ArmorPlate", 2, 46), G("OutOfCombatArmor", 1, 64), G("BearVoid", 1, 98), G("Knurl", 2, 64),
            G("Pearl", 2, 45), G("ShinyPearl", 1, 70), G("Fruit", 1, 60, 6), G("GainArmor", 1, 42, 6)
        };
        public static readonly BotBuildGoal[][] Plans = {
            new[] { G("Syringe",6,100), G("BleedOnHit",8,89), G("Missile",2,105), G("ChainLightning",1,88), G("Seed",2,65), G("StickyBomb",3,58), G("PersonalShield",1,55),
                G("MissileVoid",2,115), G("ChainLightningVoid",2,118), G("BleedOnHitVoid",3,92), G("FireballsOnHit",1,110), G("LightningStrikeOnHit",1,105),
                G("Clover",1,110,14), G("Behemoth",1,90,12), G("ArmorReductionOnHit",1,95,12), G("LunarDagger",1,30,24) },
            new[] { G("CritGlasses",9,110), G("Syringe",4,85), G("AttackSpeedOnCrit",2,108), G("HealOnCrit",2,105), G("BleedOnHit",4,56),
                G("BleedOnHitAndExplode",1,125), G("CritDamage",1,115,12), G("Clover",1,90,16), G("MissileVoid",1,88), G("ChainLightningVoid",1,84), G("LunarDagger",1,30,24) },
            new[] { G("SecondarySkillMagazine",3,112), G("CritGlasses",5,65), G("Crowbar",4,72), G("SlowOnHit",1,55), G("Feather",1,70), G("FireRing",2,112), G("IceRing",2,108),
                G("AlienHead",2,120,10), G("UtilitySkillMagazine",1,110,10), G("EquipmentMagazineVoid",2,125), G("ElementalRingVoid",1,105),
                G("ChainLightningVoid",1,90), G("LunarBadLuck",1,105), G("Behemoth",1,80,14) },
            new[] { G("Bear",5,130), G("ArmorPlate",4,100), G("Medkit",3,100), G("Syringe",3,78), G("Seed",3,110), G("Infusion",2,74),
                G("Thorns",2,100), G("OutOfCombatArmor",2,115), G("MushroomVoid",2,105), G("BearVoid",2,130), G("Knurl",3,120),
                G("IncreaseHealing",1,115,10), G("BarrierOnOverHeal",1,75,16), G("ShockNearby",1,80,14), G("LunarBadLuck",1,40) }
        };
        public static BotBuildKind ForBody(string body, int identity, bool hill)
        {
            if (hill && identity % 3 == 2) return BotBuildKind.Bulwark;
            if (body == "MageBody" || body == "MercBody" || body == "LoaderBody" || body == "SeekerBody" || body == "FalseSonBody" || body == "CrocoBody") return identity % 3 == 1 ? BotBuildKind.Bulwark : BotBuildKind.SkillCaster;
            if (body == "HuntressBody" || body == "RailgunnerBody" || body == "Bandit2Body") return identity % 3 == 1 ? BotBuildKind.OnHit : BotBuildKind.Critical;
            if (body == "EngiBody") return identity % 3 == 1 ? BotBuildKind.OnHit : BotBuildKind.Bulwark;
            if (body == "CommandoBody" || body == "CaptainBody" || body == "ToolbotBody" || body == "TreebotBody" || body == "ChefBody") return identity % 3 == 1 ? BotBuildKind.Critical : BotBuildKind.OnHit;
            return (BotBuildKind)(identity % 4);
        }
        public static string Name(BotBuildKind kind)
        { return kind == BotBuildKind.OnHit ? "On-hit" : kind == BotBuildKind.SkillCaster ? "Skill caster" : kind.ToString(); }
        public static bool WantsLevel(int spent, int level, int cap, bool enabled)
        { return enabled && level < cap && spent >= 4 && spent % 5 == 4; }
        public static int Choose(BotBuildKind kind, BotBuildItem[] offered, int earned, bool command)
        {
            int best = -1; float score = float.NegativeInfinity;
            foreach (var item in offered)
            {
                float value = item.AiAllowed ? 4f / (1 + item.Owned) : 0.001f;
                foreach (var goal in Shared) value = Math.Max(value, Score(goal, item, earned, command));
                foreach (var goal in Plans[(int)kind]) value = Math.Max(value, Score(goal, item, earned, command));
                if (command && item.Rare && earned < 8) value *= 0.02f;
                if (item.Equipment && (earned < 6 || item.Owned > 0)) value *= 0.001f;
                if (!item.AiAllowed) value *= 0.001f;
                if (value > score || value == score && item.Pickup < best) { score = value; best = item.Pickup; }
            }
            return best;
        }
        private static float Score(BotBuildGoal goal, BotBuildItem item, int earned, bool command)
        {
            if (item.Name != goal.Item || item.Owned >= goal.Count) return 0;
            return goal.Weight / (1 + item.Owned * 0.55f) * (command && earned < goal.After ? 0.01f : 1f);
        }
        public static float TargetScore(float distance, float healthFraction, bool current, bool onObjective, bool protectedTarget)
        { return protectedTarget ? float.NegativeInfinity : 100 - distance * 0.6f + (1 - healthFraction) * 22 + (current ? 9 : 0) + (onObjective ? 24 : 0); }
    }
}
