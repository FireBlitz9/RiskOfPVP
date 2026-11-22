using System.Collections.Generic;
using System;
using RoR2;

// Native catalog/telemetry access is adapted here. Policy, input arbitration,
// aim ownership and the statistical learning model are the production sources.
namespace ArenaPvP
{
    internal static class ArenaCombatLearning
    {
        internal static float DamageScale=1;
        internal static float FinisherCoefficient(CharacterBody body, Type activation)
            => activation == typeof(RoR2.NativeBanditFinisher) ? 8 : 0;
        internal sealed class Actor
        {
            internal readonly BotSkillKnowledge[] Skills=new BotSkillKnowledge[4];
            internal float ProcPower=.5f;
            internal float LastAttack => 0;
            internal float Threat(CharacterBody target,float now)=>0;
            internal float Reliability(int slot)=>1;
            internal bool Untried(int slot)=>false;
        }
        private static readonly Dictionary<GenericSkill,BotSkillKnowledge> models=new Dictionary<GenericSkill,BotSkillKnowledge>();
        internal static Actor Find(CharacterBody body)=>null;
        internal static BotSkillKnowledge Model(CharacterBody body,int slot)
        {
            if(!body || !body.skillLocator)return null;
            var skill=body.skillLocator.GetSkill((SkillSlot)slot);if(!skill)return null;
            BotSkillKnowledge model;
            if(!models.TryGetValue(skill,out model))
            {
                model=new BotSkillKnowledge { Combat=true,KnownDamage=true,Damage=slot==0 ? 1 : slot==1 ? 4 : slot==3 ? 8 : .01f,Duration=slot==3 ? 2 : .35f };
                models.Add(skill,model);
            }
            model.StateName(skill.activationState.stateType?.Name);return model;
        }
    }
}
namespace EntityStates { public class EntityState { } }
