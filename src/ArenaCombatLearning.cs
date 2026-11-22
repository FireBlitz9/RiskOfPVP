using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using RoR2;
using RoR2.CharacterAI;
using RoR2.ContentManagement;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;

namespace ArenaPvP
{
    internal static class ArenaCombatLearning
    {
        private struct ProfileKey:IEquatable<ProfileKey>
        {
            internal SkillDef Def;internal Type Type;
            internal ProfileKey(GenericSkill skill) { Def=skill.skillDef;Type=skill.activationState.stateType; }
            public bool Equals(ProfileKey other)=>Def==other.Def && Type==other.Type;
            public override bool Equals(object value)=>value is ProfileKey && Equals((ProfileKey)value);
            public override int GetHashCode()=>Def.GetHashCode()*397^(Type==null ? 0 : Type.GetHashCode());
        }
        private sealed class Profile
        {
            internal Type Type;
            internal readonly BotSkillKnowledge Knowledge=new BotSkillKnowledge();
            internal bool Configured;
        }
        internal sealed class Actor
        {
            internal readonly ArenaPlayer Player;
            internal CharacterBody Body;
            internal readonly BotPlayerStyle Style=new BotPlayerStyle();
            internal readonly BotSkillKnowledge[] Skills=new BotSkillKnowledge[4];
            private readonly SkillDef[] defs=new SkillDef[4];
            private readonly Type[] types=new Type[4];
            private readonly EntityStates.EntityState[] states=new EntityStates.EntityState[4];
            private readonly float[] castAt={-100,-100,-100,-100},hitAt={-100,-100,-100,-100};
            private readonly float[] eventAt={-100,-100,-100,-100},attempts=new float[4],successes=new float[4];
            private readonly int[] castSerial=new int[4],hitSerial={-1,-1,-1,-1};
            private readonly float[] intervals=new float[4];
            internal int LastSlot=-1;
            internal float LastCast=-100,LastAttack=-100,NextSample;
            internal float ProcPower;
            internal Actor(ArenaPlayer player) { Player=player; }
            internal void Attach(CharacterBody body)
            {
                if(Body==body)return;
                Detach();Body=body;
                if(!body)return;
                bodies[body]=this;body.onSkillActivatedServer+=Activated;Sync();
            }
            internal void Detach()
            {
                if(!ReferenceEquals(Body,null)) { Body.onSkillActivatedServer-=Activated;bodies.Remove(Body); }
                Body=null;Array.Clear(defs,0,4);Array.Clear(types,0,4);Array.Clear(states,0,4);Array.Clear(Skills,0,4);
                Array.Clear(intervals,0,4);Array.Clear(attempts,0,4);Array.Clear(successes,0,4);Array.Clear(castSerial,0,4);
                for(int i=0;i<4;i++) { castAt[i]=hitAt[i]=eventAt[i]=-100;hitSerial[i]=-1; }
                LastSlot=-1;LastCast=LastAttack=-100;NextSample=ProcPower=0;
            }
            private void Sync()
            {
                if(!Body || !Body.skillLocator)return;
                for(int i=0;i<4;i++)
                {
                    ForSlot(i);
                    var skill=Body.skillLocator.GetSkill((SkillSlot)i);if(skill && skill.skillDef)Configure(profiles[new ProfileKey(skill)]);
                }
            }
            internal BotSkillKnowledge ForSlot(int i)
            {
                var skill=Body && Body.skillLocator ? Body.skillLocator.GetSkill((SkillSlot)i) : null;
                if(!skill || !skill.skillDef)return null;
                var type=skill.activationState.stateType;
                if(defs[i]!=skill.skillDef || types[i]!=type || Skills[i]==null)
                { defs[i]=skill.skillDef;types[i]=type;Skills[i]=Knowledge(skill); }
                return Skills[i];
            }
            private void Activated(GenericSkill skill)
            {
                if(!Observing || !Body || !Body.skillLocator)return;
                for(int i=0;i<4;i++)if(Body.skillLocator.GetSkill((SkillSlot)i)==skill) { eventAt[i]=Time.unscaledTime;Cast(i,Time.unscaledTime);return; }
            }
            private void Cast(int slot,float now)
            {
                if(now-castAt[slot]<.08f)return;
                float interval=now-castAt[slot];
                if(interval>.25f && interval<60)intervals[slot]=intervals[slot]>0 ? Math.Min(intervals[slot],interval) : interval;
                castAt[slot]=now;LastSlot=slot;LastCast=now;Style.Cast(slot,now);
                attempts[slot]=Math.Min(30,attempts[slot]*.97f+1);successes[slot]*=.97f;castSerial[slot]++;
                if(Skills[slot]!=null && Skills[slot].Combat)LastAttack=now;
            }
            internal void Sample(float now)
            {
                if(!Body || !Body.skillLocator || now<NextSample)return;NextSample=now+.12f;Sync();
                for(int i=0;i<4;i++)
                {
                    var skill=Body.skillLocator.GetSkill((SkillSlot)i);
                    var state=skill && skill.stateMachine && !skill.stateMachine.IsInMainState() ? skill.stateMachine.state : null;
                    if(state!=null && state!=states[i])
                    {
                        if(state.GetType()==types[i] && now-eventAt[i]>.5f)Cast(i,now);
                        if((state.GetType()==types[i] || LastSlot==i) && Skills[i]!=null)ReadActive(state,Skills[i]);
                    }
                    states[i]=state;
                }
                var motion=Body.characterMotor ? Body.characterMotor.velocity : Vector3.zero;motion.y=0;
                Vector3 aim=Body.inputBank ? Body.inputBank.aimDirection : Vector3.forward;aim.y=0;aim.Normalize();
                float speed=motion.magnitude;
                Style.Motion(now,Mathf.Abs(Vector3.Dot(motion,Vector3.Cross(Vector3.up,aim)))/Mathf.Max(4,Body.moveSpeed),
                    Vector3.Dot(motion,aim)/Mathf.Max(4,Body.moveSpeed),speed,Body.characterMotor && Body.characterMotor.isGrounded,Body.isSprinting);
                if(Body.inventory)
                    ProcPower=Count(Body.inventory,RoR2Content.Items.Missile)*.3f+Count(Body.inventory,RoR2Content.Items.ChainLightning)*.2f
                        +Count(Body.inventory,RoR2Content.Items.BleedOnHit)*.08f+Count(Body.inventory,RoR2Content.Items.StickyBomb)*.05f
                        +Count(Body.inventory,DLC1Content.Items.MissileVoid)*.4f+Count(Body.inventory,DLC1Content.Items.ChainLightningVoid)*.4f;
            }
            internal int Slot(DamageSource source,float now)
            {
                switch(source & DamageSource.SkillMask)
                { case DamageSource.Primary:return 0;case DamageSource.Secondary:return 1;case DamageSource.Utility:return 2;case DamageSource.Special:return 3; }
                return now-LastCast<1.5f ? LastSlot : -1;
            }
            internal void Hit(int slot,float now)
            {
                if(slot<0 || slot>3 || now-hitAt[slot]<.12f || hitSerial[slot]==castSerial[slot])return;
                hitSerial[slot]=castSerial[slot];hitAt[slot]=now;successes[slot]=Math.Min(attempts[slot],successes[slot]+1);Style.Hit(now);LastAttack=now;
            }
            internal float Reliability(int slot)=>(successes[slot]+2)/(attempts[slot]+3);
            internal bool Untried(int slot)=>attempts[slot]<1;
            internal bool Opening(float now)
            {
                if(!Body || !Body.skillLocator)return false;
                for(int i=1;i<4;i++)
                {
                    var model=Skills[i];var skill=Body.skillLocator.GetSkill((SkillSlot)i);
                    if(model==null || !model.Combat || !skill || skill.maxStock>1 || model.Cooldown<3)continue;
                    float duration=Math.Max(model.Duration,model.Charge ? model.ChargeTime : 0);
                    float cooldown=intervals[i]>0 ? Math.Min(model.Cooldown,intervals[i]) : model.Cooldown;
                    if(now-castAt[i]>duration+.15f && now-castAt[i]<Math.Min(3,cooldown*.65f))return true;
                }
                return false;
            }
            internal float Threat(CharacterBody target,float now)
            {
                if(!Body || !target || !target.healthComponent || LastSlot<0 || LastSlot>3)return 0;
                var model=Skills[LastSlot];var skill=Body.skillLocator.GetSkill((SkillSlot)LastSlot);
                if(model==null || !model.Combat || !skill || !skill.stateMachine || skill.stateMachine.IsInMainState()
                    || Vector3.Distance(Body.corePosition,target.corePosition)>model.Range+model.Radius
                    || !model.Charge && now-castAt[LastSlot]>model.Duration+.5f)return 0;
                if(Body.inputBank && Vector3.Distance(Body.corePosition,target.corePosition)>model.Radius
                    && Vector3.Dot(Body.inputBank.aimDirection,(target.corePosition-Body.corePosition).normalized)<.65f)return 0;
                float armor=target.armor>=0 ? 100/(100+target.armor) : 2-100/(100-target.armor);
                float crit=1+Mathf.Clamp01(Body.crit*.01f)*Mathf.Max(0,Body.critMultiplier-1);
                return model.Damage*model.Hits*Body.damage*DamageScale*crit*armor/Mathf.Max(1,target.healthComponent.fullCombinedHealth+target.healthComponent.barrier);
            }
        }
        private static int Count(Inventory inventory,ItemDef item)=>item ? inventory.GetItemCountEffective(item) : 0;
        private static readonly Dictionary<ProfileKey,Profile> profiles=new Dictionary<ProfileKey,Profile>();
        private static readonly Dictionary<Type,EntityStateConfiguration> configurations=new Dictionary<Type,EntityStateConfiguration>();
        private static readonly Dictionary<Type,FieldInfo[]> activeFields=new Dictionary<Type,FieldInfo[]>();
        private static readonly Dictionary<ArenaPlayer,Actor> actors=new Dictionary<ArenaPlayer,Actor>();
        private static readonly Dictionary<CharacterBody,Actor> bodies=new Dictionary<CharacterBody,Actor>();
        private static readonly List<Actor> roster=new List<Actor>();
        private static EntityStateConfiguration[] configurationSource;
        private static int configurationIndex,cursor;
        private static float refreshAt;
        internal static bool Observing { get; private set; }
        internal static bool HasActors=>actors.Count>0;
        internal static float DamageScale { get; private set; }=1;
        internal static void Pause() { Observing=false; }

        internal static void Reset()
        {
            Observing=false;DamageScale=1;
            foreach(var actor in actors.Values)actor.Detach();actors.Clear();bodies.Clear();roster.Clear();profiles.Clear();
            activeFields.Clear();cursor=0;refreshAt=0;
        }
        internal static Actor Find(CharacterBody body) { Actor actor;return body && bodies.TryGetValue(body,out actor) ? actor : null; }
        internal static BotSkillKnowledge Model(CharacterBody body,int slot)
        {
            var actor=Find(body);if(actor!=null && slot>=0 && slot<4)return actor.ForSlot(slot);
            var skill=body && body.skillLocator ? body.skillLocator.GetSkill((SkillSlot)slot) : null;
            return skill && skill.skillDef ? Knowledge(skill) : null;
        }
        internal static void Step(ArenaMatch match,float now)
        {
            Observing=true;DamageScale=match.Rules?.DamageScale ?? 1;
            var source=ContentManager.entityStateConfigurations;
            if(source!=configurationSource)
            { configurationSource=source;configurationIndex=0;configurations.Clear();foreach(var profile in profiles.Values)profile.Configured=false; }
            // Catalog indexing is spread across frames, within the shared bot
            // decision budget. This never scans assemblies or loads assets.
            for(int n=0;source!=null && configurationIndex<source.Length && n<24;n++)
            {
                var config=source[configurationIndex++];if(!config)continue;
                var type=(Type)config.targetType;if(type!=null)configurations[type]=config;
            }
            if(now>=refreshAt)
            {
                refreshAt=now+.25f;roster.Clear();
                foreach(var player in match.AllPlayers)
                {
                    if(!player.Connected || !player.Master)continue;
                    Actor actor;if(!actors.TryGetValue(player,out actor)) { actor=new Actor(player);actors.Add(player,actor); }
                    actor.Attach(player.Dead ? null : player.Master.GetBody());if(actor.Body)roster.Add(actor);
                }
                foreach(var actor in actors.Values)if(!actor.Player.Connected && actor.Body)actor.Detach();
            }
            for(int n=0;n<Math.Min(4,roster.Count);n++)
            { cursor%=roster.Count;roster[cursor++].Sample(now); }
        }
        internal static BotSkillKnowledge Knowledge(GenericSkill skill)
        {
            Profile profile;var key=new ProfileKey(skill);var type=key.Type;
            if(!profiles.TryGetValue(key,out profile))
            {
                profile=new Profile { Type=type };profiles[key]=profile;
                profile.Knowledge.Combat=skill.isCombatSkill;profile.Knowledge.Cooldown=skill.baseRechargeInterval;
                if(type!=null)profile.Knowledge.StateName(type.Name);
            }
            Configure(profile);return profile.Knowledge;
        }

        internal static float RegisteredDuration(Type type) => RegisteredNumber(type, "baseDuration");

        private static float RegisteredNumber(Type type, string fieldName)
        {
            if (!configurations.TryGetValue(type, out var config)) return 0;
            var fields = config.serializedFieldsCollection.serializedFields;
            if (fields == null) return 0;
            foreach (var field in fields)
                if (string.Equals(field.fieldName, fieldName, StringComparison.OrdinalIgnoreCase)
                    && float.TryParse(field.fieldValue.stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float duration)
                    && duration > 0 && !float.IsInfinity(duration)) return duration;
            return 0;
        }

        internal static float FinisherCoefficient(CharacterBody body, Type activation)
        {
            if (activation == typeof(EntityStates.Bandit2.Weapon.PrepSidearmResetRevolver))
                return RegisteredNumber(typeof(EntityStates.Bandit2.Weapon.FireSidearmResetRevolver), "damageCoefficient");
            if (activation == typeof(EntityStates.Bandit2.Weapon.PrepSidearmSkullRevolver))
                return RegisteredNumber(typeof(EntityStates.Bandit2.Weapon.FireSidearmSkullRevolver), "damageCoefficient")
                    * (1f + .1f * body.GetBuffCount(RoR2Content.Buffs.BanditSkull));
            return 0;
        }

        internal static GameObject RegisteredProjectile(Type type)
        {
            if (!configurations.TryGetValue(type, out var config)) return null;
            var fields = config.serializedFieldsCollection.serializedFields;
            if (fields == null) return null;
            foreach (var field in fields)
                if (string.Equals(field.fieldName, "projectilePrefab", StringComparison.OrdinalIgnoreCase))
                    return field.fieldValue.objectValue as GameObject;
            return null;
        }
        internal static void SeedDrivers(CharacterBody body,AISkillDriver[] drivers)
        {
            if(!body || !body.skillLocator)return;
            foreach(var driver in drivers)
            {
                if(!driver || driver.skillSlot==SkillSlot.None || !ArenaBotKit.TargetsEnemy(driver))continue;
                var skill=body.skillLocator.GetSkill(driver.skillSlot);
                if(!skill || !skill.skillDef || driver.requiredSkill && driver.requiredSkill!=skill.skillDef || driver.forbiddenSkill==skill.skillDef)continue;
                Knowledge(skill).DriverRange(driver.minDistance,driver.maxDistance);
            }
        }
        private static void Configure(Profile profile)
        {
            EntityStateConfiguration config;
            if(profile.Configured || profile.Type==null || !configurations.TryGetValue(profile.Type,out config))return;
            profile.Configured=true;
            var fields=config.serializedFieldsCollection.serializedFields;if(fields==null)return;
            foreach(var field in fields)
            {
                if(string.IsNullOrEmpty(field.fieldName))continue;
                float number;
                if(float.TryParse(field.fieldValue.stringValue,NumberStyles.Float,CultureInfo.InvariantCulture,out number))profile.Knowledge.Hint(field.fieldName,number);
                var prefab=field.fieldValue.objectValue as GameObject;
                if(prefab && field.fieldName.IndexOf("projectile",StringComparison.OrdinalIgnoreCase)>=0)ProjectileHint(prefab,profile.Knowledge);
            }
        }
        private static void ProjectileHint(GameObject prefab,BotSkillKnowledge model)
        {
            var controller=prefab.GetComponent<ProjectileController>();if(controller)model.Proc=controller.procCoefficient;
            var simple=prefab.GetComponent<ProjectileSimple>();
            if(simple)
            { model.Speed=simple.desiredForwardSpeed;if(!model.ConfiguredRange && simple.lifetime>0 && model.Speed>0) { model.Range=Mathf.Min(1000,simple.lifetime*model.Speed);model.ConfiguredRange=true; } }
        }
        private static void ReadActive(EntityStates.EntityState state,BotSkillKnowledge model)
        {
            var type=state.GetType();FieldInfo[] fields;
            if(!activeFields.TryGetValue(type,out fields))
            {
                var selected=new List<FieldInfo>();
                foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.Public))
                {
                    string name=field.Name.ToLowerInvariant();
                    if((field.FieldType==typeof(float) || field.FieldType==typeof(int))
                        && (name.Contains("coefficient") || name.Contains("duration") || name.Contains("range") || name.Contains("radius") || name.Contains("distance") || name.Contains("count") || name.Contains("velocity") || name=="minimumdamage"))selected.Add(field);
                    if(selected.Count>=16)break;
                }
                fields=selected.ToArray();activeFields[type]=fields;
            }
            foreach(var field in fields)
                try { model.Hint(field.Name,Convert.ToSingle(field.GetValue(state),CultureInfo.InvariantCulture)); }
                catch(ArgumentException) { } catch(FieldAccessException) { }
        }
        internal static void Emission(GameObject owner,DamageTypeCombo type,float damage,float proc,float range,float speed,float radius,float hits=1)
        {
            if(!owner)return;var body=owner.GetComponent<CharacterBody>();var actor=Find(body);if(actor==null)return;
            int slot=actor.Slot(type.damageSource,Time.unscaledTime);if(slot<0)return;
            var model=Model(body,slot);if(model==null)return;
            if((ulong)(type & (DamageType.Freeze2s|DamageType.Stun1s))!=0)model.Control=Math.Max(model.Control,.8f);
            if((ulong)(type & (DamageType.BleedOnHit|DamageType.IgniteOnHit|DamageType.PoisonOnHit|DamageType.BlightOnHit))!=0)model.Dot=true;
            model.Emission(damage/Mathf.Max(1,body.damage),proc,range,speed,radius,hits);actor.LastAttack=Time.unscaledTime;
        }
        internal static void Hit(CharacterBody attacker,DamageInfo info)
        {
            var actor=Find(attacker);if(actor==null)return;
            int slot=actor.Slot(info.damageType.damageSource,Time.unscaledTime);actor.Hit(slot,Time.unscaledTime);
            // Hits supply proc evidence even for client-authoritative attacks
            // whose emission is not executed again on the server.
            if(slot>=0 && (info.damageType.damageSource&DamageSource.SkillMask)!=DamageSource.NoneSpecified && info.procChainMask.Equals(default(ProcChainMask)))
            { var model=Model(attacker,slot);if(model!=null)model.Proc=info.procCoefficient; }
        }
    }
}
