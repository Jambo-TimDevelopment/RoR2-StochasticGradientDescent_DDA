using GeneticsArtifact.SgdEngine;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace GeneticsArtifact.Telemetry
{
    internal static class TelemetrySampleBuilder
    {
        public static TelemetryEvent BuildSessionStart(TelemetrySessionState session)
        {
            var props = BuildCommonProperties(session);
            props["event_kind"] = "session_start";
            props["telemetry_enabled"] = ConfigManager.telemetryEnabled.Value;
            props["sample_interval_seconds"] = ConfigManager.telemetrySampleIntervalSeconds.Value;
            props["flush_interval_seconds"] = ConfigManager.telemetryFlushIntervalSeconds.Value;
            return new TelemetryEvent("dda_session_start", props);
        }

        public static TelemetryEvent BuildSample(TelemetrySessionState session, float dt)
        {
            var props = BuildCommonProperties(session);
            var sensors = SgdSensorsRuntimeState.HasSample ? SgdSensorsRuntimeState.Sample : default;
            var vp = SgdRuntimeState.HasVirtualPower ? SgdRuntimeState.VirtualPower : default;
            var snapshot = TelemetryDifficultySnapshot.Capture();

            AddSensorProperties(props, sensors);

            float hpSkill = EstimateMaxHealthSkill01(sensors);
            float msSkill = EstimateMoveSpeedSkill01(sensors);
            float asSkill = EstimateAttackSpeedSkill01(sensors);
            float dmgSkill = EstimateAttackDamageSkill01(sensors);

            float hpChallenge = Challenge01(GeneStat.MaxHealth, snapshot.MaxHealth);
            float msChallenge = Challenge01(GeneStat.MoveSpeed, snapshot.MoveSpeed);
            float asChallenge = Challenge01(GeneStat.AttackSpeed, snapshot.AttackSpeed);
            float dmgChallenge = Challenge01(GeneStat.AttackDamage, snapshot.AttackDamage);

            float hpError = hpChallenge - hpSkill;
            float msError = msChallenge - msSkill;
            float asError = asChallenge - asSkill;
            float dmgError = dmgChallenge - dmgSkill;

            AddAxisProperties(props, "max_health", hpSkill, hpChallenge, hpError, snapshot.MaxHealth, session.PreviousMaxHealthMultiplier, session.IsJump(snapshot.MaxHealth, session.PreviousMaxHealthMultiplier));
            AddAxisProperties(props, "move_speed", msSkill, msChallenge, msError, snapshot.MoveSpeed, session.PreviousMoveSpeedMultiplier, session.IsJump(snapshot.MoveSpeed, session.PreviousMoveSpeedMultiplier));
            AddAxisProperties(props, "attack_speed", asSkill, asChallenge, asError, snapshot.AttackSpeed, session.PreviousAttackSpeedMultiplier, session.IsJump(snapshot.AttackSpeed, session.PreviousAttackSpeedMultiplier));
            AddAxisProperties(props, "attack_damage", dmgSkill, dmgChallenge, dmgError, snapshot.AttackDamage, session.PreviousAttackDamageMultiplier, session.IsJump(snapshot.AttackDamage, session.PreviousAttackDamageMultiplier));

            float virtualChallenge = ComputeVirtualChallenge(snapshot);
            float virtualGapAbs = Mathf.Abs(virtualChallenge - vp.Total);
            float deltaVirtualPower = vp.Total - session.PreviousVirtualPower;
            float deltaVirtualChallenge = virtualChallenge - session.PreviousVirtualChallenge;
            float meanAbsError = (Mathf.Abs(hpError) + Mathf.Abs(msError) + Mathf.Abs(asError) + Mathf.Abs(dmgError)) * 0.25f;
            float degradationSignal = session.ComputeDegradationSignal(sensors, meanAbsError);

            props["virtual_power_total"] = vp.Total;
            props["virtual_power_offense"] = vp.Offense;
            props["virtual_power_defense"] = vp.Defense;
            props["virtual_power_mobility"] = vp.Mobility;
            props["virtual_challenge_total"] = virtualChallenge;
            props["virtual_gap_abs"] = virtualGapAbs;
            props["delta_virtual_power"] = deltaVirtualPower;
            props["delta_virtual_challenge"] = deltaVirtualChallenge;
            props["degradation_signal"] = degradationSignal;
            props["is_degraded"] = session.IsDegraded;
            props["recovery_elapsed_seconds"] = session.RecoveryElapsedSeconds;

            session.RecordSample(Mathf.Abs(hpError), Mathf.Abs(msError), Mathf.Abs(asError), Mathf.Abs(dmgError), virtualGapAbs, snapshot, sensors, dt);
            session.SetPreviousVirtuals(vp.Total, virtualChallenge);

            return new TelemetryEvent("dda_sample", props);
        }

        public static TelemetryEvent BuildRecovery(TelemetrySessionState session, float recoverySeconds)
        {
            var props = BuildCommonProperties(session);
            props["event_kind"] = "recovery";
            props["recovery_elapsed_seconds"] = recoverySeconds;
            props["recovery_events_count"] = session.RecoveryEventsCount;
            return new TelemetryEvent("dda_recovery", props);
        }

        public static TelemetryEvent BuildSessionEnd(TelemetrySessionState session)
        {
            var props = BuildCommonProperties(session);
            props["event_kind"] = "session_end";
            props["duration_seconds"] = session.ElapsedSeconds;
            props["samples_count"] = session.SamplesCount;
            props["mean_abs_error_max_health"] = session.MeanAbsErrorMaxHealth;
            props["mean_abs_error_move_speed"] = session.MeanAbsErrorMoveSpeed;
            props["mean_abs_error_attack_speed"] = session.MeanAbsErrorAttackSpeed;
            props["mean_abs_error_attack_damage"] = session.MeanAbsErrorAttackDamage;
            props["jump_rate_all_axes"] = session.JumpRateAllAxes;
            props["mean_virtual_gap_abs"] = session.MeanVirtualGapAbs;
            props["recovery_events_count"] = session.RecoveryEventsCount;
            props["mean_recovery_seconds"] = session.MeanRecoverySeconds;
            return new TelemetryEvent("dda_session_end", props);
        }

        private static Dictionary<string, object> BuildCommonProperties(TelemetrySessionState session)
        {
            var snapshot = TelemetryDifficultySnapshot.Capture();
            return new Dictionary<string, object>
            {
                ["distinct_id"] = ConfigManager.telemetryAnonymousUserId.Value,
                ["$process_person_profile"] = false,
                ["session_id"] = session.SessionId,
                ["mod_version"] = GeneticsArtifactPlugin.ModVer,
                ["telemetry_schema_version"] = 1,
                ["experiment_id"] = ConfigManager.telemetryExperimentId.Value,
                ["run_elapsed_seconds"] = session.ElapsedSeconds,
                ["stage_name"] = Stage.instance?.sceneDef?.baseSceneName ?? "",
                ["stage_index"] = Run.instance != null ? Run.instance.stageClearCount + 1 : 0,
                ["player_body"] = SgdSensorsRuntimeState.PlayerBodyName,
                ["dda_mode"] = snapshot.Mode,
                ["artifact_enabled"] = IsArtifactEnabled(),
                ["is_network_server"] = NetworkServer.active,
                ["queue_count"] = TelemetryEventQueue.Count
            };
        }

        private static void AddSensorProperties(Dictionary<string, object> props, in SgdSensorsSample s)
        {
            props["incoming_damage_rate"] = s.IncomingDamageRate;
            props["incoming_damage_norm01"] = s.IncomingDamageNorm01;
            props["outgoing_damage_rate"] = s.OutgoingDamageRate;
            props["outgoing_damage_norm01"] = s.OutgoingDamageNorm01;
            props["hit_rate_on_player"] = s.HitRateOnPlayer;
            props["hit_rate_on_player_norm01"] = s.HitRateOnPlayerNorm01;
            props["combat_uptime"] = s.CombatUptime;
            props["low_health_uptime"] = s.LowHealthUptime;
            props["deaths_per_window"] = s.DeathsPerWindow;
            props["deaths_per_window_norm01"] = s.DeathsPerWindowNorm01;
            props["avg_ttk_seconds"] = s.AvgTtkSeconds;
            props["avg_ttk_seconds_norm01"] = s.AvgTtkSecondsNorm01;
        }

        private static void AddAxisProperties(
            Dictionary<string, object> props,
            string axis,
            float skill01,
            float challenge01,
            float error,
            float multiplier,
            float previousMultiplier,
            bool isJump)
        {
            string prefix = "axis_" + axis + "_";
            props[prefix + "skill01"] = skill01;
            props[prefix + "challenge01"] = challenge01;
            props[prefix + "error"] = error;
            props[prefix + "abs_error"] = Mathf.Abs(error);
            props[prefix + "multiplier"] = multiplier;
            props[prefix + "delta_multiplier"] = multiplier - previousMultiplier;
            props[prefix + "is_jump"] = isJump;
        }

        private static float EstimateAttackSpeedSkill01(in SgdSensorsSample s)
        {
            float evasion = 1f - Mathf.Clamp01(s.HitRateOnPlayerNorm01);
            float survivability = 1f - Mathf.Clamp01(s.IncomingDamageNorm01);
            float safety = 1f - Mathf.Clamp01(s.LowHealthUptime);
            float deaths = 1f - Mathf.Clamp01(s.DeathsPerWindowNorm01);
            return ClampSkill((0.40f * evasion) + (0.35f * survivability) + (0.20f * safety) + (0.05f * deaths));
        }

        private static float EstimateMaxHealthSkill01(in SgdSensorsSample s)
        {
            float outgoing = Mathf.Clamp01(s.OutgoingDamageNorm01);
            float ttkSkill = s.AvgTtkSeconds > 0.01f ? (1f - Mathf.Clamp01(s.AvgTtkSecondsNorm01)) : 0.50f;
            float safety = 1f - Mathf.Clamp01(s.LowHealthUptime);
            return ClampSkill((0.45f * outgoing) + (0.45f * ttkSkill) + (0.10f * safety));
        }

        private static float EstimateMoveSpeedSkill01(in SgdSensorsSample s)
        {
            float evasion = 1f - Mathf.Clamp01(s.HitRateOnPlayerNorm01);
            float survivability = 1f - Mathf.Clamp01(s.IncomingDamageNorm01);
            float outgoing = Mathf.Clamp01(s.OutgoingDamageNorm01);
            float safety = 1f - Mathf.Clamp01(s.LowHealthUptime);
            return ClampSkill((0.45f * evasion) + (0.25f * survivability) + (0.20f * outgoing) + (0.10f * safety));
        }

        private static float EstimateAttackDamageSkill01(in SgdSensorsSample s)
        {
            float survivability = 1f - Mathf.Clamp01(s.IncomingDamageNorm01);
            float safety = 1f - Mathf.Clamp01(s.LowHealthUptime);
            float deaths = 1f - Mathf.Clamp01(s.DeathsPerWindowNorm01);
            float evasion = 1f - Mathf.Clamp01(s.HitRateOnPlayerNorm01);
            return ClampSkill((0.45f * survivability) + (0.30f * safety) + (0.20f * deaths) + (0.05f * evasion));
        }

        private static float Challenge01(GeneStat stat, float multiplier)
        {
            SgdAxisLimitProvider.GetLimits(stat, out float floor, out float cap);
            float thetaMin = Mathf.Log(floor);
            float thetaMax = Mathf.Log(cap);
            float thetaRange = Mathf.Max(0.0001f, thetaMax - thetaMin);
            float theta = Mathf.Clamp(Mathf.Log(Mathf.Max(0.0001f, multiplier)), thetaMin, thetaMax);
            return Mathf.Clamp01((theta - thetaMin) / thetaRange);
        }

        private static float ComputeVirtualChallenge(TelemetryDifficultySnapshot snapshot)
        {
            float hp = SafeLog(snapshot.MaxHealth);
            float ms = SafeLog(snapshot.MoveSpeed);
            float attackSpeed = SafeLog(snapshot.AttackSpeed);
            float attackDamage = SafeLog(snapshot.AttackDamage);
            return (0.35f * hp) + (0.15f * ms) + (0.20f * attackSpeed) + (0.30f * attackDamage);
        }

        private static bool IsArtifactEnabled()
        {
            return RunArtifactManager.instance != null &&
                   ArtifactOfGenetics.artifactDef != null &&
                   RunArtifactManager.instance.IsArtifactEnabled(ArtifactOfGenetics.artifactDef);
        }

        private static float ClampSkill(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);
        }

        private static float SafeLog(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f) return 0f;
            return Mathf.Log(value);
        }
    }
}
