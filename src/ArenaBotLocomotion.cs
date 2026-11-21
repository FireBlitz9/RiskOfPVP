using RoR2;
using UnityEngine;

namespace ArenaPvP
{
    internal sealed class ArenaBotLocomotion
    {
        private readonly BotLocomotionFeedback feedback=new BotLocomotionFeedback();
        private static readonly float[] angles={0,45,-45,90,-90,135,-135,180};
        private float recoveryUntil,holdUntil,nextProbe,nextJump;
        private Vector3 escape;
        private int angleIndex;
        private static int probeFrame=-1,probes;
        internal int Recoveries { get; private set; }
        internal void Clear() { feedback.Clear();recoveryUntil=holdUntil=nextProbe=nextJump=0;escape=Vector3.zero;angleIndex=0;Recoveries=0; }
        internal void Pause() { feedback.Clear();recoveryUntil=0; }
        internal Vector3 Resolve(CharacterBody body,Vector3 requested,Vector3 waypoint,HillView hill,float now,out bool jump)
        {
            jump=false;
            bool grounded=body.characterMotor && body.characterMotor.isGrounded && !body.isFlying;
            if(feedback.Sample(now,body.footPosition.x,body.footPosition.z,waypoint.x,waypoint.z,true,grounded))
            { recoveryUntil=now+1.5f;nextProbe=holdUntil=0;angleIndex=0;Recoveries++; }
            if(!grounded || now>=recoveryUntil)return requested;
            if(now<nextProbe)return escape;
            if(probeFrame!=Time.frameCount) { probeFrame=Time.frameCount;probes=0; }
            if(probes>=4)return Vector3.zero;
            probes++;
            nextProbe=now+.15f;
            var toward=waypoint-body.footPosition;toward.y=0;toward.Normalize();
            RaycastHit wall;
            bool frontBlocked=Blocked(body,toward,out wall);
            if(now<holdUntil && Safe(body,escape,hill))return escape;
            Vector3 best=Vector3.zero;float score=float.NegativeInfinity;
            // Wall tangents use actual collision normals. The remaining angular
            // search is capped at three candidates per probe, never a scene scan.
            for(int i=0;i<3;i++)
            {
                Vector3 candidate;
                if(frontBlocked && i<2)
                { candidate=Vector3.Cross(Vector3.up,wall.normal).normalized*(i==0 ? 1 : -1); }
                else candidate=Quaternion.Euler(0,angles[angleIndex++%angles.Length],0)*toward;
                if(!Safe(body,candidate,hill))continue;
                float value=Vector3.Dot(candidate,toward)+(escape.sqrMagnitude>.1f ? Vector3.Dot(candidate,escape)*.35f : 0);
                if(value>score) { score=value;best=candidate; }
            }
            if(best.sqrMagnitude>.1f) { escape=best;holdUntil=now+.65f;return escape; }
            escape=Vector3.zero;
            if(frontBlocked && now>=nextJump) { nextJump=now+1.4f;jump=true;return toward; }
            return Vector3.zero;
        }
        private static bool Blocked(CharacterBody body,Vector3 direction,out RaycastHit hit)
        {
            float radius=Mathf.Clamp(body.radius*.7f,.2f,.75f);
            return Physics.SphereCast(body.footPosition+Vector3.up*(radius+.2f),radius,direction,out hit,2.2f,LayerIndex.world.mask,QueryTriggerInteraction.Ignore);
        }
        internal static bool Safe(CharacterBody body,Vector3 direction,HillView hill)
        {
            if(direction.sqrMagnitude<.1f)return false;
            RaycastHit hit;
            if(Blocked(body,direction,out hit))return false;
            float distance=BotTacticsRules.StoppingDistance(Mathf.Max(body.moveSpeed,body.characterMotor.velocity.magnitude),body.acceleration,body.radius);
            var end=body.footPosition+direction*distance;
            if(hill!=null && hill.Ready && ArenaBotRoutes.InHill(hill,body.footPosition) && !ArenaBotRoutes.InHill(hill,end))return false;
            return Physics.Raycast(body.footPosition+direction*.8f+Vector3.up*1.5f,Vector3.down,out hit,3.5f,LayerIndex.world.mask,QueryTriggerInteraction.Ignore) && hit.normal.y>.5f
                && Physics.Raycast(body.footPosition+direction*(distance*.5f)+Vector3.up*1.5f,Vector3.down,out hit,3.5f,LayerIndex.world.mask,QueryTriggerInteraction.Ignore) && hit.normal.y>.5f
                && Physics.Raycast(end+Vector3.up*1.5f,Vector3.down,out hit,3.5f,LayerIndex.world.mask,QueryTriggerInteraction.Ignore) && hit.normal.y>.5f;
        }

        internal static bool ClearTravelCorridor(CharacterBody body, Vector3 direction, float distance, HillView hill)
        {
            var origin = body.footPosition;
            var landing = origin + direction * distance;
            if (hill != null && hill.Ready && ArenaBotRoutes.InHill(hill, origin)
                && !ArenaBotRoutes.InHill(hill, landing)) return false;
            float radius = Mathf.Clamp(body.radius * .7f, .25f, .85f);
            if (Physics.SphereCast(origin + Vector3.up * (radius + .4f), radius, direction, out _, distance,
                LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) return false;

            // Travel skills can cover much more ground than a walking
            // input. Confirm support along the entire swept corridor.
            int samples = Mathf.Clamp(Mathf.CeilToInt(distance / 4f), 3, 18);
            for (int i = 1; i <= samples; i++)
            {
                var probe = origin + direction * (distance * i / samples) + Vector3.up * 1.8f;
                if (!Physics.Raycast(probe, Vector3.down, out var ground, 4f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)
                    || ground.normal.y <= .5f) return false;
            }
            return true;
        }

        internal static bool ClearBoostArc(CharacterBody body, Vector3 launchPoint, Vector3 velocity, Vector3 forward, HillView hill)
        {
            float gravity = Mathf.Max(1f, -Physics.gravity.y);
            if (velocity.y < 2f) return false;
            float time = 2f * velocity.y / gravity;
            if (time < .2f || time > 2.25f) return false;
            var horizontal = velocity;
            horizontal.y = 0;
            Vector3 displacement = horizontal * time;
            if (Vector3.Dot(displacement, forward) < 7f || displacement.sqrMagnitude > 60f * 60f) return false;
            var expected = launchPoint + displacement;
            if (hill != null && hill.Ready && ArenaBotRoutes.InHill(hill, body.footPosition)
                && !ArenaBotRoutes.InHill(hill, expected)) return false;
            if (!Physics.Raycast(expected + Vector3.up * 3, Vector3.down, out var landing, 7,
                LayerIndex.world.mask, QueryTriggerInteraction.Ignore) || landing.normal.y < .55f) return false;
            var previous = launchPoint + (body.corePosition - body.footPosition);
            for (int i = 1; i <= 6; i++)
            {
                float t = time * i / 6;
                var point = launchPoint + (body.corePosition - body.footPosition)
                    + velocity * t - Vector3.up * (gravity * t * t * .5f);
                var segment = point - previous;
                if (Physics.SphereCast(previous, Mathf.Clamp(body.radius * .65f, .22f, .65f),
                    segment.normalized, out _, segment.magnitude, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) return false;
                previous = point;
            }
            return true;
        }
    }
}
