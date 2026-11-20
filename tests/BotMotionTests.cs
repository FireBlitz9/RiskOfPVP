using System;
using ArenaPvP;

internal static class BotMotionTests
{
    private static BotMotionIntent Tick(BotMotionState state,float time,BotIntelligence tier=BotIntelligence.Ultra,bool grounded=true,
        bool previousJump=false,bool protect=false,bool safe=true,bool blocked=false,bool moving=true,bool combat=true,bool sprint=true,bool previousSprint=true)
    { return state.Update(time,tier,0,moving,grounded,combat,true,protect,safe,sprint,previousSprint,previousJump,blocked); }
    internal static void Run(Action<bool,string> check)
    {
        foreach(BotIntelligence tier in Enum.GetValues(typeof(BotIntelligence)))
        {
            var motion=Tick(new BotMotionState(),1,tier);
            check(motion.Enabled==(tier>=BotIntelligence.ExtraHigh),tier+" has the expected movement policy");
            check(motion.Sprint==(tier>=BotIntelligence.ExtraHigh),tier+" has the expected sprint request");
        }
        var state=new BotMotionState();state.Initialize(0,0);
        check(!Tick(state,.1f).Jump && Tick(state,.2f).Jump,"jump cadence starts staggered rather than on every initial spawn input");
        check(!Tick(state,.22f).Jump,"jump requests are rate limited");
        check(!Tick(state,3,grounded:false).Jump,"no extra combat jump is requested in the air");
        check(!Tick(state,3,previousJump:true).Jump,"a held jump is not mistaken for a fresh press");
        check(Tick(state,3.1f).Jump,"a released grounded input may jump after its cadence elapses");
        check(!Tick(state,5,protect:true).Sprint && !Tick(state,5,protect:true).Jump,"protected skills suppress injected mobility");
        check(!Tick(state,5,safe:false).Sprint && !Tick(state,5,safe:false).Jump,"unsafe movement suppresses injected mobility");
        check(!Tick(state,5,moving:false).Sprint,"an idle objective holder does not request an idle sprint");
        check(Tick(state,5,combat:false).Sprint && !Tick(state,5,combat:false).Jump,"travel resumes sprint without unsolicited combat jumps");
        state.Clear();
        int first=Tick(state,0).DodgeSide,second=Tick(state,1).DodgeSide;
        check(first!=0 && second==-first,"evasion varies direction on successive bounded cadences");
        state.AttackInterruptedSprint();
        check(!Tick(state,1.1f,blocked:true).RestoringSprint,"sprint reset waits for native attack recovery");
        var release=Tick(state,1.2f);var press=Tick(state,1.22f,previousSprint:false);
        check(!release.Sprint && release.RestoringSprint && press.Sprint && press.RestoringSprint,"sprint resets produce distinct release and re-press inputs");
        check(Tick(state,1.24f).Sprint && !Tick(state,1.24f).RestoringSprint,"normal sprint resumes after the two reset inputs");
        state.Clear();
        check(!Tick(state,2,sprint:false).Sprint && Tick(state,2.02f,previousSprint:false).Sprint,"unexpected sprint interruption also produces a legal reset");

        state.Clear();
        var calm = state.Update(1, BotIntelligence.Ultra, 0, true, true, true, false, false, true, false, false, false, reactive: true);
        check(calm.Sprint && calm.DodgeSide == 0 && !calm.Jump, "Ultra travels normally while no attack threatens it");
        var windup = state.Update(1.1f, BotIntelligence.Ultra, 0, true, true, true, true, false, true, false, false, false, reactive: true);
        check(windup.DodgeSide != 0 && !windup.Jump, "a windup prompts a dodge without demanding a jump");
        var projectile = state.Update(2, BotIntelligence.Ultra, 0, true, true, true, true, false, true, false, false, false,
            reactive: true, projectile: true);
        check(projectile.DodgeSide != 0 && projectile.Jump, "an incoming projectile can request evasive movement and a validated jump");

        var memory = new BotMemory();
        var opponent = memory.For(12);
        opponent.See(1, 4, 11, .8f, .7f, .5f);
        opponent.See(1, 4.25f, 11, .9f, .7f, .7f);
        opponent.Damage(4.26f, .5f, true);
        opponent.Damage(4.27f, .12f, false);
        var kit = new BotKit { Range = 25, Reach = 45, OnHit = .8f, SkillUse = .5f };
        check(memory.Find(13) == null && opponent.Sustain > 0 && opponent.Pressure(4.3f) > .5f
            && opponent.Trade(4.3f) < -.28f, "the bot learns healing, pressure and trade results per opponent");
        check(BotAdaptation.Engage(.5f, .8f, 25, kit, opponent.Trade(4.3f), opponent.Pressure(4.3f), 1, 0, false, false)
            == BotEngagement.Recover, "a losing trade makes an injured bot reset the fight");
        check(BotAdaptation.Engage(.5f, .8f, 25, kit, opponent.Trade(4.3f), opponent.Pressure(4.3f), 1, 0, true, false)
            == BotEngagement.Hold, "objective holders retain the hill through a losing trade");
        check(BotAdaptation.Spacing(kit, BotEngagement.Trade, opponent.Approach, opponent.Pressure(4.3f), 1) > kit.Range,
            "the same ranged kit keeps more space against a player who rushes it");
        memory.NewLife();
        check(opponent.Pressure(5) == 0 && opponent.Familiarity(5) > 0, "respawns clear the active fight while retaining learned tendencies");
        for (int i = 0; i < 20; i++) memory.SkillHit(6 + i * .1f, false, .08f, .25f);
        var learned = memory.RefineKit(kit, 8);
        check(learned.OnHit < kit.OnHit && learned.SkillUse > kit.SkillUse && memory.RefineKit(kit, 100).OnHit == kit.OnHit,
            "recent damage sources improve build estimates and stale samples lose influence");
        check(Math.Abs(BotAdaptation.LeadTime(60, 900, 0, 0) - .5f) < .0001f
            && BotAdaptation.LeadTime(60, 900, 100, 0) > .5f
            && BotAdaptation.LeadTime(30, 900, 1600, 1200) == 0, "prediction handles stationary, lateral and unreachable targets");

        var plan=BotComboRules.Plan("CommandoBody");var sequence=new BotComboState();
        check(sequence.Pick(plan,0,1,15,10,1,false)==BotAction.Secondary,"combo begins at its survivor-specific setup");
        check(sequence.Pick(plan,.1f,1,15,10,1,false)==BotAction.Secondary,"requests without activation retain the same step");
        sequence.Confirm(BotAction.Primary,.1f);
        check(sequence.Pick(plan,.2f,1,15,10,1,false)==BotAction.Secondary,"an unrelated activation does not acknowledge the request");
        sequence.Confirm(BotAction.Secondary,.2f);
        check(sequence.Pick(plan,.3f,1,15,10,1,false)==BotAction.Special,"confirmed activation advances to burst");
        check(sequence.Pick(plan,.4f,2,15,10,1,false)==BotAction.Secondary,"switching targets resets the setup sequence");
        check(sequence.Pick(plan,.5f,2,15,10,1,true)==BotAction.None,"native busy state prevents a new combo step");
        sequence.Clear();
        check(sequence.Pick(plan,0,1,1<<0,10,1,false)==BotAction.Primary,"unavailable skill slots fall back to legal primary");
        check(sequence.Pick(plan,.1f,1,1<<0,100,1,false)==BotAction.None,"pending requests revalidate range");
        check(sequence.Pick(plan,.2f,1,0,10,1,false)==BotAction.None,"pending requests revalidate readiness");
        sequence.Clear();var bandit=BotComboRules.Plan("Bandit2Body");
        check(sequence.Pick(bandit,0,1,1<<3,20,1,false)==BotAction.None,"Bandit's execute is held for a wounded enemy");
        check(sequence.Pick(bandit,.2f,1,1<<3,20,.3f,false)==BotAction.Special,"Bandit's execute is used within its finishing threshold");
        check(BotComboRules.Plan("LoaderBody").Length==0 && BotComboRules.Plan("RailgunnerBody").Length==0
            && BotComboRules.Plan("ModdedBody").Length==0,"traversal, scoped and unknown custom chains keep their native AI sequencing");
    }
}
