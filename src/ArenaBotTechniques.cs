using RoR2;
using RoR2.CharacterAI;
using RoR2.Projectile;
using UnityEngine;
using HarmonyLib;

namespace ArenaPvP
{
    internal sealed class ArenaBotTechniques
    {
        private static readonly float[] turretAngles = { 0, 35, -35, 70, -70, 115, -115 };
        private static readonly System.Type ConcussionMine = typeof(EntityStates.Railgunner.Weapon.FireMineConcussive);
        private static readonly AccessTools.FieldRef<ProjectileImpactExplosion, bool> MineImpacted = FuseField<bool>("hasImpact");
        private static readonly AccessTools.FieldRef<ProjectileImpactExplosion, float> ImpactAge = FuseField<float>("stopwatchAfterImpact");
        private static readonly AccessTools.FieldRef<ProjectileImpactExplosion, float> MineAge = FuseField<float>("stopwatch");

        private static AccessTools.FieldRef<ProjectileImpactExplosion, T> FuseField<T>(string name)
        {
            var field = AccessTools.Field(typeof(ProjectileImpactExplosion),name);
            return field?.FieldType == typeof(T) ? AccessTools.FieldRefAccess<ProjectileImpactExplosion,T>(field) : null;
        }
        private EntityStates.EntityState blueprint;
        private int blueprintStock;
        private float blueprintUntil, nextTurret, nextTurretProbe;
        private Vector3 turretHeading, lastMove;

        private GameObject minePrefab;
        private ProjectileModifyExplisionForceToAttacker launch;
        private ProjectileImpactExplosion fuse;
        private ProjectileController mine;
        private ProjectileImpactExplosion activeFuse;
        private float nextMine, mineUntil, mineSearchAt;
        private bool seenMine;
        private Vector3 minePoint, launchHeading, runup;

        internal void Clear()
        {
            blueprint = null;
            blueprintUntil = nextTurret = nextTurretProbe = 0;
            turretHeading = lastMove = Vector3.zero;
            nextMine = mineUntil = mineSearchAt = 0;
            mine = null;
            activeFuse = null;
            seenMine = false;
        }

        // These techniques use the same aim and button inputs as a player.
        // Physical launches and turret construction belong to the game.
        internal bool Apply(CharacterBody body, ref BaseAI.BodyInputs input, BaseAI.BodyInputs previous, CharacterBody enemy,
            Vector3 destination, HillView hill, bool canTravel, bool shootingOpportunity, ArenaBotActions actions, float now)
        {
            if (!body.skillLocator || !body.inputBank || !body.characterMotor || body.currentVehicle) return false;
            if (input.moveVector.sqrMagnitude > .15f) lastMove = input.moveVector.normalized;
            var special = body.skillLocator.special;
            if (special && special.stateMachine && special.stateMachine.state is EntityStates.Engi.EngiWeapon.PlaceTurret active)
                return ConfirmTurret(body, special, active, ref input, previous, enemy, destination, hill, now);
            blueprint = null;

            if (now < mineUntil && GuideRunup(body, ref input, previous, hill, now)) return true;
            if (!body.characterMotor.isGrounded || body.isFlying)
            {
                mineUntil = 0;
                return false;
            }
            if (!canTravel) return false;
            if (special && special.CanExecute() && IsTurret(special) && now >= nextTurret
                && (enemy || hill != null && hill.Ready) && body.master
                && body.master.GetDeployableCount(DeployableSlot.EngiTurret) < body.master.GetDeployableSameSlotLimit(DeployableSlot.EngiTurret))
                return PrepareTurret(body, ref input, previous, enemy, destination, hill, now);

            if (!shootingOpportunity && now >= nextMine)
                return PrepareMine(body, ref input, previous, destination, hill, actions, now);
            return false;
        }

        private static bool IsTurret(GenericSkill skill)
        {
            var type = skill.activationState.stateType;
            return type == typeof(EntityStates.Engi.EngiWeapon.PlaceTurret)
                || type == typeof(EntityStates.Engi.EngiWeapon.PlaceWalkerTurret);
        }

        private bool PrepareTurret(CharacterBody body, ref BaseAI.BodyInputs input, BaseAI.BodyInputs previous,
            CharacterBody enemy, Vector3 destination, HillView hill, float now)
        {
            if (now >= nextTurretProbe) FindTurretHeading(body, enemy, destination, hill, now);
            if (turretHeading.sqrMagnitude < .1f) return false;
            input.desiredAimDirection = turretHeading;
            input.pressSkill4 = !previous.pressSkill4 && Vector3.Dot(Flat(body.inputBank.aimDirection), turretHeading) > .982f;
            if (input.pressSkill4)
            {
                input.pressSkill1 = false;
                nextTurret = now + .8f;
            }
            return true;
        }

        private bool ConfirmTurret(CharacterBody body, GenericSkill skill, EntityStates.EntityState active,
            ref BaseAI.BodyInputs input, BaseAI.BodyInputs previous, CharacterBody enemy, Vector3 destination, HillView hill, float now)
        {
            if (blueprint != active)
            {
                blueprint = active;
                blueprintStock = skill.stock;
                blueprintUntil = now + .75f;
                nextTurretProbe = 0;
            }
            if (input.moveVector.sqrMagnitude < .1f && ArenaBotLocomotion.Safe(body, lastMove, hill))
                input.moveVector = lastMove;
            if (skill.stock < blueprintStock) return true;
            if (now >= nextTurretProbe) FindTurretHeading(body, enemy, destination, hill, now);
            input.pressSkill4 = false;
            input.pressSkill1 = false;
            input.pressSkill2 = false;
            if (turretHeading.sqrMagnitude > .1f)
            {
                input.desiredAimDirection = turretHeading;
                var actual = Flat(body.inputBank.aimDirection);
                if (Vector3.Dot(actual, turretHeading) > .982f && TryPlacement(body, actual, out _))
                {
                    input.pressSkill1 = true;
                    return true;
                }
            }
            if (now > blueprintUntil) input.pressSkill2 = !previous.pressSkill2; // Native blueprint cancel.
            return true;
        }

        private void FindTurretHeading(CharacterBody body, CharacterBody enemy, Vector3 destination, HillView hill, float now)
        {
            nextTurretProbe = now + .22f;
            turretHeading = Vector3.zero;
            Vector3 attackPoint = enemy ? enemy.corePosition : hill != null && hill.Ready ? hill.To + Vector3.up : destination + Vector3.up;
            Vector3 toward = Flat(attackPoint - body.footPosition);
            if (toward.sqrMagnitude < .1f) toward = Flat(body.inputBank.aimDirection);
            float best = float.NegativeInfinity;
            foreach (float angle in turretAngles)
            {
                var heading = Quaternion.Euler(0, angle, 0) * toward;
                if (!TryPlacement(body, heading, out var point)) continue;
                var eye = point + Vector3.up * 1.1f;
                if (Physics.Linecast(eye, attackPoint, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) continue;
                var side = Vector3.Cross(Vector3.up, toward);
                bool backed = Physics.Linecast(eye, eye - toward * 2.8f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
                bool sided = Physics.Linecast(eye, eye + side * 2.6f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)
                    || Physics.Linecast(eye, eye - side * 2.6f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
                float score = (backed ? 5 : 0) + (sided ? 8 : 0) + (point.y - body.footPosition.y) * 2
                    + Vector3.Dot(heading, Flat(body.inputBank.aimDirection));
                if (score <= best) continue;
                best = score;
                turretHeading = heading;
            }
        }

        private static bool TryPlacement(CharacterBody body, Vector3 heading, out Vector3 point)
        {
            point = body.footPosition;
            if (heading.sqrMagnitude < .1f) return false;
            Vector3 origin = body.inputBank.aimOrigin + heading * 2f + Vector3.up;
            if (!Physics.SphereCast(origin, .5f, Vector3.down, out var hit, 4f, LayerIndex.world.mask)
                || hit.normal.y <= .5f) return false;
            point = origin + Vector3.down * (hit.distance + .5f);
            return !Physics.CheckCapsule(point + Vector3.up * 1.32f, point + Vector3.up * .5f, .45f,
                (int)LayerIndex.world.mask | (int)LayerIndex.CommonMasks.characterBodiesOrDefault);
        }

        private bool PrepareMine(CharacterBody body, ref BaseAI.BodyInputs input, BaseAI.BodyInputs previous,
            Vector3 destination, HillView hill, ArenaBotActions actions, float now)
        {
            var utility = body.skillLocator.utility;
            if (!utility || utility.activationState.stateType != ConcussionMine || !actions.NativeUtilityAvailable(body, now)
                || input.pressSkill2 || input.pressSkill3 || input.pressSkill4 || input.pressSkill1) return false;
            var forward = Flat(input.moveVector);
            if (forward.sqrMagnitude < .1f || Vector3.Dot(destination - body.footPosition, forward) < 3f) return false;
            if (!minePrefab)
            {
                minePrefab = ArenaCombatLearning.RegisteredProjectile(ConcussionMine);
                if (minePrefab)
                {
                    launch = minePrefab.GetComponent<ProjectileModifyExplisionForceToAttacker>();
                    fuse = minePrefab.GetComponent<ProjectileImpactExplosion>();
                }
            }
            if (!launch || !fuse || launch.range < 1f) return false;
            var behind = body.footPosition - forward * Mathf.Min(1.7f, launch.range * .3f);
            if (!Physics.Raycast(behind + Vector3.up * .6f, Vector3.down, out var plant, 1.8f, LayerIndex.world.mask,
                QueryTriggerInteraction.Ignore) || plant.normal.y < .55f)
            {
                nextMine = now + .25f;
                return false;
            }

            float runupDistance = Mathf.Min(3.5f, launch.range * .48f);
            Vector3 launchPoint = plant.point + forward * runupDistance;
            if (!Forecast(body, launch, plant.point, forward, launchPoint, hill))
            {
                nextMine = now + .3f;
                return false;
            }
            Vector3 aim = (plant.point - body.inputBank.aimOrigin).normalized;
            input.desiredAimDirection = aim;
            if (Vector3.Dot(aim, body.inputBank.aimDirection) < .995f) return true;
            if (!actions.TravelUtility(ref input, previous, body, now)) return true;

            mine = null;
            seenMine = false;
            minePoint = plant.point;
            launchHeading = forward;
            runup = launchPoint;
            mineSearchAt = now;
            float delay = fuse.timerAfterImpact ? fuse.lifetimeAfterImpact : fuse.lifetime;
            mineUntil = now + Mathf.Clamp(delay + .65f, .45f, 3f);
            nextMine = mineUntil + 2f;
            return true;
        }

        private static bool Forecast(CharacterBody body, ProjectileModifyExplisionForceToAttacker force, Vector3 minePoint,
            Vector3 forward, Vector3 launchPoint, HillView hill)
        {
            var hurtBox = body.mainHurtBox;
            if (!hurtBox || !hurtBox.collider || force.rangeForceCurve == null) return false;
            // Query the native collider from where the mine would be relative
            // to the future launch point, without moving the body.
            var relativeMine = minePoint + body.footPosition - launchPoint;
            var contactRay = new Ray(relativeMine, (hurtBox.transform.position - relativeMine).normalized);
            if (!hurtBox.collider.Raycast(contactRay, out var contact, force.range)) return false;
            var offset = body.transform.position - contact.point;
            var bias = Mathf.Clamp01(force.upwardDirectionInfluence);
            Vector3 direction = (offset.normalized * (1f - bias) + Vector3.up * bias).normalized;
            float mass = PhysForceInfo.GetSimplifiedMassCoefficient(body.characterMotor.mass);
            if (!(mass > 0) || float.IsInfinity(mass)) return false;
            Vector3 velocity = direction * (force.forceCoefficient * force.rangeForceCurve.Evaluate(offset.magnitude) / mass);
            return ArenaBotLocomotion.ClearBoostArc(body, launchPoint, velocity, forward, hill);
        }

        private bool GuideRunup(CharacterBody body, ref BaseAI.BodyInputs input, BaseAI.BodyInputs previous, HillView hill, float now)
        {
            if (!mine && seenMine)
            {
                mineUntil = 0;
                return false;
            }
            if (!seenMine && body.characterMotor.isGrounded && now >= mineSearchAt)
            {
                mineSearchAt = now + .08f;
                var projectiles = InstanceTracker.GetInstancesList<ProjectileController>();
                for (int i = projectiles.Count - 1; i >= Mathf.Max(0, projectiles.Count - 40); i--)
                {
                    var projectile = projectiles[i];
                    if (!projectile || projectile.isPrediction || projectile.owner != body.gameObject
                        || (projectile.transform.position - minePoint).sqrMagnitude > 16
                        || !projectile.GetComponent<ProjectileModifyExplisionForceToAttacker>()) continue;
                    mine = projectile;
                    activeFuse = projectile.GetComponent<ProjectileImpactExplosion>();
                    seenMine = true;
                    runup = mine.transform.position + launchHeading * Mathf.Min(3.5f, launch.range * .48f);
                    if (!Forecast(body, launch, mine.transform.position, launchHeading, runup, hill))
                    {
                        mineUntil = 0;
                        return false;
                    }
                    break;
                }
            }
            // Walk up to the launch window and arc around it as the device
            // arms. Keep ordinary inputs so the native blast can launch us.
            Vector3 offset = runup - body.footPosition;
            offset.y = 0;
            Vector3 toward = offset.sqrMagnitude > 1f ? Flat(offset)
                : Vector3.Cross(Vector3.up, launchHeading).normalized * .7f + launchHeading * .3f;
            if (activeFuse && MineAge != null && offset.sqrMagnitude < 2.5f && body.characterMotor.isGrounded && !previous.pressJump)
            {
                float remaining = activeFuse.lifetime - MineAge(activeFuse);
                if (activeFuse.timerAfterImpact && MineImpacted != null && ImpactAge != null && MineImpacted(activeFuse))
                    remaining = Mathf.Min(remaining, activeFuse.lifetimeAfterImpact - ImpactAge(activeFuse));
                if (remaining >= 0 && remaining <= .13f && Forecast(body,launch,mine.transform.position,launchHeading,body.footPosition,hill))
                {
                    input.moveVector = launchHeading * .42f;
                    input.pressJump = true;
                    return true;
                }
            }
            if (ArenaBotLocomotion.Safe(body, toward.normalized, hill))
            {
                input.moveVector = toward.normalized * (offset.sqrMagnitude > 2 ? .78f : .38f);
                input.pressSprint = false;
                input.pressJump = false;
            }
            return true;
        }

        private static Vector3 Flat(Vector3 value)
        {
            value.y = 0;
            return value.normalized;
        }
    }
}
