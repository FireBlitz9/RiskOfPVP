using RoR2;
using UnityEngine;

namespace ArenaPvP
{
    internal static class ArenaWorldObjects
    {
        internal static bool PreserveVehicle(Component member)
        {
            var seat = member ? member.GetComponentInParent<VehicleSeat>() : null;
            return WorldCleanupRules.PreserveVehicle(seat, seat && seat.disableInteraction,
                seat && seat.GetComponent<SojournVehicleBase>(),
                seat && seat.currentPassengerBody && ArenaMatch.IsPlayerOrOwned(seat.currentPassengerBody.master));
        }
    }
}
