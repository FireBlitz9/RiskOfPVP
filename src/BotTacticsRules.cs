using System;

namespace ArenaPvP
{
    public enum BotIntelligence { Low, Medium, High, ExtraHigh, Ultra }

    public static class BotTacticsRules
    {
        private static readonly string[] names = { "Low", "Medium", "High", "Extra High", "Ultra" };
        private static readonly float[] sight = { 100f, 140f, 180f, 200f, 220f }, damping = { .35f, .18f, .11f, .065f, .035f },
            aimSpeed = { 6f, 10f, 16f, 24f, 32f }, aimDot = { .82f, .9f, .94f, .97f, .985f };
        public static BotIntelligence Tier(int value) { return (BotIntelligence)Math.Max(0, Math.Min(4, value)); }
        public static string Name(int value)
        { return names[(int)Tier(value)]; }
        public static bool Adaptive(BotIntelligence tier) { return tier >= BotIntelligence.ExtraHigh; }
        public static bool ChaseHumans(bool enabled, ArenaMode mode) { return enabled && mode == ArenaMode.FFA; }
        public static float SightRange(BotIntelligence tier) { return sight[(int)tier]; }
        public static float AimDamping(BotIntelligence tier) { return damping[(int)tier]; }
        public static float AimSpeed(BotIntelligence tier) { return aimSpeed[(int)tier]; }
        public static float AimDot(BotIntelligence tier) { return aimDot[(int)tier]; }
        public static float TargetScore(BotIntelligence tier, float distance, float health, bool current, bool hill, bool protectedTarget, float threat)
        {
            if (protectedTarget) return float.NegativeInfinity;
            if (tier == BotIntelligence.Low || tier == BotIntelligence.High) return -distance;
            if (tier == BotIntelligence.Medium) return BotBuildRules.TargetScore(distance, health, current, hill, false);
            return 150 - distance * (tier == BotIntelligence.Ultra ? .85f : 1.2f) + (1 - health) * 35
                + (current ? 5 : 0) + (hill ? 35 : 0) + (tier == BotIntelligence.Ultra ? Math.Min(25, threat * 8) : 0);
        }
        public static bool CanFire(bool alive, bool hostile, bool visible, bool protectedTarget, float aimDot, BotIntelligence tier)
        { return alive && hostile && visible && !protectedTarget && aimDot >= AimDot(tier); }
        public static bool HeldSkill(bool requested, bool previous, bool offensive, bool canFire)
        { return requested && (!offensive || previous || canFire); }
        public static void ObjectiveOffset(float x,float z,float radius,out float safeX,out float safeZ)
        {
            float length=(float)Math.Sqrt(x*x+z*z), maximum=Math.Max(0,radius)*.55f;
            float scale=length>maximum && length>0 ? maximum/length : 1;
            safeX=x*scale;safeZ=z*scale;
        }
        public static float StoppingDistance(float speed, float acceleration, float radius)
        { return Math.Max(1.5f, Math.Min(12f, speed * .25f + speed * speed / (2 * Math.Max(10, acceleration)) + radius)); }
        public static void PressureVector(float travelX,float travelZ,float sideX,float sideZ,out float x,out float z)
        {
            float travel=(float)Math.Sqrt(travelX*travelX+travelZ*travelZ),side=(float)Math.Sqrt(sideX*sideX+sideZ*sideZ);
            x=(travel>0 ? travelX/travel*.9f : 0)+(side>0 ? sideX/side*.65f : 0);
            z=(travel>0 ? travelZ/travel*.9f : 0)+(side>0 ? sideZ/side*.65f : 0);
            float length=(float)Math.Sqrt(x*x+z*z);if(length>0) { x/=length;z/=length; }
        }
    }
}
