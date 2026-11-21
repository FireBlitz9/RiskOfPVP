using System;
using ArenaPvP;
using RoR2;
using RoR2.CharacterAI;
using UnityEngine;

internal static class BotAimPipelineTests
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(bool staleUtility in new[]{false,true})
        {
            var body=new CharacterBody();var enemy=new CharacterBody { corePosition=new Vector3(0,0,10) };
            body.inputBank.aimDirection=new Vector3(1,0,0);
            body.skillLocator.secondary.Ready=body.skillLocator.utility.Ready=body.skillLocator.special.Ready=false;
            var ai=new BaseAI { body=body };
            var actions=new ArenaBotActions();
            var drivers=new AISkillDriver[4];for(int i=0;i<4;i++)drivers[i]=new AISkillDriver { skillSlot=(SkillSlot)i,buttonPressType=AISkillDriver.ButtonPressType.Hold };
            actions.Initialize("any registered survivor",drivers,1);
            var previous=default(BaseAI.BodyInputs);float first=-1;int shots=0,nextReady=0,mainAt=0;
            for(int tick=0;tick<500;tick++)
            {
                Time.unscaledTime=tick*.02f;
                if(tick>=mainAt)body.skillLocator.primary.stateMachine.Main=true;
                if(tick>=nextReady)body.skillLocator.primary.Ready=true;
                // Native movement/utility AI aims sideways on every tick. This
                // reproduced the original deadlock: fire was checked before any
                // hitscan target aim was written, so it never became aligned.
                var input=new BaseAI.BodyInputs { desiredAimDirection=new Vector3((tick/10)%2==0 ? 1 : -1,0,0),pressSkill3=staleUtility };
                bool fire=ArenaBotAim.Apply(ref input,body.inputBank.aimDirection,new Vector3(0,0,1),true,true,true,false,false,true,Time.unscaledTime,.1f,.98f);
                actions.Combo(ai,ref input,previous,enemy,fire,BotIntelligence.Ultra);
                var desired=input.desiredAimDirection;var aim=body.inputBank.aimDirection;
                body.inputBank.aimDirection=new Vector3(aim.x+(desired.x-aim.x)*.22f,aim.y+(desired.y-aim.y)*.22f,aim.z+(desired.z-aim.z)*.22f).normalized;
                if(input.pressSkill1 && body.skillLocator.primary.CanExecute())
                { if(first<0)first=Time.unscaledTime;shots++;body.Activate(body.skillLocator.primary);nextReady=tick+10;mainAt=tick+2; }
                previous=input;
            }
            check(first>=0 && first<.6f,"a stationary visible target is attacked without waiting for utility cooldown: "+staleUtility);
            check(shots>=45,"ordinary primary cooldown cycles sustain fire during ten seconds of sideways native aim: "+staleUtility);
            actions.Dispose();
        }
        var movement=new BaseAI.BodyInputs { desiredAimDirection=new Vector3(1,0,0) };
        ArenaBotAim.Apply(ref movement,new Vector3(1,0,0),new Vector3(0,0,1),true,true,true,false,true,true,1,.1f,.98f);
        check(movement.desiredAimDirection.x==1,"active traversal retains its native aim ownership");
        check(!ArenaBotAim.Apply(ref movement,new Vector3(0,0,1),new Vector3(0,0,1),true,true,false,false,false,true,1,.1f,.98f),"aim ownership cannot authorize shooting through a wall");
        check(!ArenaBotAim.Apply(ref movement,new Vector3(0,0,1),new Vector3(0,0,1),true,true,true,true,false,true,1,.1f,.98f),"spawn protection still blocks firing");
    }
}
