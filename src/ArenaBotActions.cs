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
        private CharacterBody observed;
        private AISkillDriver[] drivers;
        private readonly float[] retryAfter=new float[4];
        private readonly float[] requestUntil = new float[4];
        private readonly bool[] manageRequest = new bool[4];
        internal int AimSlot { get; private set; }
        private GenericSkill ownedHold, recovering;
        private float holdUntil, holdStarted;
        private bool chargedHold;
        private readonly GenericSkill[] active=new GenericSkill[4];
        private BotIntelligence tier;
        private int identity;

        internal void Initialize(string name,AISkillDriver[] nativeDrivers,int ordinal)
        { drivers=nativeDrivers;identity=ordinal; }
        internal void Dispose()
        {
            if(!ReferenceEquals(observed,null))observed.onSkillActivatedServer-=Activated;
            observed=null;ownedHold=recovering=null;AimSlot=0;Array.Clear(active,0,active.Length);Array.Clear(retryAfter,0,4);
            Array.Clear(requestUntil,0,4);Array.Clear(manageRequest,0,4);motion.Clear();
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
            var action=ActionFor(observed,skill);
            if(action!=BotAction.None)
            { for(int i=0;i<4;i++)if(active[i] && active[i].stateMachine==skill.stateMachine)active[i]=null;active[(int)action]=skill; }
            if (tier != BotIntelligence.Ultra || action == BotAction.None) return;
            int slot = (int)action;
            bool managed = manageRequest[slot] && Time.unscaledTime <= requestUntil[slot];
            requestUntil[slot] = 0;
            manageRequest[slot] = false;
            retryAfter[slot] = 0;
            if (!managed) return;
            recovering=action==BotAction.Primary ? null : skill;
            var learned=ArenaCombatLearning.Model(observed,slot);
            chargedHold=learned!=null && learned.Charge;
            if(chargedHold || action!=BotAction.Primary && NativeHold(skill,action))
            {
                ownedHold=skill;holdStarted=Time.unscaledTime;
                holdUntil=holdStarted+(chargedHold ? Mathf.Clamp((learned?.ChargeTime ?? .85f)/Mathf.Max(.5f,observed.attackSpeed),.15f,8f)
                    : Mathf.Clamp((learned?.Duration ?? 3)*1.25f+.25f,.5f,10));
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

        internal int AimSlotFor(CharacterBody body)
        {
            if (body != observed) return 0;
            if (AimSlot != 0)
            {
                var skill = Skill(body, (BotAction)AimSlot);
                bool releasingCharge = skill && skill == recovering && Busy(skill)
                    && ArenaCombatLearning.Model(body, AimSlot)?.Charge == true;
                if (!skill || skill != ownedHold && !releasingCharge && !skill.CanExecute() || retryAfter[AimSlot] > Time.unscaledTime)
                    AimSlot = 0;
            }
            return AimSlot;
        }

        internal bool TraversalOwnsAim(CharacterBody body)
        {
            if (body.currentVehicle) return true;
            var utility = Skill(body, BotAction.Utility);
            return utility && utility.skillDef && utility == active[2] && Busy(utility)
                && (ArenaCombatLearning.Model(body, 2)?.Movement == true || utility.skillDef.forceSprintDuringState);
        }

        internal bool NativeUtilityAvailable(CharacterBody body, float now)
        {
            var utility = Skill(body, BotAction.Utility);
            return utility && utility.CanExecute() && retryAfter[2] <= now
                && (requestUntil[2] == 0 || now <= requestUntil[2]);
        }

        internal bool TravelUtility(ref BaseAI.BodyInputs input, BaseAI.BodyInputs previous, CharacterBody body, float now)
        {
            input.pressSkill3 = true;
            return KeepNativeRequest(ref input, previous, body, BotAction.Utility, now);
        }

        private void ExpireRequests(float now)
        {
            for (int i = 0; i < requestUntil.Length; i++)
            {
                if (requestUntil[i] == 0 || now <= requestUntil[i]) continue;
                requestUntil[i] = 0;
                manageRequest[i] = false;
                retryAfter[i] = now + .35f;
            }
        }

        private void RecordRequest(BotAction action, float now, bool managed)
        {
            int slot = (int)action;
            if (requestUntil[slot] != 0) return;
            requestUntil[slot] = now + (managed ? .3f : .14f);
            manageRequest[slot] = managed;
        }

        private bool KeepNativeRequest(ref BaseAI.BodyInputs input, BaseAI.BodyInputs previous, CharacterBody body, BotAction action, float now)
        {
            var skill = Skill(body, action);
            if (!skill || !skill.CanExecute() || retryAfter[(int)action] > now
                || skill.mustKeyPress && Pressed(previous, action))
            {
                Press(ref input, action, false);
                return false;
            }
            RecordRequest(action, now, false);
            if (Shares(Skill(body, BotAction.Primary), skill)) input.pressSkill1 = false;
            return true;
        }
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
        internal bool MovementLocked(CharacterBody body)
        {
            if(body.currentVehicle || UnknownState(body))return true;
            // Aiming/charging a weapon is not a reason to stop walking. Native
            // traversal utilities retain control of their movement and vehicle.
            return Busy(active[(int)BotAction.Utility]);
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
            var body=ai.body;Observe(body);tier=level;float now=Time.unscaledTime;
            if (level == BotIntelligence.Ultra)
            {
                ExpireRequests(now);
                Primary(ai,ref input,previous,enemy,fire);
                if (retryAfter[2] > now && !Busy(active[2])) input.pressSkill3 = false;
            }
            if(ownedHold)
            {
                var action=ActionFor(body,ownedHold);
                // A native primary request must not interrupt the coordinated
                // charged/channelled attack before its release or recovery.
                bool primary=input.pressSkill1 && !Shares(Skill(body,BotAction.Primary),ownedHold);
                input.pressSkill1=primary;input.pressSkill2=input.pressSkill3=input.pressSkill4=false;
                bool busy=now-holdStarted<.12f || Busy(ownedHold);
                if(busy && now<holdUntil)
                { Press(ref input,action,true);return; }
                if(chargedHold) { Press(ref input,action,false);recovering=ownedHold; }
                ownedHold=null;
                // Let the native charge-release/recovery run before another step.
                return;
            }
            if(recovering)
            {
                if(Busy(recovering))
                {
                    if(Shares(Skill(body,BotAction.Primary),recovering))input.pressSkill1=false;
                    input.pressSkill2 &= previous.pressSkill2;
                    input.pressSkill3 &= previous.pressSkill3;input.pressSkill4 &= previous.pressSkill4;return;
                }
                recovering=null;
            }
            for(int i=0;i<4;i++)
            {
                var live=active[i];
                if(Busy(live) && ArenaCombatLearning.Model(body,i)?.Charge==true)
                { if(i!=0 && Shares(Skill(body,BotAction.Primary),live))input.pressSkill1=false;return; }
            }
            if (level != BotIntelligence.Ultra) return;
            if (!fire || !enemy)
            {
                if (input.pressSkill3 && !Busy(active[2]))
                    KeepNativeRequest(ref input, previous, body, BotAction.Utility, now);
                if (!enemy && !input.pressSkill3) TrySelfSustain(ref input, previous, ai, body, now);
                return;
            }
            // Native CanExecute evaluates interrupt priority per state machine.
            // A busy primary must not block an independent special or vice versa.
            if(input.pressSkill3)
            {
                var utility=Skill(body,BotAction.Utility);
                if (utility && utility == active[2] && Busy(utility))
                {
                    if(Shares(Skill(body,BotAction.Primary),utility))input.pressSkill1=false;
                }
                else if (KeepNativeRequest(ref input, previous, body, BotAction.Utility, now)) return;
            }
            for(int i=1;i<4;i++)
            {
                if(!Pressed(input,(BotAction)i) || !Eligible(ai,body,enemy,(BotAction)i))continue;
                var native=ArenaCombatLearning.Model(body,i);
                if(native!=null && (!native.KnownDamage || native.Movement || native.Deployable || native.Charge || native.NativeSequence))
                {
                    if (!KeepNativeRequest(ref input, previous, body, (BotAction)i, now)) continue;
                    if (!native.Movement) AimSlot = i;
                    return;
                }
            }
            float distance=Vector3.Distance(body.corePosition,enemy.corePosition),best=float.NegativeInfinity;
            var choice=BotAction.None;
            var self=ArenaCombatLearning.Find(body);var foe=ArenaCombatLearning.Find(enemy);
            float pressure=foe?.Threat(body,now) ?? 0;
            float primaryValue = 0;
            for(int i=0;i<4;i++)
            {
                if(now<retryAfter[i] || !Eligible(ai,body,enemy,(BotAction)i))continue;
                var model=ArenaCombatLearning.Model(body,i);if(model==null)continue;
                if(model.NativeSequence)continue;
                if(i>0 && !model.Combat && !model.KnownDamage && model.Control<=0 && model.Healing<=0)continue;
                if(i==2 && (!model.KnownDamage && model.Control<=0 && model.Healing<=0 || model.Movement))continue;
                if(body.healthComponent.combinedHealthFraction<=model.HealthCost+.1f)continue;
                bool healing=model.Healing>0 && body.healthComponent.combinedHealthFraction<.7f;
                float value=healing ? 0 : model.Value(distance,ProcPower(body),enemy.healthComponent.combinedHealth/Mathf.Max(1,body.damage*ArenaCombatLearning.DamageScale),body.attackSpeed);
                value*=self?.Reliability(i) ?? 1;
                if(model.Control>0 && pressure>.3f)value*=1.6f;
                if(model.Healing>0)value+=model.Healing*Math.Max(0,1-body.healthComponent.combinedHealthFraction)*40;
                if(i>0 && !model.KnownDamage && self!=null && self.Untried(i) && body.healthComponent.combinedHealthFraction>.65f)value+=8;
                if(model.Deployable)value*=.5f;
                value+=i==1 ? .05f : i==3 ? .04f : 0;
                if (i == 0) primaryValue = Mathf.Max(0, value);
                else if (value > 0 && input.pressSkill1 && !Shares(Skill(body, BotAction.Primary), Skill(body, (BotAction)i)))
                    value += primaryValue;
                if(value>best) { best=value;choice=(BotAction)i; }
            }
            if(choice==BotAction.None)return;
            var selected=ArenaCombatLearning.Model(body,(int)choice);
            if(AimSlot!=(int)choice && selected!=null && selected.Speed>0) { AimSlot=(int)choice;return; }
            AimSlot=(int)choice;
            // A fresh edge is necessary for mustKeyPress skills. Never reset a
            // cooldown or execute/interrupt a state directly.
            var chosen=Skill(body,choice);
            bool down=choice==BotAction.Primary && chosen && !chosen.mustKeyPress || !Pressed(previous,choice);
            input.pressSkill2=input.pressSkill4=false;
            if(down && choice!=BotAction.Primary && Shares(Skill(body,BotAction.Primary),Skill(body,choice)))input.pressSkill1=false;
            Press(ref input,choice,down);
            if (down) RecordRequest(choice, now, true);
        }
        private static float ProcPower(CharacterBody body)
        {
            // The learned per-hit coefficient controls this contribution. Item
            // synergies are refined by the existing build scorer, not rerolled.
            var actor=ArenaCombatLearning.Find(body);
            return actor?.ProcPower ?? .5f;
        }
        private void TrySelfSustain(ref BaseAI.BodyInputs input, BaseAI.BodyInputs previous, BaseAI ai, CharacterBody body, float now)
        {
            if (body.currentVehicle || body.healthComponent.combinedHealthFraction >= .55f) return;
            for (int slot = 1; slot < 4; slot++)
            {
                var action = (BotAction)slot;
                var model = ArenaCombatLearning.Model(body, slot);
                if (model == null || model.Healing <= 0 || model.HealthCost > 0 || model.Movement || model.Deployable
                    || model.NativeSequence || now < retryAfter[slot] || Pressed(previous, action)
                    || !Eligible(ai, body, body, action)) continue;
                Press(ref input, action, true);
                RecordRequest(action, now, true);
                return;
            }
        }
        private static bool Shares(GenericSkill a,GenericSkill b)
        { return a && b && a.stateMachine && a.stateMachine==b.stateMachine; }
        private void Primary(BaseAI ai,ref BaseAI.BodyInputs input,BaseAI.BodyInputs previous,CharacterBody enemy,bool fire)
        {
            if(!fire || !enemy || ai.body.currentVehicle)return;
            var skill=Skill(ai.body,BotAction.Primary);if(!skill || !skill.skillDef)return;
            string state=skill.activationState.stateType==null ? "" : skill.activationState.stateType.Name;
            // Native charge/scope drivers own both start and release. Keeping a
            // held charge down forever is not automatic primary pressure.
            if(state.IndexOf("Charge",StringComparison.OrdinalIgnoreCase)>=0
                || state.IndexOf("Reload",StringComparison.OrdinalIgnoreCase)>=0)return;
            if(ownedHold && Shares(skill,ownedHold) || recovering && Busy(recovering) && Shares(skill,recovering))return;
            if(Eligible(ai,ai.body,enemy,BotAction.Primary))
                input.pressSkill1=!skill.mustKeyPress || !previous.pressSkill1;
            else if(active[0]==skill && Busy(skill) && previous.pressSkill1 && NativeHold(skill,BotAction.Primary))
                input.pressSkill1=true;
        }
        private bool Eligible(BaseAI ai,CharacterBody body,CharacterBody enemy,BotAction action)
        {
            var skill=Skill(body,action);
            if(!skill || !skill.skillDef || !skill.CanExecute())return false;
            var state=skill.activationState.stateType;
            if(state==null || !typeof(EntityStates.EntityState).IsAssignableFrom(state) || skill.skillDef.forceSprintDuringState)return false;
            var learned=ArenaCombatLearning.Model(body,(int)action);
            bool selfHeal=learned!=null && learned.Healing>0 && body.healthComponent.combinedHealthFraction<.7f;
            if(learned!=null && (!selfHeal && Vector3.Distance(body.corePosition,enemy.corePosition)>learned.Range+learned.Radius
                || action==BotAction.Utility && (learned.Movement || !learned.KnownDamage && learned.Control<=0 && !selfHeal)))return false;
            foreach(var driver in drivers)
            {
                if(!driver || (int)driver.skillSlot!=(int)action || driver.requiredSkill && driver.requiredSkill!=skill.skillDef
                    || driver.forbiddenSkill==skill.skillDef || driver.additionalForbiddenSkills!=null && Array.IndexOf(driver.additionalForbiddenSkills,skill.skillDef)>=0
                    || driver.maxTimesSelected>=0 && driver.timesSelected>=driver.maxTimesSelected)continue;
                if(!selfHeal && driver.moveTargetType!=AISkillDriver.TargetType.CurrentEnemy && driver.aimType!=AISkillDriver.AimType.AtCurrentEnemy)continue;
                float health=body.healthComponent.combinedHealthFraction;
                if(health<driver.minUserHealthFraction || health>driver.maxUserHealthFraction || driver.selectionRequiresOnGround && body.characterMotor && !body.characterMotor.isGrounded)continue;
                if(selfHeal)return true;
                if(learned!=null && learned.ConfiguredRange && learned.Combat && !learned.Deployable && action!=BotAction.Utility)
                    return true; // Native AI preferred distance is not the attack's physical range.
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
