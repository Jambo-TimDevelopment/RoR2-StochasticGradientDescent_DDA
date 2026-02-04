using RoR2;
using UnityEngine;

namespace GeneticsArtifact.SgdEngine.Actuators
{
    /// <summary>
    /// Applies GeneStat multipliers by adjusting GeneToken items in a monster inventory.
    /// This reuses the existing GeneTokenCalc pipeline (RecalculateStatsAPI).
    /// </summary>
    public static class SgdGeneStatTokenApplier
    {
        /// <summary>
        /// Sets the exact gene multiplier for a given stat on an inventory.
        /// Idempotent: running it multiple times yields the same item counts.
        /// </summary>
        public static void ApplyMultiplier(Inventory inventory, GeneStat stat, float multiplier)
        {
            if (inventory == null) return;
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier <= 0f) return;

            // Clamp defensively using existing mutation limits.
            float floor = ConfigManager.geneFloor?.Value ?? 0.01f;
            float cap = ConfigManager.geneCap?.Value ?? 10f;
            if (cap < floor)
            {
                (floor, cap) = (cap, floor);
            }

            multiplier = Mathf.Clamp(multiplier, floor, cap);

            // 1 token = +/-1% change.
            int netTokens = Mathf.RoundToInt((multiplier - 1f) * 100f);
            int desiredPlus = netTokens > 0 ? netTokens : 0;
            int desiredMinus = netTokens < 0 ? -netTokens : 0;

            var plusDef = GeneTokens.tokenDict[stat][GeneMod.Plus1];
            var minusDef = GeneTokens.tokenDict[stat][GeneMod.Minus1];

            SetExactCount(inventory, plusDef, desiredPlus);
            SetExactCount(inventory, minusDef, desiredMinus);
        }

        private static void SetExactCount(Inventory inventory, ItemDef itemDef, int desiredCount)
        {
            if (inventory == null || itemDef == null) return;
            desiredCount = Mathf.Max(0, desiredCount);

            int current = inventory.GetItemCount(itemDef);
            if (current == desiredCount) return;

            if (current > 0)
            {
                inventory.RemoveItem(itemDef, current);
            }

            if (desiredCount > 0)
            {
                inventory.GiveItem(itemDef, desiredCount);
            }
        }
    }
}

