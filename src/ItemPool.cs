using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using RoR2.Items;

namespace ArenaPvP
{
    internal sealed class ItemPool
    {
        private sealed class Bucket { internal readonly List<int> Items = new List<int>(); internal int Weight; }
        private readonly List<Bucket> buckets = new List<Bucket>();
        internal readonly int[] All;
        private readonly Random random;
        private readonly Random scrapRandom;
        private readonly EssenceWeights weightedEssences;
        private BlacklistSelection itemLimits, equipmentLimits;
        private string itemRules, equipmentRules;
        private readonly Dictionary<ItemIndex, ItemIndex[]> corruptionSources;
        private readonly HashSet<int> eligiblePickups, constrainedPickups = new HashSet<int>();

        internal ItemPool(Run run, MatchSettings settings)
        {
            random = new Random(unchecked((int)run.seed ^ 0x5241494E));
            scrapRandom = new Random(unchecked((int)run.seed ^ 0x53435250));
            weightedEssences = settings.WeightedEssences;
            corruptionSources = ContagiousItemManager.transformationInfos.GroupBy(t => t.transformedItem)
                .ToDictionary(g => g.Key, g => g.Select(t => t.originalItem).Distinct().ToArray());
            var byTier = new Dictionary<ItemTier, Bucket>();
            for (int i = 0; i < ItemCatalog.itemCount; i++)
            {
                var def = ItemCatalog.GetItemDef((ItemIndex)i);
                if (!def || def.hidden || def.tier == ItemTier.NoTier
                    || (!settings.IncludeRestricted && (!run.availableItems.Contains(def.itemIndex)
                        || def.ContainsTag(ItemTag.IgnoreForDropList) || def.ContainsTag(ItemTag.WorldUnique)))
                    || def.name == "LevelBonus" || def.name == "UseAmbientLevel"
                    || (def.requiredExpansion && !run.IsExpansionEnabled(def.requiredExpansion))) continue;
                bool standard = def.tier == ItemTier.Tier1 || def.tier == ItemTier.Tier2 || def.tier == ItemTier.Tier3;
                bool lunar = def.tier == ItemTier.Lunar;
                bool boss = def.tier == ItemTier.Boss;
                bool isVoid = def.tier == ItemTier.VoidTier1 || def.tier == ItemTier.VoidTier2 || def.tier == ItemTier.VoidTier3 || def.tier == ItemTier.VoidBoss;
                if ((lunar && !settings.IncludeLunar) || (boss && !settings.IncludeBoss) || (isVoid && !settings.IncludeVoid)
                    || (!standard && !lunar && !boss && !isVoid && !settings.IncludeSpecial)) continue;
                Bucket b;
                if (!byTier.TryGetValue(def.tier, out b))
                {
                    b = new Bucket { Weight = def.tier == ItemTier.Tier1 ? settings.CommonWeight
                        : def.tier == ItemTier.Tier2 ? settings.GreenWeight : def.tier == ItemTier.Tier3 ? settings.RedWeight : 5 };
                    byTier.Add(def.tier, b); buckets.Add(b);
                }
                int pickup = PickupCatalog.FindPickupIndex(def.itemIndex).value;
                if (pickup >= 0) b.Items.Add(pickup);
            }
            if (settings.IncludeEquipment)
            {
                var equipment = new Bucket { Weight = 5 };
                for (int i = 0; i < EquipmentCatalog.equipmentCount; i++)
                {
                    var def = EquipmentCatalog.GetEquipmentDef((EquipmentIndex)i);
                    if (!def || (!def.canDrop && !(settings.IncludeRestricted && def.passiveBuffDef && def.passiveBuffDef.isElite))
                        || (!settings.IncludeRestricted && !run.availableEquipment.Contains(def.equipmentIndex))
                        || (def.requiredExpansion && !run.IsExpansionEnabled(def.requiredExpansion)) || (def.isLunar && !settings.IncludeLunar)) continue;
                    int pickup = PickupCatalog.FindPickupIndex(def.equipmentIndex).value;
                    if (pickup >= 0) equipment.Items.Add(pickup);
                }
                if (equipment.Items.Count > 0) buckets.Add(equipment);
            }
            All = buckets.SelectMany(b => b.Items).Distinct().ToArray();
            eligiblePickups = new HashSet<int>(All);
            RefreshRestrictions(settings.BannedItems, settings.BannedEquipment);
            Plugin.Log("RiskOfArena catalog scan: " + ItemCatalog.itemCount + " item definitions, " + EquipmentCatalog.equipmentCount
                + " equipment definitions; " + All.Length + " catalog-eligible pickups before per-player restrictions. Categories: "
                + string.Join(", ", All.GroupBy(Category).Select(g => g.Key + "=" + g.Count()).ToArray()) + ".");
        }

        internal bool RefreshRestrictions(string items, string equipment)
        {
            if (itemLimits != null && itemRules == items && equipmentRules == equipment) return false;
            itemRules = items; equipmentRules = equipment;
            itemLimits = new BlacklistSelection(items); equipmentLimits = new BlacklistSelection(equipment);
            constrainedPickups.Clear();
            foreach (int pickup in All)
            {
                var definition = PickupCatalog.GetPickupDef(new PickupIndex(pickup));
                var item = ItemCatalog.GetItemDef(definition.itemIndex);
                if (item)
                {
                    var transformed = ItemCatalog.GetItemDef(ContagiousItemManager.GetTransformedItemIndex(item.itemIndex));
                    if (itemLimits.Limit(item.name) >= 0 || transformed && itemLimits.Limit(transformed.name) >= 0) constrainedPickups.Add(pickup);
                }
                else
                {
                    var gear = EquipmentCatalog.GetEquipmentDef(definition.equipmentIndex);
                    if (gear && equipmentLimits.Limit(gear.name) >= 0) constrainedPickups.Add(pickup);
                }
            }
            return true;
        }

        internal int[] DraftByEssence(int count, Inventory inventory)
        {
            // Each offer is rolled once. Going back to the essence screen never rerolls it.
            return Available(All, inventory).GroupBy(Category).SelectMany(group => group.OrderBy(x => random.Next()).Take(count)).ToArray();
        }

        internal int[] Weighted(Inventory inventory)
        {
            // Roll once per offer on the host. The queued item IDs retain that
            // essence across menus, reconnects and every snapshot of the offer.
            var allowed = Available(All, inventory);
            var kind = EssenceRollRules.Choose(allowed.Select(Category).Distinct().ToArray(), weightedEssences, total => random.Next(total));
            return kind.HasValue ? allowed.Where(p => Category(p) == kind.Value).ToArray() : new int[0];
        }

        internal bool CanOffer(ItemSelection mode, bool canLevel, Inventory inventory, int releasing = -1)
        {
            var allowed = All.Where(p => CanGrant(p, inventory, releasing)).ToArray();
            if (mode == ItemSelection.Weighted)
                return canLevel || allowed.Any(p => weightedEssences.Weight(Category(p)) > 0);
            return allowed.Length > 0 || mode != ItemSelection.Random && canLevel;
        }

        internal bool CanScrap(ItemSelection mode, int tier, bool canLevel, Inventory inventory, int releasing = -1)
        {
            return !ScrapRewardRules.PreservesTier(mode) ? CanOffer(mode, canLevel, inventory, releasing)
                : All.Any(p => ExactTier(p) == tier && CanGrant(p, inventory, releasing));
        }

        internal int[] ScrapChoices(ItemSelection mode, int tier, int draftSize, Inventory inventory)
        { return ScrapRewardRules.Choices(mode, Available(All, inventory), tier, draftSize, ExactTier, total => scrapRandom.Next(total)); }

        internal int[] Available(int[] choices, Inventory inventory)
        { return !inventory ? new int[0] : constrainedPickups.Count == 0 ? choices : choices.Where(p => CanGrant(p, inventory)).ToArray(); }
        internal bool CanGrant(int pickup, Inventory inventory, int releasing = -1)
        {
            if (!inventory) return false;
            if (!eligiblePickups.Contains(pickup)) return false;
            if (!constrainedPickups.Contains(pickup)) return true;
            var definition = PickupCatalog.GetPickupDef(new PickupIndex(pickup));
            if (definition == null) return false;
            var item = ItemCatalog.GetItemDef(definition.itemIndex);
            int removed = pickup == releasing ? 1 : 0;
            if (item)
            {
                if (!itemLimits.CanGrant(item.name, inventory.GetItemCountTotal(item.itemIndex) - removed)) return false;
                // Reserve native pending corruption too: selecting a normal item
                // must not evade its already-owned void counterpart's cap.
                ItemIndex target = ContagiousItemManager.GetTransformedItemIndex(item.itemIndex);
                if (target == ItemIndex.None || inventory.GetItemCountTotal(target) == 0) target = item.itemIndex;
                var targetDef = ItemCatalog.GetItemDef(target);
                if (!targetDef || itemLimits.Limit(targetDef.name) < 0) return true;
                long projected = inventory.GetItemCountTotal(target);
                ItemIndex[] sources;
                if (corruptionSources.TryGetValue(target, out sources))
                    foreach (var source in sources) projected += inventory.GetItemCountTotal(source);
                return itemLimits.CanGrant(targetDef.name, projected - removed);
            }
            var equipment = EquipmentCatalog.GetEquipmentDef(definition.equipmentIndex);
            if (!equipment) return false;
            int count = 0;
            for (uint slot = 0; slot < inventory.equipmentSlotCount; slot++)
                for (uint set = 0; set < inventory.equipmentSetCount; set++)
                {
                    var location = new EquipmentLocation { slot = slot, set = set };
                    if (inventory.GetEquipment(in location).equipmentIndex == equipment.equipmentIndex) count++;
                }
            return equipmentLimits.CanGrant(equipment.name, count - removed);
        }

        internal static int ExactTier(int pickup)
        {
            var def = PickupCatalog.GetPickupDef(new PickupIndex(pickup));
            if (def != null && def.equipmentIndex != EquipmentIndex.None)
            {
                var equipment = EquipmentCatalog.GetEquipmentDef(def.equipmentIndex);
                return equipment && equipment.isLunar ? ScrapRewardRules.LunarEquipmentTier : ScrapRewardRules.EquipmentTier;
            }
            var item = def == null ? null : ItemCatalog.GetItemDef(def.itemIndex);
            return item && item.tier != ItemTier.NoTier ? (int)item.tier : -1;
        }

        internal static string TierName(int tier)
        {
            if (tier == ScrapRewardRules.EquipmentTier) return "Equipment";
            if (tier == ScrapRewardRules.LunarEquipmentTier) return "Lunar Equipment";
            switch ((ItemTier)tier)
            {
                case ItemTier.Tier1: return "White";
                case ItemTier.Tier2: return "Green";
                case ItemTier.Tier3: return "Red";
                case ItemTier.Lunar: return "Lunar";
                case ItemTier.Boss: return "Boss";
                case ItemTier.VoidTier1: return "Void (White)";
                case ItemTier.VoidTier2: return "Void (Green)";
                case ItemTier.VoidTier3: return "Void (Red)";
                case ItemTier.VoidBoss: return "Void (Boss)";
                default:
                    var def = ItemTierCatalog.GetItemTierDef((ItemTier)tier);
                    return def ? def.name.Replace("<", "").Replace(">", "") : "Custom tier " + tier;
            }
        }

        internal static EssenceKind Category(int pickup)
        {
            var def = PickupCatalog.GetPickupDef(new PickupIndex(pickup));
            if (def == null) return EssenceKind.Expansion;
            if (def.equipmentIndex != EquipmentIndex.None) return EssenceKind.Equipment;
            var item = ItemCatalog.GetItemDef(def.itemIndex);
            if (!item) return EssenceKind.Expansion;
            switch (item.tier)
            {
                case ItemTier.Tier1: return EssenceKind.White;
                case ItemTier.Tier2: return EssenceKind.Green;
                case ItemTier.Tier3: return EssenceKind.Red;
                case ItemTier.Lunar: return EssenceKind.Lunar;
                case ItemTier.VoidTier1: case ItemTier.VoidTier2: case ItemTier.VoidTier3: case ItemTier.VoidBoss: return EssenceKind.Void;
                case ItemTier.Boss: return EssenceKind.Boss;
                default: return EssenceKind.Expansion;
            }
        }

        internal int Pick(Inventory inventory, HashSet<int> excluded = null)
        {
            Func<int, bool> eligible = i => (excluded == null || !excluded.Contains(i)) && CanGrant(i, inventory);
            var available = buckets.Where(b => b.Items.Any(eligible)).ToArray();
            if (available.Length == 0) return -1;
            int total = available.Sum(b => b.Weight);
            Bucket bucket = available[random.Next(available.Length)];
            if (total > 0)
            {
                int roll = random.Next(total);
                foreach (var candidate in available) { roll -= candidate.Weight; if (roll < 0) { bucket = candidate; break; } }
            }
            var candidates = bucket.Items.Where(eligible).ToArray();
            return candidates[random.Next(candidates.Length)];
        }
    }
}
