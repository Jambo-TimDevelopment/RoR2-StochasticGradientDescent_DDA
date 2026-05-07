namespace GeneticsArtifact.SgdEngine
{
    /// <summary>
    /// Four-axis virtual power estimate V_p(t) for the player build.
    /// Values are in a compressed space (e.g., log1p) and optionally smoothed.
    /// </summary>
    public readonly struct SgdVirtualPowerSample
    {
        public readonly float Hp;
        public readonly float MoveSpeed;
        public readonly float AttackSpeed;
        public readonly float AttackDamage;
        public readonly float Total;

        // Legacy aliases kept for one telemetry/debug transition window.
        public float Offense => AttackDamage;
        public float Defense => Hp;
        public float Mobility => MoveSpeed;

        public SgdVirtualPowerSample(float hp, float moveSpeed, float attackSpeed, float attackDamage)
        {
            Hp = hp;
            MoveSpeed = moveSpeed;
            AttackSpeed = attackSpeed;
            AttackDamage = attackDamage;
            Total = (hp + moveSpeed + attackSpeed + attackDamage) * 0.25f;
        }
    }
}

