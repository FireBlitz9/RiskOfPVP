using System;
using System.Collections.Generic;

namespace ArenaPvP
{
    public enum BotBuildKind { OnHit, Critical, SkillCaster, Bulwark }
    public sealed class BotBuildItem
    {
        public int Pickup, Owned;
        public string Name;
        public bool Equipment, Rare, AiAllowed = true;
        public string[] Corrupts = new string[0];
        public string ConvertsTo = "";
        public BotItemTraits Traits;
    }
    [Flags]
    public enum BotItemTraits { None = 0, Damage = 1, Healing = 2, Utility = 4, OnKill = 8, Sprint = 16, Equipment = 32, Interactable = 64 }
    public sealed class BotBuildContext
    {
        public readonly Dictionary<string,int> Inventory = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string,int> EnemyInventory = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string,int> EquipmentInventory = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> EnemyEquipment = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool PreferVoid, Hill, Ultra, HasEquipment;
        public string CurrentEquipment="";
        public float Health = 1, Shield, Crit, AttackAffinity = 1, ProcPower, MoveSpeed = 7, Armor, EnemyArmor, EnemyHealing, EnemyMoveSpeed=7, EnemyDamage = 1;
        public int Count(string name) { int n; return Inventory.TryGetValue(name, out n) ? n : 0; }
        public int EnemyCount(string name) { int n; return EnemyInventory.TryGetValue(name,out n) ? n : 0; }
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
                G("AlienHead",2,120,10), G("UtilitySkillMagazine",1,110,10), G("EquipmentMagazineVoid",2,125),
                G("ChainLightningVoid",1,90), G("Behemoth",1,80,14) },
            new[] { G("Bear",5,130), G("ArmorPlate",4,100), G("Medkit",3,100), G("Syringe",3,78), G("Seed",3,110), G("Infusion",2,74),
                G("Thorns",2,100), G("OutOfCombatArmor",2,115), G("MushroomVoid",2,105), G("BearVoid",2,130), G("Knurl",3,120),
                G("IncreaseHealing",1,115,10), G("BarrierOnOverHeal",1,75,16), G("ShockNearby",1,80,14) }
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
        public static int Choose(BotBuildKind kind, BotBuildItem[] offered, int earned, bool command, BotBuildContext context = null)
        {
            int best = -1; float score = float.NegativeInfinity;
            foreach (var item in offered)
            {
                if (context != null && !FixedCompatible(kind, item, context)) continue;
                float value = item.AiAllowed ? 4f / (1 + item.Owned) : 0.001f;
                foreach (var goal in Shared) value = Math.Max(value, Score(goal, item, earned, command));
                foreach (var goal in Plans[(int)kind]) value = Math.Max(value, Score(goal, item, earned, command));
                if (context != null && context.Count("MissileVoid") > 0 && context.Shield <= 0 && item.Name == "PersonalShield" && item.Owned == 0)
                    value = Math.Max(value,175);
                if (command && item.Rare && earned < 8) value *= 0.02f;
                if (item.Equipment && (earned < 6 || item.Owned > 0)) value *= 0.001f;
                if (!item.AiAllowed) value *= 0.001f;
                if (value > score || value == score && item.Pickup < best) { score = value; best = item.Pickup; }
            }
            return best;
        }
        public static bool FixedCompatible(BotBuildKind kind, BotBuildItem item, BotBuildContext context)
        {
            if (item.Equipment && context.HasEquipment && (item.Owned>0 || context.CurrentEquipment=="Fruit" || context.CurrentEquipment=="GainArmor")) return false;
            if (!item.AiAllowed || (item.Traits & BotItemTraits.Interactable) != 0) return false;
            if (context.Shield > .85f && (item.Name == "Medkit" || item.Name == "Seed" || item.Name == "HealOnCrit" || item.Name == "IncreaseHealing" || item.Name == "BarrierOnOverHeal")) return false;
            if (item.Name == "LunarBadLuck" && (context.ProcPower > 0 || context.Crit > 0)) return false;
            if (item.ConvertsTo.Length > 0 && (context.Count(item.ConvertsTo) > 0 || VoidRoute(kind, item.Name, context.PreferVoid))) return false;
            foreach (string source in item.Corrupts)
                if (context.Count(source) > 0 || (Planned(kind,source) && !VoidRoute(kind,source,context.PreferVoid))) return false;
            return true;
        }
        private static bool Planned(BotBuildKind kind, string name)
        {
            foreach (var goal in Shared) if (goal.Item == name) return true;
            foreach (var goal in Plans[(int)kind]) if (goal.Item == name) return true;
            return false;
        }
        private static bool VoidRoute(BotBuildKind kind, string source, bool prefer)
        {
            if (!prefer || source == "FireRing" || source == "IceRing" || source == "CritGlasses") return false;
            if (source == "BleedOnHit" && kind == BotBuildKind.Critical) return false;
            return source == "Bear" || source == "Missile" || source == "ChainLightning" || source == "BleedOnHit" || source == "Mushroom";
        }
        public static int ChooseAdaptive(BotBuildItem[] offered, BotBuildContext context)
        {
            int best = -1; float score = float.NegativeInfinity;
            foreach (var item in offered)
            {
                float value = AdaptiveScore(item,context);
                if (float.IsNegativeInfinity(value) || float.IsNaN(value)) continue;
                if (value > score || value == score && (best < 0 || item.Pickup < best)) { best=item.Pickup; score=value; }
            }
            return best;
        }
        public static float AdaptiveScore(BotBuildItem item, BotBuildContext c)
        {
            if (!item.AiAllowed || (item.Traits & BotItemTraits.Interactable) != 0) return float.NegativeInfinity;
            if (item.ConvertsTo.Length > 0 && c.Count(item.ConvertsTo) > 0) return float.NegativeInfinity;
            if (item.Name == "LunarBadLuck" && (c.ProcPower > .1f || c.Crit > 10 || c.Count("Clover") > 0)) return float.NegativeInfinity;
            float value = Utility(item,c);
            if (c.Ultra) value=CounterValue(item,c,value);
            if (item.Equipment && c.HasEquipment && c.CurrentEquipment.Length>0)
            {
                var current=new BotBuildItem { Name=c.CurrentEquipment,Equipment=true };
                float currentValue=Utility(current,c);if(c.Ultra)currentValue=CounterValue(current,c,currentValue);
                if(value<=currentValue*1.1f)return float.NegativeInfinity;
                value-=currentValue;
            }
            // Compare the proposed conversion with what the bot already built.
            // Tier alone never makes a void conversion an automatic upgrade.
            foreach (string source in item.Corrupts)
            {
                int count = c.Count(source);
                if (count > 0) value -= count * (source == "FireRing" || source == "IceRing" ? .35f : .12f);
            }
            return value>0 ? value : float.NegativeInfinity;
        }
        private static float CounterValue(BotBuildItem i,BotBuildContext c,float value)
        {
            float once=1f/(1+i.Owned);
            bool multiHit=c.EnemyCount("BearVoid")>0 || c.EnemyCount("Bear")>=3;
            bool flatArmor=c.EnemyCount("ArmorPlate")>=2;
            bool mobility=c.EnemyMoveSpeed>12 || c.EnemyCount("Hoof")>=3 || c.EnemyCount("SprintBonus")>=2 || c.EnemyCount("MushroomVoid")>0;
            bool healing=c.EnemyHealing>.02f || c.EnemyCount("IncreaseHealing")>0 || c.EnemyCount("HealOnCrit")>0 || c.EnemyCount("Seed")>=2 || c.EnemyCount("MushroomVoid")>0 || c.EnemyEquipment.Contains("Fruit");
            bool burst=c.EnemyCount("CritDamage")>0 || c.EnemyCount("FireRing")>0 || c.EnemyCount("IceRing")>0 || c.EnemyCount("CritGlasses")>=6;
            bool procs=c.EnemyCount("MissileVoid")>0 || c.EnemyCount("ChainLightningVoid")>0 || c.EnemyCount("BleedOnHit")>=4;
            if(multiHit && (i.Name=="Syringe" || i.Name=="MissileVoid" || i.Name=="ChainLightningVoid"))value=value*1.8f+.25f*once;
            if(flatArmor && (i.Name=="Syringe" || i.Name=="BleedOnHit" || i.Name=="BleedOnHitVoid"))value*=.6f;
            if(flatArmor && (i.Name=="Behemoth" || i.Name=="FireRing" || i.Name=="IceRing" || i.Name=="Crowbar"))value=value*1.45f+.12f*once;
            if(c.EnemyArmor>25 && (i.Name=="ArmorReductionOnHit" || i.Name=="PermanentDebuffOnHit"))value+=.35f*once;
            if(mobility && (i.Name=="SlowOnHit" || i.Name=="SlowOnHitVoid"))
                value+=(c.Count("SlowOnHit")+c.Count("SlowOnHitVoid")==0 ? .6f : .1f)*once;
            if(healing && (i.Name=="BleedOnHitVoid" || i.Name=="ChainLightningVoid" || i.Name=="BleedOnHit"))value+=.25f*once;
            if(burst && (i.Name=="BearVoid" || i.Name=="OutOfCombatArmor" || i.Name=="Knurl"))value+=.35f*once;
            if(procs && (i.Name=="ArmorPlate" || i.Name=="Bear"))value+=.3f*once;
            if(c.EnemyEquipment.Contains("Cleanse") && (i.Name=="BleedOnHit" || i.Name=="BleedOnHitVoid" || i.Name=="DeathMark"))value*=.65f;
            return value;
        }
        private static float Utility(BotBuildItem i, BotBuildContext c)
        {
            float n = i.Owned, attack = Math.Max(.15f,c.AttackAffinity), danger = 1 + Math.Min(1, Math.Max(0,1-c.Health)) + (c.Hill ? .3f : 0);
            float value;
            switch (i.Name)
            {
                case "Syringe": value=.3f*attack/(1+.15f*n); break;
                case "CritGlasses": value=c.Crit>=100 ? .002f : .35f*(1+c.Count("CritDamage"))/(1+c.Crit*.01f); break;
                case "CritDamage": value=.65f*Math.Min(1,c.Crit*.01f)/(1+n); break;
                case "AttackSpeedOnCrit": value=.5f*Math.Min(1,c.Crit*.03f)*attack/(1+n); break;
                case "HealOnCrit": value=c.Shield>.85f ? .002f : .35f*Math.Min(1,c.Crit*.025f)*danger/(1+n); break;
                case "Behemoth": value=n==0 ? .7f : .04f/(1+n); break;
                case "Missile": value=.48f*attack/(1+.5f*n); break;
                case "MissileVoid": value=c.Shield>0 || c.Count("PersonalShield")>0 ? .65f*attack/(1+.4f*n) : .015f; break;
                case "ChainLightning": value=(c.Hill ? .42f : .25f)*attack/(1+.5f*n); break;
                case "ChainLightningVoid": value=.58f*attack/(1+.5f*n); break;
                case "SlowOnHit": value=.25f/(1+n); break;
                case "SlowOnHitVoid": value=.33f/(1+n); break;
                case "BleedOnHit": value=c.Count("BleedOnHitVoid")>0 || n>=10 ? .002f : .33f*attack/(1+.2f*n); break;
                case "BleedOnHitVoid": value=.42f*attack/(1+.5f*n); break;
                case "BleedOnHitAndExplode": value=(.2f+Math.Min(1,c.Crit*.01f)*.5f)/(1+n); break;
                case "Clover": value=c.Count("LunarBadLuck")>0 ? .002f : Math.Min(1,c.ProcPower)*.75f/(1+n); break;
                case "ArmorReductionOnHit": value=(.25f+Math.Max(0,c.EnemyArmor)*.005f)*attack/(1+n); break;
                case "FireRing": case "IceRing": value=.48f*(2- Math.Min(1,attack))/(1+.6f*n); break;
                case "ElementalRingVoid": value=(c.Hill ? .32f : .16f)/(1+n); break;
                case "AlienHead": value=.52f*(2-Math.Min(1,attack))/(1+.4f*n); break;
                case "SecondarySkillMagazine": case "UtilitySkillMagazine": value=.45f*(2-Math.Min(1,attack))/(1+n); break;
                case "Hoof": value=c.MoveSpeed<12 ? .35f/(1+.25f*n) : .06f/(1+n); break;
                case "Feather": value=n==0 ? .28f : .035f/(1+n); break;
                case "Bear": value=.35f*danger/(1+.15f*n); break;
                case "BearVoid": value=.53f*danger/(1+.6f*n); break;
                case "ArmorPlate": value=.28f*danger/(1+.4f*n); break;
                case "Medkit": case "Seed": case "MushroomVoid": case "Knurl": value=c.Shield>.85f ? .01f : .29f*danger/(1+.5f*n); break;
                case "IncreaseHealing": value=c.Shield>.85f ? .001f : .55f*Math.Min(1,(c.Count("Seed")+c.Count("HealOnCrit")+c.Count("Medkit"))*.3f)*danger/(1+n); break;
                case "BarrierOnOverHeal": value=c.Shield>.85f ? .001f : .35f*Math.Min(1,c.Count("IncreaseHealing"))*danger/(1+n); break;
                case "PersonalShield": value=(c.Count("MissileVoid")>0 ? .6f : .18f)*danger/(1+.2f*n); break;
                case "ShockNearby": value=(c.Hill ? .7f : .45f)/(1+n); break;
                case "Fruit": value=c.Shield>.85f ? .005f : .42f*danger; break;
                case "GainArmor": value=.5f*danger; break;
                case "Lightning": value=.65f; break;
                case "CommandMissile": value=.55f; break;
                case "BFG": value=.7f; break;
                case "CritOnUse": value=.5f*Math.Max(0,100-c.Crit)/100; break;
                default:
                    value=.055f;
                    if ((i.Traits & BotItemTraits.Damage)!=0) value+=.14f;
                    if ((i.Traits & BotItemTraits.Healing)!=0) value+=c.Shield>.85f ? .002f : .12f*danger;
                    if ((i.Traits & BotItemTraits.Utility)!=0) value+=.08f;
                    if ((i.Traits & BotItemTraits.OnKill)!=0) value*=.5f;
                    if ((i.Traits & BotItemTraits.Sprint)!=0 && c.Hill) value*=.5f;
                    value/=1+.5f*n; break;
            }
            if (c.Ultra && c.EnemyHealing>.1f && (i.Traits & BotItemTraits.Damage)!=0) value*=1.15f;
            return value;
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
