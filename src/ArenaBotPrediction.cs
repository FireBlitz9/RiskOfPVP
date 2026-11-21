using UnityEngine;

namespace ArenaPvP
{
    internal sealed class ArenaBotPrediction
    {
        private const float Horizon = .85f;
        private Vector3 position, velocity, acceleration;
        private float sampleAt;
        private bool sampled;

        internal void Clear() { sampled = false; velocity = acceleration = Vector3.zero; }

        internal void Observe(Vector3 point, Vector3 reportedVelocity, float now)
        {
            float dt = now - sampleAt;
            if (sampled && dt < .045f) return;
            var delta = point - position;
            float maxDistance = 3 + 60 * dt;
            bool discontinuity = sampled && dt <= .3f && delta.sqrMagnitude > maxDistance * maxDistance;
            if (!sampled || dt > .3f || discontinuity)
            {
                velocity = discontinuity ? Vector3.zero : Limit(reportedVelocity, 45);
                acceleration = Vector3.zero;
            }
            else
            {
                // Observed positions still work when a remote motor reports old
                // velocity. Damp acceleration but react immediately to reversals.
                var measured = Limit(delta * (1 / dt), 45);
                var reported = Limit(reportedVelocity, 45);
                var next = (measured - reported).sqrMagnitude <= 16 ? measured * .75f + reported * .25f : measured;
                var change = Limit((next - velocity) * (1 / dt), 55);
                acceleration = Vector3.Dot(velocity, next) < 0 ? change : acceleration * .4f + change * .6f;
                velocity = next;
            }
            position = point;
            sampleAt = now;
            sampled = true;
        }

        internal Vector3 Intercept(Vector3 origin, Vector3 point, float speed)
        {
            if (!sampled || !(speed > 0) || float.IsInfinity(speed) || (point - origin).sqrMagnitude < .01f) return point;
            if (velocity.sqrMagnitude < .01f && acceleration.sqrMagnitude < .01f) return point;
            float low = 0, high = Horizon;
            var distance = point - origin;
            if (!CanReach(distance, speed, high)) return point;
            for (int i = 0; i < 9; i++)
            {
                float time = (low + high) * .5f;
                if (CanReach(distance, speed, time)) high = time;
                else low = time;
            }
            return point + Displacement((low + high) * .5f);
        }

        private bool CanReach(Vector3 distance, float speed, float time)
        {
            float travel = speed * time;
            return (distance + Displacement(time)).sqrMagnitude <= travel * travel;
        }

        private Vector3 Displacement(float time)
        {
            float turn = Mathf.Min(time, .18f);
            return velocity * time + acceleration * (turn * (time - turn * .5f) * .65f);
        }

        private static Vector3 Limit(Vector3 vector, float max) => vector.sqrMagnitude > max * max ? vector.normalized * max : vector;
    }
}
