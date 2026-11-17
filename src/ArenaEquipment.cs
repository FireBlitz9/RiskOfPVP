using System.Collections.Generic;
using EntityStates;
using HarmonyLib;
using RoR2;
using UnityEngine.Networking;

namespace ArenaPvP
{
    internal static class ArenaEquipment
    {
        internal static void EnsureCapacity(CharacterMaster master)
        {
            if (!Plugin.Active || !NetworkServer.active || !master || !master.inventory || !master.bodyPrefab) return;
            var body = master.bodyPrefab.GetComponent<CharacterBody>();
            if (!body || body.bodyIndex != BodyCatalog.FindBodyIndex("ToolbotBody") || master.inventory.equipmentSlotCount >= 2) return;
            // Native inventory grows a location without changing the active slot.
            var second = new EquipmentLocation { slot = 1, set = 0 };
            var empty = EquipmentState.empty; master.inventory.SetEquipment(in empty, in second);
        }
        internal static EquipmentLocation GrantLocation(Inventory inventory)
        {
            var query = new Inventory.EquipmentLocationQueryArgs { searchAllSlots = true, allowEmpty = true,
                allowOccupied = false, allowInaccessible = false };
            return inventory.TryFindBestEquipmentLocation(in query, out var empty) ? empty : inventory.activeEquipmentLocation;
        }
        internal static bool TryFind(Inventory inventory, EquipmentIndex equipment, out EquipmentLocation found)
        {
            found = inventory.activeEquipmentLocation;
            if (inventory.GetEquipment(in found).equipmentIndex == equipment) return true;
            foreach (var location in Locations(inventory))
                if (inventory.GetEquipment(in location).equipmentIndex == equipment) { found = location; return true; }
            return false;
        }
        internal static IEnumerable<EquipmentLocation> Locations(Inventory inventory)
        {
            for (uint slot = 0; slot < inventory.equipmentSlotCount; slot++)
                for (uint set = 0; set < inventory.accessibleEquipmentSetCount; set++)
                    yield return new EquipmentLocation { slot = slot, set = set };
        }
    }

    [HarmonyPatch(typeof(EntityStateMachine), "Start")]
    internal static class PreserveArenaToolbotStance
    {
        private static void Prefix(EntityStateMachine __instance)
        {
            if (!Plugin.Active || !(__instance.state is EntityStates.Uninitialized)) return;
            var type = __instance.initialStateType.stateType;
            if (type != typeof(EntityStates.Toolbot.ToolbotStanceA) && type != typeof(EntityStates.Toolbot.ToolbotStanceB)) return;
            var body = __instance.commonComponents.characterBody;
            if (!body || !body.inventory || !ArenaTeams.IsCombatantBody(body)) return;
            if (NetworkServer.active) ArenaEquipment.EnsureCapacity(body.master);
            // Choose the initial native stance before it runs, so its ordinary
            // weapon/equipment pairing remains consistent on both authorities.
            __instance.initialStateType = new SerializableEntityStateType(body.inventory.activeEquipmentSlot == 1
                ? typeof(EntityStates.Toolbot.ToolbotStanceB) : typeof(EntityStates.Toolbot.ToolbotStanceA));
        }
    }
}
