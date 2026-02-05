using GeneticsArtifact.CheatManager;
using GeneticsArtifact.SgdEngine.Actuators;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace GeneticsArtifact.SgdEngine.Decision
{
    /// <summary>
    /// Decision module for the SGD-based DDA.
    /// MVP: one axis only (Enemy AttackSpeed), updated once per StepSeconds of combat time.
    /// </summary>
    public static class SgdDecisionDriver
    {
        // --- Hyperparameters (MVP) ---
        private const float AttackSpeedLearningRate = 0.25f;
        private const float AttackSpeedMomentum = 0.65f;
        private const float AttackSpeedGradientClip = 0.50f;
        private const float AttackSpeedVelocityClip = 1.00f;
        private const float AttackSpeedMaxDeltaTheta = 0.075f; // ~7.8% multiplier step max
        private const float AttackSpeedErrorDeadZone = 0.03f;
        private const float AttackSpeedExternalSyncEpsilon = 0.001f;

        public static void Tick(CharacterBody playerBody, float dt)
        {
            if (!NetworkServer.active) return;
            if (DdaAlgorithmState.ActiveAlgorithm != DdaAlgorithmType.Sgd) return;

            if (playerBody == null) return;
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;

            EnsureAttackSpeedStateSynced();

            if (!SgdDecisionRuntimeState.IsAttackSpeedAdaptationEnabled)
            {
                return;
            }

            // Only learn while in combat (easier to reason about signals while debugging).
            if (playerBody.outOfCombat)
            {
                return;
            }

            if (!SgdSensorsRuntimeState.HasSample)
            {
                return;
            }

            SgdDecisionRuntimeState.AddCombatSeconds(dt);
            int dueSteps = SgdDecisionRuntimeState.ConsumeDueSteps();
            if (dueSteps <= 0) return;

            // In normal gameplay dt is small, so dueSteps is expected to be 1.
            // Still, handle large dt gracefully.
            for (int i = 0; i < dueSteps; i++)
            {
                StepAttackSpeed(SgdSensorsRuntimeState.Sample);
            }
        }

        private static void EnsureAttackSpeedStateSynced()
        {
            GetGeneLimits(out float floor, out float cap);
            float thetaMin = Mathf.Log(floor);
            float thetaMax = Mathf.Log(cap);

            float currentMultiplier = Mathf.Max(0.0001f, SgdActuatorsRuntimeState.AttackSpeedMultiplier);

            if (!SgdDecisionRuntimeState.HasAttackSpeedState)
            {
                float theta0 = Mathf.Clamp(Mathf.Log(currentMultiplier), thetaMin, thetaMax);
                float mult0 = Mathf.Exp(theta0);
                SgdDecisionRuntimeState.SyncAttackSpeedTheta(theta0, velocity: 0f, multiplier: mult0);
                return;
            }

            // If user adjusts the actuator via console commands, re-sync theta and clear momentum.
            float expectedMultiplier = Mathf.Exp(SgdDecisionRuntimeState.AttackSpeedTheta);
            if (Mathf.Abs(expectedMultiplier - currentMultiplier) > AttackSpeedExternalSyncEpsilon)
            {
                float theta = Mathf.Clamp(Mathf.Log(currentMultiplier), thetaMin, thetaMax);
                float mult = Mathf.Exp(theta);
                SgdDecisionRuntimeState.SyncAttackSpeedTheta(theta, velocity: 0f, multiplier: mult);
            }
        }

        private static void StepAttackSpeed(in SgdSensorsSample sensors)
        {
            GetGeneLimits(out float floor, out float cap);

            // Convert current parameter to normalized challenge in [0..1] using log-space.
            float thetaMin = Mathf.Log(floor);
            float thetaMax = Mathf.Log(cap);
            float thetaRange = Mathf.Max(0.0001f, thetaMax - thetaMin);

            float theta = Mathf.Clamp(SgdDecisionRuntimeState.AttackSpeedTheta, thetaMin, thetaMax);
            float velocity = SgdDecisionRuntimeState.AttackSpeedVelocity;

            float challenge01 = Mathf.Clamp01((theta - thetaMin) / thetaRange);
            float skill01 = EstimateAttackSpeedSkill01(sensors);

            float error = challenge01 - skill01; // >0 => too hard => decrease theta
            if (Mathf.Abs(error) < AttackSpeedErrorDeadZone) error = 0f;

            float dChallenge_dTheta = 1f / thetaRange;
            float gradient = 2f * error * dChallenge_dTheta;
            gradient = Mathf.Clamp(gradient, -AttackSpeedGradientClip, AttackSpeedGradientClip);

            // Momentum SGD.
            velocity = (AttackSpeedMomentum * velocity) + gradient;
            velocity = Mathf.Clamp(velocity, -AttackSpeedVelocityClip, AttackSpeedVelocityClip);

            float deltaTheta = AttackSpeedLearningRate * velocity;
            deltaTheta = Mathf.Clamp(deltaTheta, -AttackSpeedMaxDeltaTheta, AttackSpeedMaxDeltaTheta);

            float newTheta = Mathf.Clamp(theta - deltaTheta, thetaMin, thetaMax);
            float newMultiplier = Mathf.Exp(newTheta);

            float beforeMultiplier = SgdActuatorsRuntimeState.AttackSpeedMultiplier;
            SgdActuatorsRuntimeState.SetAttackSpeedMultiplier(newMultiplier);
            float afterMultiplier = SgdActuatorsRuntimeState.AttackSpeedMultiplier;

            // Apply only if it actually changed.
            int applied = 0;
            if (Mathf.Abs(afterMultiplier - beforeMultiplier) > 0.0005f)
            {
                applied = SgdActuatorsApplier.ApplyToAllLivingMonsters();
            }

            // Persist state and telemetry for debug overlay.
            SgdDecisionRuntimeState.SyncAttackSpeedTheta(newTheta, velocity, afterMultiplier);
            SgdDecisionRuntimeState.RecordAttackSpeedStep(
                skill01: skill01,
                challenge01: challenge01,
                error: error,
                gradient: gradient,
                learningRate: AttackSpeedLearningRate,
                deltaTheta: deltaTheta,
                newMultiplier: afterMultiplier,
                appliedMonsters: applied);
        }

        private static float EstimateAttackSpeedSkill01(in SgdSensorsSample s)
        {
            // AttackSpeed axis primarily affects how often enemies can pressure the player.
            // Use "stress" sensors (hit rate / incoming DPS / low HP) to infer relative skill.
            float evasion = 1f - Mathf.Clamp01(s.HitRateOnPlayerNorm01);
            float survivability = 1f - Mathf.Clamp01(s.IncomingDamageNorm01);
            float safety = 1f - Mathf.Clamp01(s.LowHealthUptime);
            float deaths = 1f - Mathf.Clamp01(s.DeathsPerWindowNorm01);

            float skill01 =
                (0.40f * evasion) +
                (0.35f * survivability) +
                (0.20f * safety) +
                (0.05f * deaths);

            if (float.IsNaN(skill01) || float.IsInfinity(skill01)) return 0f;
            return Mathf.Clamp01(skill01);
        }

        private static void GetGeneLimits(out float floor, out float cap)
        {
            floor = ConfigManager.geneFloor?.Value ?? 0.01f;
            cap = ConfigManager.geneCap?.Value ?? 10f;
            if (cap < floor)
            {
                (floor, cap) = (cap, floor);
            }

            // Avoid log(0) and other numeric weirdness.
            floor = Mathf.Max(0.0001f, floor);
            cap = Mathf.Max(floor, cap);
        }
    }
}

