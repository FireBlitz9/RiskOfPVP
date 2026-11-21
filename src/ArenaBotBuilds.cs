using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using RoR2.Items;
using UnityEngine;

namespace ArenaPvP
{
    internal static class ArenaBotBuilds
    {
        private sealed class Definition
        {
            internal string Name, ConvertsTo;
            internal string[] Corrupts;
            internal ItemIndex Item;
            internal EquipmentIndex Equipment;
            internal bool Rare, Allowed;
            internal float Cooldown;
            internal BotItemTraits Traits;
        }
        private static readonly Dictionary<int,Definition> definitions = new Dictionary<int,Definition>();
        private static Dictionary<ItemIndex,string[]> corruption;
        private sealed class Loadout
        {
            internal float Until;
            internal readonly Dictionary<string,int> Items=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            internal readonly HashSet<string> Equipment=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        private static readonly Dictionary<CharacterMaster,Loadout> loadouts=new Dictionary<CharacterMaster,Loadout>();
        internal static void Reset() { definitions.Clear();corruption=null;loadouts.Clear(); }
        private static Loadout ReadLoadout(CharacterMaster master)
        {
            Loadout result;
            if(!loadouts.TryGetValue(master,out result)) { result=new Loadout();loadouts.Add(master,result); }
            if(Time.unscaledTime<result.Until)return result;
            result.Until=Time.unscaledTime+.5f;result.Items.Clear();result.Equipment.Clear();
            if(!master.inventory)return result;
            foreach(var index in master.inventory.itemAcquisitionOrder)
            {
                var def=ItemCatalog.GetItemDef(index);if(!def || def.hidden)continue;
                int count=master.inventory.GetItemCountEffective(index);if(count>0)result.Items[def.name]=count;
            }
            foreach(var location in ArenaEquipment.Locations(master.inventory))
            {
                var def=EquipmentCatalog.GetEquipmentDef(master.inventory.GetEquipment(in location).equipmentIndex);
                if(def)result.Equipment.Add(def.name);
            }
            return result;
        }
        private static void ReadOpponents(ArenaPlayer bot,CharacterBody body,ArenaMatch match,BotBuildContext context)
        {
            ArenaPlayer first=null,second=null,third=null;float a=float.PositiveInfinity,b=a,c=a;
            float now = Time.unscaledTime;
            bool humans=false;
            if(BotTacticsRules.ChaseHumans(match.Rules.BotChase,match.Rules.Mode))
                foreach(var p in match.AllPlayers)if(!p.IsBot && p.Connected && !p.Dead && p.Master && p.Master.GetBody()) { humans=true;break; }
            foreach(var p in match.AllPlayers)
            {
                if(!p.Connected || !p.Master || !match.AreOpponents(bot,p) || humans && p.IsBot)continue;
                var enemy = p.Master.GetBody();
                var habit = bot.BotMemory?.Find(p.UserId);
                float score = body && enemy ? Math.Min(3, Vector3.Distance(enemy.corePosition, body.corePosition) / 35f) : 2;
                if (habit != null) score -= habit.Pressure(now) * 3 + habit.Familiarity(now)+habit.SurvivalPressure(now)*1.5f;
                if(p.UserId==bot.LastAttacker)score-=.75f;
                if (p.Dead) score += .25f;
                if(score<a) { third=second;c=b;second=first;b=a;first=p;a=score; }
                else if(score<b) { third=second;c=b;second=p;b=score; }
                else if(score<c) { third=p;c=score; }
            }
            AddOpponent(first, bot, context, now, 1f);
            AddOpponent(second, bot, context, now, .65f);
            AddOpponent(third, bot, context, now, .45f);
        }

        private static void AddOpponent(ArenaPlayer player, ArenaPlayer bot, BotBuildContext context, float now, float priority)
        {
            if(player==null)return;
            var loadout=ReadLoadout(player.Master);
            foreach(var item in loadout.Items)
            { int held;context.EnemyInventory.TryGetValue(item.Key,out held);context.EnemyInventory[item.Key]=Math.Max(held,item.Value); }
            foreach(var equipment in loadout.Equipment)context.EnemyEquipment.Add(equipment);
            int shieldBoosters;
            if (loadout.Items.TryGetValue("ShieldBooster", out shieldBoosters))
                context.EnemyArmor = Math.Max(context.EnemyArmor, Math.Min(60, shieldBoosters * 15));
            var habit = bot.BotMemory?.Find(player.UserId);
            var enemyBody=player.Master.GetBody();
            var own=bot.Master.GetBody();
            if(enemyBody)
            {
                var knowledge=ArenaCombatLearning.Find(enemyBody);
                if(knowledge!=null)foreach(var attack in knowledge.Skills)
                {
                    if(attack==null || !attack.Combat || !attack.KnownDamage)continue;
                    context.EnemySkillProc=Math.Max(context.EnemySkillProc,attack.Proc*attack.Hits/Math.Max(.15f,attack.Duration));
                    context.EnemySkillBurst=Math.Max(context.EnemySkillBurst,attack.Damage*attack.Hits*enemyBody.damage*ArenaCombatLearning.DamageScale/Math.Max(1,own && own.healthComponent ? own.healthComponent.fullCombinedHealth : 100));
                    context.EnemySkillDot|=attack.Dot;
                }
                context.EnemyArmor=Math.Max(context.EnemyArmor,enemyBody.armor);
                context.EnemyMoveSpeed=Math.Max(context.EnemyMoveSpeed,enemyBody.moveSpeed);
                if(own)context.EnemyDamage=Math.Max(context.EnemyDamage,Math.Min(3.5f,
                    enemyBody.damage*Math.Max(.5f,enemyBody.attackSpeed)/Math.Max(1,own.damage*Math.Max(.5f,own.attackSpeed))));
            }
            if (habit == null) return;
            float recency = habit.Familiarity(now);
            context.EnemyMoveSpeed = Math.Max(context.EnemyMoveSpeed, habit.Speed * recency);
            context.EnemyHealing = Math.Max(context.EnemyHealing, habit.Sustain * recency);
            context.EnemyStrafe = Math.Max(context.EnemyStrafe, habit.Strafe * recency);
            context.EnemyDamage = Math.Max(context.EnemyDamage, 1 + habit.Pressure(now));
            float weight = priority * (habit.Trade(now) < -.15f ? 1f : .85f);
            context.MobilityEvidence = Math.Max(context.MobilityEvidence, habit.MobilityEvidence(now) * weight);
            context.SustainEvidence = Math.Max(context.SustainEvidence, habit.SustainEvidence(now) * weight);
            context.BurstEvidence = Math.Max(context.BurstEvidence, habit.BurstEvidence(now) * weight);
            context.SurvivalPressure=Math.Max(context.SurvivalPressure,habit.SurvivalPressure(now));
        }
        private static Definition Describe(int id)
        {
            Definition result;
            if (definitions.TryGetValue(id,out result)) return result;
            if (corruption == null) corruption = ContagiousItemManager.transformationInfos.GroupBy(t=>t.transformedItem)
                .ToDictionary(g=>g.Key,g=>g.Select(t=>ItemCatalog.GetItemDef(t.originalItem)).Where(d=>d).Select(d=>d.name).ToArray());
            var pickup = PickupCatalog.GetPickupDef(new PickupIndex(id));
            var item = pickup == null ? null : ItemCatalog.GetItemDef(pickup.itemIndex);
            var equipment = pickup == null ? null : EquipmentCatalog.GetEquipmentDef(pickup.equipmentIndex);
            string[] sources;
            var converted = item ? ItemCatalog.GetItemDef(ContagiousItemManager.GetTransformedItemIndex(item.itemIndex)) : null;
            result = new Definition { Name=item ? item.name : equipment ? equipment.name : "", Item=item ? item.itemIndex : ItemIndex.None,
                Equipment=equipment ? equipment.equipmentIndex : EquipmentIndex.None, ConvertsTo=converted ? converted.name : "",
                Corrupts=item && corruption.TryGetValue(item.itemIndex,out sources) ? sources : new string[0],
                Rare=equipment || item && (item.tier==ItemTier.Tier3 || item.tier==ItemTier.Boss || item.tier==ItemTier.Lunar),
                Allowed=equipment || item && !item.ContainsTag(ItemTag.AIBlacklist), Cooldown=equipment ? equipment.cooldown : 0 };
            if (item)
            {
                if(item.ContainsTag(ItemTag.Damage)) result.Traits|=BotItemTraits.Damage;
                if(item.ContainsTag(ItemTag.Healing)) result.Traits|=BotItemTraits.Healing;
                if(item.ContainsTag(ItemTag.Utility)) result.Traits|=BotItemTraits.Utility;
                if(item.ContainsTag(ItemTag.OnKillEffect)) result.Traits|=BotItemTraits.OnKill;
                if(item.ContainsTag(ItemTag.SprintRelated)) result.Traits|=BotItemTraits.Sprint;
                if(item.ContainsTag(ItemTag.InteractableRelated)) result.Traits|=BotItemTraits.Interactable;
            }
            definitions.Add(id,result); return result;
        }
        internal static int Choose(ArenaPlayer bot, int[] offered, ItemSelection mode, bool mayLevel = false, int level = 1)
        {
            if(offered.Length==0)return -1;
            var inventory=bot.Master.inventory; var body=bot.Master.GetBody(); var match=ArenaMatch.Instance;
            var context=new BotBuildContext { PreferVoid=(bot.BotOrdinal&1)!=0, Hill=match.Rules.Mode==ArenaMode.KOTH,
                Teams=match.Rules.Mode!=ArenaMode.FFA, Ultra=match.Rules.BotIntelligence==BotIntelligence.Ultra, MayLevel=mayLevel };
            string bodyName=bot.Master.bodyPrefab.name;
            context.AttackAffinity=bodyName=="MageBody" || bodyName=="LoaderBody" || bodyName=="CrocoBody" || bodyName=="SeekerBody" ? .35f : 1f;
            if (context.Ultra && bot.BotKit.Reach > 0)
            {
                var kit = bot.BotMemory?.RefineKit(bot.BotKit, Time.unscaledTime) ?? bot.BotKit;
                context.AttackAffinity = kit.OnHit;
                context.SkillUse = kit.SkillUse;
                context.CloseRange = kit.Range < 12;
            }
            if(context.Ultra)context.SurvivalPressure=bot.BotMemory?.SurvivalPressure(Time.unscaledTime) ?? 0;
            foreach (var index in inventory.itemAcquisitionOrder)
            {
                var item=ItemCatalog.GetItemDef(index); if(!item || item.hidden)continue;
                int count=inventory.GetItemCountTotal(index); if(count>0)context.Inventory[item.name]=count;
            }
            var location=ArenaEquipment.GrantLocation(inventory);
            context.HasEquipment=inventory.GetEquipment(in location).equipmentIndex!=EquipmentIndex.None;
            var current=EquipmentCatalog.GetEquipmentDef(inventory.GetEquipment(in location).equipmentIndex);context.CurrentEquipment=current ? current.name : "";
            if (context.Ultra && current)
                context.CurrentGear = new BotBuildItem { Name = current.name, Equipment = true, Cooldown = current.cooldown };
            foreach(var slot in ArenaEquipment.Locations(inventory))
            {
                var gear=EquipmentCatalog.GetEquipmentDef(inventory.GetEquipment(in slot).equipmentIndex);if(!gear)continue;
                int held;context.EquipmentInventory.TryGetValue(gear.name,out held);context.EquipmentInventory[gear.name]=held+1;
            }
            if(body && body.healthComponent)
            {
                context.Health=bot.Dead ? 1 : body.healthComponent.combinedHealthFraction; context.Shield=body.maxShield/Math.Max(1,body.maxHealth+body.maxShield);
                context.Crit=body.crit; context.MoveSpeed=body.moveSpeed; context.Armor=body.armor;
            }
            // Also works during a death/respawn choice, after the old body is gone.
            if(context.Ultra)ReadOpponents(bot,body,match,context);
            context.ProcPower=context.Count("Missile")*.3f+context.Count("ChainLightning")*.2f+context.Count("BleedOnHit")*.08f+context.Count("StickyBomb")*.05f;
            context.Crit=Math.Max(context.Crit,(body ? body.baseCrit : 1)+context.Count("CritGlasses")*10);
            if(context.Count("ShieldOnly")>0)context.Shield=.99f;
            else if(context.Count("PersonalShield")>0)context.Shield=Math.Max(.02f,context.Shield);
            else if (context.Ultra && context.Count("ShieldBooster") > 0) context.Shield = Math.Max(.04f, context.Shield);
            var candidates=new BotBuildItem[offered.Length];
            for(int i=0;i<offered.Length;i++)
            {
                var d=Describe(offered[i]); bool equipment=d.Equipment!=EquipmentIndex.None;
                int held=0;if(equipment)context.EquipmentInventory.TryGetValue(d.Name,out held);else held=inventory.GetItemCountTotal(d.Item);
                candidates[i]=new BotBuildItem { Pickup=offered[i], Name=d.Name, Owned=held,
                    Equipment=equipment, Rare=d.Rare, AiAllowed=d.Allowed, Corrupts=d.Corrupts, ConvertsTo=d.ConvertsTo, Traits=d.Traits, Cooldown=d.Cooldown };
            }
            return BotTacticsRules.Adaptive(match.Rules.BotIntelligence) ? BotBuildRules.ChooseAdaptive(candidates,context,mayLevel,level)
                : BotBuildRules.Choose(bot.BotBuild,candidates,bot.BotRewards,mode==ItemSelection.Command,context);
        }
    }
}
