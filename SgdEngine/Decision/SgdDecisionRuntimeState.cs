using UnityEngine;

namespace GeneticsArtifact.SgdEngine.Decision
{
    /// <summary>
    /// Runtime state for the SGD decision module.
    /// Each axis is an independent 1D SGD optimization that updates one actuator multiplier.
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
        public static int AppliedMonstersLast { get; private set; }

        // --- Axis: MaxHealth (HP) ---
        public static bool IsMaxHealthAdaptationEnabled { get; private set; } = true;
        public static bool HasMaxHealthState { get; private set; }
        public static float MaxHealthTheta { get; private set; }
        public static float MaxHealthVelocity { get; private set; }
        public static int MaxHealthStepsDone { get; private set; }
        public static float MaxHealthSkill01Last { get; private set; }
        public static float MaxHealthChallenge01Last { get; private set; }
        public static float MaxHealthErrorLast { get; private set; }
        public static float MaxHealthGradientLast { get; private set; }
        public static float MaxHealthLearningRateLast { get; private set; }
        public static float MaxHealthDeltaThetaLast { get; private set; }
        public static float MaxHealthMultiplierLast { get; private set; } = 1f;

        // --- Axis: MoveSpeed (MS) ---
        public static bool IsMoveSpeedAdaptationEnabled { get; private set; } = true;
        public static bool HasMoveSpeedState { get; private set; }
        public static float MoveSpeedTheta { get; private set; }
        public static float MoveSpeedVelocity { get; private set; }
        public static int MoveSpeedStepsDone { get; private set; }
        public static float MoveSpeedSkill01Last { get; private set; }
        public static float MoveSpeedChallenge01Last { get; private set; }
        public static float MoveSpeedErrorLast { get; private set; }
        public static float MoveSpeedGradientLast { get; private set; }
        public static float MoveSpeedLearningRateLast { get; private set; }
        public static float MoveSpeedDeltaThetaLast { get; private set; }
        public static float MoveSpeedMultiplierLast { get; private set; } = 1f;

        // --- Axis: AttackSpeed (AS) ---
        public static bool IsAttackSpeedAdaptationEnabled { get; private set; } = true;
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
        public static int AttackSpeedStepsDone { get; private set; }

        // --- Axis: AttackDamage (DMG) ---
        public static bool IsAttackDamageAdaptationEnabled { get; private set; } = true;
        public static bool HasAttackDamageState { get; private set; }
        public static float AttackDamageTheta { get; private set; }
        public static float AttackDamageVelocity { get; private set; }
        public static int AttackDamageStepsDone { get; private set; }
        public static float AttackDamageSkill01Last { get; private set; }
        public static float AttackDamageChallenge01Last { get; private set; }
        public static float AttackDamageErrorLast { get; private set; }
        public static float AttackDamageGradientLast { get; private set; }
        public static float AttackDamageLearningRateLast { get; private set; }
        public static float AttackDamageDeltaThetaLast { get; private set; }
        public static float AttackDamageMultiplierLast { get; private set; } = 1f;

        public static float CombatSecondsUntilNextStep =>
            Mathf.Max(0f, StepSeconds - CombatSecondsSinceLastStep);

        public static void Reset()
        {
            CombatSecondsSinceLastStep = 0f;
            TotalStepsDone = 0;
            AppliedMonstersLast = 0;

            IsMaxHealthAdaptationEnabled = true;
            HasMaxHealthState = false;
            MaxHealthTheta = 0f;
            MaxHealthVelocity = 0f;
            MaxHealthStepsDone = 0;
            MaxHealthSkill01Last = 0f;
            MaxHealthChallenge01Last = 0f;
            MaxHealthErrorLast = 0f;
            MaxHealthGradientLast = 0f;
            MaxHealthLearningRateLast = 0f;
            MaxHealthDeltaThetaLast = 0f;
            MaxHealthMultiplierLast = 1f;

            IsMoveSpeedAdaptationEnabled = true;
            HasMoveSpeedState = false;
            MoveSpeedTheta = 0f;
            MoveSpeedVelocity = 0f;
            MoveSpeedStepsDone = 0;
            MoveSpeedSkill01Last = 0f;
            MoveSpeedChallenge01Last = 0f;
            MoveSpeedErrorLast = 0f;
            MoveSpeedGradientLast = 0f;
            MoveSpeedLearningRateLast = 0f;
            MoveSpeedDeltaThetaLast = 0f;
            MoveSpeedMultiplierLast = 1f;

            IsAttackSpeedAdaptationEnabled = true;
            HasAttackSpeedState = false;
            AttackSpeedTheta = 0f;
            AttackSpeedVelocity = 0f;
            AttackSpeedStepsDone = 0;

            AttackSpeedSkill01Last = 0f;
            AttackSpeedChallenge01Last = 0f;
            AttackSpeedErrorLast = 0f;
            AttackSpeedGradientLast = 0f;
            AttackSpeedLearningRateLast = 0f;
            AttackSpeedDeltaThetaLast = 0f;
            AttackSpeedMultiplierLast = 1f;

            IsAttackDamageAdaptationEnabled = true;
            HasAttackDamageState = false;
            AttackDamageTheta = 0f;
            AttackDamageVelocity = 0f;
            AttackDamageStepsDone = 0;
            AttackDamageSkill01Last = 0f;
            AttackDamageChallenge01Last = 0f;
            AttackDamageErrorLast = 0f;
            AttackDamageGradientLast = 0f;
            AttackDamageLearningRateLast = 0f;
            AttackDamageDeltaThetaLast = 0f;
            AttackDamageMultiplierLast = 1f;
        }

        public static void SetMaxHealthAdaptationEnabled(bool enabled)
        {
            IsMaxHealthAdaptationEnabled = enabled;
            MaxHealthVelocity = 0f;
        }

        public static void SetMoveSpeedAdaptationEnabled(bool enabled)
        {
            IsMoveSpeedAdaptationEnabled = enabled;
            MoveSpeedVelocity = 0f;
        }

        public static void SetAttackSpeedAdaptationEnabled(bool enabled)
        {
            IsAttackSpeedAdaptationEnabled = enabled;
            AttackSpeedVelocity = 0f;
        }

        public static void SetAttackDamageAdaptationEnabled(bool enabled)
        {
            IsAttackDamageAdaptationEnabled = enabled;
            AttackDamageVelocity = 0f;
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

        internal static void RecordGlobalStep(int appliedMonsters)
        {
            TotalStepsDone++;
            AppliedMonstersLast = Mathf.Max(0, appliedMonsters);
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

        internal static void SyncMaxHealthTheta(float theta, float velocity, float multiplier)
        {
            HasMaxHealthState = true;
            MaxHealthTheta = theta;
            MaxHealthVelocity = velocity;
            MaxHealthMultiplierLast = multiplier;
        }

        internal static void RecordMaxHealthStep(
            float skill01,
            float challenge01,
            float error,
            float gradient,
            float learningRate,
            float deltaTheta,
            float newMultiplier)
        {
            MaxHealthStepsDone++;
            MaxHealthSkill01Last = skill01;
            MaxHealthChallenge01Last = challenge01;
            MaxHealthErrorLast = error;
            MaxHealthGradientLast = gradient;
            MaxHealthLearningRateLast = learningRate;
            MaxHealthDeltaThetaLast = deltaTheta;
            MaxHealthMultiplierLast = newMultiplier;
        }

        internal static void SyncMoveSpeedTheta(float theta, float velocity, float multiplier)
        {
            HasMoveSpeedState = true;
            MoveSpeedTheta = theta;
            MoveSpeedVelocity = velocity;
            MoveSpeedMultiplierLast = multiplier;
        }

        internal static void RecordMoveSpeedStep(
            float skill01,
            float challenge01,
            float error,
            float gradient,
            float learningRate,
            float deltaTheta,
            float newMultiplier)
        {
            MoveSpeedStepsDone++;
            MoveSpeedSkill01Last = skill01;
            MoveSpeedChallenge01Last = challenge01;
            MoveSpeedErrorLast = error;
            MoveSpeedGradientLast = gradient;
            MoveSpeedLearningRateLast = learningRate;
            MoveSpeedDeltaThetaLast = deltaTheta;
            MoveSpeedMultiplierLast = newMultiplier;
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
            float newMultiplier)
        {
            AttackSpeedStepsDone++;
            AttackSpeedSkill01Last = skill01;
            AttackSpeedChallenge01Last = challenge01;
            AttackSpeedErrorLast = error;
            AttackSpeedGradientLast = gradient;
            AttackSpeedLearningRateLast = learningRate;
            AttackSpeedDeltaThetaLast = deltaTheta;
            AttackSpeedMultiplierLast = newMultiplier;
        }

        internal static void SyncAttackDamageTheta(float theta, float velocity, float multiplier)
        {
            HasAttackDamageState = true;
            AttackDamageTheta = theta;
            AttackDamageVelocity = velocity;
            AttackDamageMultiplierLast = multiplier;
        }

        internal static void RecordAttackDamageStep(
            float skill01,
            float challenge01,
            float error,
            float gradient,
            float learningRate,
            float deltaTheta,
            float newMultiplier)
        {
            AttackDamageStepsDone++;
            AttackDamageSkill01Last = skill01;
            AttackDamageChallenge01Last = challenge01;
            AttackDamageErrorLast = error;
            AttackDamageGradientLast = gradient;
            AttackDamageLearningRateLast = learningRate;
            AttackDamageDeltaThetaLast = deltaTheta;
            AttackDamageMultiplierLast = newMultiplier;
        }
    }
}

