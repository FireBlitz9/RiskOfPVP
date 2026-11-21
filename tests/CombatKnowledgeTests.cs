using System;
using ArenaPvP;

internal static class CombatKnowledgeTests
{
    internal static void Run(Action<bool,string> check)
    {
        var skill=new BotSkillKnowledge();skill.Hint("damageCoefficient",4);skill.Hint("procCoefficient",.2f);skill.Hint("maxDistance",80);skill.Hint("baseChargeDuration",1.4f);
        check(skill.KnownDamage && skill.Damage==4 && skill.Proc==.2f && skill.Range==80 && skill.ChargeTime==1.4f,"registered state numbers provide damage, proc, physical range and charge priors");
        skill.Emission(6,.7f,100,50,2,3);
        check(skill.Samples==1 && skill.Damage==6 && Math.Abs(skill.Proc-.7f)<.001f && skill.Range==100 && skill.Speed==50 && skill.Hits==3,"actual attack parameters refine the native prior without casting a synthetic attack");
        skill.Hint("procCoefficient",.1f);skill.Hint("damageCoefficient",1);
        check(skill.Damage==6 && Math.Abs(skill.Proc-.7f)<.001f,"later configuration seeding does not erase measured damage/proc data");
        var direct=new BotSkillKnowledge { Damage=3,Proc=0,Duration=.5f,KnownDamage=true,Range=100 };
        var proc=new BotSkillKnowledge { Damage=3,Proc=1,Duration=.5f,KnownDamage=true,Range=100 };
        check(proc.Value(20,3,20,1)>direct.Value(20,3,20,1),"proc coefficients affect attack choice when proc items are available");
        check(float.IsNegativeInfinity(proc.Value(101,0,20,1)),"an attack is not planned beyond its known physical range");
        var melee=new BotSkillKnowledge();melee.DriverRange(0,3);
        check(melee.Range==3,"melee driver priors do not inherit the generic forty-metre fallback");
        melee.Hint("maxDistance",120);melee.DriverRange(0,30);
        check(melee.Range==120,"a native AI preferred distance does not shrink a known attack range");
        melee.Hint("maxDistance",0);
        check(melee.Range==120,"an unset zero-distance override cannot shrink a weapon into a half-metre range");
        var harmless=new BotSkillKnowledge { Damage=0,KnownDamage=true,Proc=1 };
        check(harmless.Value(10,100,10,1)==0,"zero-damage support does not gain imaginary proc damage");
        proc.FalloffStart=25;proc.FalloffEnd=60;proc.MinimumDamage=.5f;
        check(Math.Abs(proc.Value(60,0,20,1)*2-proc.Value(25,0,20,1))<.001f,"learned bullet falloff reduces ranged damage valuation at the native minimum");
        var instant=new BotSkillKnowledge();instant.StateName("FireSnipe");
        check(!instant.Charge,"instant sniper shots are not mistaken for held charge states");
        var novice=new BotPlayerStyle();var expert=new BotPlayerStyle();
        for(int i=0;i<150;i++)
        {
            float now=i*.2f;
            novice.Motion(now,0,0,3,true,false);expert.Motion(now,.9f,.5f,14,i%8<5,true);
            if(i%4==0)novice.Cast(0,now);
            expert.Cast(i%4,now);expert.Hit(now);
        }
        check(expert.Skill>novice.Skill+.2f && expert.Reaction<novice.Reaction && expert.AimSpeed>novice.AimSpeed,"measured attack/movement proficiency changes aim and reaction without withholding primary fire");
        float before=expert.Skill;expert.Motion(31,0,0,0,true,false);
        check(expert.Skill>before-.05f,"briefly standing still does not instantly erase learned proficiency");
        var feedback=new BotLocomotionFeedback();bool stuck=false;
        for(int i=0;i<15;i++)stuck|=feedback.Sample(i*.1f,0,(i%2)*1.5f,10,0,true,true);
        check(stuck,"large sideways oscillation is detected when it makes no progress along the route");
        feedback.Clear();stuck=false;
        for(int i=0;i<15;i++)stuck|=feedback.Sample(i*.1f,i*.3f,0,10,0,true,true);
        check(!stuck,"normal route progress does not trigger obstacle recovery");
        check(!feedback.Sample(10,0,0,10,0,true,false),"airborne/native traversal pauses displacement-based recovery");
    }
}
