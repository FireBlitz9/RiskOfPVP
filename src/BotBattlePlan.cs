using System;

namespace ArenaPvP
{
    public struct BotRouteOption
    {
        public const int NoNode = -1;
        public int Node, FirstHop;
        public float Travel, Range, HillDistance, AllyDistance, Sector, Approach, Crossfire;
        public bool HasShot, FirstHopExposed, HasCoverExit;
    }

    public sealed class BotRoutePlanner
    {
        private struct Blockage
        {
            internal int Node, Corridor;
            internal float Until;

            internal float Penalty(BotRouteOption option, float now)
            { return now >= Until ? 0 : option.Node == Node ? 55f : option.FirstHop == Corridor ? 24f : 0; }
        }

        private int goal = BotRouteOption.NoNode, corridor = BotRouteOption.NoNode;
        private Blockage lastBlockage, earlierBlockage;
        private float checkedAt, bestRemaining;
        private float positionAt, positionX, positionZ;
        private bool hasPosition;
        private readonly int[] arrivals=new int[4];
        private readonly float[] arrivalUntil=new float[4];
        private int arrivalIndex,lastArrival=BotRouteOption.NoNode;

        public void Clear()
        {
            goal = corridor = BotRouteOption.NoNode;
            checkedAt = bestRemaining = 0;
            lastBlockage = earlierBlockage = default(Blockage);
            hasPosition=false;
            Array.Clear(arrivalUntil,0,arrivalUntil.Length);arrivalIndex=0;lastArrival=BotRouteOption.NoNode;
        }

        // This clock is independent of SetGoal. Alternating nearby goals must
        // not keep erasing evidence that the body is stuck in the same place.
        public bool Movement(float now,float x,float z,bool canMove,float remaining)
        {
            float dx=x-positionX,dz=z-positionZ;
            if(!hasPosition || !canMove || remaining<=2 || dx*dx+dz*dz>=1.21f)
            { hasPosition=true;positionAt=now;positionX=x;positionZ=z;return false; }
            if(now-positionAt<1.15f)return false;
            Block(now);positionAt=now;positionX=x;positionZ=z;return true;
        }

        private void Block(float now)
        {
            if(goal==BotRouteOption.NoNode)return;
            if(now>=lastBlockage.Until || lastBlockage.Corridor!=corridor)earlierBlockage=lastBlockage;
            lastBlockage=new Blockage { Node=goal,Corridor=corridor,Until=now+3f };
            goal=corridor=BotRouteOption.NoNode;
        }

        public void MarkBlocked(float now) => Block(now);

        public void Progress(float now, float remaining, bool canMove)
        {
            if (goal == BotRouteOption.NoNode) return;
            if(remaining<=2 && goal!=lastArrival)
            { arrivals[arrivalIndex]=goal;arrivalUntil[arrivalIndex]=now+3;arrivalIndex=(arrivalIndex+1)%arrivals.Length;lastArrival=goal; }
            if (!canMove || remaining <= 2f || remaining < bestRemaining - .75f)
            {
                checkedAt = now;
                bestRemaining = remaining;
            }
            if (remaining > 2f && now - checkedAt >= 1.1f)
            {
                Block(now);
            }
        }

        public void SetGoal(BotRouteOption option, float now, float remaining)
        {
            if (option.Node != goal)
            {
                checkedAt = now;
                bestRemaining = remaining;
            }
            goal = option.Node;
            corridor = option.FirstHop;
        }

        public float Score(BotRouteOption option, BotEngagement mode, float desiredRange, float hillRadius, float now)
        {
            bool onHill = hillRadius > 0;
            float score = -option.Travel * .26f - Math.Abs(option.Range - desiredRange) * (mode == BotEngagement.Recover ? .16f : .47f);
            if (mode == BotEngagement.Recover)
                score += (option.HasShot ? -16f : 14f) - (option.FirstHopExposed ? 6f : 0f);
            else
                score += option.HasShot ? 12f : -5f;
            if(mode!=BotEngagement.Recover)score+=Math.Max(-8,Math.Min(8,option.Approach))*(mode==BotEngagement.Press ? .75f : .55f);
            if (option.HasCoverExit) score += mode == BotEngagement.Press ? 1f : 4f;
            score -= option.Crossfire * (mode == BotEngagement.Recover ? 18f : 13f);
            if (onHill)
            {
                float outside = Math.Max(0, option.HillDistance - hillRadius * .80f);
                score -= outside * 2.1f;
                if (outside == 0)
                    score += 18f + option.Sector * 4f - Math.Max(0, 6f - option.AllyDistance) * 1.7f;
            }
            if (option.Node == goal) score += 2.3f;
            if(mode!=BotEngagement.Recover)for(int i=0;i<arrivals.Length;i++)
                if(now<arrivalUntil[i] && option.Node==arrivals[i]) { score-=14;break; }
            return score - Math.Max(lastBlockage.Penalty(option, now), earlierBlockage.Penalty(option, now));
        }
    }

    public sealed class BotCastWindow
    {
        private struct Cast
        {
            internal int Skill, Token;
            internal float At, Until;
        }

        private readonly Cast[] casts = new Cast[2];
        private int body;

        // Only secondary and special casts with a single native stock supply
        // evidence. Repeated casts may shorten the estimate as builds change.
        public void Observe(int bodyId, int slot, int skill, float at, float baseInterval, int baseStock, int token = 0)
        {
            if (body != bodyId) { Clear(); body = bodyId; }
            if (slot < 0 || slot >= casts.Length) return;
            if (baseStock != 1 || baseInterval < 4f) { casts[slot] = default(Cast); return; }
            var previous = casts[slot];
            float elapsed = at - previous.At;
            if (previous.Skill == skill && previous.Until > 0 && (elapsed < .20f || token != 0 && previous.Token == token)) return;
            float interval = previous.Skill == skill && previous.Until > 0
                ? Math.Min(baseInterval, elapsed) : baseInterval;
            casts[slot] = new Cast { Skill = skill, Token = token, At = at, Until = at + Math.Min(2.5f, interval * .40f) };
        }

        public bool IsOpen(int bodyId, float now)
        {
            if (body != bodyId) return false;
            foreach (var cast in casts)
                if (cast.Until > now && now - cast.At >= .45f) return true;
            return false;
        }

        public void Clear() { Array.Clear(casts, 0, casts.Length); body = 0; }
    }

    public struct BotTempo
    {
        public BotEngagement Engagement;
        public bool ReserveEscape;
    }

    public sealed class BotFightTiming
    {
        private float retreatUntil;
        private float lastSeen = -100;

        public void Clear() { retreatUntil = 0; lastSeen = -100; }

        public BotTempo Decide(float now, float health, float enemyHealth, float distance, BotKit kit,
            float trade, float pressure, int enemies, int allies, bool holdingHill,
            bool attackReady, bool escapeReady, bool visible, bool castWindow)
        {
            if (visible) lastSeen = now;
            bool outnumbered = enemies > allies + 1;
            bool risk = health < .46f || pressure > .48f || outnumbered;
            bool window = visible && castWindow && attackReady && health >= .43f && !outnumbered;
            BotEngagement mode;
            if (!visible)
                mode = holdingHill ? BotEngagement.Hold : now < retreatUntil ? BotEngagement.Recover : BotEngagement.Press;
            else
            {
                mode = BotAdaptation.Engage(health, enemyHealth, distance, kit, trade, pressure, enemies, allies, holdingHill, attackReady);
                if (!holdingHill && mode == BotEngagement.Recover) retreatUntil = now + 1.15f;
                if (!holdingHill && now < retreatUntil) mode = BotEngagement.Recover;
                if (window && mode == BotEngagement.Trade && distance <= kit.Reach) mode = BotEngagement.Press;
                if (outnumbered && mode == BotEngagement.Press && enemyHealth > .20f) mode = BotEngagement.Hold;
            }
            // Loss of sight during the reset is expected. Release any reserved
            // escape once the old fight has gone stale.
            bool reserve = escapeReady && (risk || mode == BotEngagement.Recover || (visible && !window && !attackReady));
            return new BotTempo { Engagement = mode, ReserveEscape = reserve && now - lastSeen < 2f };
        }
    }

    public sealed class BotEngagementPolicy
    {
        private float coverUntil, reconsiderAt;

        public void Clear() { coverUntil = reconsiderAt = 0; }

        public BotTempo Decide(float now, float health, float lethalFraction, float recentDamage, int attackers,
            bool holdingHill, bool escapeReady, bool hasUsableShot, bool canFinish, bool opening)
        {
            if (holdingHill)
                return new BotTempo { Engagement = BotEngagement.Hold, ReserveEscape = escapeReady && attackers > 0 };

            // Numbers alone favor taking an angle, not running away. Retreat
            // costs pressure and is reserved for a credible immediate loss.
            bool imminent = attackers > 0 && (lethalFraction >= health * 1.08f
                || health <= .22f && recentDamage >= .14f
                || health <= .36f && attackers >= 2 && recentDamage >= .43f);
            if (imminent && now >= reconsiderAt && (!canFinish || lethalFraction >= health * 1.45f))
            {
                coverUntil = now + .95f;
                reconsiderAt = now + 1.35f;
            }
            if (now < coverUntil)
                return new BotTempo { Engagement = BotEngagement.Recover, ReserveEscape = escapeReady };

            var intent = !hasUsableShot || canFinish || opening && attackers <= 1 ? BotEngagement.Press
                : attackers > 1 ? BotEngagement.Hold : BotEngagement.Trade;
            return new BotTempo { Engagement = intent, ReserveEscape = escapeReady && (attackers > 1 || imminent) };
        }
    }
}
