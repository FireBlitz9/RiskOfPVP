using System;
using System.Globalization;

namespace ArenaPvP
{
    // Numeric priors come from registered state configuration; emitted attacks
    // refine them. No state is instantiated or executed to discover its damage.
    public sealed class BotSkillKnowledge
    {
        public float Damage=1, Proc=1, Hits=1, Range=40, MinimumRange, Radius, Speed, Duration=.35f, ChargeTime=.85f, Cooldown;
        public bool KnownDamage, ConfiguredRange, Charge, Movement, Deployable, Combat;
        public float Control;
        public float Healing, HealthCost;
        public bool Dot, NativeSequence;
        public float FalloffStart,FalloffEnd,MinimumDamage=1;
        internal float TravelDuration, TravelSpeedCoefficient, TravelStartCoefficient, TravelEndCoefficient;
        internal bool InstantShot;
        public int Samples;
        private float driverRange;
        public void DriverRange(float minimum,float maximum)
        {
            if(maximum<=0 || float.IsNaN(maximum))return;
            if(float.IsInfinity(maximum))maximum=250;
            MinimumRange=driverRange>0 ? Math.Min(MinimumRange,Math.Max(0,minimum)) : Math.Max(0,minimum);
            driverRange=Math.Max(driverRange,Math.Min(1000,maximum));
            if(!ConfiguredRange)Range=driverRange;
        }
        public void Hint(string field,float value)
        {
            if(float.IsNaN(value) || float.IsInfinity(value) || value<0)return;
            string key=field.ToLowerInvariant();
            if(key.Contains("healthcost") && value<1) { HealthCost=Math.Max(HealthCost,value);return; }
            if(key.Contains("self") || key.Contains("recoil"))return;
            if(Samples>0 && (key.Contains("damagecoefficient") || key.Contains("proccoefficient") || key.Contains("count")))return;
            if(key.Contains("damagecoefficient")) { Damage=Math.Max(0,value);KnownDamage=true;Combat=true; }
            else if(key=="healfraction" || key=="healingfraction" || key=="barrierfraction")Healing=Math.Min(1,value);
            else if(key.Contains("proccoefficient"))Proc=Math.Min(10,value);
            else if(key=="bulletcount" || key=="shotcount" || key=="projectilecount" || key=="attackcount")Hits=Math.Max(1,Math.Min(64,value));
            else if(key.Contains("chargeduration") || key.Contains("chargeupduration")) { ChargeTime=Math.Max(.1f,value);Charge=true; }
            else if (key == "duration")
            {
                Duration = Math.Max(.08f, value);
                TravelDuration = value;
            }
            else if (key == "baseduration") Duration = Math.Max(.08f, value);
            else if (key == "speedcoefficient") TravelSpeedCoefficient = value;
            else if (key == "initialspeedcoefficient") TravelStartCoefficient = value;
            else if (key == "finalspeedcoefficient") TravelEndCoefficient = value;
            else if(key=="maxdistance" || key=="maxrange" || key=="maxbulletdistance" || key=="bulletmaxdistance" || key=="attackrange")
            { if(value>0) { Range=Math.Max(.5f,Math.Min(1000,value));ConfiguredRange=true; } }
            else if(key=="radius" || key=="blastradius" || key=="attackradius")Radius=Math.Min(100,value);
            else if(key=="projectilespeed" || key=="projectilevelocity" || key=="forwardvelocity")Speed=Math.Min(500,value);
            else if(key=="falloffstartdistance")FalloffStart=value;
            else if(key=="falloffenddistance")FalloffEnd=value;
            else if(key=="minimumdamage")MinimumDamage=Math.Max(0,Math.Min(1,value));
        }
        public void StateName(string name)
        {
            name=(name ?? "").ToLowerInvariant();
            Charge|=name.Contains("charge");
            Movement |= LooksLikeTravel(name);
            Deployable|=name.Contains("place") || name.Contains("deploy") || name.Contains("turret") || name.Contains("supply");
            NativeSequence|=name.Contains("scope") || name.Contains("reload") || name.Contains("grapple") || name.Contains("stance");
            if(name.Contains("freeze") || name.Contains("icewall"))Control=Math.Max(Control,.8f);
        }
        private static bool LooksLikeTravel(string name) => name.Contains("blink") || name.Contains("dodge")
            || name.Contains("slide") || name.Contains("leap") || name.Contains("assaulter") || name.Contains("grapple")
            || name.Contains("dash") || name.Contains("teleport");

        internal static bool IsTravelState(Type type) => type != null && LooksLikeTravel(type.Name.ToLowerInvariant());
        public void Emission(float coefficient,float proc,float range,float speed,float radius,float hits=1)
        {
            if(coefficient<=0 || float.IsNaN(coefficient) || float.IsInfinity(coefficient))return;
            float weight=Samples==0 ? 1 : .22f;
            if(!KnownDamage)Damage=coefficient;else Damage=Math.Max(Damage*.8f,Damage+(coefficient-Damage)*weight);
            Proc=Math.Max(0,Math.Min(10,Proc+(proc-Proc)*weight));
            if(range>0) { Range=Math.Max(.5f,Math.Min(1000,range));ConfiguredRange=true; }
            if(speed>0) { Speed=Math.Min(500,speed);InstantShot=false; }
            if(radius>0)Radius=Math.Min(100,radius);
            Hits=Math.Max(1,Math.Min(64,hits));
            KnownDamage=Combat=true;Samples++;
        }
        public float Value(float distance,float procPower,float enemyHealth,float attackSpeed)
        {
            if(distance<MinimumRange || distance>Range+Radius)return float.NegativeInfinity;
            float duration=Math.Max(.12f,(Charge ? ChargeTime : Duration)/Math.Max(.5f,attackSpeed));
            float value=(Damage+(Damage>0 || !KnownDamage ? Math.Max(0,procPower)*Proc*.6f : 0))*Hits/duration;
            if(FalloffEnd>FalloffStart && distance>FalloffStart)
                value*=MinimumDamage+(1-MinimumDamage)*Math.Max(0,Math.Min(1,(FalloffEnd-distance)/(FalloffEnd-FalloffStart)));
            if(Dot)value+=Math.Max(.5f,enemyHealth*.03f)/Math.Max(.4f,duration);
            value+=Control*5;
            if(Damage*Hits>enemyHealth)value*=1.3f;
            if(!KnownDamage)value*=.55f;
            return value;
        }
    }

    public sealed class BotPlayerStyle
    {
        private float casts,hits,lastEvent,lastMotion,strafe,approach,jumps,sprint,speed,seconds;
        private bool wasGrounded=true;
        private readonly float[] slots=new float[4];
        public float Strafe=>strafe;
        public float Approach=>approach;
        public float Jumping=>jumps;
        public float Sprinting=>sprint;
        public float Skill
        {
            get
            {
                int variety=0;for(int i=0;i<4;i++)if(slots[i]>=1)variety++;
                float accuracy=(hits+2)/(casts+4);
                float learned=Clamp(.12f+accuracy*.55f+Math.Min(1,strafe)*.16f+Math.Min(1,jumps)*.10f+Math.Max(0,variety-1)*.05f,.2f,1);
                float confidence=Math.Min(1,seconds/10f)*Math.Min(1,casts/8f);
                return .62f+(learned-.62f)*confidence;
            }
        }
        private static float Clamp(float n,float a,float b)=>Math.Max(a,Math.Min(b,n));
        private void Decay(float now)
        {
            float decay=(float)Math.Exp(-Math.Max(0,now-lastEvent)/24f);
            casts*=decay;hits*=decay;for(int i=0;i<4;i++)slots[i]*=decay;lastEvent=now;
        }
        public void Cast(int slot,float now) { if(slot<0||slot>3)return;Decay(now);casts++;slots[slot]++; }
        public void Hit(float now) { Decay(now);hits=Math.Min(casts,hits+1); }
        public void Motion(float now,float lateral,float closing,float movementSpeed,bool grounded,bool sprinting)
        {
            float dt=lastMotion==0 ? .15f : Clamp(now-lastMotion,0,.4f);lastMotion=now;seconds+=dt;
            float w=1-(float)Math.Exp(-dt/4f);
            strafe+=(Clamp(lateral,0,1)-strafe)*w;approach+=(Clamp(closing,-1,1)-approach)*w;
            speed+=(movementSpeed-speed)*w;sprint+=((sprinting ? 1 : 0)-sprint)*w;
            jumps*=1-dt*.3f;if(wasGrounded && !grounded)jumps=Math.Min(1,jumps+.22f);wasGrounded=grounded;
        }
        public float Reaction=>.06f+(1-Skill)*.18f;
        public float AimDamping=>.025f+(1-Skill)*.09f;
        public float AimSpeed=>16+Skill*20;
        public float Alignment=>.94f+Skill*.04f;
    }

    public static class BotCombatDecision
    {
        public static bool OwnAim(bool visible,bool movementOwnsAim)=>visible && !movementOwnsAim;
        public static bool Fire(bool alive,bool hostile,bool visible,bool protectedTarget,bool readyPhase,float seenFor,float reaction,float aimDot,float alignment)
        { return alive && hostile && visible && !protectedTarget && readyPhase && seenFor>=reaction && aimDot>=alignment; }
    }
}
