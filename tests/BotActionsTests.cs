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
    private static ArenaBotActions Controller(string name, CharacterBody body, out BaseAI ai, out CharacterBody enemy,bool shared=true,Action<AISkillDriver[]> configure=null)
    {
        Time.unscaledTime=0;
        ai=new BaseAI { body=body };enemy=new CharacterBody { corePosition=new Vector3(0,0,10) };
        if(shared)
            body.skillLocator.secondary.stateMachine=body.skillLocator.utility.stateMachine=body.skillLocator.special.stateMachine=body.skillLocator.primary.stateMachine;
        var drivers=new AISkillDriver[4];
        for(int i=0;i<4;i++)drivers[i]=new AISkillDriver { skillSlot=(SkillSlot)i,buttonPressType=AISkillDriver.ButtonPressType.PressOnce };
        drivers[0].buttonPressType=AISkillDriver.ButtonPressType.Hold;
        configure?.Invoke(drivers);
        if(name=="MageBody")ArenaCombatLearning.Model(body,2).Control=3;
        if(name=="LoaderBody" || name=="RailgunnerBody" || name=="CustomSurvivorBody")
            for(int i=1;i<4;i++)ArenaCombatLearning.Model(body,i).Damage=.01f;
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
        SustainedPressure(check);
        LearnedSkills(check);
        ContinuousAttackFallbacks(check);
        BanditFinisher(check);
    }
    private static void BanditFinisher(Action<bool, string> check)
    {
        var body = new CharacterBody();
        var controller = Controller("Bandit2Body", body, out var ai, out var enemy);
        body.skillLocator.special.activationState = new SerializableEntityStateType(typeof(NativeBanditFinisher));
        body.skillLocator.secondary.Ready = body.skillLocator.utility.Ready = false;
        enemy.healthComponent.combinedHealth = 75;
        enemy.healthComponent.barrier = 50;
        var input = Step(controller, ai, enemy, 0);
        check(input.pressSkill1 && !input.pressSkill4, "Bandit saves a finisher when barrier would absorb its kill");
        enemy.healthComponent.barrier = 0;
        enemy.armor = 100;
        input = Step(controller, ai, enemy, .1f);
        check(input.pressSkill1 && !input.pressSkill4, "Bandit saves a finisher against armor that would prevent the kill");
        enemy.armor = 0;
        input = Step(controller, ai, enemy, .2f);
        check(!input.pressSkill1 && input.pressSkill4, "Bandit spends the finisher when it can earn its native on-kill effect");
        controller.Dispose();
    }
    private static void ContinuousAttackFallbacks(Action<bool, string> check)
    {
        var body = new CharacterBody();
        var controller = Controller("CommandoBody", body, out var ai, out var enemy);
        body.skillLocator.secondary.Ready = body.skillLocator.special.Ready = false;
        var native = new BaseAI.BodyInputs { pressSkill3 = true };
        var previous = default(BaseAI.BodyInputs);
        for (int tick = 0; tick < 10; tick++)
        {
            Time.unscaledTime = tick * .02f;
            // The driver asks for a utility that reports readiness but never
            // activates. It cannot keep the aim or shared weapon indefinitely.
            var input = native;
            controller.Combo(ai, ref input, previous, enemy, tick >= 8, BotIntelligence.Ultra);
            previous = input;
        }
        check(previous.pressSkill1 && !previous.pressSkill3 && !controller.NativeUtilityAvailable(body, .18f),
            "a stale native utility times out even before aim locks, allowing shared primary damage");
        controller.Dispose();

        body = new CharacterBody();
        controller = Controller("CommandoBody", body, out ai, out enemy);
        ArenaCombatLearning.Model(body, 1).Speed = 25;
        var shot = Step(controller, ai, enemy, 0);
        check(controller.AimSlotFor(body) == 1, "a ready projectile secondary prepares its own lead");
        body.skillLocator.secondary.Ready = false;
        check(controller.AimSlotFor(body) == 0, "a cooled-down projectile cannot keep the aiming slot");
        body.skillLocator.special.Ready = false;
        shot = Step(controller, ai, enemy, .02f, shot);
        check(shot.pressSkill1, "hitscan primary resumes as soon as the projectile becomes unavailable");
        controller.Dispose();

        body = new CharacterBody();
        controller = Controller("CommandoBody", body, out ai, out enemy, false);
        body.skillLocator.special.Ready = body.skillLocator.utility.Ready = false;
        ArenaCombatLearning.Model(body, 0).Damage = 12;
        ArenaCombatLearning.Model(body, 1).Damage = 1;
        shot = Step(controller, ai, enemy, 0);
        check(shot.pressSkill1 && shot.pressSkill2, "a weaker independent shot adds damage without displacing a stronger primary");
        controller.Dispose();

        body = new CharacterBody();
        controller = Controller("CommandoBody", body, out ai, out enemy);
        var travel = default(BaseAI.BodyInputs);
        check(controller.TravelUtility(ref travel, default, body, 0) && travel.pressSkill3,
            "a ready travel ability uses normal input without a combat target");
        body.skillLocator.utility.Ready = false;
        check(!controller.TravelUtility(ref travel, default, body, .02f) && !travel.pressSkill3,
            "travel input does not bypass the skill's cooldown");
        controller.Dispose();

        body = new CharacterBody();
        controller = Controller("CommandoBody", body, out ai, out enemy);
        body.healthComponent.combinedHealthFraction = .3f;
        var sustain = ArenaCombatLearning.Model(body, 2);
        sustain.Healing = .3f;
        var healInput = default(BaseAI.BodyInputs);
        controller.Combo(ai, ref healInput, default, null, false, BotIntelligence.Ultra);
        check(healInput.pressSkill3 && !healInput.pressSkill1, "an injured bot can use a known safe self-heal without an opponent");
        controller.Dispose();
    }
    private static void LearnedSkills(Action<bool,string> check)
    {
        BaseAI ai;CharacterBody enemy;var body=new CharacterBody();
        var controller=Controller("CustomSurvivorBody",body,out ai,out enemy);
        body.skillLocator.primary.activationState=new SerializableEntityStateType(typeof(NativeChargeBomb));
        body.skillLocator.secondary.Ready=body.skillLocator.utility.Ready=body.skillLocator.special.Ready=false;
        var model=ArenaCombatLearning.Model(body,0);model.ChargeTime=1.6f;
        var input=Step(controller,ai,enemy,0);
        check(input.pressSkill1,"a registered custom charged primary can start without a named survivor combo");
        body.Activate(body.skillLocator.primary);
        input=Step(controller,ai,enemy,1,input);
        check(input.pressSkill1 && controller.HasHold,"a managed primary holds for its learned charge duration");
        input=Step(controller,ai,enemy,1.65f,input);
        check(!input.pressSkill1 && !controller.HasHold,"a managed primary releases at the learned duration without forcing state completion");
        controller.Dispose();
        body=new CharacterBody();controller=Controller("CustomSurvivorBody",body,out ai,out enemy);
        body.healthComponent.combinedHealthFraction=.3f;enemy.corePosition=new Vector3(0,0,100);
        var heal=ArenaCombatLearning.Model(body,2);heal.Healing=.3f;heal.Range=5;heal.Damage=0;heal.KnownDamage=true;
        input=Step(controller,ai,enemy,0);
        check(input.pressSkill3,"a threatened bot can use a self-heal outside the enemy's attack range");
        controller.Dispose();
        body=new CharacterBody();controller=Controller("CustomSurvivorBody",body,out ai,out enemy);
        body.skillLocator.secondary.Ready=body.skillLocator.utility.Ready=body.skillLocator.special.Ready=false;
        model=ArenaCombatLearning.Model(body,0);model.Range=120;model.ConfiguredRange=true;
        enemy.corePosition=new Vector3(0,0,80);ai.PassesDriverFilters=false;
        input=Step(controller,ai,enemy,0);
        check(input.pressSkill1,"known physical range permits a shot outside the native driver's preferred spacing");
        enemy.corePosition=new Vector3(0,0,130);
        input=new BaseAI.BodyInputs();controller.Combo(ai,ref input,default(BaseAI.BodyInputs),enemy,true,BotIntelligence.Ultra);
        check(!input.pressSkill1,"physical range still rejects genuinely unreachable targets");
        controller.Dispose();
    }
    private static void SustainedPressure(Action<bool,string> check)
    {
        BaseAI ai;CharacterBody enemy;var body=new CharacterBody();
        var controller=Controller("CommandoBody",body,out ai,out enemy,false);
        var input=Step(controller,ai,enemy,0);
        check(input.pressSkill1 && input.pressSkill2,"independent primary and secondary state machines can attack together");
        body.Activate(body.skillLocator.secondary);
        input=Step(controller,ai,enemy,.15f);
        check(input.pressSkill1,"primary continues during an independent secondary recovery");
        check(!controller.MovementLocked(body),"a weapon animation does not stop walking and evasion");
        body.skillLocator.secondary.stateMachine.Main=true;
        body.skillLocator.primary.Ready=body.skillLocator.secondary.Ready=body.skillLocator.special.Ready=false;
        body.skillLocator.primary.Ready=true;
        int held=0;var previous=default(BaseAI.BodyInputs);
        for(int i=0;i<200;i++)
        {
            Time.unscaledTime=1+i*.02f;
            input=new BaseAI.BodyInputs(); // A non-primary native driver supplies no primary request.
            controller.Combo(ai,ref input,previous,enemy,true,BotIntelligence.Ultra);
            if(input.pressSkill1)held++;
            previous=input;
        }
        check(held==200,"automatic primary pressure stays continuous across 200 ticks while burst skills recharge");
        int shots=0;held=0;
        for(int i=0;i<200;i++)
        {
            Time.unscaledTime=5+i*.02f;
            if(i%4==0) { body.skillLocator.primary.Ready=true;body.skillLocator.primary.stateMachine.Main=true; }
            input=new BaseAI.BodyInputs();controller.Combo(ai,ref input,previous,enemy,true,BotIntelligence.Ultra);
            if(input.pressSkill1) { held++;if(body.skillLocator.primary.Ready) { shots++;body.Activate(body.skillLocator.primary); } }
            previous=input;
        }
        check(held==200 && shots==50,"native activation/recovery cycles sustain automatic fire without resetting stock or cooldowns");
        body.skillLocator.primary.Ready=true;body.skillLocator.primary.stateMachine.Main=true;
        body.skillLocator.primary.mustKeyPress=true;
        input=Step(controller,ai,enemy,10,new BaseAI.BodyInputs { pressSkill1=true });
        var next=Step(controller,ai,enemy,10.1f,input);
        check(!input.pressSkill1 && next.pressSkill1,"press-once primaries receive release and fresh press edges");
        controller.Dispose();
        body=new CharacterBody();controller=Controller("CommandoBody",body,out ai,out enemy);
        input=Step(controller,ai,enemy,0);body.Activate(body.skillLocator.secondary);
        input=Step(controller,ai,enemy,.1f);
        check(!input.pressSkill1,"a primary cannot interrupt a shared-machine combo recovery");
        controller.Dispose();
        body=new CharacterBody();controller=Controller("MageBody",body,out ai,out enemy,false);
        Step(controller,ai,enemy,0);body.Activate(body.skillLocator.utility);
        check(controller.MovementLocked(body),"native traversal/body utility states keep movement ownership");
        controller.Dispose();
        body=new CharacterBody();controller=Controller("RailgunnerBody",body,out ai,out enemy);
        body.skillLocator.secondary.activationState=new SerializableEntityStateType(typeof(NativeScope));
        Time.unscaledTime=0;
        input=new BaseAI.BodyInputs { pressSkill2=true };
        controller.Combo(ai,ref input,default(BaseAI.BodyInputs),enemy,true,BotIntelligence.Ultra);
        check(input.pressSkill2 && !input.pressSkill1,"primary pressure cannot steal a shared native scope/utility activation");
        body.skillLocator.primary.activationState=new SerializableEntityStateType(typeof(NativeChargeBomb));
        body.Activate(body.skillLocator.primary);Time.unscaledTime=.2f;
        input=new BaseAI.BodyInputs();
        controller.Combo(ai,ref input,new BaseAI.BodyInputs { pressSkill1=true },enemy,true,BotIntelligence.Ultra);
        check(!input.pressSkill1,"a native charged primary can release instead of being held down indefinitely");
        controller.Dispose();
        body=new CharacterBody();
        controller=Controller("CommandoBody",body,out ai,out enemy,true,
            ds=>ds[1].additionalForbiddenSkills=new[]{body.skillLocator.secondary.skillDef});
        input=Step(controller,ai,enemy,0);
        check(!input.pressSkill2 && input.pressSkill4,"additional native loadout exclusions remain effective during attack arbitration");
        controller.Dispose();
    }
}
