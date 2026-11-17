using System;
using System.Collections.Generic;
using System.Linq;
using ArenaPvP;
using RoR2;

// Executes the production ItemPool source with a small deterministic catalog and
// inventory adapter. No copied reward logic, Unity execution or game mutations.
internal static class ItemPoolTests
{
    private static int assertions;
    private static void Check(bool value, string name) { assertions++; if (!value) throw new Exception(name); }
    private static MatchSettings Settings(string items = "", string equipment = "")
    { return new MatchSettings { BannedItems = items, BannedEquipment = equipment }; }
    private static int Id(int index) { return PickupCatalog.FindPickupIndex((ItemIndex)index).value; }
    public static int Main()
    {
        try
        {
            ItemCatalog.Definitions = new[] {
                new ItemDef("Bear", ItemTier.Tier1, 0), new ItemDef("Syringe", ItemTier.Tier1, 1),
                new ItemDef("Feather", ItemTier.Tier2, 2), new ItemDef("AlienHead", ItemTier.Tier3, 3),
                new ItemDef("BearVoid", ItemTier.VoidTier1, 4), new ItemDef("LevelBonus", ItemTier.Tier1, 5) };
            EquipmentCatalog.Definitions = new[] { new EquipmentDef("MissileLauncher", 0), new EquipmentDef("Fruit", 1) };
            RoR2.Items.ContagiousItemManager.transformationInfos = new[] { new RoR2.Items.TransformationInfo { originalItem = (ItemIndex)0, transformedItem = (ItemIndex)4 } };
            var inventory = new Inventory(); var pool = new ItemPool(new Run(), Settings("Bear,Feather=1", "MissileLauncher"));
            inventory.Counts[(ItemIndex)2] = 1;
            var command = pool.Available(pool.All, inventory);
            Check(!command.Contains(Id(0)) && !command.Contains(Id(2)) && command.Contains(Id(1)), "actual Command filtering removes hard bans and reached caps");
            Check(!command.Contains(1000) && command.Contains(1001), "equipment restrictions share the actual eligibility path");
            Check(!pool.All.Contains(Id(5)), "reserved leveling items never enter the reward catalog");
            for (int i = 0; i < 40; i++)
            {
                Check(!pool.Weighted(inventory).Any(p => p == Id(0) || p == Id(2) || p == 1000), "actual Weighted never rolls banned/capped pickups");
                Check(!pool.DraftByEssence(4, inventory).Any(p => p == Id(0) || p == Id(2) || p == 1000), "actual Draft excludes banned/capped choices before drawing");
                int random = pool.Pick(inventory);
                Check(random != Id(0) && random != Id(2) && random != 1000, "actual Random excludes banned/capped pickups before drawing");
            }
            var originalOffer = (int[])pool.All.Clone();
            Check(pool.RefreshRestrictions("Syringe,Bear=2", "Fruit"), "live saved rules update the existing production pool");
            inventory.Counts[(ItemIndex)0] = 2;
            var filtered = pool.Available(originalOffer, inventory);
            Check(!filtered.Contains(Id(0)) && !filtered.Contains(Id(1)) && filtered.Contains(Id(2)), "existing queued offers lose newly banned/capped items and reflect unbanned ones");
            inventory.Counts[(ItemIndex)0] = 1;
            Check(pool.Available(originalOffer, inventory).Contains(Id(0)), "a retained Command offer restores an item after scrapping below its limit");
            Check(!pool.RefreshRestrictions("Syringe,Bear=2", "Fruit"), "unchanged rules reuse cached restrictions");
            pool.RefreshRestrictions("BearVoid=2", ""); inventory.Counts.Clear(); inventory.Counts[(ItemIndex)4] = 2;
            Check(!pool.CanGrant(Id(0), inventory) && !pool.CanGrant(Id(4), inventory), "the actual pool blocks direct and converted grants at a void limit");
            inventory.Counts.Clear(); inventory.Counts[(ItemIndex)0] = 2;
            Check(!pool.CanGrant(Id(4), inventory), "selecting void cannot convert existing normal stacks over its cap");
            inventory.Counts.Clear(); inventory.Counts[(ItemIndex)4] = 1;
            Check(pool.CanGrant(Id(0), inventory), "a converted grant below its destination cap remains available");
            inventory.Counts[(ItemIndex)0] = 1;
            Check(!pool.CanGrant(Id(0), inventory), "pending native conversion also reserves capacity before a second grant");
            pool.RefreshRestrictions("Bear=1", ""); inventory.Counts.Clear(); inventory.Counts[(ItemIndex)0] = 1;
            Check(!pool.CanGrant(Id(0), inventory) && pool.CanGrant(Id(0), inventory, Id(0)), "scrapper preflight accounts for the source copy that will be removed");
            pool.RefreshRestrictions("Bear,Syringe,Feather,AlienHead,BearVoid", "MissileLauncher,Fruit");
            Check(pool.Available(pool.All, inventory).Length == 0 && pool.Weighted(inventory).Length == 0
                && pool.DraftByEssence(4, inventory).Length == 0 && pool.Pick(inventory) == -1, "all-banned real pools produce no forbidden fallback in any mode");
            pool.RefreshRestrictions("", "");
            Check(pool.Available(pool.All, inventory).Length == pool.All.Length, "unbanning restores the existing catalog without a new match");
            var other = new ItemPool(new Run(), Settings("Bear", ""));
            Check(!other.CanGrant(Id(0), inventory) && pool.CanGrant(Id(0), inventory), "separate mode pool policies remain independent");
            var a = new ItemPool(new Run(), Settings()); var b = new ItemPool(new Run(), Settings());
            bool sequence = true;
            for (int i = 0; i < 12; i++) { b.RefreshRestrictions("Bear", ""); b.RefreshRestrictions("", ""); sequence &= a.Pick(inventory) == b.Pick(inventory); }
            Check(sequence, "live policy edits never reseed or advance the reward RNG");
            var version = new ChoiceListVersion();
            Check(version.Update(new[] { 1, 2 }) && !version.Update(new[] { 1, 2 }), "new snapshot arrays with unchanged contents do not rebuild the menu");
            Check(version.Update(new[] { 2 }) && version.Update(new[] { 1, 2 }), "reaching a cap removes the icon and freeing capacity restores it");
            Check(version.Update(new int[0]) && !version.Update(new int[0]), "empty eligibility rebuilds once and then stays stable");
            var mutable = new[] { 2, 3 }; version.Update(mutable); mutable[0] = 1;
            Check(version.Update(mutable), "menu tracking notices reused arrays whose contents change");
            Console.WriteLine("PASS: " + assertions + " production ItemPool and reward-layout integration assertions."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAILED: " + error); return 1; }
    }
}

namespace ArenaPvP
{
    internal static class Plugin { internal static void Log(string message) { } }
    internal sealed class MatchSettings
    {
        internal string BannedItems = "", BannedEquipment = "";
        internal bool IncludeRestricted = true, IncludeLunar = true, IncludeVoid = true, IncludeBoss = true, IncludeSpecial = true, IncludeEquipment = true;
        internal int CommonWeight = 70, GreenWeight = 25, RedWeight = 5;
        internal EssenceWeights WeightedEssences = EssenceWeights.Default;
    }
}
namespace RoR2
{
    public class TestObject { public static implicit operator bool(TestObject value) { return value != null; } }
    public enum ItemTier { Tier1, Tier2, Tier3, Lunar, Boss, VoidTier1, VoidTier2, VoidTier3, VoidBoss, NoTier }
    public enum ItemIndex { None = -1 }
    public enum EquipmentIndex { None = -1 }
    public enum ItemTag { IgnoreForDropList, WorldUnique }
    public struct PickupIndex { public int value; public PickupIndex(int i) { value = i; } }
    public struct EquipmentLocation { public uint slot, set; }
    public struct EquipmentState { public EquipmentIndex equipmentIndex; }
    public class ExpansionDef : TestObject { }
    public class ItemTierDef : TestObject { public string name = "Custom"; }
    public class BuffDef : TestObject { public bool isElite = false; }
    public class ItemDef : TestObject
    {
        public string name; public ItemTier tier; public ItemIndex itemIndex; public bool hidden = false; public ExpansionDef requiredExpansion = null;
        public ItemDef(string n, ItemTier t, int i) { name = n; tier = t; itemIndex = (ItemIndex)i; }
        public bool ContainsTag(ItemTag tag) { return false; }
    }
    public class EquipmentDef : TestObject
    {
        public string name; public EquipmentIndex equipmentIndex; public bool canDrop = true, isLunar = false;
        public BuffDef passiveBuffDef = null; public ExpansionDef requiredExpansion = null;
        public EquipmentDef(string n, int i) { name = n; equipmentIndex = (EquipmentIndex)i; }
    }
    public class PickupDef { public ItemIndex itemIndex = ItemIndex.None; public EquipmentIndex equipmentIndex = EquipmentIndex.None; }
    public static class ItemCatalog
    {
        public static ItemDef[] Definitions; public static int itemCount { get { return Definitions.Length; } }
        public static ItemDef GetItemDef(ItemIndex i) { return (int)i >= 0 && (int)i < itemCount ? Definitions[(int)i] : null; }
    }
    public static class EquipmentCatalog
    {
        public static EquipmentDef[] Definitions; public static int equipmentCount { get { return Definitions.Length; } }
        public static EquipmentDef GetEquipmentDef(EquipmentIndex i) { return (int)i >= 0 && (int)i < equipmentCount ? Definitions[(int)i] : null; }
    }
    public static class ItemTierCatalog { public static ItemTierDef GetItemTierDef(ItemTier tier) { return new ItemTierDef(); } }
    public static class PickupCatalog
    {
        public static PickupIndex FindPickupIndex(ItemIndex i) { return new PickupIndex((int)i); }
        public static PickupIndex FindPickupIndex(EquipmentIndex i) { return new PickupIndex(1000 + (int)i); }
        public static PickupDef GetPickupDef(PickupIndex index)
        { return index.value >= 1000 ? new PickupDef { equipmentIndex = (EquipmentIndex)(index.value - 1000) } : new PickupDef { itemIndex = (ItemIndex)index.value }; }
    }
    public class Inventory : TestObject
    {
        public readonly Dictionary<ItemIndex, int> Counts = new Dictionary<ItemIndex, int>();
        public int GetItemCountTotal(ItemIndex i) { int count; return Counts.TryGetValue(i, out count) ? count : 0; }
        public byte equipmentSlotCount = 1, equipmentSetCount = 1;
        public EquipmentState GetEquipment(in EquipmentLocation location) { return new EquipmentState { equipmentIndex = EquipmentIndex.None }; }
    }
    public class Run
    {
        public ulong seed = 78123;
        public readonly HashSet<ItemIndex> availableItems = new HashSet<ItemIndex>();
        public readonly HashSet<EquipmentIndex> availableEquipment = new HashSet<EquipmentIndex>();
        public bool IsExpansionEnabled(ExpansionDef expansion) { return true; }
    }
}
namespace RoR2.Items
{
    public struct TransformationInfo { public RoR2.ItemIndex originalItem, transformedItem; }
    public static class ContagiousItemManager
    {
        public static TransformationInfo[] transformationInfos;
        public static RoR2.ItemIndex GetTransformedItemIndex(RoR2.ItemIndex index)
        { foreach (var t in transformationInfos) if (t.originalItem == index) return t.transformedItem; return RoR2.ItemIndex.None; }
    }
}
