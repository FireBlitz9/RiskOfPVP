using RoR2.CharacterAI;
using UnityEngine;

namespace ArenaPvP
{
    internal static class ArenaBotAim
    {
        internal static bool Apply(ref BaseAI.BodyInputs input,Vector3 actualAim,Vector3 targetAim,bool alive,bool hostile,bool visible,
            bool protectedTarget,bool movementOwnsAim,bool readyPhase,float seenFor,float reaction,float alignment)
        {
            // This must happen even while fire=false. Otherwise a travel/utility
            // driver can aim sideways forever and never pass the firing gate.
            if(BotCombatDecision.OwnAim(visible,movementOwnsAim))input.desiredAimDirection=targetAim;
            return BotCombatDecision.Fire(alive,hostile,visible,protectedTarget,readyPhase,seenFor,reaction,
                Vector3.Dot(actualAim,targetAim),alignment);
        }
    }
}
