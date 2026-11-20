using System;
using RoR2;
using RoR2.CharacterAI;
using RoR2.Skills;
using UnityEngine;

namespace ArenaPvP
{
    // One controller per registered server bot. It emits normal input requests;
    // native skill readiness, state machines and movement remain authoritative.
    internal sealed class ArenaBotActions
    {
        private readonly BotMotionState motion=new BotMotionState();
        private readonly BotComboState combo=new BotComboState();
        private CharacterBody observed;
        private AISkillDriver[] drivers;
        private string bodyName;
        private BotComboStep[] plan;
        private GenericSkill ownedHold, recovering;
        private float holdUntil, holdStarted;
        private bool chargedHold;
        private BotAction requested=BotAction.None;
        private float requestedUntil;
        private readonly GenericSkill[] active=new GenericSkill[4];
        private BotIntelligence tier;
        private int identity;

        internal void Initialize(string name,AISkillDriver[] nativeDrivers,int ordinal)
        { bodyName=name;plan=BotComboRules.Plan(name);drivers=nativeDrivers;identity=ordinal; }
        internal void Dispose()
        {
            if(!ReferenceEquals(observed,null))observed.onSkillActivatedServer-=Activated;
            observed=null;ownedHold=recovering=null;requested=BotAction.None;Array.Clear(active,0,active.Length);combo.Clear();motion.Clear();
        }
        private void Observe(CharacterBody body)
        {
            if(observed==body)return;
            Dispose();observed=body;observed.onSkillActivatedServer+=Activated;motion.Initialize(Time.unscaledTime,identity);
        }
        private static BotAction ActionFor(CharacterBody body,GenericSkill skill)
        {
            var locator=body.skillLocator;if(!locator)return BotAction.None;
            return locator.primary==skill ? BotAction.Primary : locator.secondary==skill ? BotAction.Secondary
                : locator.utility==skill ? BotAction.Utility : locator.special==skill ? BotAction.Special : BotAction.None;
        }
        private void Activated(GenericSkill skill)
        {
            if(!skill || !skill.skillDef || !BotTacticsRules.Adaptive(tier))return;
            if(skill.skillDef.cancelSprintingOnActivation)motion.AttackInterruptedSprint();
            var action=ActionFor(observed,skill);combo.Confirm(action,Time.unscaledTime);
            if(action!=BotAction.None)
            { for(int i=0;i<4;i++)if(active[i] && active[i].stateMachine==skill.stateMachine)active[i]=null;active[(int)action]=skill; }
            if(tier!=BotIntelligence.Ultra || action!=requested || Time.unscaledTime>requestedUntil)return;
            recovering=skill;requested=BotAction.None;
            string state=skill.activationState.stateType==null ? "" : skill.activationState.stateType.Name;
            chargedHold=action==BotAction.Secondary && state.IndexOf("Charge",StringComparison.OrdinalIgnoreCase)>=0;
            if(chargedHold || action!=BotAction.Primary && NativeHold(skill,action))
            {
                ownedHold=skill;holdStarted=Time.unscaledTime;
                holdUntil=holdStarted+(chargedHold ? Mathf.Clamp(.85f/Mathf.Max(.5f,observed.attackSpeed),.25f,1.4f) : 5f);
            }
        }
        private bool NativeHold(GenericSkill skill,BotAction action)
        {
            foreach(var driver in drivers)
                if(driver && (int)driver.skillSlot==(int)action && (!driver.requiredSkill || driver.requiredSkill==skill.skillDef)
                    && driver.buttonPressType==AISkillDriver.ButtonPressType.Hold)return true;
            return false;
        }
        private static GenericSkill Skill(CharacterBody body,BotAction action)
        {
            if(!body.skillLocator)return null;
            return body.skillLocator.GetSkill((SkillSlot)action);
        }
        private static bool Busy(GenericSkill skill)
        { return skill && skill.stateMachine && !skill.stateMachine.IsInMainState(); }
        private static bool Pressed(BaseAI.BodyInputs input,BotAction action)
        { return action==BotAction.Primary ? input.pressSkill1 : action==BotAction.Secondary ? input.pressSkill2 : action==BotAction.Utility ? input.pressSkill3 : input.pressSkill4; }
        private static void Press(ref BaseAI.BodyInputs input,BotAction action,bool value)
        {
            switch(action) { case BotAction.Primary:input.pressSkill1=value;break;case BotAction.Secondary:input.pressSkill2=value;break;
                case BotAction.Utility:input.pressSkill3=value;break;case BotAction.Special:input.pressSkill4=value;break; }
        }
        internal bool Protected(CharacterBody body,BaseAI.BodyInputs previous)
        {
            if(body.currentVehicle || !body.skillLocator || UnknownState(body))return true;
            for(int i=0;i<4;i++)
            {
                var skill=active[i];
                if(!Busy(skill) || !skill.skillDef)continue;
                // Respect native attack/charge recovery. Sprint must not cancel
                // a utility, scoped shot, hold or multistate animation.
                if(i>=2 || i==1 && (skill.skillDef.canceledFromSprinting || Pressed(previous,(BotAction)i)))return true;
            }
            return false;
        }
        internal bool SprintBlocked(CharacterBody body)
        {
            if(body.currentVehicle || UnknownState(body))return true;
            for(int i=0;i<4;i++)
            {
                var skill=active[i];if(!Busy(skill) || !skill.skillDef)continue;
                if(!skill.skillDef.forceSprintDuringState && (skill.skillDef.canceledFromSprinting || skill.skillDef.cancelSprintingOnActivation || i>=1))return true;
            }
            return false;
        }
        private bool UnknownState(CharacterBody body)
        {
            for(int i=0;i<4;i++)
            {
                var skill=Skill(body,(BotAction)i);if(!Busy(skill))continue;
                bool known=false;
                for(int j=0;j<4;j++)if(active[j] && active[j].stateMachine==skill.stateMachine) { known=true;break; }
                if(!known)return true;
            }
            return false;
        }
        internal bool HasHold { get { return ownedHold; } }
        internal void Combo(BaseAI ai,ref BaseAI.BodyInputs input,BaseAI.BodyInputs previous,CharacterBody enemy,bool fire,BotIntelligence level)
        {
            var body=ai.body;Observe(body);tier=level;
            if(ownedHold)
            {
                var action=ActionFor(body,ownedHold);float now=Time.unscaledTime;
                // A native primary request must not interrupt the coordinated
                // charged/channelled attack before its release or recovery.
                input.pressSkill1=input.pressSkill2=input.pressSkill3=input.pressSkill4=false;
                bool busy=now-holdStarted<.12f || Busy(ownedHold);
                if(busy && now<holdUntil)
                { Press(ref input,action,true);return; }
                if(chargedHold)Press(ref input,action,false);
                ownedHold=null;
                // Let the native charge-release/recovery run before another step.
                return;
            }
            if(recovering)
            {
                if(Busy(recovering))
                {
                    input.pressSkill1 &= previous.pressSkill1;input.pressSkill2 &= previous.pressSkill2;
                    input.pressSkill3 &= previous.pressSkill3;input.pressSkill4 &= previous.pressSkill4;return;
                }
                recovering=null;
            }
            if(level!=BotIntelligence.Ultra || !fire || !enemy || plan.Length==0)return;
            bool busyAny=false;
            for(int i=0;i<4;i++)if(Busy(Skill(body,(BotAction)i))) { busyAny=true;break; }
            if(busyAny || input.pressSkill3)return;
            int ready=0;
            for(int i=0;i<4;i++)if(Eligible(ai,body,enemy,(BotAction)i))ready|=1<<i;
            var choice=combo.Pick(plan,Time.unscaledTime,enemy.GetInstanceID(),ready,Vector3.Distance(body.corePosition,enemy.corePosition),enemy.healthComponent.combinedHealthFraction,false);
            if(choice==BotAction.None)return;
            // A fresh edge is necessary for mustKeyPress skills. Never reset a
            // cooldown or execute/interrupt a state directly.
            bool down=!Pressed(previous,choice);
            input.pressSkill1=input.pressSkill2=input.pressSkill4=false;
            Press(ref input,choice,down);if(down) { requested=choice;requestedUntil=Time.unscaledTime+.4f; }
        }
        private bool Eligible(BaseAI ai,CharacterBody body,CharacterBody enemy,BotAction action)
        {
            var skill=Skill(body,action);
            if(!skill || !skill.skillDef || !skill.CanExecute())return false;
            var state=skill.activationState.stateType;
            if(state==null || state.Assembly!=typeof(CharacterBody).Assembly || skill.skillDef.forceSprintDuringState)return false;
            // No guessed traversal skills. Mage's freeze setup is the only
            // explicit utility; other utilities keep their native AI chains.
            if(action==BotAction.Utility && bodyName!="MageBody")return false;
            foreach(var driver in drivers)
            {
                if(!driver || (int)driver.skillSlot!=(int)action || driver.requiredSkill && driver.requiredSkill!=skill.skillDef
                    || driver.forbiddenSkill==skill.skillDef || driver.maxTimesSelected>=0 && driver.timesSelected>=driver.maxTimesSelected)continue;
                if(driver.moveTargetType!=AISkillDriver.TargetType.CurrentEnemy && driver.aimType!=AISkillDriver.AimType.AtCurrentEnemy)continue;
                float health=body.healthComponent.combinedHealthFraction;
                if(health<driver.minUserHealthFraction || health>driver.maxUserHealthFraction || driver.selectionRequiresOnGround && body.characterMotor && !body.characterMotor.isGrounded)continue;
                float distance;
                if(ai.GameObjectPassesSkillDriverFilters(ai.currentEnemy,driver,out distance))return true;
            }
            return false;
        }
        internal BotMotionIntent Motion(CharacterBody body,BaseAI.BodyInputs input,BaseAI.BodyInputs previous,CharacterBody enemy,bool visible,bool protectedSkill,int identity,BotIntelligence level,
            bool ultraThreat = false, bool projectileThreat = false)
        {
            Observe(body);tier=level;
            bool moving=input.moveVector.sqrMagnitude>.01f || visible || projectileThreat;
            bool threat=ultraThreat;
            if(level != BotIntelligence.Ultra && visible && enemy && enemy.inputBank)
            {
                var toward=(body.corePosition-enemy.inputBank.aimOrigin).normalized;
                threat=Vector3.Dot(enemy.inputBank.aimDirection,toward)>.92f && (enemy.inputBank.skill1.down || enemy.inputBank.skill2.down || enemy.inputBank.skill4.down);
            }
            return motion.Update(Time.unscaledTime,level,identity,moving,body.characterMotor && body.characterMotor.isGrounded,visible || projectileThreat,threat,protectedSkill,true,
                body.isSprinting,previous.pressSprint,previous.pressJump,SprintBlocked(body), level == BotIntelligence.Ultra, projectileThreat);
        }
    }
}
