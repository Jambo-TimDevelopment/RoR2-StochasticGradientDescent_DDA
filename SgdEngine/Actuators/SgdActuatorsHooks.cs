using GeneticsArtifact.CheatManager;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace GeneticsArtifact.SgdEngine.Actuators
{
    /// <summary>
    /// Applies current SGD actuator parameters to newly spawned monsters.
    /// Scope: new spawns only (CharacterBody.Start).
    /// </summary>
    public static class SgdActuatorsHooks
    {
        public static void RegisterHooks()
        {
            On.RoR2.CharacterBody.Start += CharacterBody_Start;
        }

        private static void CharacterBody_Start(On.RoR2.CharacterBody.orig_Start orig, CharacterBody self)
        {
            orig(self);

            if (!NetworkServer.active) return;
            if (self == null) return;

            if (DdaAlgorithmState.ActiveAlgorithm != DdaAlgorithmType.Sgd)
            {
                return;
            }

            if (RunArtifactManager.instance == null || !RunArtifactManager.instance.IsArtifactEnabled(ArtifactOfGenetics.artifactDef))
            {
                return;
            }

            if (self.teamComponent == null || self.teamComponent.teamIndex != TeamIndex.Monster)
            {
                return;
            }

            if (self.inventory == null)
            {
                return;
            }

            // MVP: apply only MaxHealth multiplier.
            SgdGeneStatTokenApplier.ApplyMultiplier(self.inventory, GeneStat.MaxHealth, SgdActuatorsRuntimeState.MaxHealthMultiplier);
            self.RecalculateStats();
        }
    }
}

