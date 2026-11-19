using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using RoR2.CharacterAI;
using UnityEngine;

namespace ArenaPvP
{
    internal struct BotTarget
    {
        internal ArenaPlayer Player;
        internal CharacterBody Body;
        internal bool Summon;
    }
    internal sealed class ArenaBotBrain : MonoBehaviour
    {
        internal float NextThink;
        internal float NextChoice;
        internal uint ChoiceArrival;
        internal CharacterBody Enemy { get; private set; }
        private ArenaPlayer player;
        private ArenaPlayer enemyPlayer;
        private BaseAI[] ais;
        private GameObject goal;
        private float range, projectileSpeed, nextGroundProbe;
        private Vector3 lastSeen, previousProbeDirection, avoidedMove;
        private float seenAt = -100;
        private bool unsafeMove, hasGoal;
        private int patrol;
        private BotIntelligence tier = BotIntelligence.Medium;
        internal ArenaPlayer Participant { get { return player; } }
        internal Vector3 Destination { get { return goal ? goal.transform.position : transform.position; } }

        internal void Initialize(ArenaPlayer participant, string bodyName)
        {
            player=participant;
            range=bodyName=="MercBody" || bodyName=="LoaderBody" || bodyName=="FalseSonBody" ? 5 : bodyName=="CrocoBody" ? 10 : 24;
            projectileSpeed=bodyName=="MageBody" ? 60 : bodyName=="SeekerBody" ? 70 : bodyName=="TreebotBody" || bodyName=="CrocoBody" ? 50 : 0;
            goal=new GameObject("Arena bot navigation goal");
            var body=player.Master.GetBody(); if(body)goal.transform.position=body.footPosition;
            var movement=gameObject.AddComponent<AISkillDriver>();
            movement.customName="Arena travel (no attack)"; movement.skillSlot=SkillSlot.None; movement.requireSkillReady=false;
            movement.moveTargetType=AISkillDriver.TargetType.Custom; movement.aimType=AISkillDriver.AimType.MoveDirection;
            movement.movementType=AISkillDriver.MovementType.ChaseMoveTarget; movement.buttonPressType=AISkillDriver.ButtonPressType.Abstain;
            movement.driverUpdateTimerOverride=.2f;
            // Native combat drivers keep their exact priority, required skills and
            // press/release semantics. Travel is only a non-firing last resort.
            var drivers=gameObject.GetComponents<AISkillDriver>().Where(d=>d!=movement).Concat(new[]{movement}).ToArray();
            ais=gameObject.GetComponents<BaseAI>();
            foreach(var ai in ais)
            {
                ai.neverRetaliateFriendlies=true; ai.xrayVision=false; ai.ReplaceSkillDrivers(drivers);
                ai.SetCustomTargetGameObject(goal); ArenaBotControl.Register(ai,this);
            }
            NextThink=Time.unscaledTime+(player.BotOrdinal%8)*.06f;
            nextGroundProbe=Time.unscaledTime+(player.BotOrdinal%5)*.02f; patrol=player.BotOrdinal*17;
        }
        private void OnDestroy()
        { if(ais!=null)foreach(var ai in ais)ArenaBotControl.Unregister(ai,this); if(goal)Destroy(goal); }
        internal bool CanSteer(BaseAI ai)
        {
            if(!hasGoal || !goal || player==null || !player.Connected || player.Dead || !ai.body || ai.body.currentVehicle) return false;
            var driver=ai.skillDriverEvaluation.dominantSkillDriver;
            return !driver || driver.skillSlot!=SkillSlot.Utility && driver.skillSlot!=SkillSlot.Special;
        }
        internal void Think(ArenaMatch match,List<BotTarget> targets,float now)
        {
            NextThink=now+Mathf.Clamp(match.Rules.BotThinkSeconds,.1f,2f);
            if(!player.Connected || player.Dead || !player.Master || !goal) { hasGoal=false; Enemy=null; return; }
            var body=player.Master.GetBody(); if(!body || !body.healthComponent || !body.healthComponent.alive)return;
            tier=match.Rules.BotIntelligence;
            foreach(var ai in ais)if(ai)
            { ai.fullVision=tier>=BotIntelligence.High; ai.aimVectorDampTime=BotTacticsRules.AimDamping(tier); ai.aimVectorMaxSpeed=BotTacticsRules.AimSpeed(tier); }
            var hill=match.Rules.Mode==ArenaMode.KOTH ? match.Hill.View(match.Rules) : null;
            bool objective=hill!=null && hill.Ready;
            bool chase=BotTacticsRules.ChaseHumans(match.Rules.BotChase,match.Rules.Mode);
            bool human=false;
            if(chase)foreach(var target in targets)
                if(!target.Summon && !target.Player.IsBot && Candidate(match,body,target)) { human=true; break; }
            BotTarget first=default(BotTarget), second=first, third=first;
            float a=float.NegativeInfinity,b=a,c=a;
            foreach(var candidate in targets)
            {
                if(!Candidate(match,body,candidate) || candidate.Summon && tier<BotIntelligence.High || human && (candidate.Player.IsBot || candidate.Summon))continue;
                var enemy=candidate.Body; float distance=Vector3.Distance(body.corePosition,enemy.corePosition);
                if(distance>BotTacticsRules.SightRange(tier))continue;
                bool onHill=objective && (enemy.footPosition-hill.To).sqrMagnitude<hill.Radius*hill.Radius;
                float threat=enemy.damage*Mathf.Max(.5f,enemy.attackSpeed)/Mathf.Max(1,body.damage);
                float score=BotTacticsRules.TargetScore(tier,distance,enemy.healthComponent.combinedHealthFraction,enemy==Enemy,onHill,candidate.Player.ProtectionUntil>match.Clock,threat);
                if(score>a) { third=second;c=b;second=first;b=a;first=candidate;a=score; }
                else if(score>b) { third=second;c=b;second=candidate;b=score; }
                else if(score>c) { third=candidate;c=score; }
            }
            var selected=Visible(body,first.Body) ? first : Visible(body,second.Body) ? second : Visible(body,third.Body) ? third : default(BotTarget);
            Enemy=selected.Body; enemyPlayer=selected.Player;
            if(Enemy) { lastSeen=Enemy.footPosition;seenAt=now; }
            foreach(var ai in ais)
            {
                if(!ai || ai.currentEnemy==null)continue;
                ai.currentEnemy.gameObject=Enemy ? Enemy.gameObject : null;
                if(Enemy) { ai.currentEnemy.bestHurtBox=Enemy.mainHurtBox;ai.currentEnemy.hasLoS=true;ai.enemyAttention=Mathf.Max(2.5f,match.Rules.BotThinkSeconds+.3f); }
            }
            Vector3 desired=body.footPosition;
            if(Enemy)
            {
                Vector3 away=body.footPosition-Enemy.footPosition;away.y=0;
                away=away.sqrMagnitude<.01f ? Vector3.forward : away.normalized;
                float spacing=body.healthComponent.combinedHealthFraction<.3f && tier>=BotIntelligence.Medium ? Mathf.Max(32,range) : range;
                Vector3 lateral=tier==BotIntelligence.Low ? Vector3.zero : Vector3.Cross(Vector3.up,away)*Mathf.Sin(now*.65f+player.BotOrdinal*2.4f)*(range<12 ? 1.5f : 7f);
                desired=Enemy.footPosition+away*spacing+lateral;
            }
            else if(now-seenAt<3)desired=lastSeen;
            else
            {
                float closest=float.PositiveInfinity;
                foreach(var candidate in targets)
                {
                    if(!Candidate(match,body,candidate) || human && (candidate.Player.IsBot || candidate.Summon) || candidate.Summon && tier<BotIntelligence.High)continue;
                    float distance=(candidate.Body.footPosition-body.footPosition).sqrMagnitude;
                    if(distance<closest) { closest=distance;desired=candidate.Body.footPosition; }
                }
                if(float.IsPositiveInfinity(closest))
                {
                    var points=match.SpawnPositions as IList<Vector3>;
                    if(points!=null && points.Count>0)
                    { if((Destination-body.footPosition).sqrMagnitude<16)patrol++; desired=points[patrol%points.Count]; }
                }
            }
            // Objective priority is independent of intelligence and health. Stay
            // within the capture area instead of kiting or retreating out of it.
            if(objective)
            {
                var radial=desired-hill.To;radial.y=0;
                float x,z;BotTacticsRules.ObjectiveOffset(radial.x,radial.z,hill.Radius,out x,out z);
                desired=hill.To+new Vector3(x,0,z);
            }
            Vector3 safe;
            if(!ProjectGround(body,desired,out safe))safe=body.footPosition;
            if(objective && (safe-hill.To).sqrMagnitude>hill.Radius*hill.Radius)ProjectGround(body,hill.To,out safe);
            bool moved=!hasGoal || (safe-Destination).sqrMagnitude>4;
            goal.transform.position=safe; hasGoal=true;
            foreach(var ai in ais)if(ai && ai.body)
            { var agent=ai.broadNavigationAgent;if(moved || !agent.goalPosition.HasValue) { agent.goalPosition=safe;agent.InvalidatePath(); } }
        }
        private bool Candidate(ArenaMatch match,CharacterBody body,BotTarget candidate)
        { return candidate.Body && candidate.Player!=null && candidate.Body.healthComponent && candidate.Body.healthComponent.alive && candidate.Player.Connected
            && match.AreOpponents(player,candidate.Player) && candidate.Body.GetVisibilityLevel(body)==VisibilityLevel.Visible; }
        private static bool Visible(CharacterBody body,CharacterBody enemy)
        { return enemy && !Physics.Linecast(body.corePosition,enemy.mainHurtBox ? enemy.mainHurtBox.transform.position : enemy.corePosition,LayerIndex.world.mask,QueryTriggerInteraction.Ignore); }
        private static bool ProjectGround(CharacterBody body,Vector3 desired,out Vector3 safe)
        {
            RaycastHit hit;
            if(Physics.Raycast(desired+Vector3.up*8,Vector3.down,out hit,24,LayerIndex.world.mask,QueryTriggerInteraction.Ignore) && hit.normal.y>.55f)
            { safe=hit.point;return true; }
            var graph=SceneInfo.instance ? SceneInfo.instance.groundNodes : null;
            if(graph!=null && graph.GetNodePosition(graph.FindClosestNode(desired,body.hullClassification,60),out safe))return true;
            safe=body.footPosition;return false;
        }
        internal BaseAI.BodyInputs FilterInputs(BaseAI ai,BaseAI.BodyInputs input,BaseAI.BodyInputs previous)
        {
            var body=ai.body; var match=ArenaMatch.Instance;
            if(!body || !body.inputBank || !body.healthComponent || !match || match.Rules==null || player==null || player.Dead)return input;
            if(match.Finished)
            {
                input.pressSkill1=input.pressSkill2=input.pressActivateEquipment=false;input.moveVector=Vector3.zero;input.pressSprint=false;
                input.pressSkill3 &= previous.pressSkill3;input.pressSkill4 &= previous.pressSkill4;return input;
            }
            bool hostile=Enemy && enemyPlayer!=null && enemyPlayer.Connected && match.AreOpponents(player,enemyPlayer);
            bool alive=hostile && Enemy.healthComponent && Enemy.healthComponent.alive;
            bool visible=alive && ai.currentEnemy!=null && ai.currentEnemy.gameObject==Enemy.gameObject && ai.currentEnemy.hasLoS && Enemy.GetVisibilityLevel(body)==VisibilityLevel.Visible;
            Vector3 aim=Vector3.zero;
            if(visible)
            {
                Vector3 point=Enemy.mainHurtBox ? Enemy.mainHurtBox.transform.position : Enemy.corePosition;
                var driver=ai.skillDriverEvaluation.dominantSkillDriver;
                if(tier>=BotIntelligence.ExtraHigh && projectileSpeed>0 && Enemy.characterMotor && (!driver || driver.skillSlot==SkillSlot.Primary || driver.skillSlot==SkillSlot.Secondary))
                {
                    float travel=Mathf.Min(.5f,Vector3.Distance(body.corePosition,point)/projectileSpeed)*(tier==BotIntelligence.Ultra ? 1 : .65f);
                    point+=Vector3.ClampMagnitude(Enemy.characterMotor.velocity,45)*travel;
                    input.desiredAimDirection=(point-body.inputBank.aimOrigin).normalized;
                }
                aim=(point-body.inputBank.aimOrigin).normalized;
            }
            bool fire=match.WarmupRemaining<=0 && BotTacticsRules.CanFire(alive,hostile,visible,enemyPlayer!=null && enemyPlayer.ProtectionUntil>match.Clock,
                visible ? Vector3.Dot(body.inputBank.aimDirection,aim) : -1,tier);
            input.pressSkill1 &= fire;
            var dominant=ai.skillDriverEvaluation.dominantSkillDriver;
            bool offensive=dominant && (dominant.aimType==AISkillDriver.AimType.AtCurrentEnemy || dominant.moveTargetType==AISkillDriver.TargetType.CurrentEnemy);
            if(offensive)
            {
                input.pressSkill2 &= fire;
                // Do not cut a native held utility/special short when its target
                // disappears: multi-state/channelled survivor skills own release.
                input.pressSkill3=BotTacticsRules.HeldSkill(input.pressSkill3,previous.pressSkill3,true,fire);
                input.pressSkill4=BotTacticsRules.HeldSkill(input.pressSkill4,previous.pressSkill4,true,fire);
            }
            if(body.equipmentSlot && body.inventory)
            {
                var equipment=EquipmentCatalog.GetEquipmentDef(body.inventory.GetActiveEquipment().equipmentIndex);
                if(equipment && !previous.pressActivateEquipment)
                    input.pressActivateEquipment=equipment.name=="Fruit" ? body.healthComponent.combinedHealthFraction<.5f
                        : equipment.name=="GainArmor" ? body.healthComponent.combinedHealthFraction<.65f && alive : fire;
            }
            bool plannedJump=false;
            if(body.characterMotor && body.characterMotor.isGrounded && input.pressJump)
            {
                var path=ai.broadNavigationAgent.output;RaycastHit landing;
                plannedJump=path.desiredJumpVelocity>0 && path.nextPosition.HasValue
                    && Physics.Raycast(path.nextPosition.Value+Vector3.up*2,Vector3.down,out landing,6,LayerIndex.world.mask,QueryTriggerInteraction.Ignore) && landing.normal.y>.45f;
            }
            if(!plannedJump && CanSteer(ai) && body.characterMotor && body.characterMotor.isGrounded)
            {
                // Native "stand and shoot" drivers can scale movement to zero.
                // Use the already-computed safe path vector so an enemy cannot
                // pin an objective bot outside the zone merely by being visible.
                input.moveVector=(Destination-body.footPosition).sqrMagnitude>2.25f ? ai.localNavigator.moveVector : Vector3.zero;
                if(!Enemy && input.moveVector.sqrMagnitude>.01f)input.pressSprint=true;
            }
            if(!plannedJump && body.characterMotor && body.characterMotor.isGrounded && !body.isFlying && !body.currentVehicle && input.moveVector.sqrMagnitude>.01f)
            {
                var direction=input.moveVector.normalized;
                if(Time.unscaledTime>=nextGroundProbe)
                {
                    nextGroundProbe=Time.unscaledTime+.1f;previousProbeDirection=direction;unsafeMove=!SupportedAhead(body,direction);avoidedMove=Vector3.zero;
                    if(unsafeMove)
                    {
                        var left=Quaternion.Euler(0,-65,0)*direction;var right=Quaternion.Euler(0,65,0)*direction;
                        if(SupportedAhead(body,left))avoidedMove=left;else if(SupportedAhead(body,right))avoidedMove=right;
                    }
                }
                if(unsafeMove && Vector3.Dot(direction,previousProbeDirection)>.5f) { input.moveVector=avoidedMove;input.pressSprint=false;input.pressJump=false; }
            }
            return input;
        }
        private static bool SupportedAhead(CharacterBody body,Vector3 direction)
        {
            float speed=Mathf.Max(body.moveSpeed,body.characterMotor.velocity.magnitude);
            float distance=BotTacticsRules.StoppingDistance(speed,body.acceleration,body.radius);
            RaycastHit hit;
            return Physics.Raycast(body.footPosition+direction*distance+Vector3.up*1.5f,Vector3.down,out hit,5f,LayerIndex.world.mask,QueryTriggerInteraction.Ignore) && hit.normal.y>.45f;
        }
    }
}
