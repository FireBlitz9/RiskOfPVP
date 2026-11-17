using System.Linq;
using RoR2;

namespace ArenaPvP
{
    internal static class ArenaBotBuilds
    {
        internal static int Choose(ArenaPlayer bot, int[] offered, ItemSelection mode)
        {
            var inventory = bot.Master.inventory;
            var candidates = offered.Select(id => {
                var pickup = PickupCatalog.GetPickupDef(new PickupIndex(id));
                var item = pickup == null ? null : ItemCatalog.GetItemDef(pickup.itemIndex);
                var equipment = pickup == null ? null : EquipmentCatalog.GetEquipmentDef(pickup.equipmentIndex);
                return new BotBuildItem { Pickup = id, Name = item ? item.name : equipment ? equipment.name : "",
                    Owned = item ? inventory.GetItemCountPermanent(item.itemIndex) : inventory.GetActiveEquipment().equipmentIndex != EquipmentIndex.None ? 1 : 0,
                    Equipment = equipment, Rare = equipment || item && (item.tier == ItemTier.Tier3 || item.tier == ItemTier.Boss || item.tier == ItemTier.Lunar),
                    AiAllowed = !item || !item.ContainsTag(ItemTag.AIBlacklist) };
            }).ToArray();
            return BotBuildRules.Choose(bot.BotBuild, candidates, bot.BotRewards, mode == ItemSelection.Command);
        }
    }
}
