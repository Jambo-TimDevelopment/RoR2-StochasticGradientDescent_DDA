using UnityEngine;

namespace GeneticsArtifact.SgdEngine
{
    /// <summary>
    /// Centralized source of per-axis SGD multiplier limits.
    /// Keeps decision and actuator clamping consistent.
    /// </summary>
    internal static class SgdAxisLimitProvider
    {
        private const float FallbackHpFloor = 0.50f;
        private const float FallbackHpCap = 2.00f;
        private const float FallbackMoveSpeedFloor = 0.80f;
        private const float FallbackMoveSpeedCap = 1.25f;
        private const float FallbackAttackSpeedFloor = 0.60f;
        private const float FallbackAttackSpeedCap = 1.6667f;
        private const float FallbackAttackDamageFloor = 0.60f;
        private const float FallbackAttackDamageCap = 1.6667f;
        private const float AbsoluteMinFloor = 0.0001f;

        public static void GetMaxHealthLimits(out float floor, out float cap)
        {
            NormalizeLimits(
                ConfigManager.sgdHpFloor?.Value ?? FallbackHpFloor,
                ConfigManager.sgdHpCap?.Value ?? FallbackHpCap,
                out floor,
                out cap);
        }

        public static void GetMoveSpeedLimits(out float floor, out float cap)
        {
            NormalizeLimits(
                ConfigManager.sgdMsFloor?.Value ?? FallbackMoveSpeedFloor,
                ConfigManager.sgdMsCap?.Value ?? FallbackMoveSpeedCap,
                out floor,
                out cap);
        }

        public static void GetAttackSpeedLimits(out float floor, out float cap)
        {
            NormalizeLimits(
                ConfigManager.sgdAsFloor?.Value ?? FallbackAttackSpeedFloor,
                ConfigManager.sgdAsCap?.Value ?? FallbackAttackSpeedCap,
                out floor,
                out cap);
        }

        public static void GetAttackDamageLimits(out float floor, out float cap)
        {
            NormalizeLimits(
                ConfigManager.sgdDmgFloor?.Value ?? FallbackAttackDamageFloor,
                ConfigManager.sgdDmgCap?.Value ?? FallbackAttackDamageCap,
                out floor,
                out cap);
        }

        public static void GetLimits(GeneStat stat, out float floor, out float cap)
        {
            switch (stat)
            {
                case GeneStat.MaxHealth:
                    GetMaxHealthLimits(out floor, out cap);
                    return;
                case GeneStat.MoveSpeed:
                    GetMoveSpeedLimits(out floor, out cap);
                    return;
                case GeneStat.AttackSpeed:
                    GetAttackSpeedLimits(out floor, out cap);
                    return;
                case GeneStat.AttackDamage:
                    GetAttackDamageLimits(out floor, out cap);
                    return;
                default:
                    NormalizeLimits(FallbackHpFloor, FallbackHpCap, out floor, out cap);
                    return;
            }
        }

        public static float Clamp(GeneStat stat, float value)
        {
            GetLimits(stat, out float floor, out float cap);
            return Mathf.Clamp(value, floor, cap);
        }

        private static void NormalizeLimits(float rawFloor, float rawCap, out float floor, out float cap)
        {
            floor = rawFloor;
            cap = rawCap;

            if (cap < floor)
            {
                (floor, cap) = (cap, floor);
            }

            floor = Mathf.Max(AbsoluteMinFloor, floor);
            cap = Mathf.Max(floor, cap);
        }
    }
}
