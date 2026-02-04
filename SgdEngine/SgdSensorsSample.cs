namespace GeneticsArtifact.SgdEngine
{
    /// <summary>
    /// Minimal mandatory sensors (telemetry) for controlling GeneStat actuators (HP/MS/AS/DMG).
    /// Rates are expected to be smoothed (e.g., EMA).
    /// </summary>
    public readonly struct SgdSensorsSample
    {
        public readonly float IncomingDamageRate;
        public readonly float OutgoingDamageRate;
        public readonly float HitRateOnPlayer;
        public readonly float CombatUptime;
        public readonly float LowHealthUptime;
        public readonly float DeathsPerWindow;
        public readonly float AvgTtkSeconds;

        public SgdSensorsSample(
            float incomingDamageRate,
            float outgoingDamageRate,
            float hitRateOnPlayer,
            float combatUptime,
            float lowHealthUptime,
            float deathsPerWindow,
            float avgTtkSeconds)
        {
            IncomingDamageRate = incomingDamageRate;
            OutgoingDamageRate = outgoingDamageRate;
            HitRateOnPlayer = hitRateOnPlayer;
            CombatUptime = combatUptime;
            LowHealthUptime = lowHealthUptime;
            DeathsPerWindow = deathsPerWindow;
            AvgTtkSeconds = avgTtkSeconds;
        }
    }
}

