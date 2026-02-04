using UnityEngine;

namespace GeneticsArtifact.SgdEngine.Actuators
{
    /// <summary>
    /// Runtime state for SGD actuators (difficulty parameters θ).
    /// MVP: GeneStat multipliers (HP/MS/AS/DMG).
    /// </summary>
    public static class SgdActuatorsRuntimeState
    {
        public static float MaxHealthMultiplier { get; private set; } = 1f;
        public static float MoveSpeedMultiplier { get; private set; } = 1f;
        public static float AttackSpeedMultiplier { get; private set; } = 1f;
        public static float AttackDamageMultiplier { get; private set; } = 1f;

        public static void Reset()
        {
            MaxHealthMultiplier = 1f;
            MoveSpeedMultiplier = 1f;
            AttackSpeedMultiplier = 1f;
            AttackDamageMultiplier = 1f;
        }

        public static void SetMaxHealthMultiplier(float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
            {
                return;
            }

            MaxHealthMultiplier = ClampToGeneLimits(multiplier);
        }

        public static void SetMoveSpeedMultiplier(float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
            {
                return;
            }

            MoveSpeedMultiplier = ClampToGeneLimits(multiplier);
        }

        public static void SetAttackSpeedMultiplier(float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
            {
                return;
            }

            AttackSpeedMultiplier = ClampToGeneLimits(multiplier);
        }

        public static void SetAttackDamageMultiplier(float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
            {
                return;
            }

            AttackDamageMultiplier = ClampToGeneLimits(multiplier);
        }

        private static float ClampToGeneLimits(float multiplier)
        {
            float floor = ConfigManager.geneFloor?.Value ?? 0.01f;
            float cap = ConfigManager.geneCap?.Value ?? 10f;
            if (cap < floor)
            {
                (floor, cap) = (cap, floor);
            }

            return Mathf.Clamp(multiplier, floor, cap);
        }
    }
}

