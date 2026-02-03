using RoR2;
using UnityEngine;

namespace GeneticsArtifact.SgdEngine
{
    /// <summary>
    /// Stable, low-noise estimate of player's virtual power V_p(t).
    /// Uses absolute raw proxies + log compression + EMA smoothing.
    /// </summary>
    public sealed class SgdVirtualPowerEstimator
    {
        // Total aggregation weights.
        public const float WeightOffense = 0.50f;
        public const float WeightDefense = 0.35f;
        public const float WeightMobility = 0.15f;

        // Make regen comparable to EHP (still compressed by log1p).
        public const float RegenWeight = 25f;

        // EMA time constant (seconds). Higher = smoother.
        public const float DefaultTauSeconds = 7.5f;

        private float _tauSeconds;
        private bool _hasEma;
        private SgdVirtualPowerSample _ema;

        public SgdVirtualPowerEstimator(float tauSeconds = DefaultTauSeconds)
        {
            _tauSeconds = Mathf.Max(0.1f, tauSeconds);
        }

        public void Reset()
        {
            _hasEma = false;
            _ema = default;
        }

        public static SgdVirtualPowerSample ComputeRaw(CharacterBody body)
        {
            if (body == null)
            {
                return default;
            }

            // Offense proxy: DPS-like term. Crit is approximated as expected x2:
            // E[mult] ≈ 1 + p, where p=critChance.
            float damage = Mathf.Max(0f, body.damage);
            float attackSpeed = Mathf.Max(0f, body.attackSpeed);
            float critChance = Mathf.Clamp(body.crit, 0f, 100f) / 100f;
            float offenseRaw = damage * attackSpeed * (1f + critChance);

            // Defense proxy: effective health + weighted regen.
            // Use combined max HP+shield; barrier is ignored (too volatile).
            float hp = Mathf.Max(0f, body.maxHealth);
            float shield = Mathf.Max(0f, body.maxShield);
            float combined = hp + shield;

            // EHP approximation via armor factor:
            // damageTakenMultiplier = 100/(100+armor) => EHP = combined/(mult) = combined*(100+armor)/100.
            // Clamp the factor for extreme negative/positive armor.
            float armorFactor = Mathf.Clamp((100f + body.armor) / 100f, 0.05f, 10f);
            float ehp = combined * armorFactor;

            float regen = Mathf.Max(0f, body.regen);
            float defenseRaw = ehp + (RegenWeight * regen);

            // Mobility proxy: move speed.
            float mobilityRaw = Mathf.Max(0f, body.moveSpeed);

            return new SgdVirtualPowerSample(offenseRaw, defenseRaw, mobilityRaw, total: 0f);
        }

        public SgdVirtualPowerSample ComputeSmoothed(CharacterBody body, float dt)
        {
            var raw = ComputeRaw(body);

            // Log compression.
            float o = SafeLog1p(raw.Offense);
            float d = SafeLog1p(raw.Defense);
            float m = SafeLog1p(raw.Mobility);
            float total = (WeightOffense * o) + (WeightDefense * d) + (WeightMobility * m);

            var sample = new SgdVirtualPowerSample(o, d, m, total);

            float alpha = ComputeEmaAlpha(dt, _tauSeconds);
            if (!_hasEma)
            {
                _ema = sample;
                _hasEma = true;
                return _ema;
            }

            _ema = new SgdVirtualPowerSample(
                offense: Mathf.Lerp(_ema.Offense, sample.Offense, alpha),
                defense: Mathf.Lerp(_ema.Defense, sample.Defense, alpha),
                mobility: Mathf.Lerp(_ema.Mobility, sample.Mobility, alpha),
                total: Mathf.Lerp(_ema.Total, sample.Total, alpha)
            );

            return _ema;
        }

        private static float SafeLog1p(float x)
        {
            if (float.IsNaN(x) || float.IsInfinity(x) || x <= 0f) return 0f;
            return Mathf.Log(x + 1f);
        }

        private static float ComputeEmaAlpha(float dt, float tauSeconds)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return 1f;
            tauSeconds = Mathf.Max(0.01f, tauSeconds);
            // alpha = 1 - exp(-dt/tau)
            float alpha = 1f - Mathf.Exp(-dt / tauSeconds);
            if (float.IsNaN(alpha) || float.IsInfinity(alpha)) return 1f;
            return Mathf.Clamp01(alpha);
        }
    }
}

