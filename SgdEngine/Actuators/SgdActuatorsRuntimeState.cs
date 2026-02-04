using UnityEngine;

namespace GeneticsArtifact.SgdEngine.Actuators
{
    /// <summary>
    /// Runtime state for SGD actuators (difficulty parameters θ).
    /// MVP: only GeneStat.MaxHealth multiplier is supported.
    /// </summary>
    public static class SgdActuatorsRuntimeState
    {
        public static float MaxHealthMultiplier { get; private set; } = 1f;

        public static void Reset()
        {
            MaxHealthMultiplier = 1f;
        }

        public static void SetMaxHealthMultiplier(float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
            {
                return;
            }

            float floor = ConfigManager.geneFloor?.Value ?? 0.01f;
            float cap = ConfigManager.geneCap?.Value ?? 10f;
            if (cap < floor)
            {
                (floor, cap) = (cap, floor);
            }

            MaxHealthMultiplier = Mathf.Clamp(multiplier, floor, cap);
        }
    }
}

