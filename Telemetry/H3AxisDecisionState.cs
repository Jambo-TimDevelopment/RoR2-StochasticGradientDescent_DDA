using GeneticsArtifact.CheatManager;
using GeneticsArtifact.SgdEngine;
using GeneticsArtifact.SgdEngine.Decision;
using UnityEngine;

namespace GeneticsArtifact.Telemetry
{
    internal readonly struct H3AxisDecisionSnapshot
    {
        public readonly string Mode;
        public readonly int StepIndex;
        public readonly bool IsDecisionStep;
        public readonly string StepReason;
        public readonly string DeltaSource;
        public readonly float StepIntervalSeconds;
        public readonly SgdVirtualPowerSample ObservedVirtualPower;
        public readonly SgdVirtualPowerSample BaselineVirtualPower;
        public readonly SgdVirtualPowerSample VirtualPower;
        public readonly SgdVirtualPowerSample VirtualChallenge;
        public readonly SgdVirtualPowerSample DeltaVirtualPower;
        public readonly SgdVirtualPowerSample DeltaVirtualChallenge;

        public H3AxisDecisionSnapshot(
            string mode,
            int stepIndex,
            bool isDecisionStep,
            string stepReason,
            float stepIntervalSeconds,
            in SgdVirtualPowerSample observedVirtualPower,
            in SgdVirtualPowerSample baselineVirtualPower,
            in SgdVirtualPowerSample virtualPower,
            in SgdVirtualPowerSample virtualChallenge,
            in SgdVirtualPowerSample deltaVirtualPower,
            in SgdVirtualPowerSample deltaVirtualChallenge)
        {
            Mode = mode ?? "";
            StepIndex = stepIndex;
            IsDecisionStep = isDecisionStep;
            StepReason = stepReason ?? "no_step";
            DeltaSource = "decision_step";
            StepIntervalSeconds = stepIntervalSeconds;
            ObservedVirtualPower = observedVirtualPower;
            BaselineVirtualPower = baselineVirtualPower;
            VirtualPower = virtualPower;
            VirtualChallenge = virtualChallenge;
            DeltaVirtualPower = deltaVirtualPower;
            DeltaVirtualChallenge = deltaVirtualChallenge;
        }
    }

    internal static class H3AxisDecisionState
    {
        private const float ChangeEpsilon = 0.0001f;

        private static bool _hasModeState;
        private static bool _hasBaselineVirtualPower;
        private static bool _hasPreviousDecision;
        private static string _mode = "";
        private static int _stepIndex;
        private static int _lastSgdSteps;
        private static int _lastGaLearnSteps;
        private static SgdVirtualPowerSample _baselineVirtualPower;
        private static SgdVirtualPowerSample _previousDecisionVirtualPower;
        private static SgdVirtualPowerSample _previousDecisionVirtualChallenge;
        private static SgdVirtualPowerSample _lastObservedVirtualChallenge;

        public static void Reset()
        {
            _hasModeState = false;
            _hasBaselineVirtualPower = false;
            _hasPreviousDecision = false;
            _mode = "";
            _stepIndex = 0;
            _lastSgdSteps = 0;
            _lastGaLearnSteps = 0;
            _baselineVirtualPower = default;
            _previousDecisionVirtualPower = default;
            _previousDecisionVirtualChallenge = default;
            _lastObservedVirtualChallenge = default;
        }

        public static SgdVirtualPowerSample ComputeVirtualChallengeAxes(TelemetryDifficultySnapshot snapshot)
        {
            return new SgdVirtualPowerSample(
                hp: ToVirtualChallenge(GeneStat.MaxHealth, snapshot.MaxHealth),
                moveSpeed: ToVirtualChallenge(GeneStat.MoveSpeed, snapshot.MoveSpeed),
                attackSpeed: ToVirtualChallenge(GeneStat.AttackSpeed, snapshot.AttackSpeed),
                attackDamage: ToVirtualChallenge(GeneStat.AttackDamage, snapshot.AttackDamage));
        }

        public static H3AxisDecisionSnapshot BuildSnapshot(
            string mode,
            in SgdVirtualPowerSample observedVirtualPower,
            in SgdVirtualPowerSample virtualChallenge)
        {
            mode = string.IsNullOrWhiteSpace(mode) ? DdaAlgorithmState.GetTelemetryMode() : mode;
            int currentSgdSteps = SgdDecisionRuntimeState.TotalStepsDone;
            int currentGaLearnSteps = H3GaDecisionObserver.TotalLearnSteps;

            if (!_hasModeState || _mode != mode)
            {
                ResetModeState(mode, currentSgdSteps, currentGaLearnSteps);
            }

            if (!_hasBaselineVirtualPower)
            {
                _baselineVirtualPower = Sanitize(observedVirtualPower);
                _hasBaselineVirtualPower = true;
            }

            var safeObservedPower = Sanitize(observedVirtualPower);
            var safeChallenge = Sanitize(virtualChallenge);
            var relativeVirtualPower = Subtract(safeObservedPower, _baselineVirtualPower);

            bool isDecisionStep = DetermineDecisionStep(
                mode,
                currentSgdSteps,
                currentGaLearnSteps,
                safeChallenge,
                out string reason);

            SgdVirtualPowerSample deltaVirtualPower = default;
            SgdVirtualPowerSample deltaVirtualChallenge = default;
            int snapshotStepIndex = _stepIndex;

            if (isDecisionStep)
            {
                snapshotStepIndex = _stepIndex + 1;
                deltaVirtualPower = _hasPreviousDecision
                    ? Subtract(relativeVirtualPower, _previousDecisionVirtualPower)
                    : default;
                deltaVirtualChallenge = _hasPreviousDecision
                    ? Subtract(safeChallenge, _previousDecisionVirtualChallenge)
                    : default;

                _stepIndex = snapshotStepIndex;
                _previousDecisionVirtualPower = relativeVirtualPower;
                _previousDecisionVirtualChallenge = safeChallenge;
                _hasPreviousDecision = true;
            }

            _lastSgdSteps = currentSgdSteps;
            _lastGaLearnSteps = currentGaLearnSteps;
            _lastObservedVirtualChallenge = safeChallenge;

            return new H3AxisDecisionSnapshot(
                mode,
                snapshotStepIndex,
                isDecisionStep,
                reason,
                GetStepIntervalSeconds(mode),
                safeObservedPower,
                _baselineVirtualPower,
                relativeVirtualPower,
                safeChallenge,
                deltaVirtualPower,
                deltaVirtualChallenge);
        }

        private static void ResetModeState(string mode, int currentSgdSteps, int currentGaLearnSteps)
        {
            _hasModeState = true;
            _hasBaselineVirtualPower = false;
            _hasPreviousDecision = false;
            _mode = mode ?? "";
            _stepIndex = 0;
            _lastSgdSteps = currentSgdSteps;
            _lastGaLearnSteps = currentGaLearnSteps;
            _baselineVirtualPower = default;
            _previousDecisionVirtualPower = default;
            _previousDecisionVirtualChallenge = default;
            _lastObservedVirtualChallenge = default;
        }

        private static bool DetermineDecisionStep(
            string mode,
            int currentSgdSteps,
            int currentGaLearnSteps,
            in SgdVirtualPowerSample virtualChallenge,
            out string reason)
        {
            if (!_hasPreviousDecision)
            {
                reason = "initial";
                return true;
            }

            if (mode == "FLS")
            {
                reason = "fls_sample_tick";
                return true;
            }

            if (mode == "SGD" && currentSgdSteps > _lastSgdSteps)
            {
                reason = "sgd_step";
                return true;
            }

            if (mode == "GA")
            {
                if (currentGaLearnSteps > _lastGaLearnSteps)
                {
                    reason = "ga_learn";
                    return true;
                }

                if (HasChallengeChanged(virtualChallenge, _lastObservedVirtualChallenge))
                {
                    reason = "ga_snapshot_change";
                    return true;
                }
            }

            reason = "no_step";
            return false;
        }

        private static float GetStepIntervalSeconds(string mode)
        {
            if (mode == "SGD")
            {
                return SgdDecisionRuntimeState.StepSeconds;
            }

            if (mode == "GA")
            {
                return Mathf.Max(0f, ConfigManager.timeLimit != null ? ConfigManager.timeLimit.Value : 0f);
            }

            return Mathf.Max(1f, ConfigManager.telemetrySampleIntervalSeconds != null ? ConfigManager.telemetrySampleIntervalSeconds.Value : 1f);
        }

        private static bool HasChallengeChanged(in SgdVirtualPowerSample a, in SgdVirtualPowerSample b)
        {
            return Mathf.Abs(a.Hp - b.Hp) > ChangeEpsilon ||
                   Mathf.Abs(a.MoveSpeed - b.MoveSpeed) > ChangeEpsilon ||
                   Mathf.Abs(a.AttackSpeed - b.AttackSpeed) > ChangeEpsilon ||
                   Mathf.Abs(a.AttackDamage - b.AttackDamage) > ChangeEpsilon;
        }

        private static float ToVirtualChallenge(GeneStat stat, float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier <= 0f)
            {
                multiplier = 1f;
            }

            return SafeLog(SgdAxisLimitProvider.Clamp(stat, multiplier));
        }

        private static SgdVirtualPowerSample Sanitize(in SgdVirtualPowerSample sample)
        {
            return new SgdVirtualPowerSample(
                hp: Sanitize(sample.Hp),
                moveSpeed: Sanitize(sample.MoveSpeed),
                attackSpeed: Sanitize(sample.AttackSpeed),
                attackDamage: Sanitize(sample.AttackDamage));
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static float SafeLog(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                return 0f;
            }

            return Mathf.Log(value);
        }

        private static SgdVirtualPowerSample Subtract(in SgdVirtualPowerSample a, in SgdVirtualPowerSample b)
        {
            return new SgdVirtualPowerSample(
                hp: a.Hp - b.Hp,
                moveSpeed: a.MoveSpeed - b.MoveSpeed,
                attackSpeed: a.AttackSpeed - b.AttackSpeed,
                attackDamage: a.AttackDamage - b.AttackDamage);
        }
    }
}
