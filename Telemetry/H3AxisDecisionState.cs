using GeneticsArtifact.CheatManager;
using GeneticsArtifact.SgdEngine;
using GeneticsArtifact.SgdEngine.Decision;
using UnityEngine;

namespace GeneticsArtifact.Telemetry
{
    internal readonly struct H3AxisQualitySnapshot
    {
        public readonly int PairCountHp;
        public readonly int PairCountMoveSpeed;
        public readonly int PairCountAttackSpeed;
        public readonly int PairCountAttackDamage;
        public readonly int NonzeroDvcCountHp;
        public readonly int NonzeroDvcCountMoveSpeed;
        public readonly int NonzeroDvcCountAttackSpeed;
        public readonly int NonzeroDvcCountAttackDamage;
        public readonly bool HasVarianceHp;
        public readonly bool HasVarianceMoveSpeed;
        public readonly bool HasVarianceAttackSpeed;
        public readonly bool HasVarianceAttackDamage;

        public H3AxisQualitySnapshot(
            int pairCountHp,
            int pairCountMoveSpeed,
            int pairCountAttackSpeed,
            int pairCountAttackDamage,
            int nonzeroDvcCountHp,
            int nonzeroDvcCountMoveSpeed,
            int nonzeroDvcCountAttackSpeed,
            int nonzeroDvcCountAttackDamage,
            bool hasVarianceHp,
            bool hasVarianceMoveSpeed,
            bool hasVarianceAttackSpeed,
            bool hasVarianceAttackDamage)
        {
            PairCountHp = pairCountHp;
            PairCountMoveSpeed = pairCountMoveSpeed;
            PairCountAttackSpeed = pairCountAttackSpeed;
            PairCountAttackDamage = pairCountAttackDamage;
            NonzeroDvcCountHp = nonzeroDvcCountHp;
            NonzeroDvcCountMoveSpeed = nonzeroDvcCountMoveSpeed;
            NonzeroDvcCountAttackSpeed = nonzeroDvcCountAttackSpeed;
            NonzeroDvcCountAttackDamage = nonzeroDvcCountAttackDamage;
            HasVarianceHp = hasVarianceHp;
            HasVarianceMoveSpeed = hasVarianceMoveSpeed;
            HasVarianceAttackSpeed = hasVarianceAttackSpeed;
            HasVarianceAttackDamage = hasVarianceAttackDamage;
        }
    }

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
        private const float NonzeroDvcEpsilon = 1e-6f;
        private const float VarianceEpsilon = 1e-8f;

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
        private static int _pairCountHp;
        private static int _pairCountMoveSpeed;
        private static int _pairCountAttackSpeed;
        private static int _pairCountAttackDamage;
        private static int _nonzeroDvcCountHp;
        private static int _nonzeroDvcCountMoveSpeed;
        private static int _nonzeroDvcCountAttackSpeed;
        private static int _nonzeroDvcCountAttackDamage;
        private static float _sumDvpHp;
        private static float _sumDvpMoveSpeed;
        private static float _sumDvpAttackSpeed;
        private static float _sumDvpAttackDamage;
        private static float _sumDvpSqHp;
        private static float _sumDvpSqMoveSpeed;
        private static float _sumDvpSqAttackSpeed;
        private static float _sumDvpSqAttackDamage;
        private static float _sumDvcHp;
        private static float _sumDvcMoveSpeed;
        private static float _sumDvcAttackSpeed;
        private static float _sumDvcAttackDamage;
        private static float _sumDvcSqHp;
        private static float _sumDvcSqMoveSpeed;
        private static float _sumDvcSqAttackSpeed;
        private static float _sumDvcSqAttackDamage;

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
            ResetAxisQuality();
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

            bool hasPreviousDecisionForDelta = _hasPreviousDecision;
            if (isDecisionStep)
            {
                snapshotStepIndex = _stepIndex + 1;
                deltaVirtualPower = hasPreviousDecisionForDelta
                    ? Subtract(relativeVirtualPower, _previousDecisionVirtualPower)
                    : default;
                deltaVirtualChallenge = hasPreviousDecisionForDelta
                    ? Subtract(safeChallenge, _previousDecisionVirtualChallenge)
                    : default;
                if (hasPreviousDecisionForDelta)
                {
                    RecordAxisPair(in deltaVirtualPower, in deltaVirtualChallenge);
                }

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

        public static H3AxisQualitySnapshot GetQualitySnapshot()
        {
            return new H3AxisQualitySnapshot(
                _pairCountHp,
                _pairCountMoveSpeed,
                _pairCountAttackSpeed,
                _pairCountAttackDamage,
                _nonzeroDvcCountHp,
                _nonzeroDvcCountMoveSpeed,
                _nonzeroDvcCountAttackSpeed,
                _nonzeroDvcCountAttackDamage,
                HasVariance(_pairCountHp, _sumDvpHp, _sumDvpSqHp, _sumDvcHp, _sumDvcSqHp),
                HasVariance(_pairCountMoveSpeed, _sumDvpMoveSpeed, _sumDvpSqMoveSpeed, _sumDvcMoveSpeed, _sumDvcSqMoveSpeed),
                HasVariance(_pairCountAttackSpeed, _sumDvpAttackSpeed, _sumDvpSqAttackSpeed, _sumDvcAttackSpeed, _sumDvcSqAttackSpeed),
                HasVariance(_pairCountAttackDamage, _sumDvpAttackDamage, _sumDvpSqAttackDamage, _sumDvcAttackDamage, _sumDvcSqAttackDamage));
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
            ResetAxisQuality();
        }

        private static void ResetAxisQuality()
        {
            _pairCountHp = 0;
            _pairCountMoveSpeed = 0;
            _pairCountAttackSpeed = 0;
            _pairCountAttackDamage = 0;
            _nonzeroDvcCountHp = 0;
            _nonzeroDvcCountMoveSpeed = 0;
            _nonzeroDvcCountAttackSpeed = 0;
            _nonzeroDvcCountAttackDamage = 0;
            _sumDvpHp = 0f;
            _sumDvpMoveSpeed = 0f;
            _sumDvpAttackSpeed = 0f;
            _sumDvpAttackDamage = 0f;
            _sumDvpSqHp = 0f;
            _sumDvpSqMoveSpeed = 0f;
            _sumDvpSqAttackSpeed = 0f;
            _sumDvpSqAttackDamage = 0f;
            _sumDvcHp = 0f;
            _sumDvcMoveSpeed = 0f;
            _sumDvcAttackSpeed = 0f;
            _sumDvcAttackDamage = 0f;
            _sumDvcSqHp = 0f;
            _sumDvcSqMoveSpeed = 0f;
            _sumDvcSqAttackSpeed = 0f;
            _sumDvcSqAttackDamage = 0f;
        }

        private static void RecordAxisPair(in SgdVirtualPowerSample deltaVirtualPower, in SgdVirtualPowerSample deltaVirtualChallenge)
        {
            RecordAxisPair(
                deltaVirtualPower.Hp,
                deltaVirtualChallenge.Hp,
                ref _pairCountHp,
                ref _nonzeroDvcCountHp,
                ref _sumDvpHp,
                ref _sumDvpSqHp,
                ref _sumDvcHp,
                ref _sumDvcSqHp);
            RecordAxisPair(
                deltaVirtualPower.MoveSpeed,
                deltaVirtualChallenge.MoveSpeed,
                ref _pairCountMoveSpeed,
                ref _nonzeroDvcCountMoveSpeed,
                ref _sumDvpMoveSpeed,
                ref _sumDvpSqMoveSpeed,
                ref _sumDvcMoveSpeed,
                ref _sumDvcSqMoveSpeed);
            RecordAxisPair(
                deltaVirtualPower.AttackSpeed,
                deltaVirtualChallenge.AttackSpeed,
                ref _pairCountAttackSpeed,
                ref _nonzeroDvcCountAttackSpeed,
                ref _sumDvpAttackSpeed,
                ref _sumDvpSqAttackSpeed,
                ref _sumDvcAttackSpeed,
                ref _sumDvcSqAttackSpeed);
            RecordAxisPair(
                deltaVirtualPower.AttackDamage,
                deltaVirtualChallenge.AttackDamage,
                ref _pairCountAttackDamage,
                ref _nonzeroDvcCountAttackDamage,
                ref _sumDvpAttackDamage,
                ref _sumDvpSqAttackDamage,
                ref _sumDvcAttackDamage,
                ref _sumDvcSqAttackDamage);
        }

        private static void RecordAxisPair(
            float dvp,
            float dvc,
            ref int pairCount,
            ref int nonzeroDvcCount,
            ref float sumDvp,
            ref float sumDvpSq,
            ref float sumDvc,
            ref float sumDvcSq)
        {
            pairCount++;
            sumDvp += dvp;
            sumDvpSq += dvp * dvp;
            sumDvc += dvc;
            sumDvcSq += dvc * dvc;
            if (Mathf.Abs(dvc) > NonzeroDvcEpsilon)
            {
                nonzeroDvcCount++;
            }
        }

        private static bool HasVariance(
            int n,
            float sumDvp,
            float sumDvpSq,
            float sumDvc,
            float sumDvcSq)
        {
            if (n < 2)
            {
                return false;
            }

            float nFloat = n;
            float varDvp = (sumDvpSq / nFloat) - ((sumDvp / nFloat) * (sumDvp / nFloat));
            float varDvc = (sumDvcSq / nFloat) - ((sumDvc / nFloat) * (sumDvc / nFloat));
            return varDvp > VarianceEpsilon && varDvc > VarianceEpsilon;
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
