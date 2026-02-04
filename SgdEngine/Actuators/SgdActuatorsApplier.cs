using GeneticsArtifact.CheatManager;
using RoR2;
using UnityEngine.Networking;

namespace GeneticsArtifact.SgdEngine.Actuators
{
    /// <summary>
    /// Utility to apply current actuator parameters to all existing monsters.
    /// </summary>
    public static class SgdActuatorsApplier
    {
        public static int ApplyToAllLivingMonsters()
        {
            if (!NetworkServer.active)
            {
                return 0;
            }

            if (DdaAlgorithmState.ActiveAlgorithm != DdaAlgorithmType.Sgd)
            {
                return 0;
            }

            int applied = 0;
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                if (body == null) continue;
                if (body.teamComponent == null || body.teamComponent.teamIndex != TeamIndex.Monster) continue;
                if (body.inventory == null) continue;

                SgdGeneStatTokenApplier.ApplyMultiplier(body.inventory, GeneStat.MaxHealth, SgdActuatorsRuntimeState.MaxHealthMultiplier);
                body.RecalculateStats();
                applied++;
            }

            return applied;
        }
    }
}

