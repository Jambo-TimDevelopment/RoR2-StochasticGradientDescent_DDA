using GeneticsArtifact.CheatManager;
using RoR2;
using UnityEngine;

namespace GeneticsArtifact.SgdEngine
{
    /// <summary>
    /// Hooks for collecting mandatory sensor signals.
    /// </summary>
    public static class SgdSensorsHooks
    {
        private static SgdSensorsEstimator _estimator = new SgdSensorsEstimator();
        private static CharacterBody _trackedPlayerBody;

        public static void RegisterHooks()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            On.RoR2.CharacterBody.OnDestroy += CharacterBody_OnDestroy;
        }

        public static void Reset(CharacterBody newPlayerBody)
        {
            _trackedPlayerBody = newPlayerBody;
            _estimator.Reset();
            SgdSensorsRuntimeState.Clear();
        }

        public static void Tick(CharacterBody playerBody, float dt, in SgdVirtualPowerSample vp)
        {
            if (DdaAlgorithmState.ActiveAlgorithm != DdaAlgorithmType.Sgd && !DdaAlgorithmState.IsDebugOverlayEnabled)
            {
                return;
            }

            if (playerBody == null)
            {
                Reset(null);
                return;
            }

            if (_trackedPlayerBody != playerBody)
            {
                Reset(playerBody);
            }

            _estimator.TickPlayerBody(playerBody, dt);
            SgdSensorsRuntimeState.Set(_estimator.GetCurrentSample(vp), playerBody);
        }

        private static void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            orig(self, damageInfo);

            if (DdaAlgorithmState.ActiveAlgorithm != DdaAlgorithmType.Sgd && !DdaAlgorithmState.IsDebugOverlayEnabled)
            {
                return;
            }

            if (self == null || damageInfo.damage <= 0f) return;

            var victimBody = self.body;
            if (victimBody == null) return;

            // Incoming damage: victim is tracked player.
            if (_trackedPlayerBody != null && victimBody == _trackedPlayerBody)
            {
                _estimator.ObserveIncomingDamage(_trackedPlayerBody, damageInfo.damage, Time.deltaTime);
                return;
            }

            // Outgoing damage: attacker is tracked player, victim is monster.
            if (_trackedPlayerBody != null && damageInfo.attacker is GameObject attackerObj)
            {
                var attackerBody = attackerObj.GetComponent<CharacterBody>();
                if (attackerBody != null && attackerBody == _trackedPlayerBody)
                {
                    if (victimBody.teamComponent != null && victimBody.teamComponent.teamIndex == TeamIndex.Monster)
                    {
                        _estimator.ObserveOutgoingDamage(_trackedPlayerBody, victimBody, damageInfo.damage, Time.deltaTime);
                    }
                }
            }
        }

        private static void CharacterBody_OnDestroy(On.RoR2.CharacterBody.orig_OnDestroy orig, CharacterBody self)
        {
            // Detect monster death for TTK and player death for DeathsPerWindow.
            try
            {
                if (DdaAlgorithmState.ActiveAlgorithm == DdaAlgorithmType.Sgd || DdaAlgorithmState.IsDebugOverlayEnabled)
                {
                    if (self != null)
                    {
                        if (_trackedPlayerBody != null && self == _trackedPlayerBody)
                        {
                            // Player body destroyed. This is a proxy for "death" (may include some edge cases).
                            _estimator.ObservePlayerDeath();
                        }
                        else if (self.teamComponent != null && self.teamComponent.teamIndex == TeamIndex.Monster)
                        {
                            _estimator.ObserveMonsterDeath(self);
                        }
                    }
                }
            }
            finally
            {
                orig(self);
            }
        }
    }
}

