using RoR2;
using UnityEngine;

namespace GeneticsArtifact.SgdEngine
{
    /// <summary>
    /// Stable, low-noise estimate of player's virtual power V_p(t).
    /// Uses the same four axes as SGD difficulty actuators: HP, move speed,
    /// attack speed, and attack damage. Item tags add proc/synergy power that
    /// is not always visible in CharacterBody scalar stats.
    /// </summary>
    public sealed class SgdVirtualPowerEstimator
    {
        public const float WeightHp = 0.25f;
        public const float WeightMoveSpeed = 0.25f;
        public const float WeightAttackSpeed = 0.25f;
        public const float WeightAttackDamage = 0.25f;

        // Legacy telemetry aliases kept for one schema transition.
        public const float WeightOffense = WeightAttackDamage;
        public const float WeightDefense = WeightHp;
        public const float WeightMobility = WeightMoveSpeed;

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

            // HP axis: effective health + weighted regeneration.
            float hp = Mathf.Max(0f, body.maxHealth);
            float shield = Mathf.Max(0f, body.maxShield);
            float combined = hp + shield;

            // EHP approximation via armor factor:
            // damageTakenMultiplier = 100/(100+armor) => EHP = combined/(mult) = combined*(100+armor)/100.
            // Clamp the factor for extreme negative/positive armor.
            float armorFactor = Mathf.Clamp((100f + body.armor) / 100f, 0.05f, 10f);
            float ehp = combined * armorFactor;

            float regen = Mathf.Max(0f, body.regen);
            float hpRaw = ehp + (RegenWeight * regen);

            float moveSpeedRaw = Mathf.Max(0f, body.moveSpeed);
            float attackSpeedRaw = Mathf.Max(0f, body.attackSpeed);

            // AttackDamage axis: expected hit damage. Crit is approximated as expected x2:
            // E[mult] ~= 1 + p, where p=critChance.
            float damage = Mathf.Max(0f, body.damage);
            float critChance = Mathf.Clamp(body.crit, 0f, 100f) / 100f;
            float attackDamageRaw = damage * (1f + critChance);

            return new SgdVirtualPowerSample(hpRaw, moveSpeedRaw, attackSpeedRaw, attackDamageRaw);
        }

        public SgdVirtualPowerSample ComputeSmoothed(CharacterBody body, float dt)
        {
            var raw = ComputeRaw(body);

            // Log compression + item-aware additive bonus in the same compressed space.
            var itemBonus = SgdBuildPowerItemModel.EstimateInventoryBonus(body?.inventory);
            float hp = SafeLog1p(raw.Hp) + itemBonus.Hp;
            float moveSpeed = SafeLog1p(raw.MoveSpeed) + itemBonus.MoveSpeed;
            float attackSpeed = SafeLog1p(raw.AttackSpeed) + itemBonus.AttackSpeed;
            float attackDamage = SafeLog1p(raw.AttackDamage) + itemBonus.AttackDamage;

            var sample = new SgdVirtualPowerSample(hp, moveSpeed, attackSpeed, attackDamage);

            float alpha = ComputeEmaAlpha(dt, _tauSeconds);
            if (!_hasEma)
            {
                _ema = sample;
                _hasEma = true;
                return _ema;
            }

            _ema = new SgdVirtualPowerSample(
                hp: Mathf.Lerp(_ema.Hp, sample.Hp, alpha),
                moveSpeed: Mathf.Lerp(_ema.MoveSpeed, sample.MoveSpeed, alpha),
                attackSpeed: Mathf.Lerp(_ema.AttackSpeed, sample.AttackSpeed, alpha),
                attackDamage: Mathf.Lerp(_ema.AttackDamage, sample.AttackDamage, alpha)
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

