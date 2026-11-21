using System;
using ArenaPvP;

internal static class UltraPressureTests
{
    internal static void Run(Action<bool,string> check)
    {
        var routes=new BotRoutePlanner();
        var a=new BotRouteOption { Node=1,FirstHop=1,Travel=10,Range=20,HasShot=true };
        var b=new BotRouteOption { Node=2,FirstHop=2,Travel=10,Range=20,HasShot=true };
        bool stalled=false;
        for(int i=0;i<6;i++)
        {
            routes.SetGoal((i&1)==0 ? a : b,i*.25f,10);
            stalled|=routes.Movement(i*.25f,(i&1)*.1f,0,true,10);
        }
        check(stalled,"changing goals cannot conceal a body oscillating in the same spot");
        var c=new BotRouteOption { Node=3,FirstHop=3,Travel=10,Range=20,HasShot=true };
        check(routes.Score(c,BotEngagement.Press,20,0,1.3f)>routes.Score(b,BotEngagement.Press,20,0,1.3f),"independent movement evidence penalizes the blocked route");
        routes.Clear();bool falseStall=false;
        for(int i=0;i<8;i++)
        { routes.SetGoal(a,i*.5f,10-i);falseStall|=routes.Movement(i*.5f,i*1.2f,0,true,10-i); }
        check(!falseStall,"ordinary body progress does not trigger recovery");
        routes.Clear();routes.SetGoal(a,0,10);routes.Movement(0,0,0,true,10);
        check(!routes.Movement(5,0,0,false,10),"native immobility or a channel does not count as a path blockage");
        routes.Progress(5,1,true);routes.SetGoal(b,5.1f,10);
        check(routes.Score(c,BotEngagement.Trade,20,0,5.2f)>routes.Score(a,BotEngagement.Trade,20,0,5.2f),"recently reached points do not cause immediate route ping-pong");
        for(int degrees=0;degrees<360;degrees+=15)
        {
            double angle=degrees*Math.PI/180;
            float tx=(float)Math.Cos(angle),tz=(float)Math.Sin(angle);
            foreach(int side in new[]{-1,1})
            {
                float x,z;BotTacticsRules.PressureVector(tx,tz,-tz*side,tx*side,out x,out z);
                check(x*tx+z*tz>.7f && Math.Abs(x*x+z*z-1)<.0001,"a dodge retains pursuit/cover progress without a diagonal speed bonus: "+degrees+"/"+side);
            }
        }
        var memory=new BotMemory();var opponent=memory.For(7);
        for(int i=0;i<16;i++)opponent.Damage(i*.1f,.05f,true);
        check(opponent.SurvivalPressure(1.6f)>.35f,"sustained small hits teach defence without requiring a single large hit");
        opponent.Defeated(1.6f);memory.NewLife();
        check(opponent.SurvivalPressure(2)>.8f && opponent.Pressure(2)==0,"respawn clears immediate fight state while retaining survival lessons");
        check(opponent.SurvivalPressure(300)<.02f,"old defeat evidence decays instead of permanently forcing a defensive build");

        var armor=new BotBuildItem { Pickup=1,Name="BearVoid" };
        var damage=new BotBuildItem { Pickup=2,Name="Behemoth" };
        var context=new BotBuildContext { Ultra=true };
        check(BotBuildRules.ChooseAdaptive(new[]{armor,damage},context)==1,"an unprotected Ultra bot buys an initial mitigation layer instead of a glass cannon");
        context.Inventory["Syringe"]=12;context.SurvivalPressure=1;context.EnemyDamage=2;
        check(BotBuildRules.ChooseAdaptive(new[]{armor,damage},context)==1,"full-health respawn and offensive investment do not erase a survival deficit");
        var shield=new BotBuildContext { Ultra=true,Shield=.99f,SurvivalPressure=1 };
        check(BotBuildRules.ChooseAdaptive(new[]{new BotBuildItem { Pickup=3,Name="Seed" },new BotBuildItem { Pickup=4,Name="MissileVoid" }},shield)==4,"shield-only builds do not buy ineffective health sustain under pressure");
        var offer=new[]{new BotBuildItem { Pickup=0,Name="Bear" },new BotBuildItem { Pickup=1,Name="Syringe" },
            new BotBuildItem { Pickup=2,Name="Medkit" },new BotBuildItem { Pickup=3,Name="Seed" },
            new BotBuildItem { Pickup=4,Name="CritGlasses" },new BotBuildItem { Pickup=5,Name="Missile" }};
        context=new BotBuildContext { Ultra=true,SurvivalPressure=1,EnemyDamage=1.5f };
        int protection=0,offense=0;
        for(int i=0;i<40;i++)
        {
            int pick=BotBuildRules.ChooseAdaptive(offer,context);
            var item=offer[pick];item.Owned++;context.Inventory[item.Name]=item.Owned;
            context.Crit=1+context.Count("CritGlasses")*10;
            if(pick==0 || pick==2 || pick==3)protection++;else offense++;
        }
        check(protection>=10 && offense>=10,"forty real scoring decisions sustain both defence and offensive pressure instead of overcorrecting to one role");
        var learned=new BotBuildContext { Ultra=true };
        var plate=new BotBuildItem { Name="ArmorPlate" };var barrier=new BotBuildItem { Name="BearVoid" };var cleanse=new BotBuildItem { Name="Cleanse",Equipment=true };
        float normalPlate=BotBuildRules.AdaptiveScore(plate,learned),normalBarrier=BotBuildRules.AdaptiveScore(barrier,learned),normalCleanse=BotBuildRules.AdaptiveScore(cleanse,learned);
        learned.EnemySkillProc=8;learned.EnemySkillBurst=.8f;learned.EnemySkillDot=true;
        check(BotBuildRules.AdaptiveScore(plate,learned)>normalPlate,"observed high-proc skills raise repeat-hit mitigation even without an enemy proc-item list");
        check(BotBuildRules.AdaptiveScore(barrier,learned)>normalBarrier,"observed skill burst raises burst protection before the first defeat");
        check(BotBuildRules.AdaptiveScore(cleanse,learned)>normalCleanse,"observed native damage-over-time skills raise cleanse utility");
        Console.WriteLine("INFO: sustained-pressure build simulation: "+protection+" defence / "+offense+" offence rewards.");
    }
}
