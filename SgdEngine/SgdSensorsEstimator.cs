using RoR2;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GeneticsArtifact.SgdEngine
{
    /// <summary>
    /// Tracks a minimal set of mandatory sensors required to control GeneStat actuators.
    /// Uses EMA for rates/uptimes and fixed time windows for deaths and TTK.
    /// </summary>
    public sealed class SgdSensorsEstimator
    {
        public const float DefaultTauSeconds = 7.5f;
        public const float DefaultWindowSeconds = 60f;

        // Threshold for low-health uptime sensor.
        public const float DefaultLowHealthThreshold = 0.30f;

        private readonly float _tauSeconds;
        private readonly float _windowSeconds;
        private readonly float _lowHealthThreshold;

        private float _incomingDamageRateEma;
        private float _outgoingDamageRateEma;
        private float _hitRateOnPlayerEma;
        private float _combatUptimeEma;
        private float _lowHealthUptimeEma;
        private float _avgTtkSecondsEma;

        private readonly List<float> _playerDeathTimes = new List<float>(8);

        // Victim instanceId -> first hit timestamp (seconds).
        private readonly Dictionary<int, float> _victimFirstHitTime = new Dictionary<int, float>(128);
        private readonly List<float> _ttkSamples = new List<float>(64);

        public SgdSensorsEstimator(
            float tauSeconds = DefaultTauSeconds,
            float windowSeconds = DefaultWindowSeconds,
            float lowHealthThreshold = DefaultLowHealthThreshold)
        {
            _tauSeconds = Mathf.Max(0.1f, tauSeconds);
            _windowSeconds = Mathf.Max(5f, windowSeconds);
            _lowHealthThreshold = Mathf.Clamp01(lowHealthThreshold);
        }

        public void Reset()
        {
            _incomingDamageRateEma = 0f;
            _outgoingDamageRateEma = 0f;
            _hitRateOnPlayerEma = 0f;
            _combatUptimeEma = 0f;
            _lowHealthUptimeEma = 0f;
            _avgTtkSecondsEma = 0f;
            _playerDeathTimes.Clear();
            _victimFirstHitTime.Clear();
            _ttkSamples.Clear();
        }

        public void TickPlayerBody(CharacterBody playerBody, float dt)
        {
            if (playerBody == null || dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
            {
                return;
            }

            float alpha = ComputeEmaAlpha(dt, _tauSeconds);

            // Combat uptime: 1 when in combat, else 0.
            float combatSignal = playerBody.outOfCombat ? 0f : 1f;
            _combatUptimeEma = Ema(_combatUptimeEma, combatSignal, alpha);

            // Low health uptime: 1 when below threshold, else 0.
            var hc = playerBody.healthComponent;
            float combinedHealth = hc != null ? hc.combinedHealth : 0f;
            float fullCombinedHealth = hc != null ? hc.fullCombinedHealth : 0f;
            float hpFraction = SafeDiv(combinedHealth, fullCombinedHealth);
            float lowHealthSignal = (hpFraction > 0f && hpFraction < _lowHealthThreshold) ? 1f : 0f;
            _lowHealthUptimeEma = Ema(_lowHealthUptimeEma, lowHealthSignal, alpha);

            // Avg TTK over window (smoothed).
            PruneOldTtkSamples();
            float avgTtkWindow = _ttkSamples.Count > 0 ? Average(_ttkSamples) : 0f;
            _avgTtkSecondsEma = Ema(_avgTtkSecondsEma, avgTtkWindow, alpha);
        }

        public void ObserveIncomingDamage(CharacterBody victimPlayerBody, float damage, float dt)
        {
            if (victimPlayerBody == null) return;
            if (damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage)) return;
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;

            float alpha = ComputeEmaAlpha(dt, _tauSeconds);
            float rate = damage / dt;
            _incomingDamageRateEma = Ema(_incomingDamageRateEma, rate, alpha);
            _hitRateOnPlayerEma = Ema(_hitRateOnPlayerEma, 1f / dt, alpha);
        }

        public void ObserveOutgoingDamage(CharacterBody attackerPlayerBody, CharacterBody victimMonsterBody, float damage, float dt)
        {
            if (attackerPlayerBody == null || victimMonsterBody == null) return;
            if (damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage)) return;
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;

            float alpha = ComputeEmaAlpha(dt, _tauSeconds);
            float rate = damage / dt;
            _outgoingDamageRateEma = Ema(_outgoingDamageRateEma, rate, alpha);

            int victimId = victimMonsterBody.gameObject != null ? victimMonsterBody.gameObject.GetInstanceID() : 0;
            if (victimId != 0 && !_victimFirstHitTime.ContainsKey(victimId))
            {
                _victimFirstHitTime[victimId] = Time.time;
            }
        }

        public void ObserveMonsterDeath(CharacterBody deadMonsterBody)
        {
            if (deadMonsterBody == null) return;
            int victimId = deadMonsterBody.gameObject != null ? deadMonsterBody.gameObject.GetInstanceID() : 0;
            if (victimId == 0) return;

            if (_victimFirstHitTime.TryGetValue(victimId, out float t0))
            {
                _victimFirstHitTime.Remove(victimId);
                float ttk = Mathf.Max(0f, Time.time - t0);
                AddTtkSample(ttk);
            }
        }

        public void ObservePlayerDeath()
        {
            float now = Time.time;
            _playerDeathTimes.Add(now);
            PruneOldDeathTimes(now);
        }

        public SgdSensorsSample GetCurrentSample()
        {
            float now = Time.time;
            PruneOldDeathTimes(now);

            float deathsPerWindow = _playerDeathTimes.Count;
            return new SgdSensorsSample(
                incomingDamageRate: Sanitize(_incomingDamageRateEma),
                outgoingDamageRate: Sanitize(_outgoingDamageRateEma),
                hitRateOnPlayer: Sanitize(_hitRateOnPlayerEma),
                combatUptime: Mathf.Clamp01(Sanitize(_combatUptimeEma)),
                lowHealthUptime: Mathf.Clamp01(Sanitize(_lowHealthUptimeEma)),
                deathsPerWindow: deathsPerWindow,
                avgTtkSeconds: Sanitize(_avgTtkSecondsEma)
            );
        }

        private void AddTtkSample(float ttkSeconds)
        {
            if (float.IsNaN(ttkSeconds) || float.IsInfinity(ttkSeconds)) return;
            ttkSeconds = Mathf.Clamp(ttkSeconds, 0f, 600f);
            _ttkSamples.Add(ttkSeconds);
            PruneOldTtkSamples();
        }

        private void PruneOldDeathTimes(float now)
        {
            float cutoff = now - _windowSeconds;
            for (int i = _playerDeathTimes.Count - 1; i >= 0; i--)
            {
                if (_playerDeathTimes[i] < cutoff)
                {
                    _playerDeathTimes.RemoveAt(i);
                }
            }
        }

        private void PruneOldTtkSamples()
        {
            // We keep only the last N samples to avoid unbounded growth.
            // Window logic is approximate; TTK is smoothed anyway.
            const int maxSamples = 64;
            int extra = _ttkSamples.Count - maxSamples;
            if (extra > 0)
            {
                _ttkSamples.RemoveRange(0, extra);
            }
        }

        private static float Ema(float prev, float x, float alpha)
        {
            if (float.IsNaN(x) || float.IsInfinity(x)) return prev;
            if (float.IsNaN(prev) || float.IsInfinity(prev)) prev = 0f;
            return Mathf.Lerp(prev, x, alpha);
        }

        private static float ComputeEmaAlpha(float dt, float tauSeconds)
        {
            tauSeconds = Mathf.Max(0.01f, tauSeconds);
            float alpha = 1f - Mathf.Exp(-dt / tauSeconds);
            if (float.IsNaN(alpha) || float.IsInfinity(alpha)) return 1f;
            return Mathf.Clamp01(alpha);
        }

        private static float SafeDiv(float a, float b)
        {
            if (b <= 0f || float.IsNaN(b) || float.IsInfinity(b)) return 0f;
            float r = a / b;
            if (float.IsNaN(r) || float.IsInfinity(r)) return 0f;
            return r;
        }

        private static float Average(List<float> values)
        {
            if (values == null || values.Count == 0) return 0f;
            double sum = 0;
            for (int i = 0; i < values.Count; i++) sum += values[i];
            return (float)(sum / values.Count);
        }

        private static float Sanitize(float x)
        {
            if (float.IsNaN(x) || float.IsInfinity(x)) return 0f;
            return Mathf.Max(0f, x);
        }
    }
}

