using System;
using System.Collections.Generic;
using ArenaPvP;
using RoR2;
using RoR2.ContentManagement;
using UnityEngine;

// Executes the production catalog, cast/state observer, telemetry and registry
// lifecycle. Native Unity asset/event APIs are a small deterministic adapter.
internal static class CombatCatalogTests
{
    private static int checks;
    private static void Check(bool value,string name) { checks++;if(!value)throw new Exception(name); }
    public static int Main()
    {
        try
        {
            var configs=new List<EntityStateConfiguration>();
            for(int i=0;i<24;i++)configs.Add(Config(typeof(NoiseState),"damageCoefficient",1));
            var fire=Config(typeof(FireState),"damageCoefficient",2.5f,"procCoefficient",.25f,"maxDistance",95,"baseDuration",.4f);
            configs.Add(fire);ContentManager.entityStateConfigurations=configs.ToArray();
            var body=new CharacterBody();body.skillLocator.primary.activationState=new SerializableEntityStateType(typeof(FireState));
            var player=new ArenaPlayer { Master=new CharacterMaster { Body=body } };var match=new ArenaMatch();match.AllPlayers.Add(player);
            ArenaCombatLearning.Reset();Time.unscaledTime=0;ArenaCombatLearning.Step(match,0);
            Check(body.Subscribers==1 && ArenaCombatLearning.Model(body,0).Damage==1,"first batch registers the body without synchronously indexing the entire catalog");
            Time.unscaledTime=.2f;ArenaCombatLearning.Step(match,.2f);
            var learned=ArenaCombatLearning.Model(body,0);
            Check(learned.Damage==2.5f && learned.Proc==.25f && learned.Range==95,"a later catalog batch supplies actual numeric skill priors");
            body.skillLocator.primary.stateMachine.state=new FireState();body.skillLocator.primary.stateMachine.Main=false;
            body.Activate(body.skillLocator.primary,.3f);
            ArenaCombatLearning.Emission(body.gameObject,new DamageTypeCombo { damageSource=DamageSource.Primary },36,.6f,120,60,1,2);
            Check(learned.Samples==1 && learned.Damage==3 && Math.Abs(learned.Proc-.6f)<.001f && learned.Speed==60 && learned.Hits==2,"actual emission values reach the registered skill rather than a fabricated survivor preset");
            var actor=ArenaCombatLearning.Find(body);var style=actor.Style;
            ArenaCombatLearning.Hit(body,new DamageInfo { damageType=new DamageTypeCombo { damageSource=DamageSource.Primary },procCoefficient=.3f });
            Check(learned.Proc==.3f && actor.Reliability(0)>.6f,"authoritative hit proc coefficient and cast outcome refine the same actor");
            var target=new CharacterBody { corePosition=new Vector3(0,0,10) };
            learned.Damage=12;learned.Hits=1;Time.unscaledTime=.35f;
            Check(actor.Threat(target,.35f)>1,"an aimed active high-damage skill is recognized before damage lands");
            float unarmored=actor.Threat(target,.35f);target.armor=100;
            Check(Math.Abs(actor.Threat(target,.35f)*2-unarmored)<.001f,"counter planning accounts for the target's native armor");
            target.armor=0;match.Rules.DamageScale=2;ArenaCombatLearning.Step(match,.35f);
            Check(Math.Abs(actor.Threat(target,.35f)-unarmored*2)<.001f,"threat estimates respect the host's PvP damage multiplier");
            body.inputBank.aimDirection=new Vector3(0,0,-1);
            Check(actor.Threat(target,.35f)==0,"a heavy attack aimed elsewhere does not force a false retreat");
            body.inputBank.aimDirection=Vector3.forward;
            var secondary=body.skillLocator.secondary;secondary.baseRechargeInterval=10;
            secondary.activationState=new SerializableEntityStateType(typeof(FireState));ArenaCombatLearning.Model(body,1);
            body.Activate(secondary,1);secondary.stateMachine.Main=true;
            Check(actor.Opening(2),"an observed single-stock cast supplies a counterattack opening");
            secondary.maxStock=2;
            Check(!actor.Opening(2),"multi-stock skills are not assumed empty after one cast");
            var old=learned;body.skillLocator.primary.skillDef=new RoR2.Skills.SkillDef();
            body.skillLocator.primary.activationState=new SerializableEntityStateType(typeof(NoiseState));
            Check(!ReferenceEquals(ArenaCombatLearning.Model(body,0),old),"loadout/stance skill replacement is recognized immediately instead of waiting for the sampling tick");
            player.Dead=true;ArenaCombatLearning.Step(match,2.3f);
            Check(body.Subscribers==0 && ArenaCombatLearning.Find(body)==null,"death detaches the old body and its native activation event");
            var next=new CharacterBody();player.Master.Body=next;player.Dead=false;ArenaCombatLearning.Step(match,2.6f);
            Check(next.Subscribers==1 && ReferenceEquals(ArenaCombatLearning.Find(next).Style,style),"respawn preserves the player model while attaching only the new body");
            Check(ArenaCombatLearning.Find(next).LastSlot==-1 && !ArenaCombatLearning.Find(next).Opening(2.6f),"respawn clears stale cast/cooldown evidence while retaining longer-term style");
            ArenaCombatLearning.Pause();
            Check(!ArenaCombatLearning.Observing,"finished matches suspend telemetry instead of learning from post-match attacks");
            ArenaCombatLearning.Reset();
            Check(next.Subscribers==0 && ArenaCombatLearning.Find(next)==null,"run cleanup releases subscriptions and actor references");
            Console.WriteLine("PASS: "+checks+" production combat-catalog and telemetry assertions.");return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e);return 1; }
    }
    private static EntityStateConfiguration Config(Type type,params object[] values)
    {
        var fields=new SerializedField[values.Length/2];
        for(int i=0;i<fields.Length;i++)fields[i]=new SerializedField { fieldName=(string)values[i*2],fieldValue=new SerializedValue { stringValue=Convert.ToString(values[i*2+1],System.Globalization.CultureInfo.InvariantCulture) } };
        return new EntityStateConfiguration { targetType=type,serializedFieldsCollection=new SerializedFields { serializedFields=fields } };
    }
}
public class FireState:EntityStates.EntityState { public float damageCoefficient=2.5f,procCoefficient=.25f,baseDuration=.4f; }
public class NoiseState:EntityStates.EntityState { }
namespace ArenaPvP
{
    internal class ArenaPlayer { internal bool Connected=true,Dead=false;internal CharacterMaster Master; }
    internal class ArenaMatch { internal readonly List<ArenaPlayer> AllPlayers=new List<ArenaPlayer>();internal Rules Rules=new Rules(); }
    internal class Rules { internal float DamageScale=1; }
    internal static class ArenaBotKit { internal static bool TargetsEnemy(RoR2.CharacterAI.AISkillDriver d)=>true; }
}
namespace EntityStates { public class EntityState { } }
namespace EntityStates.Bandit2.Weapon
{
    public class PrepSidearmResetRevolver : EntityStates.EntityState { }
    public class PrepSidearmSkullRevolver : EntityStates.EntityState { }
    public class FireSidearmResetRevolver : EntityStates.EntityState { }
    public class FireSidearmSkullRevolver : EntityStates.EntityState { }
}
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value)=>value!=null; }
    public class GameObject:Object { public CharacterBodyRef Ref=new CharacterBodyRef();public T GetComponent<T>() where T:class=>Ref.Body as T; }
    public class CharacterBodyRef { public RoR2.CharacterBody Body=null; }
    public struct Vector3
    {
        public float x,y,z;public Vector3(float a,float b,float c) { x=a;y=b;z=c; }
        public static Vector3 zero=>new Vector3();public static Vector3 forward=>new Vector3(0,0,1);public static Vector3 up=>new Vector3(0,1,0);
        public float magnitude=>(float)Math.Sqrt(x*x+y*y+z*z);
        public Vector3 normalized=>magnitude>0 ? new Vector3(x/magnitude,y/magnitude,z/magnitude) : zero;
        public void Normalize() { this=normalized; }
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        public static Vector3 Cross(Vector3 a,Vector3 b)=>new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
        public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
    }
    public static class Mathf { public static float Abs(float n)=>Math.Abs(n);public static float Min(float a,float b)=>Math.Min(a,b);public static float Max(float a,float b)=>Math.Max(a,b);public static float Clamp01(float n)=>Math.Max(0,Math.Min(1,n)); }
    public static class Time { public static float unscaledTime; }
}
namespace RoR2
{
    public class CharacterBody:UnityEngine.Object
    {
        public SkillLocator skillLocator=new SkillLocator();public CharacterMotor characterMotor=new CharacterMotor();
        public InputBank inputBank=new InputBank();public Inventory inventory=new Inventory();public HealthComponent healthComponent=new HealthComponent();
        public float moveSpeed=7,damage=12,crit=0,critMultiplier=2,armor=0;public bool isSprinting=false;public Vector3 corePosition;
        public readonly GameObject gameObject=new GameObject();
        public CharacterBody() { gameObject.Ref.Body=this; }
        public int GetBuffCount(object buff) => 0;
        public event Action<GenericSkill> onSkillActivatedServer;
        public int Subscribers=>onSkillActivatedServer?.GetInvocationList().Length ?? 0;
        public void Activate(GenericSkill skill,float now) { Time.unscaledTime=now;onSkillActivatedServer?.Invoke(skill); }
    }
    public class CharacterMaster:UnityEngine.Object { public CharacterBody Body;public CharacterBody GetBody()=>Body; }
    public class CharacterMotor:UnityEngine.Object { public Vector3 velocity=Vector3.zero;public bool isGrounded=true,disableAirControlUntilCollision; }
    public class InputBank:UnityEngine.Object { public Vector3 aimDirection=Vector3.forward; }
    public class Inventory:UnityEngine.Object { public int GetItemCountEffective(ItemDef item)=>0; }
    public class HealthComponent:UnityEngine.Object { public float fullCombinedHealth=100,barrier=0; }
    public class ItemDef:UnityEngine.Object { }
    public static class RoR2Content { public static class Items { public static ItemDef Missile,ChainLightning,BleedOnHit,StickyBomb; } public static class Buffs { public static readonly object BanditSkull = new object(); } }
    public static class DLC1Content { public static class Items { public static ItemDef MissileVoid,ChainLightningVoid; } }
    public enum SkillSlot { None=-1,Primary,Secondary,Utility,Special }
    public struct SerializableEntityStateType { public Type stateType;public SerializableEntityStateType(Type type) { stateType=type; } }
    public class EntityStateMachine:UnityEngine.Object { public bool Main=true;public EntityStates.EntityState state;public bool IsInMainState()=>Main; }
    public class GenericSkill:UnityEngine.Object
    { public RoR2.Skills.SkillDef skillDef=new RoR2.Skills.SkillDef();public SerializableEntityStateType activationState=new SerializableEntityStateType(typeof(NoiseState));public bool isCombatSkill=true;public float baseRechargeInterval=1;public int maxStock=1;public EntityStateMachine stateMachine=new EntityStateMachine(); }
    public class SkillLocator:UnityEngine.Object { public GenericSkill primary=new GenericSkill(),secondary=new GenericSkill(),utility=new GenericSkill(),special=new GenericSkill();public GenericSkill GetSkill(SkillSlot s)=>s==SkillSlot.Primary ? primary : s==SkillSlot.Secondary ? secondary : s==SkillSlot.Utility ? utility : special; }
    [Flags] public enum DamageSource { NoneSpecified=0,Primary=1,Secondary=2,Utility=4,Special=8,SkillMask=15 }
    [Flags] public enum DamageType { Freeze2s=1,Stun1s=2,BleedOnHit=4,IgniteOnHit=8,PoisonOnHit=16,BlightOnHit=32 }
    public struct DamageTypeCombo { public DamageSource damageSource;public DamageType damageType;public static DamageType operator &(DamageTypeCombo a,DamageType b)=>a.damageType&b; }
    public struct ProcChainMask { public int bits; }
    public class DamageInfo { public DamageTypeCombo damageType;public ProcChainMask procChainMask;public float procCoefficient; }
    public class EntityStateConfiguration:UnityEngine.Object { public Type targetType;public SerializedFields serializedFieldsCollection; }
    public class SerializedFields { public SerializedField[] serializedFields; }
    public struct SerializedField { public string fieldName;public SerializedValue fieldValue; }
    public struct SerializedValue { public string stringValue;public UnityEngine.Object objectValue; }
}
namespace RoR2.Skills { public class SkillDef:UnityEngine.Object { } }
namespace RoR2.ContentManagement { public static class ContentManager { public static RoR2.EntityStateConfiguration[] entityStateConfigurations; } }
namespace RoR2.CharacterAI { public class AISkillDriver:UnityEngine.Object { public SkillSlot skillSlot;public RoR2.Skills.SkillDef requiredSkill,forbiddenSkill;public float minDistance,maxDistance; } }
namespace RoR2.Projectile
{
    public class ProjectileController:UnityEngine.Object { public float procCoefficient=1; }
    public class ProjectileSimple:UnityEngine.Object { public float desiredForwardSpeed=60,lifetime=5; }
}
