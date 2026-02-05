using UnityEngine;

namespace GeneticsArtifact.SgdEngine.Decision
{
    /// <summary>
    /// Runtime state for the SGD decision module (MVP).
    /// For now, we implement only a single axis: Enemy AttackSpeed.
    /// </summary>
    public static class SgdDecisionRuntimeState
    {
        public const float DefaultStepSeconds = 10f;

        public static float StepSeconds { get; private set; } = DefaultStepSeconds;

        /// <summary>
        /// Combat time accumulated since the last SGD step.
        /// We only advance this timer while the player is in combat.
        /// </summary>
        public static float CombatSecondsSinceLastStep { get; private set; }

        public static int TotalStepsDone { get; private set; }

        // --- Axis: AttackSpeed ---
        public static bool HasAttackSpeedState { get; private set; }

        /// <summary>
        /// AttackSpeed parameter in log-space: theta = ln(multiplier).
        /// This makes multiplicative changes smooth and keeps positivity naturally.
        /// </summary>
        public static float AttackSpeedTheta { get; private set; }

        /// <summary>
        /// Momentum velocity for AttackSpeed axis.
        /// </summary>
        public static float AttackSpeedVelocity { get; private set; }

        // Debug / overlay telemetry (last step)
        public static float AttackSpeedSkill01Last { get; private set; }
        public static float AttackSpeedChallenge01Last { get; private set; }
        public static float AttackSpeedErrorLast { get; private set; }
        public static float AttackSpeedGradientLast { get; private set; }
        public static float AttackSpeedLearningRateLast { get; private set; }
        public static float AttackSpeedDeltaThetaLast { get; private set; }
        public static float AttackSpeedMultiplierLast { get; private set; } = 1f;
        public static int AttackSpeedAppliedMonstersLast { get; private set; }

        public static float CombatSecondsUntilNextStep =>
            Mathf.Max(0f, StepSeconds - CombatSecondsSinceLastStep);

        public static void Reset()
        {
            CombatSecondsSinceLastStep = 0f;
            TotalStepsDone = 0;

            HasAttackSpeedState = false;
            AttackSpeedTheta = 0f;
            AttackSpeedVelocity = 0f;

            AttackSpeedSkill01Last = 0f;
            AttackSpeedChallenge01Last = 0f;
            AttackSpeedErrorLast = 0f;
            AttackSpeedGradientLast = 0f;
            AttackSpeedLearningRateLast = 0f;
            AttackSpeedDeltaThetaLast = 0f;
            AttackSpeedMultiplierLast = 1f;
            AttackSpeedAppliedMonstersLast = 0;
        }

        public static void SetStepSeconds(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
            {
                return;
            }

            // Safety clamp for debugging: 1s..300s.
            StepSeconds = Mathf.Clamp(seconds, 1f, 300f);

            // If we just made the step smaller, ensure we don't "instantly" trigger
            // multiple steps because of already accumulated combat time.
            CombatSecondsSinceLastStep = Mathf.Clamp(CombatSecondsSinceLastStep, 0f, StepSeconds);
        }

        internal static void AddCombatSeconds(float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
            {
                return;
            }

            CombatSecondsSinceLastStep += dt;
            CombatSecondsSinceLastStep = Mathf.Clamp(CombatSecondsSinceLastStep, 0f, 10_000f);
        }

        internal static int ConsumeDueSteps()
        {
            float step = StepSeconds;
            if (step <= 0f || float.IsNaN(step) || float.IsInfinity(step))
            {
                step = DefaultStepSeconds;
                StepSeconds = step;
            }

            int due = (int)(CombatSecondsSinceLastStep / step);
            if (due <= 0) return 0;

            CombatSecondsSinceLastStep -= due * step;
            CombatSecondsSinceLastStep = Mathf.Clamp(CombatSecondsSinceLastStep, 0f, step);
            return due;
        }

        internal static void SyncAttackSpeedTheta(float theta, float velocity, float multiplier)
        {
            HasAttackSpeedState = true;
            AttackSpeedTheta = theta;
            AttackSpeedVelocity = velocity;
            AttackSpeedMultiplierLast = multiplier;
        }

        internal static void RecordAttackSpeedStep(
            float skill01,
            float challenge01,
            float error,
            float gradient,
            float learningRate,
            float deltaTheta,
            float newMultiplier,
            int appliedMonsters)
        {
            TotalStepsDone++;

            AttackSpeedSkill01Last = skill01;
            AttackSpeedChallenge01Last = challenge01;
            AttackSpeedErrorLast = error;
            AttackSpeedGradientLast = gradient;
            AttackSpeedLearningRateLast = learningRate;
            AttackSpeedDeltaThetaLast = deltaTheta;
            AttackSpeedMultiplierLast = newMultiplier;
            AttackSpeedAppliedMonstersLast = appliedMonsters;
        }
    }
}

