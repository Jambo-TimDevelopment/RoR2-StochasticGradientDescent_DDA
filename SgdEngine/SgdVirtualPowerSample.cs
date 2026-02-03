namespace GeneticsArtifact.SgdEngine
{
    /// <summary>
    /// Virtual power estimate V_p(t) for the player build.
    /// Values are in a compressed space (e.g., log1p) and optionally smoothed.
    /// </summary>
    public readonly struct SgdVirtualPowerSample
    {
        public readonly float Offense;
        public readonly float Defense;
        public readonly float Mobility;
        public readonly float Total;

        public SgdVirtualPowerSample(float offense, float defense, float mobility, float total)
        {
            Offense = offense;
            Defense = defense;
            Mobility = mobility;
            Total = total;
        }
    }
}

