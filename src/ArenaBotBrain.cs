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
    }

    // Host-only tactics; native BaseAI still owns movement, aiming and skill use.
    // No per-frame scene searches or projectile scans. At most three sight checks
    // and one ground/clearance probe per scheduled decision.
    internal sealed class ArenaBotBrain : MonoBehaviour
    {
        internal float NextThink;
        private ArenaPlayer player;
        private BaseAI[] ais;
        private AISkillDriver movement;
        private GameObject goal;
        private float range;
        private CharacterBody previousEnemy;
        private Vector3 lastSeen;
        private float seenAt = -100;

        internal void Initialize(ArenaPlayer participant, string bodyName)
        {
            player = participant;
            range = bodyName == "MercBody" || bodyName == "LoaderBody" || bodyName == "FalseSonBody" ? 5 : bodyName == "CrocoBody" ? 10 : 24;
            goal = new GameObject("Arena bot navigation goal");
            var initialBody = player.Master.GetBody();
            if (initialBody) goal.transform.position = initialBody.footPosition;
            movement = gameObject.AddComponent<AISkillDriver>();
            movement.customName = "Arena tactical movement";
            movement.skillSlot = SkillSlot.Primary;
            movement.requireSkillReady = false;
            movement.moveTargetType = AISkillDriver.TargetType.Custom;
            movement.aimType = AISkillDriver.AimType.AtCurrentEnemy;
            movement.movementType = AISkillDriver.MovementType.ChaseMoveTarget;
            movement.buttonPressType = AISkillDriver.ButtonPressType.Hold;
            movement.activationRequiresAimTargetLoS = true;
            movement.buttonEnsureReleasedForOneSample = true;
            movement.driverUpdateTimerOverride = 0.25f;
            var native = gameObject.GetComponents<AISkillDriver>().Where(d => d != movement).ToArray();
            // Preserve native high-priority skill handling, including multi-state
            // utilities. The movement fallback must not monopolize skill slots.
            var drivers = native.Where(d => d.skillSlot != SkillSlot.Primary && d.skillSlot != SkillSlot.None)
                .Concat(new[] { movement }).Concat(native).Distinct().ToArray();
            ais = gameObject.GetComponents<BaseAI>();
            foreach (var ai in ais)
            {
                ai.neverRetaliateFriendlies = true;
                ai.aimVectorDampTime = 0.18f; ai.aimVectorMaxSpeed = 10f;
                ai.ReplaceSkillDrivers(drivers); ai.SetCustomTargetGameObject(goal);
            }
            NextThink = Time.unscaledTime + (player.BotOrdinal % 8) * 0.06f;
        }

        private void OnDestroy() { if (goal) Destroy(goal); }

        internal void Think(ArenaMatch match, List<BotTarget> targets, float now)
        {
            NextThink = now + Mathf.Clamp(match.Rules.BotThinkSeconds, 0.2f, 2f);
            if (!player.Connected || player.Dead || !player.Master || !goal) return;
            var body = player.Master.GetBody();
            if (!body || !body.healthComponent || !body.healthComponent.alive) return;
            var hill = match.Rules.Mode == ArenaMode.KOTH ? match.Hill.View(match.Rules) : null;
            bool objective = hill != null && hill.Ready;
            Vector3 hillPoint = objective ? hill.To : body.footPosition;

            // Rank cheaply first, then check only the best three visible targets.
            CharacterBody first = null, second = null, third = null;
            float a = float.NegativeInfinity, b = a, c = a;
            foreach (var candidate in targets)
            {
                var enemy = candidate.Body;
                if (!enemy || !enemy.healthComponent || !enemy.healthComponent.alive || !match.AreOpponents(player, candidate.Player)
                    || enemy.GetVisibilityLevel(body) != VisibilityLevel.Visible) continue;
                float distance = Vector3.Distance(body.corePosition, enemy.corePosition);
                if (distance > 140) continue;
                bool onHill = objective && (enemy.footPosition - hillPoint).sqrMagnitude < hill.Radius * hill.Radius;
                float score = BotBuildRules.TargetScore(distance, enemy.healthComponent.combinedHealthFraction,
                    enemy == previousEnemy, onHill, candidate.Player.ProtectionUntil > match.Clock);
                if (score > a) { third = second; c = b; second = first; b = a; first = enemy; a = score; }
                else if (score > b) { third = second; c = b; second = enemy; b = score; }
                else if (score > c) { third = enemy; c = score; }
            }
            var target = Visible(body, first) ? first : Visible(body, second) ? second : Visible(body, third) ? third : null;
            if (target) { previousEnemy = target; lastSeen = target.footPosition; seenAt = now; }
            foreach (var ai in ais)
            {
                if (!ai || ai.currentEnemy == null) continue;
                ai.currentEnemy.gameObject = target ? target.gameObject : null;
                if (target) { ai.currentEnemy.bestHurtBox = target.mainHurtBox; ai.enemyAttention = 2.5f; }
            }

            float health = body.healthComponent.combinedHealthFraction;
            Vector3 desired = body.footPosition;
            if (target)
            {
                Vector3 away = body.footPosition - target.footPosition; away.y = 0;
                if (away.sqrMagnitude < 0.01f) away = Vector3.forward; else away.Normalize();
                bool retreat = health < 0.3f;
                float spacing = retreat ? Mathf.Max(32, range) : range;
                var lateral = Vector3.Cross(Vector3.up, away) * Mathf.Sin(now * 0.65f + player.BotOrdinal * 2.4f) * (range < 12 ? 1.5f : 7f);
                desired = target.footPosition + away * spacing + lateral;
                // Occupy the focus instead of chasing a distant enemy off it.
                if (objective && !retreat && Vector3.Distance(target.footPosition, hillPoint) > hill.Radius * 1.5f)
                    desired = hillPoint + away * hill.Radius * 0.35f;
            }
            else if (objective) desired = hillPoint;
            else if (now - seenAt < 3) desired = lastSeen;
            else
            {
                // Out of sight: navigate toward the closest opponent, without
                // acquiring an aim target through geometry or invisibility.
                float closest = float.PositiveInfinity;
                foreach (var candidate in targets)
                {
                    if (!candidate.Body || !match.AreOpponents(player, candidate.Player)
                        || candidate.Body.GetVisibilityLevel(body) != VisibilityLevel.Visible) continue;
                    float distance = (candidate.Body.footPosition - body.footPosition).sqrMagnitude;
                    if (distance < closest) { closest = distance; desired = candidate.Body.footPosition; }
                }
            }
            RaycastHit ground;
            if (Physics.Raycast(desired + Vector3.up * 8, Vector3.down, out ground, 24, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)
                && ground.normal.y > 0.55f
                && !Physics.CheckCapsule(ground.point + Vector3.up * 1.5f, ground.point + Vector3.up * 3, 0.6f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                goal.transform.position = ground.point;
            else goal.transform.position = body.footPosition;
            movement.shouldSprint = !target || health < 0.3f;
            movement.buttonPressType = target && health >= 0.3f ? AISkillDriver.ButtonPressType.Hold : AISkillDriver.ButtonPressType.Abstain;
            var equipment = body.inventory ? EquipmentCatalog.GetEquipmentDef(body.inventory.GetActiveEquipment().equipmentIndex) : null;
            movement.shouldFireEquipment = equipment && (equipment.name == "Fruit" ? health < 0.5f : equipment.name == "GainArmor" ? health < 0.6f && target : target);
        }

        private static bool Visible(CharacterBody body, CharacterBody enemy)
        {
            return enemy && !Physics.Linecast(body.corePosition, enemy.mainHurtBox ? enemy.mainHurtBox.transform.position : enemy.corePosition,
                LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
        }
    }
}
