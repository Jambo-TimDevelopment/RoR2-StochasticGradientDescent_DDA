using GeneticsArtifact.CheatManager;
using GeneticsArtifact.SgdEngine.Actuators;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace GeneticsArtifact.SgdEngine.Decision
{
    /// <summary>
    /// Decision module for the SGD-based DDA.
    /// Each axis is updated once per StepSeconds of combat time.
    /// </summary>
    public static class SgdDecisionDriver
    {
        // --- Hyperparameters ---
        private const float DefaultMomentum = 0.65f;
        private const float DefaultGradientClip = 0.50f;
        private const float DefaultVelocityClip = 1.00f;
        private const float DefaultErrorDeadZone = 0.03f;
        private const float ExternalSyncEpsilon = 0.001f;

        // Per-axis learning rates and step caps.
        private const float HpLearningRate = 0.22f;
        private const float HpMaxDeltaTheta = 0.060f; // ~6.2% multiplier step max

        private const float MsLearningRate = 0.20f;
        private const float MsMaxDeltaTheta = 0.050f; // ~5.1% multiplier step max

        private const float AsLearningRate = 0.25f;
        private const float AsMaxDeltaTheta = 0.075f; // ~7.8% multiplier step max

        private const float DmgLearningRate = 0.18f;
        private const float DmgMaxDeltaTheta = 0.050f; // ~5.1% multiplier step max

        private const float AxisApplyEpsilon = 0.0005f;

        public static void Tick(CharacterBody playerBody, float dt)
        {
            if (!NetworkServer.active) return;
            if (DdaAlgorithmState.ActiveAlgorithm != DdaAlgorithmType.Sgd) return;

            if (playerBody == null) return;
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;

            EnsureAxisStatesSynced();

            bool anyAxisEnabled =
                SgdDecisionRuntimeState.IsMaxHealthAdaptationEnabled ||
                SgdDecisionRuntimeState.IsMoveSpeedAdaptationEnabled ||
                SgdDecisionRuntimeState.IsAttackSpeedAdaptationEnabled ||
                SgdDecisionRuntimeState.IsAttackDamageAdaptationEnabled;

            if (!anyAxisEnabled) return;

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
                StepAllAxes(SgdSensorsRuntimeState.Sample);
            }
        }

        private static void EnsureAxisStatesSynced()
        {
            EnsureMaxHealthStateSynced();
            EnsureMoveSpeedStateSynced();
            EnsureAttackSpeedStateSynced();
            EnsureAttackDamageStateSynced();
        }

        private static void EnsureMaxHealthStateSynced()
        {
            GetGeneLimits(out float floor, out float cap);
            float thetaMin = Mathf.Log(floor);
            float thetaMax = Mathf.Log(cap);

            float currentMultiplier = Mathf.Max(0.0001f, SgdActuatorsRuntimeState.MaxHealthMultiplier);

            if (!SgdDecisionRuntimeState.HasMaxHealthState)
            {
                float theta0 = Mathf.Clamp(Mathf.Log(currentMultiplier), thetaMin, thetaMax);
                float mult0 = Mathf.Exp(theta0);
                SgdDecisionRuntimeState.SyncMaxHealthTheta(theta0, velocity: 0f, multiplier: mult0);
                return;
            }

            float expectedMultiplier = Mathf.Exp(SgdDecisionRuntimeState.MaxHealthTheta);
            if (Mathf.Abs(expectedMultiplier - currentMultiplier) > ExternalSyncEpsilon)
            {
                float theta = Mathf.Clamp(Mathf.Log(currentMultiplier), thetaMin, thetaMax);
                float mult = Mathf.Exp(theta);
                SgdDecisionRuntimeState.SyncMaxHealthTheta(theta, velocity: 0f, multiplier: mult);
            }
        }

        private static void EnsureMoveSpeedStateSynced()
        {
            GetGeneLimits(out float floor, out float cap);
            float thetaMin = Mathf.Log(floor);
            float thetaMax = Mathf.Log(cap);

            float currentMultiplier = Mathf.Max(0.0001f, SgdActuatorsRuntimeState.MoveSpeedMultiplier);

            if (!SgdDecisionRuntimeState.HasMoveSpeedState)
            {
                float theta0 = Mathf.Clamp(Mathf.Log(currentMultiplier), thetaMin, thetaMax);
                float mult0 = Mathf.Exp(theta0);
                SgdDecisionRuntimeState.SyncMoveSpeedTheta(theta0, velocity: 0f, multiplier: mult0);
                return;
            }

            float expectedMultiplier = Mathf.Exp(SgdDecisionRuntimeState.MoveSpeedTheta);
            if (Mathf.Abs(expectedMultiplier - currentMultiplier) > ExternalSyncEpsilon)
            {
                float theta = Mathf.Clamp(Mathf.Log(currentMultiplier), thetaMin, thetaMax);
                float mult = Mathf.Exp(theta);
                SgdDecisionRuntimeState.SyncMoveSpeedTheta(theta, velocity: 0f, multiplier: mult);
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
            if (Mathf.Abs(expectedMultiplier - currentMultiplier) > ExternalSyncEpsilon)
            {
                float theta = Mathf.Clamp(Mathf.Log(currentMultiplier), thetaMin, thetaMax);
                float mult = Mathf.Exp(theta);
                SgdDecisionRuntimeState.SyncAttackSpeedTheta(theta, velocity: 0f, multiplier: mult);
            }
        }

        private static void EnsureAttackDamageStateSynced()
        {
            GetGeneLimits(out float floor, out float cap);
            float thetaMin = Mathf.Log(floor);
            float thetaMax = Mathf.Log(cap);

            float currentMultiplier = Mathf.Max(0.0001f, SgdActuatorsRuntimeState.AttackDamageMultiplier);

            if (!SgdDecisionRuntimeState.HasAttackDamageState)
            {
                float theta0 = Mathf.Clamp(Mathf.Log(currentMultiplier), thetaMin, thetaMax);
                float mult0 = Mathf.Exp(theta0);
                SgdDecisionRuntimeState.SyncAttackDamageTheta(theta0, velocity: 0f, multiplier: mult0);
                return;
            }

            float expectedMultiplier = Mathf.Exp(SgdDecisionRuntimeState.AttackDamageTheta);
            if (Mathf.Abs(expectedMultiplier - currentMultiplier) > ExternalSyncEpsilon)
            {
                float theta = Mathf.Clamp(Mathf.Log(currentMultiplier), thetaMin, thetaMax);
                float mult = Mathf.Exp(theta);
                SgdDecisionRuntimeState.SyncAttackDamageTheta(theta, velocity: 0f, multiplier: mult);
            }
        }

        private static void StepAllAxes(in SgdSensorsSample sensors)
        {
            GetGeneLimits(out float floor, out float cap);

            // Convert current parameter to normalized challenge in [0..1] using log-space.
            float thetaMin = Mathf.Log(floor);
            float thetaMax = Mathf.Log(cap);
            float thetaRange = Mathf.Max(0.0001f, thetaMax - thetaMin);

            bool changed = false;

            if (SgdDecisionRuntimeState.IsMaxHealthAdaptationEnabled)
            {
                changed |= StepMaxHealth(sensors, thetaMin, thetaMax, thetaRange);
            }
            if (SgdDecisionRuntimeState.IsMoveSpeedAdaptationEnabled)
            {
                changed |= StepMoveSpeed(sensors, thetaMin, thetaMax, thetaRange);
            }
            if (SgdDecisionRuntimeState.IsAttackSpeedAdaptationEnabled)
            {
                changed |= StepAttackSpeed(sensors, thetaMin, thetaMax, thetaRange);
            }
            if (SgdDecisionRuntimeState.IsAttackDamageAdaptationEnabled)
            {
                changed |= StepAttackDamage(sensors, thetaMin, thetaMax, thetaRange);
            }

            int applied = changed ? SgdActuatorsApplier.ApplyToAllLivingMonsters() : 0;
            SgdDecisionRuntimeState.RecordGlobalStep(appliedMonsters: applied);
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

        private static float EstimateMaxHealthSkill01(in SgdSensorsSample s)
        {
            // MaxHealth axis is mostly about "kill efficiency" (how fast the player clears enemies).
            // Use outgoing efficiency + TTK proxy; keep it robust when TTK is not yet observed.
            float outgoing = Mathf.Clamp01(s.OutgoingDamageNorm01);
            float ttkSkill = s.AvgTtkSeconds > 0.01f ? (1f - Mathf.Clamp01(s.AvgTtkSecondsNorm01)) : 0.50f;
            float safety = 1f - Mathf.Clamp01(s.LowHealthUptime);

            float skill01 =
                (0.45f * outgoing) +
                (0.45f * ttkSkill) +
                (0.10f * safety);

            if (float.IsNaN(skill01) || float.IsInfinity(skill01)) return 0f;
            return Mathf.Clamp01(skill01);
        }

        private static float EstimateMoveSpeedSkill01(in SgdSensorsSample s)
        {
            // MoveSpeed axis pressures positioning/kiting. Use evasion + survivability,
            // with a small contribution from outgoing efficiency (aim while moving).
            float evasion = 1f - Mathf.Clamp01(s.HitRateOnPlayerNorm01);
            float survivability = 1f - Mathf.Clamp01(s.IncomingDamageNorm01);
            float outgoing = Mathf.Clamp01(s.OutgoingDamageNorm01);
            float safety = 1f - Mathf.Clamp01(s.LowHealthUptime);

            float skill01 =
                (0.45f * evasion) +
                (0.25f * survivability) +
                (0.20f * outgoing) +
                (0.10f * safety);

            if (float.IsNaN(skill01) || float.IsInfinity(skill01)) return 0f;
            return Mathf.Clamp01(skill01);
        }

        private static float EstimateAttackDamageSkill01(in SgdSensorsSample s)
        {
            // AttackDamage axis is primarily about survivability under pressure.
            float survivability = 1f - Mathf.Clamp01(s.IncomingDamageNorm01);
            float safety = 1f - Mathf.Clamp01(s.LowHealthUptime);
            float deaths = 1f - Mathf.Clamp01(s.DeathsPerWindowNorm01);
            float evasion = 1f - Mathf.Clamp01(s.HitRateOnPlayerNorm01);

            float skill01 =
                (0.45f * survivability) +
                (0.30f * safety) +
                (0.20f * deaths) +
                (0.05f * evasion);

            if (float.IsNaN(skill01) || float.IsInfinity(skill01)) return 0f;
            return Mathf.Clamp01(skill01);
        }

        private static bool StepMaxHealth(in SgdSensorsSample sensors, float thetaMin, float thetaMax, float thetaRange)
        {
            float theta = Mathf.Clamp(SgdDecisionRuntimeState.MaxHealthTheta, thetaMin, thetaMax);
            float velocity = SgdDecisionRuntimeState.MaxHealthVelocity;

            float challenge01 = Mathf.Clamp01((theta - thetaMin) / thetaRange);
            float skill01 = EstimateMaxHealthSkill01(sensors);

            float error = challenge01 - skill01; // >0 => too hard => decrease theta
            if (Mathf.Abs(error) < DefaultErrorDeadZone) error = 0f;

            float gradient = 2f * error * (1f / thetaRange);
            gradient = Mathf.Clamp(gradient, -DefaultGradientClip, DefaultGradientClip);

            velocity = (DefaultMomentum * velocity) + gradient;
            velocity = Mathf.Clamp(velocity, -DefaultVelocityClip, DefaultVelocityClip);

            float deltaTheta = HpLearningRate * velocity;
            deltaTheta = Mathf.Clamp(deltaTheta, -HpMaxDeltaTheta, HpMaxDeltaTheta);

            float newTheta = Mathf.Clamp(theta - deltaTheta, thetaMin, thetaMax);
            float newMultiplier = Mathf.Exp(newTheta);

            float before = SgdActuatorsRuntimeState.MaxHealthMultiplier;
            SgdActuatorsRuntimeState.SetMaxHealthMultiplier(newMultiplier);
            float after = SgdActuatorsRuntimeState.MaxHealthMultiplier;

            SgdDecisionRuntimeState.SyncMaxHealthTheta(newTheta, velocity, after);
            SgdDecisionRuntimeState.RecordMaxHealthStep(
                skill01: skill01,
                challenge01: challenge01,
                error: error,
                gradient: gradient,
                learningRate: HpLearningRate,
                deltaTheta: deltaTheta,
                newMultiplier: after);

            return Mathf.Abs(after - before) > AxisApplyEpsilon;
        }

        private static bool StepMoveSpeed(in SgdSensorsSample sensors, float thetaMin, float thetaMax, float thetaRange)
        {
            float theta = Mathf.Clamp(SgdDecisionRuntimeState.MoveSpeedTheta, thetaMin, thetaMax);
            float velocity = SgdDecisionRuntimeState.MoveSpeedVelocity;

            float challenge01 = Mathf.Clamp01((theta - thetaMin) / thetaRange);
            float skill01 = EstimateMoveSpeedSkill01(sensors);

            float error = challenge01 - skill01; // >0 => too hard => decrease theta
            if (Mathf.Abs(error) < DefaultErrorDeadZone) error = 0f;

            float gradient = 2f * error * (1f / thetaRange);
            gradient = Mathf.Clamp(gradient, -DefaultGradientClip, DefaultGradientClip);

            velocity = (DefaultMomentum * velocity) + gradient;
            velocity = Mathf.Clamp(velocity, -DefaultVelocityClip, DefaultVelocityClip);

            float deltaTheta = MsLearningRate * velocity;
            deltaTheta = Mathf.Clamp(deltaTheta, -MsMaxDeltaTheta, MsMaxDeltaTheta);

            float newTheta = Mathf.Clamp(theta - deltaTheta, thetaMin, thetaMax);
            float newMultiplier = Mathf.Exp(newTheta);

            float before = SgdActuatorsRuntimeState.MoveSpeedMultiplier;
            SgdActuatorsRuntimeState.SetMoveSpeedMultiplier(newMultiplier);
            float after = SgdActuatorsRuntimeState.MoveSpeedMultiplier;

            SgdDecisionRuntimeState.SyncMoveSpeedTheta(newTheta, velocity, after);
            SgdDecisionRuntimeState.RecordMoveSpeedStep(
                skill01: skill01,
                challenge01: challenge01,
                error: error,
                gradient: gradient,
                learningRate: MsLearningRate,
                deltaTheta: deltaTheta,
                newMultiplier: after);

            return Mathf.Abs(after - before) > AxisApplyEpsilon;
        }

        private static bool StepAttackSpeed(in SgdSensorsSample sensors, float thetaMin, float thetaMax, float thetaRange)
        {
            float theta = Mathf.Clamp(SgdDecisionRuntimeState.AttackSpeedTheta, thetaMin, thetaMax);
            float velocity = SgdDecisionRuntimeState.AttackSpeedVelocity;

            float challenge01 = Mathf.Clamp01((theta - thetaMin) / thetaRange);
            float skill01 = EstimateAttackSpeedSkill01(sensors);

            float error = challenge01 - skill01; // >0 => too hard => decrease theta
            if (Mathf.Abs(error) < DefaultErrorDeadZone) error = 0f;

            float gradient = 2f * error * (1f / thetaRange);
            gradient = Mathf.Clamp(gradient, -DefaultGradientClip, DefaultGradientClip);

            velocity = (DefaultMomentum * velocity) + gradient;
            velocity = Mathf.Clamp(velocity, -DefaultVelocityClip, DefaultVelocityClip);

            float deltaTheta = AsLearningRate * velocity;
            deltaTheta = Mathf.Clamp(deltaTheta, -AsMaxDeltaTheta, AsMaxDeltaTheta);

            float newTheta = Mathf.Clamp(theta - deltaTheta, thetaMin, thetaMax);
            float newMultiplier = Mathf.Exp(newTheta);

            float before = SgdActuatorsRuntimeState.AttackSpeedMultiplier;
            SgdActuatorsRuntimeState.SetAttackSpeedMultiplier(newMultiplier);
            float after = SgdActuatorsRuntimeState.AttackSpeedMultiplier;

            SgdDecisionRuntimeState.SyncAttackSpeedTheta(newTheta, velocity, after);
            SgdDecisionRuntimeState.RecordAttackSpeedStep(
                skill01: skill01,
                challenge01: challenge01,
                error: error,
                gradient: gradient,
                learningRate: AsLearningRate,
                deltaTheta: deltaTheta,
                newMultiplier: after);

            return Mathf.Abs(after - before) > AxisApplyEpsilon;
        }

        private static bool StepAttackDamage(in SgdSensorsSample sensors, float thetaMin, float thetaMax, float thetaRange)
        {
            float theta = Mathf.Clamp(SgdDecisionRuntimeState.AttackDamageTheta, thetaMin, thetaMax);
            float velocity = SgdDecisionRuntimeState.AttackDamageVelocity;

            float challenge01 = Mathf.Clamp01((theta - thetaMin) / thetaRange);
            float skill01 = EstimateAttackDamageSkill01(sensors);

            float error = challenge01 - skill01; // >0 => too hard => decrease theta
            if (Mathf.Abs(error) < DefaultErrorDeadZone) error = 0f;

            float gradient = 2f * error * (1f / thetaRange);
            gradient = Mathf.Clamp(gradient, -DefaultGradientClip, DefaultGradientClip);

            velocity = (DefaultMomentum * velocity) + gradient;
            velocity = Mathf.Clamp(velocity, -DefaultVelocityClip, DefaultVelocityClip);

            float deltaTheta = DmgLearningRate * velocity;
            deltaTheta = Mathf.Clamp(deltaTheta, -DmgMaxDeltaTheta, DmgMaxDeltaTheta);

            float newTheta = Mathf.Clamp(theta - deltaTheta, thetaMin, thetaMax);
            float newMultiplier = Mathf.Exp(newTheta);

            float before = SgdActuatorsRuntimeState.AttackDamageMultiplier;
            SgdActuatorsRuntimeState.SetAttackDamageMultiplier(newMultiplier);
            float after = SgdActuatorsRuntimeState.AttackDamageMultiplier;

            SgdDecisionRuntimeState.SyncAttackDamageTheta(newTheta, velocity, after);
            SgdDecisionRuntimeState.RecordAttackDamageStep(
                skill01: skill01,
                challenge01: challenge01,
                error: error,
                gradient: gradient,
                learningRate: DmgLearningRate,
                deltaTheta: deltaTheta,
                newMultiplier: after);

            return Mathf.Abs(after - before) > AxisApplyEpsilon;
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

