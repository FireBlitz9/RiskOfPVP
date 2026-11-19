using System;

namespace ArenaPvP
{
    public enum BotAction { None=-1, Primary, Secondary, Utility, Special }
    public struct BotMotionIntent
    {
        public bool Enabled, Sprint, Jump, RestoringSprint;
        public int DodgeSide;
    }
    // Cadence and input edges only. Native motors/skills still provide every
    // movement speed, jump, cooldown and damage effect.
    public sealed class BotMotionState
    {
        private float nextDodge, dodgeUntil, nextJump, nextReset;
        private int side=1, resetPhase, serial;
        private bool resetPending;
        public void Clear() { nextDodge=dodgeUntil=nextJump=nextReset=0;side=1;resetPhase=serial=0;resetPending=false; }
        public void Initialize(float now,int identity)
        { Clear();nextJump=now+.15f+(identity%7)*.08f;nextDodge=now+(identity%5)*.045f; }
        public void AttackInterruptedSprint() { resetPending=true; }
        public BotMotionIntent Update(float now, BotIntelligence tier, int identity, bool moving, bool grounded, bool combat,
            bool threatened, bool protectedSkill, bool safe, bool isSprinting, bool previousSprint, bool previousJump,bool sprintBlocked=false)
        {
            var result=new BotMotionIntent { Enabled=BotTacticsRules.Adaptive(tier) };
            if(!result.Enabled || !moving || protectedSkill || !safe)return result;
            result.Sprint=true;
            if(now>=nextDodge && combat && grounded)
            {
                serial++;side=((serial*17+identity*7)&1)==0 ? -1 : 1;
                dodgeUntil=now+(tier==BotIntelligence.Ultra ? .3f : .38f);
                nextDodge=now+(threatened ? tier==BotIntelligence.Ultra ? .38f : .6f : tier==BotIntelligence.Ultra ? .65f : .95f)+(identity%3)*.04f;
            }
            if(combat && grounded && now<dodgeUntil)result.DodgeSide=side;
            if(combat && grounded && !previousJump && now>=nextJump)
            { result.Jump=true;nextJump=now+(tier==BotIntelligence.Ultra ? 1.05f : 1.5f)+(identity%4)*.07f; }
            if(sprintBlocked) { result.Sprint=false;return result; }
            if(resetPhase==1)
            { result.RestoringSprint=true;resetPhase=0;resetPending=false;nextReset=now+.25f; }
            else if(now>=nextReset && (resetPending || !isSprinting && previousSprint))
            { result.Sprint=false;result.RestoringSprint=true;resetPhase=1; }
            return result;
        }
    }

    public sealed class BotComboStep
    {
        public readonly BotAction Action;
        public readonly float Range, Health;
        public BotComboStep(BotAction action,float range,float health=1) { Action=action;Range=range;Health=health; }
    }
    public static class BotComboRules
    {
        private static readonly BotComboStep[] commando={ S(BotAction.Secondary,80),S(BotAction.Special,65),S(BotAction.Primary,80) };
        private static readonly BotComboStep[] huntress={ S(BotAction.Secondary,60),S(BotAction.Special,50),S(BotAction.Primary,60) };
        private static readonly BotComboStep[] mage={ S(BotAction.Utility,45),S(BotAction.Secondary,70),S(BotAction.Special,22),S(BotAction.Primary,70) };
        private static readonly BotComboStep[] bandit={ S(BotAction.Secondary,9),S(BotAction.Primary,35),S(BotAction.Special,60,.4f) };
        private static readonly BotComboStep[] merc={ S(BotAction.Secondary,8),S(BotAction.Special,12),S(BotAction.Primary,6) };
        private static readonly BotComboStep[] rex={ S(BotAction.Special,55),S(BotAction.Secondary,65),S(BotAction.Primary,65) };
        private static readonly BotComboStep[] acrid={ S(BotAction.Special,80),S(BotAction.Secondary,55),S(BotAction.Primary,7) };
        private static readonly BotComboStep[] burst={ S(BotAction.Secondary,45),S(BotAction.Primary,55),S(BotAction.Special,35) };
        private static readonly BotComboStep[] simple={ S(BotAction.Secondary,45),S(BotAction.Primary,55) };
        private static readonly BotComboStep[] native=new BotComboStep[0];
        private static BotComboStep S(BotAction action,float range,float health=1) { return new BotComboStep(action,range,health); }
        public static BotComboStep[] Plan(string body)
        {
            switch(body)
            {
                case "CommandoBody":return commando; case "HuntressBody":return huntress; case "MageBody":return mage;
                case "Bandit2Body":return bandit; case "MercBody":return merc;case "TreebotBody":return rex;case "CrocoBody":return acrid;
                case "ToolbotBody":case "ChefBody":case "FalseSonBody":return burst;
                case "CaptainBody":case "EngiBody":case "SeekerBody":return simple;
                // Grapple/punch, scope/reload and custom survivor chains are left
                // to their native nextHighPriorityOverride and state handling.
                default:return native;
            }
        }
    }
    public sealed class BotComboState
    {
        private int index, target;
        private BotAction pending=BotAction.None;
        private float deadline, next;
        public void Clear() { index=target=0;pending=BotAction.None;deadline=next=0; }
        public BotAction Pick(BotComboStep[] steps,float now,int enemy,int readyMask,float distance,float health,bool busy)
        {
            if(enemy!=target) { Clear();target=enemy; }
            if(busy || enemy==0 || steps.Length==0 || now<next)return BotAction.None;
            if(pending!=BotAction.None && now<deadline)
            {
                var step=steps[index%steps.Length];
                return (readyMask&(1<<(int)pending))!=0 && distance<=step.Range && health<=step.Health ? pending : BotAction.None;
            }
            pending=BotAction.None;
            for(int tried=0;tried<steps.Length;tried++)
            {
                index%=steps.Length;var step=steps[index];
                if((readyMask&(1<<(int)step.Action))!=0 && distance<=step.Range && health<=step.Health)
                { pending=step.Action;deadline=now+.35f;next=now+.06f;return pending; }
                index++;
            }
            next=now+.1f;return BotAction.None;
        }
        public void Confirm(BotAction action,float now)
        { if(action==pending) { index++;pending=BotAction.None;next=now+.08f; } }
    }
}
