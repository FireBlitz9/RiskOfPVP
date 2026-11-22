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
        private static int traversalFrame = -1, traversalProbes;
        internal int Recoveries { get; private set; }
        internal void Clear() { feedback.Clear();recoveryUntil=holdUntil=nextProbe=nextJump=0;escape=Vector3.zero;angleIndex=0;Recoveries=0; }
        internal void Pause() { feedback.Clear();recoveryUntil=0; }

        internal static bool TryTraversalProbe()
        {
            if (traversalFrame != Time.frameCount) { traversalFrame = Time.frameCount; traversalProbes = 0; }
            if (traversalProbes >= 6) return false;
            traversalProbes++;
            return true;
        }
        internal Vector3 Resolve(CharacterBody body,Vector3 requested,Vector3 waypoint,HillView hill,float now,out bool jump)
        {
            jump=false;
            bool grounded=body.characterMotor && body.characterMotor.isGrounded && !body.isFlying;
            if(feedback.Sample(now,body.footPosition.x,body.footPosition.z,waypoint.x,waypoint.z,true,grounded))
            { recoveryUntil=now+1.5f;nextProbe=holdUntil=0;angleIndex=0;Recoveries++; }
            if(!grounded || now>=recoveryUntil)return requested;
            if(now<nextProbe)return escape;
            if(probeFrame!=Time.frameCount) { probeFrame=Time.frameCount;probes=0; }
            if(probes>=4)return requested;
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
            float distance=BotTacticsRules.StoppingDistance(Mathf.Max(body.moveSpeed,body.characterMotor.velocity.magnitude),body.acceleration,body.radius);
            var end=body.footPosition+direction*distance;
            if(hill!=null && hill.Ready && ArenaBotRoutes.InHill(hill,body.footPosition) && !ArenaBotRoutes.InHill(hill,end))return false;
            return Walkable(body, direction, distance);
        }

        private static bool Floor(Vector3 point, float up, float down, out RaycastHit floor)
            => Physics.Raycast(point + Vector3.up * up, Vector3.down, out floor, up + down,
                LayerIndex.world.mask, QueryTriggerInteraction.Ignore) && floor.normal.y > .48f;

        internal static bool Walkable(CharacterBody body, Vector3 direction, float distance)
        {
            var feet = body.footPosition;
            float near = Mathf.Min(.85f, distance * .5f);
            if (!Floor(feet + direction * near, 1.5f, 1.65f, out var first)) return false;
            // Cast along the slope instead of straight into its face. A low
            // step can still be climbed by the motor if the upper lane is clear.
            var lift = Vector3.up * (Mathf.Clamp(body.radius * .65f, .2f, .7f) + .25f);
            if (Physics.Linecast(feet + lift, first.point + lift, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) return false;
            if (Blocked(body, direction, out var obstacle) && first.point.y <= feet.y + .18f && obstacle.normal.y < .48f) return false;
            if (!Floor(first.point + direction * (distance * .5f - near), 2.4f, 2.4f, out var middle)) return false;
            return Floor(middle.point + direction * (distance * .5f), 2.7f, 2.7f, out _);
        }

        internal static Vector3 HoldHill(CharacterBody body, HillView hill, Vector3 requested, bool sprint)
        {
            var radial = body.footPosition - hill.To;
            radial.y = 0;
            float distance = radial.magnitude;
            if (distance < .05f) return requested;
            var outward = radial.normalized;
            float speed = body.moveSpeed * (sprint || body.isSprinting ? body.sprintingSpeedMultiplier : 1f);
            float buffer = Mathf.Min(hill.Radius * .64f, Mathf.Max(.65f, body.radius + speed * .14f));
            float outerSpeed = body.characterMotor ? Mathf.Max(0, Vector3.Dot(body.characterMotor.velocity, outward)) : 0;
            var projected = radial + requested * (speed * .28f) + outward * (outerSpeed * .18f);
            if (projected.sqrMagnitude <= (hill.Radius - buffer) * (hill.Radius - buffer)) return requested;
            float outwardMove = Mathf.Max(0, Vector3.Dot(requested, outward));
            return (requested - outward * (outwardMove + .85f)).normalized;
        }

        internal static bool CanDrop(CharacterBody body, Vector3 direction, Vector3 goal, HillView hill, out Vector3 landing)
        {
            landing = body.footPosition;
            var origin = body.footPosition;
            var forward = goal - origin;
            forward.y = 0;
            float gain = Vector3.Dot(forward, direction);
            if (gain < 1.7f || goal.y >= origin.y - 2 || Blocked(body, direction, out _)) return false;
            float gravity = Mathf.Max(1, -Physics.gravity.y);
            for (int i = 0; i < 3; i++)
            {
                float span = Mathf.Min(gain, Mathf.Max(3, body.moveSpeed * (.45f + i * .25f)));
                if (!Floor(origin + direction * span, .5f, 16, out var floor)) continue;
                float drop = origin.y - floor.point.y;
                if (drop < 2 || drop > 15 || span > 2 + body.moveSpeed * Mathf.Sqrt(2 * drop / gravity)) continue;
                if (hill != null && ArenaBotRoutes.InHill(hill, origin) && !ArenaBotRoutes.InHill(hill, floor.point)) continue;
                if (!Floor(floor.point + direction, 1.5f, 1.4f, out _)
                    || Physics.Linecast(origin + Vector3.up * 1.2f, floor.point + Vector3.up * 1.2f,
                        LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) continue;
                landing = floor.point;
                return true;
            }
            return false;
        }

        internal static bool LandingAhead(CharacterBody body, Vector3 horizontal, float lift, Vector3 destination,
            HillView hill, out Vector3 landing)
        {
            landing = body.footPosition;
            var gravity = Mathf.Max(1, -Physics.gravity.y);
            var origin = body.footPosition;
            var last = origin;
            for (int i = 1; i <= 8; i++)
            {
                float time = i * .18f;
                var point = origin + horizontal * time + Vector3.up * (lift * time - gravity * time * time * .5f);
                var head = Vector3.up * Mathf.Max(.8f, body.radius);
                var checkedPoint = point;
                RaycastHit floor = default(RaycastHit);
                bool landed = point.y < last.y && Floor(new Vector3(point.x,last.y,point.z), .4f, last.y - point.y + .35f, out floor);
                if (landed) checkedPoint = floor.point;
                var segment = checkedPoint - last;
                if (Physics.SphereCast(last + head, Mathf.Clamp(body.radius*.6f,.2f,.65f), segment.normalized, out _, segment.magnitude,
                    LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) break;
                if (landed)
                {
                    if (hill != null && ArenaBotRoutes.InHill(hill, origin) && !ArenaBotRoutes.InHill(hill, floor.point)) return false;
                    var progress = (destination - origin); progress.y = 0;
                    if (Vector3.Dot(floor.point - origin, progress.normalized) < 1.5f) return false;
                    landing = floor.point;
                    return true;
                }
                last = point;
            }
            return false;
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
