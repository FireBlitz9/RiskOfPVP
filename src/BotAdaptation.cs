using System;
using System.Collections.Generic;

namespace ArenaPvP
{
    public enum BotEngagement { Press, Trade, Hold, Recover }

    public struct BotKit
    {
        public float Range, Reach, OnHit, SkillUse;
    }

    public sealed class BotOpponent
    {
        private float dealt, taken, burst, lastTrade;
        private float lastSeen, lastHealth;
        private int body, observations;
        public float Speed { get; private set; }
        public float Approach { get; private set; }
        public float Strafe { get; private set; }
        public float Sustain { get; private set; }

        public void See(int bodyId, float now, float speed, float approach, float strafe, float health)
        {
            bool continuous = observations > 0 && body == bodyId && now - lastSeen <= 0.75f;
            float weight = observations == 0 ? 1f : 0.22f;
            Speed += (speed - Speed) * weight;
            Approach += (approach - Approach) * weight;
            Strafe += (strafe - Strafe) * weight;
            if (continuous)
                Sustain += (BotAdaptation.Clamp((health - lastHealth) / Math.Max(.08f, now - lastSeen), 0, 1) - Sustain) * .16f;
            body = bodyId;
            lastSeen = now;
            lastHealth = health;
            observations++;
        }

        public void Damage(float now, float fraction, bool received)
        {
            float decay = Decay(now);
            dealt *= decay;
            taken *= decay;
            burst *= decay;
            lastTrade = now;
            if (received)
            {
                taken = Math.Min(2f, taken + fraction);
                burst = Math.Max(burst, fraction);
            }
            else dealt = Math.Min(2f, dealt + fraction);
        }

        private float Decay(float now) { return (float)Math.Exp(-Math.Max(0, now - lastTrade) / 6f); }
        public float Trade(float now) { return (dealt - taken) * Decay(now); }
        public float Pressure(float now) { return (taken * .4f + burst) * Decay(now); }
        public float Familiarity(float now) { return observations > 0 ? Math.Max(0, 1 - (now - lastSeen) / 25f) : 0; }
        internal void NewLife() { dealt = taken = burst = 0; }
    }

    public sealed class BotMemory
    {
        private readonly Dictionary<uint, BotOpponent> opponents = new Dictionary<uint, BotOpponent>();
        private float primaryDamage, otherSkillDamage, procWeight, skillHits, lastHit;

        public BotOpponent For(uint id)
        {
            BotOpponent opponent;
            if (!opponents.TryGetValue(id, out opponent)) opponents.Add(id, opponent = new BotOpponent());
            return opponent;
        }

        public BotOpponent Find(uint id)
        {
            BotOpponent opponent;
            return opponents.TryGetValue(id, out opponent) ? opponent : null;
        }

        public void NewLife()
        {
            foreach (var opponent in opponents.Values) opponent.NewLife();
        }

        public void SkillHit(float now, bool primary, float fraction, float procCoefficient)
        {
            float decay = (float)Math.Exp(-Math.Max(0, now - lastHit) / 35f);
            lastHit = now;
            primaryDamage *= decay;
            otherSkillDamage *= decay;
            if (primary) primaryDamage += fraction; else otherSkillDamage += fraction;
            procWeight = procWeight * decay + BotAdaptation.Clamp(procCoefficient, 0, 1);
            skillHits = skillHits * decay + 1;
        }

        public BotKit RefineKit(BotKit kit, float now)
        {
            if (kit.Reach <= 0 || skillHits < 4) return kit;
            float confidence = Math.Min(.55f, skillHits * .025f) * Math.Max(0, 1 - (now - lastHit) / 45f);
            float skillShare = otherSkillDamage / Math.Max(.001f, primaryDamage + otherSkillDamage);
            float procAffinity = .32f + .55f * procWeight / skillHits + .12f * (1 - skillShare);
            kit.OnHit += (procAffinity - kit.OnHit) * confidence;
            kit.SkillUse += (.42f + skillShare * .58f - kit.SkillUse) * confidence;
            return kit;
        }
    }

    public static class BotAdaptation
    {
        internal static float Clamp(float value, float min, float max) { return Math.Max(min, Math.Min(max, value)); }

        public static BotEngagement Engage(float health, float enemyHealth, float distance, BotKit kit,
            float trade, float pressure, int enemies, int allies, bool holdingHill, bool combatSkillReady)
        {
            if (holdingHill) return BotEngagement.Hold;
            if (health < .27f || (health < .62f && (trade < -.28f || enemies > allies + 1)
                && enemyHealth > .25f)) return BotEngagement.Recover;
            if (distance > kit.Range * 1.30f || (enemyHealth < .31f && health > enemyHealth + .2f))
                return combatSkillReady || distance > kit.Reach ? BotEngagement.Press : BotEngagement.Trade;
            if (pressure > .34f && (trade < -.1f || !combatSkillReady) && kit.Range >= 12f)
                return BotEngagement.Hold;
            return BotEngagement.Trade;
        }

        public static float Spacing(BotKit kit, BotEngagement engagement, float approach, float pressure, float enemyHealth)
        {
            float range = kit.Range;
            if (engagement == BotEngagement.Recover) return Math.Max(22, range + 12);
            if (range < 12f) return range;
            if (engagement == BotEngagement.Press && enemyHealth < .35f) return Math.Max(9, range * .65f);
            return Clamp(range + approach * 8 + pressure * 7, range * .73f, Math.Max(range, kit.Reach * .85f));
        }

        // Solve the intercept against observed motion and bound the lead
        // horizon. An impossible intercept falls back to a direct shot.
        public static float LeadTime(float speed, float distanceSquared, float velocitySquared, float rangeDotVelocity)
        {
            if (speed <= 0 || distanceSquared <= 0) return 0;
            float a = velocitySquared - speed * speed;
            float b = 2 * rangeDotVelocity;
            if (Math.Abs(a) < .001f) return b < -.001f ? Clamp(-distanceSquared / b, 0, .65f) : 0;
            float discriminant = b * b - 4 * a * distanceSquared;
            if (discriminant < 0) return 0;
            float root = (float)Math.Sqrt(discriminant);
            float first = (-b - root) / (2 * a), second = (-b + root) / (2 * a);
            float time = first > 0 && second > 0 ? Math.Min(first, second) : Math.Max(first, second);
            return Clamp(time, 0, .65f);
        }
    }
}
