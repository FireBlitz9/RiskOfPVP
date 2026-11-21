using RoR2;
using UnityEngine;

namespace ArenaPvP
{
    // Short maneuvers share a heading for several physics ticks. Isolated
    // dodge inputs used to repeatedly cancel each other's lateral movement.
    internal sealed class ArenaBotFootwork
    {
        private readonly BotLocomotionFeedback progress = new BotLocomotionFeedback();
        private uint random = 1;
        private int pattern, side;
        private float switchAt, probeAt;
        private enum MeleePhase { Orbit, Strike, Peel }
        private MeleePhase meleePhase;
        private float meleeUntil;
        private Vector3 probeHeading, safeHeading, probeOrigin;

        internal void Reset(int ordinal, float now)
        {
            random = ((uint)(ordinal + 1) * 747796405u ^ (uint)(now * 997f)) | 1u;
            side = (Next() & 1) == 0 ? 1 : -1;
            pattern = 0;
            switchAt = now + .65f;
            probeAt = 0;
            meleePhase = MeleePhase.Orbit;
            meleeUntil = now + .35f;
            progress.Clear();
        }

        private uint Next()
        {
            random ^= random << 13;
            random ^= random >> 17;
            return random ^= random << 5;
        }

        internal void PauseProgress() => progress.Clear();

        internal bool ApproachStalled(Vector3 origin, Vector3 opponent, bool closing, float now)
        {
            // Ordinary jumps still contribute horizontal displacement.
            return progress.Sample(now, origin.x, origin.z, opponent.x, opponent.z, closing, true);
        }

        internal Vector3 Steer(CharacterBody body, Vector3 opponent, Vector3 fallback, HillView hill, float spacing,
            bool closing, bool threatened, bool melee, bool attackReady, float now)
        {
            Vector3 toward = opponent - body.footPosition;
            toward.y = 0;
            float distance = toward.magnitude;
            if (distance < .1f) return fallback;
            toward.Normalize();
            if (now >= switchAt)
            {
                int previousPattern = pattern;
                pattern = (pattern + 1 + (int)(Next() % 3)) % 4;
                switchAt = now + .55f + (Next() % 81) * .01f;
                // Change orbit direction after a forward cut, never from one
                // lateral strafe straight into its opposite.
                if (previousPattern == 1 && (Next() & 1) == 0) side = -side;
                probeAt = 0;
            }
            Vector3 tangent = Vector3.Cross(Vector3.up, toward) * side;
            float radial;
            float lateral;
            if (melee && !closing)
            {
                var previous = meleePhase;
                if (meleePhase == MeleePhase.Orbit && attackReady && now >= meleeUntil)
                {
                    meleePhase = MeleePhase.Strike;
                    meleeUntil = now + .32f + spacing / Mathf.Max(6f, body.moveSpeed) * .45f;
                }
                else if (meleePhase == MeleePhase.Strike && (now >= meleeUntil || !attackReady && distance < spacing))
                {
                    meleePhase = MeleePhase.Peel;
                    meleeUntil = now + .36f + (Next() % 20) * .01f;
                }
                else if (meleePhase == MeleePhase.Peel && (now >= meleeUntil || distance > spacing + 2f))
                {
                    meleePhase = MeleePhase.Orbit;
                    meleeUntil = now + .22f + (Next() % 47) * .01f;
                }
                if (previous != meleePhase) probeAt = 0;
            }
            if (closing)
            {
                radial = pattern == 1 ? 1f : .82f;
                lateral = pattern == 1 ? .18f : pattern == 2 || threatened ? .63f : .43f;
            }
            else if (melee)
            {
                float orbitDistance = spacing + 1.2f;
                radial = meleePhase == MeleePhase.Strike ? .94f : meleePhase == MeleePhase.Peel ? -.65f
                    : Mathf.Clamp((distance - orbitDistance) / Mathf.Max(2, spacing), -.55f, .36f);
                lateral = meleePhase == MeleePhase.Strike ? .28f : meleePhase == MeleePhase.Peel ? .76f : 1f;
            }
            else
            {
                float spacingError = Mathf.Clamp((distance - spacing) / Mathf.Max(2, spacing), -.55f, .55f);
                radial = Mathf.Max(.12f, spacingError + (pattern == 1 ? .60f : pattern == 2 ? .08f : .24f));
                lateral = pattern == 1 ? .38f : .88f;
                if (distance < Mathf.Min(2f, spacing * .6f)) radial = -.12f; // Clear the opponent's collision radius on the arc.
            }
            Vector3 preferred = (toward * radial + tangent * lateral).normalized;
            if (now < probeAt && (body.footPosition - probeOrigin).sqrMagnitude <= 1.5f
                && Vector3.Dot(preferred, probeHeading) > .83f) return safeHeading.sqrMagnitude > .1f ? safeHeading : fallback;

            probeAt = now + .14f;
            probeOrigin = body.footPosition;
            probeHeading = preferred;
            if (ArenaBotLocomotion.Safe(body, preferred, hill)) safeHeading = preferred;
            else
            {
                Vector3 narrower = (toward * (closing ? 1f : radial) - tangent * (closing ? .35f : .72f)).normalized;
                if (ArenaBotLocomotion.Safe(body, narrower, hill)) safeHeading = narrower;
                else if (ArenaBotLocomotion.Safe(body, toward, hill)) safeHeading = toward;
                else if (!closing && ArenaBotLocomotion.Safe(body, (-toward + tangent * .65f).normalized, hill))
                    safeHeading = (-toward + tangent * .65f).normalized;
                else safeHeading = Vector3.zero;
            }
            return safeHeading.sqrMagnitude > .1f ? safeHeading : fallback;
        }
    }
}
