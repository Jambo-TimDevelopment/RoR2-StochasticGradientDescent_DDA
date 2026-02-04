namespace GeneticsArtifact.SgdEngine
{
    /// <summary>
    /// Minimal mandatory sensors (telemetry) for controlling GeneStat actuators (HP/MS/AS/DMG).
    /// Rates are expected to be smoothed (e.g., EMA).
    /// </summary>
    public readonly struct SgdSensorsSample
    {
        public readonly float IncomingDamageRate;
        public readonly float IncomingDamageNorm01;
        public readonly float OutgoingDamageRate;
        public readonly float OutgoingDamageNorm01;
        public readonly float HitRateOnPlayer;
        public readonly float HitRateOnPlayerNorm01;
        public readonly float CombatUptime;
        public readonly float LowHealthUptime;
        public readonly float DeathsPerWindow;
        public readonly float DeathsPerWindowNorm01;
        public readonly float AvgTtkSeconds;
        public readonly float AvgTtkSecondsNorm01;

        public SgdSensorsSample(
            float incomingDamageRate,
            float incomingDamageNorm01,
            float outgoingDamageRate,
            float outgoingDamageNorm01,
            float hitRateOnPlayer,
            float hitRateOnPlayerNorm01,
            float combatUptime,
            float lowHealthUptime,
            float deathsPerWindow,
            float deathsPerWindowNorm01,
            float avgTtkSeconds,
            float avgTtkSecondsNorm01)
        {
            IncomingDamageRate = incomingDamageRate;
            IncomingDamageNorm01 = incomingDamageNorm01;
            OutgoingDamageRate = outgoingDamageRate;
            OutgoingDamageNorm01 = outgoingDamageNorm01;
            HitRateOnPlayer = hitRateOnPlayer;
            HitRateOnPlayerNorm01 = hitRateOnPlayerNorm01;
            CombatUptime = combatUptime;
            LowHealthUptime = lowHealthUptime;
            DeathsPerWindow = deathsPerWindow;
            DeathsPerWindowNorm01 = deathsPerWindowNorm01;
            AvgTtkSeconds = avgTtkSeconds;
            AvgTtkSecondsNorm01 = avgTtkSecondsNorm01;
        }
    }
}

