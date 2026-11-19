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
                var def=ItemCatalog.GetItemDef(index);if(!def)continue;
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
            bool humans=false;
            if(BotTacticsRules.ChaseHumans(match.Rules.BotChase,match.Rules.Mode))
                foreach(var p in match.AllPlayers)if(!p.IsBot && p.Connected && !p.Dead && p.Master && p.Master.GetBody()) { humans=true;break; }
            foreach(var p in match.AllPlayers)
            {
                if(!p.Connected || p.Dead || !p.Master || !match.AreOpponents(bot,p) || humans && p.IsBot)continue;
                var enemy=p.Master.GetBody();if(!enemy || !enemy.healthComponent || !enemy.healthComponent.alive)continue;
                float distance=(enemy.corePosition-(body ? body.corePosition : Vector3.zero)).sqrMagnitude;
                if(bot.LastAttacker==p.UserId)distance=-1;
                if(distance<a) { third=second;c=b;second=first;b=a;first=p;a=distance; }
                else if(distance<b) { third=second;c=b;second=p;b=distance; }
                else if(distance<c) { third=p;c=distance; }
            }
            AddOpponent(first,body,context);AddOpponent(second,body,context);AddOpponent(third,body,context);
        }
        private static void AddOpponent(ArenaPlayer player,CharacterBody body,BotBuildContext context)
        {
            if(player==null)return;
            var master=player.Master;var enemy=master.GetBody();var loadout=ReadLoadout(master);
            foreach(var item in loadout.Items)
            { int held;context.EnemyInventory.TryGetValue(item.Key,out held);context.EnemyInventory[item.Key]=Math.Max(held,item.Value); }
            foreach(var equipment in loadout.Equipment)context.EnemyEquipment.Add(equipment);
            context.EnemyArmor=Math.Max(context.EnemyArmor,enemy.armor);
            context.EnemyMoveSpeed=Math.Max(context.EnemyMoveSpeed,enemy.moveSpeed);
            context.EnemyHealing=Math.Max(context.EnemyHealing,enemy.regen/Math.Max(1,enemy.maxHealth+enemy.maxShield));
            context.EnemyDamage=Math.Max(context.EnemyDamage,enemy.damage*Math.Max(.5f,enemy.attackSpeed)/Math.Max(1,body ? body.damage : 12));
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
                Rare=equipment || item && (item.tier==ItemTier.Tier3 || item.tier==ItemTier.Boss || item.tier==ItemTier.Lunar), Allowed=!item || !item.ContainsTag(ItemTag.AIBlacklist) };
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
        internal static int Choose(ArenaPlayer bot, int[] offered, ItemSelection mode)
        {
            if(offered.Length==0)return -1;
            var inventory=bot.Master.inventory; var body=bot.Master.GetBody(); var match=ArenaMatch.Instance;
            var context=new BotBuildContext { PreferVoid=(bot.BotOrdinal&1)!=0, Hill=match.Rules.Mode==ArenaMode.KOTH, Ultra=match.Rules.BotIntelligence==BotIntelligence.Ultra };
            string bodyName=bot.Master.bodyPrefab.name;
            context.AttackAffinity=bodyName=="MageBody" || bodyName=="LoaderBody" || bodyName=="CrocoBody" || bodyName=="SeekerBody" ? .35f : 1f;
            foreach (var index in inventory.itemAcquisitionOrder)
            {
                var item=ItemCatalog.GetItemDef(index); if(!item)continue;
                int count=inventory.GetItemCountTotal(index); if(count>0)context.Inventory[item.name]=count;
            }
            var location=ArenaEquipment.GrantLocation(inventory);
            context.HasEquipment=inventory.GetEquipment(in location).equipmentIndex!=EquipmentIndex.None;
            var current=EquipmentCatalog.GetEquipmentDef(inventory.GetEquipment(in location).equipmentIndex);context.CurrentEquipment=current ? current.name : "";
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
            var candidates=new BotBuildItem[offered.Length];
            for(int i=0;i<offered.Length;i++)
            {
                var d=Describe(offered[i]); bool equipment=d.Equipment!=EquipmentIndex.None;
                int held=0;if(equipment)context.EquipmentInventory.TryGetValue(d.Name,out held);else held=inventory.GetItemCountTotal(d.Item);
                candidates[i]=new BotBuildItem { Pickup=offered[i], Name=d.Name, Owned=held,
                    Equipment=equipment, Rare=d.Rare, AiAllowed=d.Allowed, Corrupts=d.Corrupts, ConvertsTo=d.ConvertsTo, Traits=d.Traits };
            }
            return BotTacticsRules.Adaptive(match.Rules.BotIntelligence) ? BotBuildRules.ChooseAdaptive(candidates,context)
                : BotBuildRules.Choose(bot.BotBuild,candidates,bot.BotRewards,mode==ItemSelection.Command,context);
        }
    }
}
