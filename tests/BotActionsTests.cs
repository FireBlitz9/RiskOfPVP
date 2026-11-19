using System;
using ArenaPvP;
using RoR2;
using RoR2.CharacterAI;
using UnityEngine;

// Executes the real action controller with the same small native API adapter
// as ItemPoolTests. Activation events and state completion are driven separately
// from requested buttons, so failed requests cannot masquerade as activations.
internal static class BotActionsTests
{
    private static ArenaBotActions Controller(string name, CharacterBody body, out BaseAI ai, out CharacterBody enemy)
    {
        Time.unscaledTime=0;
        ai=new BaseAI { body=body };enemy=new CharacterBody { corePosition=new Vector3(0,0,10) };
        var drivers=new AISkillDriver[4];
        for(int i=0;i<4;i++)drivers[i]=new AISkillDriver { skillSlot=(SkillSlot)i,buttonPressType=AISkillDriver.ButtonPressType.PressOnce };
        var controller=new ArenaBotActions();controller.Initialize(name,drivers,0);return controller;
    }
    private static BaseAI.BodyInputs Step(ArenaBotActions controller,BaseAI ai,CharacterBody enemy,float time,
        BaseAI.BodyInputs previous=default(BaseAI.BodyInputs),BotIntelligence tier=BotIntelligence.Ultra,bool fire=true)
    {
        Time.unscaledTime=time;
        var input=new BaseAI.BodyInputs { pressSkill1=true,moveVector=new Vector3(0,0,1) };
        controller.Combo(ai,ref input,previous,enemy,fire,tier);return input;
    }
    internal static void Run(Action<bool,string> check)
    {
        BaseAI ai;CharacterBody enemy;var body=new CharacterBody();
        var controller=Controller("CommandoBody",body,out ai,out enemy);
        var input=Step(controller,ai,enemy,0);
        check(input.pressSkill2 && !input.pressSkill1 && !input.pressSkill4,"Ultra requests Commando's secondary before its burst and primary");
        var edge=Step(controller,ai,enemy,.1f,input);
        check(!edge.pressSkill2,"a held previous button is released before a new must-key-press request");
        input=Step(controller,ai,enemy,.2f,edge);
        check(input.pressSkill2 && !input.pressSkill4,"an unconfirmed request does not advance the combo");
        body.Activate(body.skillLocator.secondary);
        check(!body.skillLocator.secondary.Ready,"the controller does not replenish stock after activation");
        input=Step(controller,ai,enemy,.25f);
        check(!input.pressSkill1 && !input.pressSkill4,"new attacks cannot interrupt a confirmed combo step during native recovery");
        Time.unscaledTime=.3f;
        check(controller.SprintBlocked(body),"native sprint-cancelling attack recovery blocks forced sprint");
        body.skillLocator.secondary.stateMachine.Main=true;
        input=Step(controller,ai,enemy,.4f);
        check(input.pressSkill4 && !input.pressSkill1 && !input.pressSkill2,"actual activation and recovery advance Commando to its special");
        body.Activate(body.skillLocator.special);body.skillLocator.special.stateMachine.Main=true;
        input=Step(controller,ai,enemy,.6f);
        check(input.pressSkill1 && !input.pressSkill4,"the primary follows the confirmed burst");
        controller.Dispose();

        body=new CharacterBody();controller=Controller("MageBody",body,out ai,out enemy);
        body.skillLocator.secondary.activationState=new SerializableEntityStateType(typeof(NativeChargeBomb));
        body.skillLocator.secondary.skillDef.canceledFromSprinting=true;
        input=Step(controller,ai,enemy,0);
        check(input.pressSkill3 && !input.pressSkill1,"Artificer opens its sequence with its native freeze utility");
        body.Activate(body.skillLocator.utility);
        check(controller.Protected(body,input),"an activated utility remains protected while its native state is busy");
        body.skillLocator.utility.stateMachine.Main=true;
        input=Step(controller,ai,enemy,.2f);
        check(input.pressSkill2,"Artificer charges secondary after the confirmed freeze");
        body.Activate(body.skillLocator.secondary);
        input=Step(controller,ai,enemy,.4f);
        check(input.pressSkill2 && !input.pressSkill1 && !input.pressSkill4 && controller.HasHold,"the controller holds an activated charge without competing primary requests");
        check(controller.Protected(body,input) && controller.SprintBlocked(body),"charging is protected from sprint cancellation and evasion");
        input=Step(controller,ai,enemy,1.1f,input);
        check(!input.pressSkill2 && !controller.HasHold,"charge release occurs through normal button input");
        check(!body.skillLocator.secondary.Ready && !body.skillLocator.secondary.stateMachine.Main,"releasing does not reset readiness or forcibly exit a native state");
        body.skillLocator.secondary.stateMachine.Main=true;
        input=Step(controller,ai,enemy,1.3f);
        check(input.pressSkill4,"Artificer follows charge recovery with its short-range special");
        controller.Dispose();

        foreach(var tier in new[]{BotIntelligence.Low,BotIntelligence.Medium,BotIntelligence.High,BotIntelligence.ExtraHigh})
        {
            body=new CharacterBody();controller=Controller("CommandoBody",body,out ai,out enemy);
            input=Step(controller,ai,enemy,0,default(BaseAI.BodyInputs),tier);
            check(input.pressSkill1 && !input.pressSkill2 && !input.pressSkill4,tier+" retains native attack sequencing");controller.Dispose();
        }
        foreach(string name in new[]{"LoaderBody","RailgunnerBody","CustomSurvivorBody"})
        {
            body=new CharacterBody();controller=Controller(name,body,out ai,out enemy);
            input=Step(controller,ai,enemy,0);
            check(input.pressSkill1 && !input.pressSkill2,name+" retains its native traversal/scope/custom skill chain");controller.Dispose();
        }
        body=new CharacterBody();controller=Controller("CommandoBody",body,out ai,out enemy);
        body.skillLocator.secondary.Ready=false;
        input=Step(controller,ai,enemy,0);
        check(input.pressSkill4 && !body.skillLocator.secondary.Ready,"cooldown-unavailable secondary is skipped without refilling it");
        controller.Dispose();
        controller=Controller("CommandoBody",body,out ai,out enemy);
        body.skillLocator.secondary.Ready=true;
        body.skillLocator.secondary.activationState=new SerializableEntityStateType(typeof(string));
        input=Step(controller,ai,enemy,0);
        check(!input.pressSkill2 && input.pressSkill4,"a substituted external skill state is left to its own native drivers");
        controller.Dispose();
        body=new CharacterBody();controller=Controller("CommandoBody",body,out ai,out enemy);
        input=Step(controller,ai,enemy,0,default(BaseAI.BodyInputs),BotIntelligence.Ultra,false);
        check(input.pressSkill1 && !input.pressSkill2,"no validated target means no injected combo requests");
        body.skillLocator.secondary.stateMachine.Main=false;
        check(controller.Protected(body,input) && controller.SprintBlocked(body),"an unobserved native state is conservatively protected");
        body.skillLocator.secondary.stateMachine.Main=true;
        body.skillLocator.primary.skillDef.forceSprintDuringState=true;
        body.Activate(body.skillLocator.primary);
        check(!controller.SprintBlocked(body),"native force-sprint states retain their sprint permission");
        body.skillLocator.primary.stateMachine.Main=true;
        var newBody=new CharacterBody();ai.body=newBody;Step(controller,ai,enemy,.2f);
        body.Activate(body.skillLocator.special);
        check(!controller.SprintBlocked(newBody),"respawn detaches activation events from the old body");
        controller.Dispose();

        body=new CharacterBody();controller=Controller("CommandoBody",body,out ai,out enemy);
        input=Step(controller,ai,enemy,0,default(BaseAI.BodyInputs),BotIntelligence.ExtraHigh);
        Time.unscaledTime=.4f;
        var motion=controller.Motion(body,input,default(BaseAI.BodyInputs),enemy,true,false,0,BotIntelligence.ExtraHigh);
        check(motion.Sprint && motion.DodgeSide!=0 && motion.Jump,"Extra High requests evasive sprinting and a grounded combat jump");
        body.Activate(body.skillLocator.primary);
        Time.unscaledTime=.5f;
        motion=controller.Motion(body,input,new BaseAI.BodyInputs { pressSprint=true },enemy,true,false,0,BotIntelligence.ExtraHigh);
        check(!motion.Sprint && !motion.RestoringSprint,"an active native attack blocks sprint until recovery");
        body.skillLocator.primary.stateMachine.Main=true;Time.unscaledTime=.6f;
        motion=controller.Motion(body,input,new BaseAI.BodyInputs { pressSprint=true },enemy,true,false,0,BotIntelligence.ExtraHigh);
        check(!motion.Sprint && motion.RestoringSprint,"completed attacks issue a sprint release edge");
        Time.unscaledTime=.62f;
        motion=controller.Motion(body,input,default(BaseAI.BodyInputs),enemy,true,false,0,BotIntelligence.ExtraHigh);
        check(motion.Sprint && motion.RestoringSprint,"the following input re-presses sprint after recovery");
        controller.Dispose();
    }
}
