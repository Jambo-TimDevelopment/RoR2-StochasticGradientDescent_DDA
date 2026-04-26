using GeneticsArtifact.SgdEngine;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GeneticsArtifact.Telemetry
{
    internal sealed class TelemetryDegradationTransition
    {
        public string EventKind { get; }
        public float RunElapsedSeconds { get; }
        public float RecoveryElapsedSeconds { get; }
        public string Trigger { get; }
        public float TriggerValue { get; }
        public float DegradationSignal { get; }
        public float IncomingDamageNorm01 { get; }
        public float DeathsPerWindowNorm01 { get; }
        public float LowHealthUptime { get; }
        public float MeanAbsError { get; }

        public TelemetryDegradationTransition(
            string eventKind,
            float runElapsedSeconds,
            float recoveryElapsedSeconds,
            string trigger,
            float triggerValue,
            float degradationSignal,
            in SgdSensorsSample sensors,
            float meanAbsError)
        {
            EventKind = eventKind;
            RunElapsedSeconds = Sanitize(runElapsedSeconds);
            RecoveryElapsedSeconds = Sanitize(recoveryElapsedSeconds);
            Trigger = trigger ?? "";
            TriggerValue = Sanitize(triggerValue);
            DegradationSignal = Sanitize(degradationSignal);
            IncomingDamageNorm01 = Sanitize(sensors.IncomingDamageNorm01);
            DeathsPerWindowNorm01 = Sanitize(sensors.DeathsPerWindowNorm01);
            LowHealthUptime = Sanitize(sensors.LowHealthUptime);
            MeanAbsError = Sanitize(meanAbsError);
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
        }
    }

    internal sealed class TelemetrySessionState
    {
        public string SessionId { get; private set; } = "";
        public float StartedAtUnityTime { get; private set; }
        public int SamplesCount { get; private set; }
        public int RecoveryEventsCount { get; private set; }
        public int DegradationEventsCount { get; private set; }
        public int PlayerDeathsCount { get; private set; }
        public int MissedSampleIntervals { get; private set; }
        public int SurveyFairnessLikert { get; private set; }
        public int SurveyContinuityLikert { get; private set; }
        public string SurveyComment { get; private set; } = "";
        public bool HasSurveyCompleted { get; private set; }
        public bool HasSessionEndQueued { get; private set; }
        public bool HasSurvey => SurveyFairnessLikert > 0 && SurveyContinuityLikert > 0;

        public float PreviousMaxHealthMultiplier { get; private set; } = 1f;
        public float PreviousMoveSpeedMultiplier { get; private set; } = 1f;
        public float PreviousAttackSpeedMultiplier { get; private set; } = 1f;
        public float PreviousAttackDamageMultiplier { get; private set; } = 1f;
        public float PreviousVirtualPower { get; private set; }
        public float PreviousVirtualChallenge { get; private set; }

        public float SumAbsErrorMaxHealth { get; private set; }
        public float SumAbsErrorMoveSpeed { get; private set; }
        public float SumAbsErrorAttackSpeed { get; private set; }
        public float SumAbsErrorAttackDamage { get; private set; }
        public int JumpCount { get; private set; }
        public int JumpObservations { get; private set; }
        public float SumVirtualGapAbs { get; private set; }
        public float SumRecoverySeconds { get; private set; }

        public bool IsDegraded { get; private set; }
        public float RecoveryElapsedSeconds { get; private set; }
        public string CurrentDegradationTrigger { get; private set; } = "";
        public float CurrentDegradationTriggerValue { get; private set; }

        private bool _hasPreviousSnapshot;
        private float _pendingRecoverySeconds = -1f;
        private readonly Queue<TelemetryDegradationTransition> _pendingDegradationTransitions = new Queue<TelemetryDegradationTransition>();

        public float ElapsedSeconds => Mathf.Max(0f, Time.time - StartedAtUnityTime);
        public float MeanAbsErrorMaxHealth => Mean(SumAbsErrorMaxHealth, SamplesCount);
        public float MeanAbsErrorMoveSpeed => Mean(SumAbsErrorMoveSpeed, SamplesCount);
        public float MeanAbsErrorAttackSpeed => Mean(SumAbsErrorAttackSpeed, SamplesCount);
        public float MeanAbsErrorAttackDamage => Mean(SumAbsErrorAttackDamage, SamplesCount);
        public float JumpRateAllAxes => JumpObservations > 0 ? JumpCount / (float)JumpObservations : 0f;
        public float MeanVirtualGapAbs => Mean(SumVirtualGapAbs, SamplesCount);
        public float MeanRecoverySeconds => Mean(SumRecoverySeconds, RecoveryEventsCount);

        public void StartNewRun()
        {
            SessionId = "run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StartedAtUnityTime = Time.time;
            SamplesCount = 0;
            RecoveryEventsCount = 0;
            DegradationEventsCount = 0;
            PlayerDeathsCount = 0;
            MissedSampleIntervals = 0;
            SurveyFairnessLikert = 0;
            SurveyContinuityLikert = 0;
            SurveyComment = "";
            HasSurveyCompleted = false;
            HasSessionEndQueued = false;
            PreviousMaxHealthMultiplier = 1f;
            PreviousMoveSpeedMultiplier = 1f;
            PreviousAttackSpeedMultiplier = 1f;
            PreviousAttackDamageMultiplier = 1f;
            PreviousVirtualPower = 0f;
            PreviousVirtualChallenge = 0f;
            SumAbsErrorMaxHealth = 0f;
            SumAbsErrorMoveSpeed = 0f;
            SumAbsErrorAttackSpeed = 0f;
            SumAbsErrorAttackDamage = 0f;
            JumpCount = 0;
            JumpObservations = 0;
            SumVirtualGapAbs = 0f;
            SumRecoverySeconds = 0f;
            IsDegraded = false;
            RecoveryElapsedSeconds = 0f;
            CurrentDegradationTrigger = "";
            CurrentDegradationTriggerValue = 0f;
            _hasPreviousSnapshot = false;
            _pendingRecoverySeconds = -1f;
            _pendingDegradationTransitions.Clear();
        }

        public void RecordMissedSampleIntervals(int missedIntervals)
        {
            if (missedIntervals > 0)
            {
                MissedSampleIntervals += missedIntervals;
            }
        }

        public void RecordPlayerDeath()
        {
            PlayerDeathsCount++;
        }

        public void RecordSurvey(int fairnessLikert, int continuityLikert, string comment)
        {
            SurveyFairnessLikert = Mathf.Clamp(fairnessLikert, 1, 7);
            SurveyContinuityLikert = Mathf.Clamp(continuityLikert, 1, 7);
            SurveyComment = comment ?? "";
            HasSurveyCompleted = true;
        }

        public void RecordSurveySkipped(string comment)
        {
            SurveyFairnessLikert = 0;
            SurveyContinuityLikert = 0;
            SurveyComment = comment ?? "";
            HasSurveyCompleted = true;
        }

        public void MarkSessionEndQueued()
        {
            HasSessionEndQueued = true;
        }

        public void RecordSample(
            float absErrorMaxHealth,
            float absErrorMoveSpeed,
            float absErrorAttackSpeed,
            float absErrorAttackDamage,
            float virtualGapAbs,
            TelemetryDifficultySnapshot snapshot,
            in SgdSensorsSample sensors,
            float dt)
        {
            SamplesCount++;
            SumAbsErrorMaxHealth += Sanitize(absErrorMaxHealth);
            SumAbsErrorMoveSpeed += Sanitize(absErrorMoveSpeed);
            SumAbsErrorAttackSpeed += Sanitize(absErrorAttackSpeed);
            SumAbsErrorAttackDamage += Sanitize(absErrorAttackDamage);
            SumVirtualGapAbs += Sanitize(virtualGapAbs);

            RecordJump(snapshot.MaxHealth, PreviousMaxHealthMultiplier);
            RecordJump(snapshot.MoveSpeed, PreviousMoveSpeedMultiplier);
            RecordJump(snapshot.AttackSpeed, PreviousAttackSpeedMultiplier);
            RecordJump(snapshot.AttackDamage, PreviousAttackDamageMultiplier);

            float meanAbsError = (absErrorMaxHealth + absErrorMoveSpeed + absErrorAttackSpeed + absErrorAttackDamage) * 0.25f;
            UpdateRecovery(sensors, meanAbsError, ComputeDegradationSignal(sensors, meanAbsError), dt);

            PreviousMaxHealthMultiplier = snapshot.MaxHealth;
            PreviousMoveSpeedMultiplier = snapshot.MoveSpeed;
            PreviousAttackSpeedMultiplier = snapshot.AttackSpeed;
            PreviousAttackDamageMultiplier = snapshot.AttackDamage;
            _hasPreviousSnapshot = true;
        }

        public bool IsJump(float currentMultiplier, float previousMultiplier)
        {
            if (!_hasPreviousSnapshot) return false;
            float threshold = ConfigManager.telemetryJumpThreshold?.Value ?? 0.10f;
            return Mathf.Abs(currentMultiplier - previousMultiplier) > Mathf.Max(0.001f, threshold);
        }

        public float ComputeDegradationSignal(in SgdSensorsSample sensors, float meanAbsError)
        {
            return Mathf.Max(
                Mathf.Clamp01(sensors.IncomingDamageNorm01),
                Mathf.Clamp01(sensors.DeathsPerWindowNorm01),
                Mathf.Clamp01(sensors.LowHealthUptime),
                Mathf.Clamp01(meanAbsError));
        }

        public bool TryConsumeRecoveryEvent(out float recoverySeconds)
        {
            if (_pendingRecoverySeconds >= 0f)
            {
                recoverySeconds = _pendingRecoverySeconds;
                _pendingRecoverySeconds = -1f;
                return true;
            }

            recoverySeconds = 0f;
            return false;
        }

        public bool TryConsumeDegradationTransition(out TelemetryDegradationTransition transition)
        {
            if (_pendingDegradationTransitions.Count > 0)
            {
                transition = _pendingDegradationTransitions.Dequeue();
                return true;
            }

            transition = null;
            return false;
        }

        public void SetPreviousVirtuals(float virtualPower, float virtualChallenge)
        {
            PreviousVirtualPower = virtualPower;
            PreviousVirtualChallenge = virtualChallenge;
        }

        private void RecordJump(float current, float previous)
        {
            JumpObservations++;
            if (IsJump(current, previous))
            {
                JumpCount++;
            }
        }

        private void UpdateRecovery(in SgdSensorsSample sensors, float meanAbsError, float degradationSignal, float dt)
        {
            float degradationThreshold = ConfigManager.telemetryDegradationThreshold?.Value ?? 0.70f;
            float recoveryThreshold = ConfigManager.telemetryRecoveryThreshold?.Value ?? 0.35f;

            if (!IsDegraded && degradationSignal >= degradationThreshold)
            {
                DetermineDegradationTrigger(
                    sensors,
                    meanAbsError,
                    out string trigger,
                    out float triggerValue);
                IsDegraded = true;
                RecoveryElapsedSeconds = 0f;
                CurrentDegradationTrigger = trigger;
                CurrentDegradationTriggerValue = triggerValue;
                DegradationEventsCount++;
                _pendingDegradationTransitions.Enqueue(new TelemetryDegradationTransition(
                    "degradation_start",
                    ElapsedSeconds,
                    0f,
                    trigger,
                    triggerValue,
                    degradationSignal,
                    sensors,
                    meanAbsError));
                return;
            }

            if (!IsDegraded)
            {
                RecoveryElapsedSeconds = 0f;
                return;
            }

            RecoveryElapsedSeconds += Mathf.Max(0f, dt);
            if (degradationSignal <= recoveryThreshold)
            {
                IsDegraded = false;
                RecoveryEventsCount++;
                SumRecoverySeconds += RecoveryElapsedSeconds;
                _pendingRecoverySeconds = RecoveryElapsedSeconds;
                _pendingDegradationTransitions.Enqueue(new TelemetryDegradationTransition(
                    "degradation_end",
                    ElapsedSeconds,
                    RecoveryElapsedSeconds,
                    CurrentDegradationTrigger,
                    CurrentDegradationTriggerValue,
                    degradationSignal,
                    sensors,
                    meanAbsError));
                CurrentDegradationTrigger = "";
                CurrentDegradationTriggerValue = 0f;
            }
        }

        private static void DetermineDegradationTrigger(
            in SgdSensorsSample sensors,
            float meanAbsError,
            out string trigger,
            out float triggerValue)
        {
            trigger = "mean_abs_error";
            triggerValue = Mathf.Clamp01(meanAbsError);

            Consider("incoming_damage_norm01", sensors.IncomingDamageNorm01, ref trigger, ref triggerValue);
            Consider("deaths_per_window_norm01", sensors.DeathsPerWindowNorm01, ref trigger, ref triggerValue);
            Consider("low_health_uptime", sensors.LowHealthUptime, ref trigger, ref triggerValue);
        }

        private static void Consider(string name, float value, ref string trigger, ref float triggerValue)
        {
            value = Mathf.Clamp01(value);
            if (value > triggerValue)
            {
                trigger = name;
                triggerValue = value;
            }
        }

        private static float Mean(float sum, int count)
        {
            return count > 0 ? sum / count : 0f;
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
        }
    }
}
