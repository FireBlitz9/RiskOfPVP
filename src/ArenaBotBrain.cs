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
        private AISkillDriver[] drivers;
        private GameObject goal;
        private float range, projectileSpeed, nextGroundProbe;
        private Vector3 lastSeen, previousProbeDirection, avoidedMove;
        private float seenAt = -100;
        private bool unsafeMove, hasGoal;
        private int patrol;
        private BotIntelligence tier = BotIntelligence.Medium;
        private readonly ArenaBotActions actions=new ArenaBotActions();
        private HillView movementHill;
        private CharacterBody kitBody, sightTarget;
        private struct RankedTarget { internal BotTarget Target; internal float Priority; }
        private readonly List<RankedTarget> candidates = new List<RankedTarget>();
        private readonly EntityStates.EntityState[] threatStates = new EntityStates.EntityState[4];
        private float acquiredAt, sightUntil, dodgeAt, dodgeUntil;
        private bool clearSight;
        private Vector3 projectileDirection;
        private float projectileAt = -100;
        private BotEngagement engagement;
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
            drivers=gameObject.GetComponents<AISkillDriver>().Where(d=>d!=movement).Concat(new[]{movement}).ToArray();
            participant.BotKit = ArenaBotKit.Read(body, drivers, range);
            actions.Initialize(bodyName,drivers,player.BotOrdinal);
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
        { actions.Dispose();if(ais!=null)foreach(var ai in ais)ArenaBotControl.Unregister(ai,this); if(goal)Destroy(goal); }
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
            if (kitBody != body)
            {
                kitBody = body;
                player.BotMemory.NewLife();
                Enemy = null; enemyPlayer = null; seenAt = -100;
                projectileAt = -100;
                ResetSight(now);
            }
            foreach(var ai in ais)if(ai)
            { ai.fullVision=tier>=BotIntelligence.High; ai.aimVectorDampTime=BotTacticsRules.AimDamping(tier); ai.aimVectorMaxSpeed=BotTacticsRules.AimSpeed(tier); }
            var hill=match.Rules.Mode==ArenaMode.KOTH ? match.Hill.View(match.Rules) : null;
            movementHill=hill;
            bool objective=hill!=null && hill.Ready;
            if (tier == BotIntelligence.Ultra)
            {
                ThinkUltra(match, targets, body, hill, objective, now);
                return;
            }
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
            ApplyEnemy(match);
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
            ApplyGoal(safe);
        }

        private void ApplyEnemy(ArenaMatch match)
        {
            foreach (var ai in ais)
            {
                if (!ai || ai.currentEnemy == null) continue;
                ai.currentEnemy.gameObject = Enemy ? Enemy.gameObject : null;
                if (Enemy)
                {
                    ai.currentEnemy.bestHurtBox = Enemy.mainHurtBox;
                    ai.currentEnemy.hasLoS = true;
                    ai.enemyAttention = Mathf.Max(2.5f, match.Rules.BotThinkSeconds + .3f);
                }
            }
        }

        private void ApplyGoal(Vector3 safe)
        {
            bool moved=!hasGoal || (safe-Destination).sqrMagnitude>4;
            goal.transform.position=safe; hasGoal=true;
            foreach(var ai in ais)if(ai && ai.body)
            { var agent=ai.broadNavigationAgent;if(moved || !agent.goalPosition.HasValue) { agent.goalPosition=safe;agent.InvalidatePath(); } }
        }

        private void ThinkUltra(ArenaMatch match, List<BotTarget> targets, CharacterBody body, HillView hill, bool objective, float now)
        {
            var kit = ArenaBotKit.Read(body, drivers, range);
            player.BotKit = kit;
            candidates.Clear();
            int allies = 0;
            Vector3 origin = body.footPosition;
            var currentEnemy = Enemy;
            bool chase = BotTacticsRules.ChaseHumans(match.Rules.BotChase, match.Rules.Mode);
            foreach (var target in targets)
            {
                if (Candidate(match, body, target)
                    && (target.Body.corePosition - body.corePosition).sqrMagnitude <= 220f * 220f)
                    candidates.Add(new RankedTarget { Target = target, Priority = SearchPriority(target, origin, currentEnemy, chase) });
                else if (match.Rules.Mode != ArenaMode.FFA && target.Body && !target.Summon && target.Player != player
                    && target.Player.Side == player.Side && (target.Body.footPosition - body.footPosition).sqrMagnitude < 28f * 28f) allies++;
            }
            candidates.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            BotTarget selected = default(BotTarget);
            float best = float.NegativeInfinity;
            int nearbyEnemies = 0;
            for (int i = 0; i < Math.Min(8, candidates.Count); i++)
            {
                var target = candidates[i].Target;
                var enemy = target.Body;
                if (target.Player.ProtectionUntil > match.Clock || !Visible(body, enemy)) continue;
                float distance = Vector3.Distance(origin, enemy.footPosition);
                if (distance < 28 && !target.Summon) nearbyEnemies++;
                bool onHill = objective && InHill(hill, enemy.footPosition);
                var known = player.BotMemory.Find(target.Player.UserId);
                float pressure = known == null ? 0 : known.Pressure(now);
                float health = enemy.healthComponent.combinedHealthFraction;
                float score = BotTacticsRules.TargetScore(tier, distance, health, enemy == Enemy, onHill, false, pressure)
                    + (distance <= kit.Reach ? 9 : 0) + (chase && !target.Player.IsBot && !target.Summon ? 30 : 0)
                    + (target.Summon ? -18 : 0) + (enemy == Enemy ? 7 : 0);
                if (score > best) { best = score; selected = target; }
            }
            Enemy = selected.Body;
            enemyPlayer = selected.Player;
            if (Enemy != currentEnemy) ResetSight(now);
            ApplyEnemy(match);
            ObserveProjectiles(match, body, now);
            if (!Enemy)
            {
                // Ultra may know an opponent's location for routing. The native AI
                // remains without an attack target until it has line of sight.
                Vector3 desired = candidates.Count > 0 ? candidates[0].Target.Body.footPosition : SearchGoal(match, body, now);
                if (objective) desired = HillGoal(hill, desired);
                Vector3 safe;
                ApplyGoal(ProjectGround(body, desired, out safe) ? safe : origin);
                return;
            }

            lastSeen = Enemy.footPosition;
            seenAt = now;
            Vector3 separation = origin - lastSeen;
            separation.y = 0;
            Vector3 away = separation.sqrMagnitude > .01f ? separation.normalized : Vector3.forward;
            float enemyHealth = Enemy.healthComponent.combinedHealthFraction;
            BotOpponent profile = player.BotMemory.For(enemyPlayer.UserId);
            if (!selected.Summon)
            {
                var velocity = Enemy.characterMotor ? Enemy.characterMotor.velocity : Vector3.zero;
                velocity.y = 0;
                profile.See(Enemy.GetInstanceID(), now, velocity.magnitude, Vector3.Dot(velocity, away) / 10f,
                    Mathf.Abs(Vector3.Dot(velocity, Vector3.Cross(Vector3.up, away))) / 10f, enemyHealth);
            }
            float pressureNow = profile.Pressure(now);
            engagement = BotAdaptation.Engage(body.healthComponent.combinedHealthFraction, enemyHealth, separation.magnitude,
                kit, profile.Trade(now), pressureNow, nearbyEnemies, allies, objective && InHill(hill, origin), ArenaBotKit.Ready(body));
            float spacing = BotAdaptation.Spacing(kit, engagement, profile.Approach, pressureNow, enemyHealth);
            ApplyGoal(FightGoal(body, Enemy, away, spacing, hill, objective));
        }

        private static float SearchPriority(BotTarget target, Vector3 origin, CharacterBody current, bool chase)
        {
            return (target.Body.footPosition - origin).sqrMagnitude * (target.Body == current ? .25f : 1f)
                * (chase && !target.Player.IsBot && !target.Summon ? .55f : 1f) + (target.Summon ? 250f : 0);
        }

        private Vector3 SearchGoal(ArenaMatch match, CharacterBody body, float now)
        {
            if (now - seenAt < 3 && (body.footPosition - lastSeen).sqrMagnitude > 12) return lastSeen;
            var points = match.SpawnPositions as IList<Vector3>;
            if (points == null || points.Count == 0) return body.footPosition;
            if ((Destination - body.footPosition).sqrMagnitude < 16) patrol++;
            return points[patrol % points.Count];
        }

        private static bool InHill(HillView hill, Vector3 point)
        {
            Vector3 offset = point - hill.To;
            return Mathf.Abs(offset.y) <= 8 && offset.x * offset.x + offset.z * offset.z <= hill.Radius * hill.Radius;
        }

        private static Vector3 HillGoal(HillView hill, Vector3 point)
        {
            Vector3 offset = point - hill.To;
            float x, z;
            BotTacticsRules.ObjectiveOffset(offset.x, offset.z, hill.Radius, out x, out z);
            return hill.To + new Vector3(x, 0, z);
        }

        private Vector3 FightGoal(CharacterBody body, CharacterBody enemy, Vector3 away, float spacing, HillView hill, bool objective)
        {
            Vector3 origin = body.footPosition;
            Vector3 best = origin;
            float bestScore = float.NegativeInfinity;
            var enemyPoint = enemy.mainHurtBox ? enemy.mainHurtBox.transform.position : enemy.corePosition;
            float side = player.BotOrdinal % 2 == 0 ? 1 : -1;
            for (int i = 0; i < 3; i++)
            {
                float angle = i == 0 ? 0 : i == 1 ? 32 * side : -32 * side;
                Vector3 desired = enemy.footPosition + (Quaternion.Euler(0, angle, 0) * away) * spacing;
                if (objective) desired = HillGoal(hill, desired);
                Vector3 safe;
                if (!ProjectGround(body, desired, out safe) || (objective && !InHill(hill, safe))) continue;
                bool covered = Physics.Linecast(safe + (body.corePosition - origin), enemyPoint, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
                float score = -(safe - origin).magnitude * .3f - Mathf.Abs(Vector3.Distance(safe, enemy.footPosition) - spacing) * .45f;
                score += (engagement == BotEngagement.Recover ? covered : !covered) ? 8 : 0;
                if (engagement == BotEngagement.Hold && !objective && i != 0) score += 2;
                if (score > bestScore) { best = safe; bestScore = score; }
            }
            Vector3 center;
            if (float.IsNegativeInfinity(bestScore) && objective && ProjectGround(body, hill.To, out center)) return center;
            return best;
        }
        private bool Candidate(ArenaMatch match,CharacterBody body,BotTarget candidate)
        { return candidate.Body && candidate.Player!=null && candidate.Body.healthComponent && candidate.Body.healthComponent.alive && candidate.Player.Connected
            && match.AreOpponents(player,candidate.Player) && candidate.Body.GetVisibilityLevel(body)==VisibilityLevel.Visible; }
        private static bool Visible(CharacterBody body,CharacterBody enemy)
        { return enemy && !Physics.Linecast(body.corePosition,enemy.mainHurtBox ? enemy.mainHurtBox.transform.position : enemy.corePosition,LayerIndex.world.mask,QueryTriggerInteraction.Ignore); }

        private void ResetSight(float now)
        {
            sightTarget = null; sightUntil = 0; clearSight = false;
            acquiredAt = now; dodgeAt = dodgeUntil = 0;
            Array.Clear(threatStates, 0, threatStates.Length);
        }

        private bool HasClearSight(CharacterBody body, float now)
        {
            if (sightTarget != Enemy || now >= sightUntil)
            {
                sightTarget = Enemy;
                sightUntil = now + .055f;
                clearSight = Visible(body, Enemy);
            }
            return clearSight;
        }

        private void ObserveProjectiles(ArenaMatch match, CharacterBody body, float now)
        {
            var projectiles = InstanceTracker.GetInstancesList<RoR2.Projectile.ProjectileController>();
            var motion = body.characterMotor ? body.characterMotor.velocity : Vector3.zero;
            float hitRadius = body.radius + 1.6f;
            float best = .65f;
            bool incoming = false;
            for (int i = projectiles.Count - 1; i >= Math.Max(0, projectiles.Count - 48); i--)
            {
                var projectile = projectiles[i];
                if (!projectile || !projectile.owner || projectile.isPrediction) continue;
                Vector3 offset = projectile.transform.position - body.corePosition;
                if (offset.sqrMagnitude > 42f * 42f) continue;
                Rigidbody rigidbody;
                if (!projectile.TryGetComponent(out rigidbody)) continue;
                Vector3 velocity = rigidbody.velocity;
                if (velocity.sqrMagnitude < 16f)
                {
                    var simple = projectile.GetComponent<RoR2.Projectile.ProjectileSimple>();
                    if (!simple || simple.desiredForwardSpeed < 4f) continue;
                    velocity = projectile.transform.forward * simple.desiredForwardSpeed;
                }
                velocity -= motion;
                if (velocity.sqrMagnitude < 16f) continue;
                float time = -Vector3.Dot(offset, velocity) / velocity.sqrMagnitude;
                if (time < .07f || time >= best || (offset + velocity * time).sqrMagnitude > hitRadius * hitRadius) continue;
                var owner = projectile.owner.GetComponent<CharacterBody>();
                if (!owner || !match.AreOpponents(player, match.Find(ArenaMatch.PlayerOwner(owner.master)))) continue;
                if (Physics.Linecast(body.corePosition, projectile.transform.position, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) continue;
                best = time;
                projectileDirection = offset;
                incoming = true;
            }
            if (incoming) projectileAt = now;
        }

        private bool AttackThreat(CharacterBody body, bool visible, float now)
        {
            if (!visible || !Enemy || !Enemy.skillLocator || !Enemy.inputBank) return false;
            bool aimedAtBot = Vector3.Dot(Enemy.inputBank.aimDirection, (body.corePosition - Enemy.aimOrigin).normalized) > .88f;
            bool newAttack = false;
            for (int i = 0; i < threatStates.Length; i++)
            {
                var skill = Enemy.skillLocator.GetSkill((SkillSlot)i);
                var state = skill && skill.isCombatSkill && skill.stateMachine && !skill.stateMachine.IsInMainState() ? skill.stateMachine.state : null;
                if (state != null && state != threatStates[i]) newAttack = true;
                threatStates[i] = state;
            }
            if (newAttack && aimedAtBot && now >= dodgeUntil)
            {
                dodgeAt = now + .10f;
                dodgeUntil = dodgeAt + .22f;
            }
            return now >= dodgeAt && now < dodgeUntil;
        }

        private Vector3 DodgeDirection(CharacterBody body, Vector3 threat, int preferredSide, bool sprint)
        {
            threat.y = 0;
            var side = Vector3.Cross(Vector3.up, threat.normalized) * preferredSide;
            if (side.sqrMagnitude < .1f) return Vector3.zero;
            if (KeepInObjective(body, ref side) && SupportedAhead(body, side, sprint)) return side;
            side = Vector3.Cross(Vector3.up, threat.normalized) * -preferredSide;
            return KeepInObjective(body, ref side) && SupportedAhead(body, side, sprint) ? side : Vector3.zero;
        }

        private bool UseUltraEquipment(CharacterBody body, EquipmentDef equipment, bool fire, bool visible,
            bool threatened, bool incoming, float now)
        {
            if (body.equipmentSlot.stock <= 0) return false;
            float health = body.healthComponent.combinedHealthFraction;
            switch (equipment.name)
            {
                case "Fruit": return health < .55f;
                case "HealAndRevive": case "BossHunter": case "MultiShopCard": case "LunarPortalOnUse": return false;
                case "Parry": return threatened;
                case "Cleanse":
                    var dots = DotController.FindDotController(body.gameObject);
                    return incoming || dots && dots.NetworkactiveDotFlags != 0;
                case "GainArmor":
                    return threatened || visible && health < .65f;
                case "GroundEnemies":
                    return visible && (Enemy.corePosition - body.corePosition).sqrMagnitude < 9f * 9f;
                case "CritOnUse": case "DroneShockDamage":
                    return fire && engagement != BotEngagement.Recover
                        && (Enemy.corePosition - body.corePosition).sqrMagnitude <= player.BotKit.Reach * player.BotKit.Reach;
                default:
                    return fire && engagement != BotEngagement.Recover && now - acquiredAt >= .18f;
            }
        }

        private static bool NativeHold(CharacterBody body, AISkillDriver driver, SkillSlot slot, bool previous)
        {
            if (!previous || !driver || driver.skillSlot != slot || driver.buttonPressType != AISkillDriver.ButtonPressType.Hold
                || !body.skillLocator) return false;
            var skill = body.skillLocator.GetSkill(slot);
            return skill && skill.stateMachine && !skill.stateMachine.IsInMainState();
        }

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
            bool ultra = tier == BotIntelligence.Ultra;
            float now = Time.unscaledTime;
            bool hostile=Enemy && enemyPlayer!=null && enemyPlayer.Connected && match.AreOpponents(player,enemyPlayer);
            bool alive=hostile && Enemy.healthComponent && Enemy.healthComponent.alive;
            bool visible=alive && ai.currentEnemy!=null && ai.currentEnemy.gameObject==Enemy.gameObject && ai.currentEnemy.hasLoS && Enemy.GetVisibilityLevel(body)==VisibilityLevel.Visible;
            if (ultra && visible) visible = HasClearSight(body, now);
            var dominant=ai.skillDriverEvaluation.dominantSkillDriver;
            Vector3 aim=Vector3.zero;
            if(visible)
            {
                Vector3 point=Enemy.mainHurtBox ? Enemy.mainHurtBox.transform.position : Enemy.corePosition;
                float speed = ultra ? ArenaBotKit.ProjectileSpeed(body, dominant, projectileSpeed) : projectileSpeed;
                if(tier>=BotIntelligence.ExtraHigh && speed>0 && Enemy.characterMotor && (!dominant || dominant.skillSlot==SkillSlot.Primary || dominant.skillSlot==SkillSlot.Secondary))
                {
                    Vector3 velocity = Vector3.ClampMagnitude(Enemy.characterMotor.velocity, 45);
                    Vector3 offset = point - body.inputBank.aimOrigin;
                    float travel = ultra ? BotAdaptation.LeadTime(speed, offset.sqrMagnitude, velocity.sqrMagnitude, Vector3.Dot(offset, velocity))
                        : Mathf.Min(.5f, Vector3.Distance(body.corePosition, point) / speed) * .65f;
                    Vector3 intercept = point + velocity * travel;
                    if (!ultra || !Physics.Linecast(body.inputBank.aimOrigin, intercept, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                        point = intercept;
                    input.desiredAimDirection=(point-body.inputBank.aimOrigin).normalized;
                }
                aim=(point-body.inputBank.aimOrigin).normalized;
            }
            bool fire=match.WarmupRemaining<=0 && (!ultra || now - acquiredAt >= .10f)
                && BotTacticsRules.CanFire(alive,hostile,visible,enemyPlayer!=null && enemyPlayer.ProtectionUntil>match.Clock,
                visible ? Vector3.Dot(body.inputBank.aimDirection,aim) : -1,tier);
            input.pressSkill1 &= fire || ultra && NativeHold(body, dominant, SkillSlot.Primary, previous.pressSkill1);
            bool offensive=dominant && (dominant.aimType==AISkillDriver.AimType.AtCurrentEnemy || dominant.moveTargetType==AISkillDriver.TargetType.CurrentEnemy);
            if(offensive)
            {
                input.pressSkill2 &= fire || ultra && NativeHold(body, dominant, SkillSlot.Secondary, previous.pressSkill2);
                // Do not cut a native held utility/special short when its target
                // disappears: multi-state/channelled survivor skills own release.
                input.pressSkill3=BotTacticsRules.HeldSkill(input.pressSkill3,previous.pressSkill3,true,fire);
                input.pressSkill4=BotTacticsRules.HeldSkill(input.pressSkill4,previous.pressSkill4,true,fire);
            }
            if(BotTacticsRules.Adaptive(tier) || actions.HasHold)
                actions.Combo(ai,ref input,previous,Enemy,fire && (!ultra || engagement != BotEngagement.Recover),tier);
            bool incoming = ultra && now - projectileAt >= .08f && now - projectileAt < .35f;
            bool attackThreat = ultra && AttackThreat(body, visible, now);
            bool threatened = attackThreat || incoming;
            if(body.equipmentSlot && body.inventory)
            {
                var equipment=EquipmentCatalog.GetEquipmentDef(body.inventory.GetActiveEquipment().equipmentIndex);
                if(equipment && !previous.pressActivateEquipment)
                    input.pressActivateEquipment=ultra ? match.WarmupRemaining <= 0 && UseUltraEquipment(body, equipment, fire, visible, threatened, incoming, now)
                        : equipment.name=="Fruit" ? body.healthComponent.combinedHealthFraction<.5f
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
            bool addedMobility=false,wantsCombatJump=false;
            if(!plannedJump && BotTacticsRules.Adaptive(tier) && CanSteer(ai) && body.characterMotor && !body.isFlying)
            {
                bool protectedSkill=actions.Protected(body,previous);
                var intent=actions.Motion(body,input,previous,Enemy,visible,protectedSkill,player.BotOrdinal,tier,threatened,incoming);
                if(!protectedSkill)
                {
                    if(intent.DodgeSide!=0 && body.characterMotor.isGrounded)
                    {
                        if (ultra)
                        {
                            Vector3 threat = incoming ? projectileDirection : Enemy ? Enemy.corePosition - body.corePosition : body.inputBank.aimDirection;
                            Vector3 direction = DodgeDirection(body, threat, intent.DodgeSide, false);
                            if (direction.sqrMagnitude > .01f)
                            { input.moveVector = direction; previousProbeDirection = direction; unsafeMove = false; addedMobility = true; }
                        }
                        else
                        {
                            var forward=body.inputBank.aimDirection;forward.y=0;forward.Normalize();
                            var direction=(forward*.7f+Vector3.Cross(Vector3.up,forward)*intent.DodgeSide).normalized;
                            if(KeepInObjective(body,ref direction)) { input.moveVector=direction;addedMobility=true; }
                        }
                    }
                    bool sprintBlocked=actions.SprintBlocked(body);
                    if(!sprintBlocked)
                    {
                        bool directionAllowed=(body.bodyFlags&CharacterBody.BodyFlags.SprintAnyDirection)!=0 || SprintDirection(body,input.moveVector);
                        input.pressSprint=intent.Sprint && directionAllowed && input.moveVector.sqrMagnitude>.01f;
                        if(intent.RestoringSprint)
                        { input.pressSkill1=input.pressSkill2=false;if(!previous.pressSkill3)input.pressSkill3=false;if(!previous.pressSkill4)input.pressSkill4=false; }
                    }
                    else input.pressSprint=false;
                    wantsCombatJump=intent.Jump;
                }
            }
            if(!plannedJump && body.characterMotor && body.characterMotor.isGrounded && !body.isFlying && !body.currentVehicle && input.moveVector.sqrMagnitude>.01f)
            {
                var direction=input.moveVector.normalized;
                if(Time.unscaledTime>=nextGroundProbe)
                {
                    nextGroundProbe=Time.unscaledTime+.1f;previousProbeDirection=direction;unsafeMove=!SupportedAhead(body,direction,input.pressSprint);avoidedMove=Vector3.zero;
                    if(unsafeMove)
                    {
                        var left=Quaternion.Euler(0,-65,0)*direction;var right=Quaternion.Euler(0,65,0)*direction;
                        if(SupportedAhead(body,left,input.pressSprint))avoidedMove=left;else if(SupportedAhead(body,right,input.pressSprint))avoidedMove=right;
                    }
                }
                if(addedMobility && Vector3.Dot(direction,previousProbeDirection)<.5f)
                { input.moveVector=Vector3.zero;input.pressSprint=false;input.pressJump=false;wantsCombatJump=false; }
                if(unsafeMove && Vector3.Dot(direction,previousProbeDirection)>.5f) { input.moveVector=avoidedMove;input.pressSprint=false;input.pressJump=false;wantsCombatJump=false; }
            }
            if(wantsCombatJump && !unsafeMove && !previous.pressJump && SafeCombatJump(body,input.moveVector,input.pressSprint))input.pressJump=true;
            return input;
        }
        private static bool SprintDirection(CharacterBody body,Vector3 movement)
        { var aim=body.inputBank.aimDirection;aim.y=0;movement.y=0;return Vector3.Dot(aim.normalized,movement.normalized)>=.5f; }
        private bool KeepInObjective(CharacterBody body,ref Vector3 direction)
        {
            if(movementHill==null || !movementHill.Ready)return true;
            var offset=body.footPosition-movementHill.To;offset.y=0;
            if(offset.magnitude>movementHill.Radius*.8f)return false;
            float speed=body.moveSpeed*(body.isSprinting ? 1 : body.sprintingSpeedMultiplier);
            var projected=offset+direction*speed*.4f;
            if(projected.magnitude>movementHill.Radius*.85f)direction=-offset.normalized;
            return true;
        }
        private bool SafeCombatJump(CharacterBody body,Vector3 movement,bool sprint)
        {
            if(!body.characterMotor || !body.characterMotor.isGrounded || body.currentVehicle || body.jumpPower<=0 || movement.sqrMagnitude<.01f)return false;
            float flight=2*body.jumpPower/Mathf.Max(1,Mathf.Abs(Physics.gravity.y));
            float speed=body.moveSpeed*(sprint && !body.isSprinting ? body.sprintingSpeedMultiplier : 1);
            Vector3 destination=body.footPosition+movement.normalized*speed*Mathf.Clamp(flight,.3f,2f);
            if(movementHill!=null && movementHill.Ready && (destination-movementHill.To).sqrMagnitude>movementHill.Radius*movementHill.Radius*.81f)return false;
            RaycastHit landing;
            return !Physics.Raycast(body.corePosition,Vector3.up,2.5f,LayerIndex.world.mask,QueryTriggerInteraction.Ignore)
                && !Physics.Linecast(body.corePosition,destination+Vector3.up*2,LayerIndex.world.mask,QueryTriggerInteraction.Ignore)
                && Physics.Raycast(destination+Vector3.up*4,Vector3.down,out landing,8,LayerIndex.world.mask,QueryTriggerInteraction.Ignore) && landing.normal.y>.55f;
        }
        private static bool SupportedAhead(CharacterBody body,Vector3 direction,bool sprint)
        {
            float speed=Mathf.Max(body.moveSpeed*(sprint && !body.isSprinting ? body.sprintingSpeedMultiplier : 1),body.characterMotor.velocity.magnitude);
            float distance=BotTacticsRules.StoppingDistance(speed,body.acceleration,body.radius);
            RaycastHit hit;
            return Physics.Raycast(body.footPosition+direction*distance+Vector3.up*1.5f,Vector3.down,out hit,5f,LayerIndex.world.mask,QueryTriggerInteraction.Ignore) && hit.normal.y>.45f;
        }
    }
}
