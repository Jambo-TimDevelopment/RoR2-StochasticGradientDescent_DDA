using GeneticsArtifact.SgdEngine;
using GeneticsArtifact.SgdEngine.Decision;
using RoR2;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
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

            AddAxisProperties(props, "max_health", "hp", hpSkill, hpChallenge, hpError, snapshot.MaxHealth, session.PreviousMaxHealthMultiplier, session.PreviousMaxHealthSkill01, session.PreviousMaxHealthChallenge01, session.HasPreviousSample, session.IsJump(snapshot.MaxHealth, session.PreviousMaxHealthMultiplier));
            AddAxisProperties(props, "move_speed", "moveSpeed", msSkill, msChallenge, msError, snapshot.MoveSpeed, session.PreviousMoveSpeedMultiplier, session.PreviousMoveSpeedSkill01, session.PreviousMoveSpeedChallenge01, session.HasPreviousSample, session.IsJump(snapshot.MoveSpeed, session.PreviousMoveSpeedMultiplier));
            AddAxisProperties(props, "attack_speed", "attackSpeed", asSkill, asChallenge, asError, snapshot.AttackSpeed, session.PreviousAttackSpeedMultiplier, session.PreviousAttackSpeedSkill01, session.PreviousAttackSpeedChallenge01, session.HasPreviousSample, session.IsJump(snapshot.AttackSpeed, session.PreviousAttackSpeedMultiplier));
            AddAxisProperties(props, "attack_damage", "damage", dmgSkill, dmgChallenge, dmgError, snapshot.AttackDamage, session.PreviousAttackDamageMultiplier, session.PreviousAttackDamageSkill01, session.PreviousAttackDamageChallenge01, session.HasPreviousSample, session.IsJump(snapshot.AttackDamage, session.PreviousAttackDamageMultiplier));

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
            props["is_within_stable_error_epsilon"] = meanAbsError <= (ConfigManager.telemetryStableErrorEpsilon?.Value ?? 0.10f);
            props["is_within_virtual_gap_epsilon"] = virtualGapAbs <= (ConfigManager.telemetryVirtualGapEpsilon?.Value ?? 0.50f);
            props["h3_axis_model"] = "damage,attackSpeed,hp,moveSpeed";
            props["h3_axis_mean_abs_error"] = meanAbsError;
            props["h3_legacy_virtual_gap_abs"] = virtualGapAbs;

            AddPlayerBuildProperties(props, FindAnyPlayerBody());

            session.RecordSample(
                Mathf.Abs(hpError),
                Mathf.Abs(msError),
                Mathf.Abs(asError),
                Mathf.Abs(dmgError),
                hpSkill,
                msSkill,
                asSkill,
                dmgSkill,
                hpChallenge,
                msChallenge,
                asChallenge,
                dmgChallenge,
                virtualGapAbs,
                snapshot,
                sensors,
                dt);
            session.SetPreviousVirtuals(vp.Total, virtualChallenge);

            props["degradation_signal"] = degradationSignal;
            props["is_degraded"] = session.IsDegraded;
            props["is_degraded_050"] = degradationSignal >= 0.50f;
            props["is_degraded_060"] = degradationSignal >= 0.60f;
            props["is_degraded_070"] = degradationSignal >= 0.70f;
            props["degradation_signal_above_050_seconds"] = session.DegradationSignalAbove050Seconds;
            props["degradation_signal_above_060_seconds"] = session.DegradationSignalAbove060Seconds;
            props["degradation_signal_above_070_seconds"] = session.DegradationSignalAbove070Seconds;
            props["degradation_signal_below_recovery_seconds"] = session.DegradationSignalBelowRecoverySeconds;
            props["recovery_elapsed_seconds"] = session.RecoveryElapsedSeconds;
            props["current_degradation_trigger"] = session.CurrentDegradationTrigger;
            props["current_degradation_trigger_value"] = session.CurrentDegradationTriggerValue;

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

        public static TelemetryEvent BuildDegradationTransition(TelemetrySessionState session, TelemetryDegradationTransition transition)
        {
            var props = BuildCommonProperties(session);
            props["event_kind"] = transition.EventKind;
            props["transition_run_elapsed_seconds"] = transition.RunElapsedSeconds;
            props["recovery_elapsed_seconds"] = transition.RecoveryElapsedSeconds;
            props["degradation_trigger"] = transition.Trigger;
            props["degradation_trigger_value"] = transition.TriggerValue;
            props["degradation_signal"] = transition.DegradationSignal;
            props["incoming_damage_norm01"] = transition.IncomingDamageNorm01;
            props["deaths_per_window_norm01"] = transition.DeathsPerWindowNorm01;
            props["low_health_uptime"] = transition.LowHealthUptime;
            props["mean_abs_error_all_axes"] = transition.MeanAbsError;
            props["degradation_threshold"] = ConfigManager.telemetryDegradationThreshold.Value;
            props["recovery_threshold"] = ConfigManager.telemetryRecoveryThreshold.Value;
            props["degradation_signal_above_050_seconds"] = session.DegradationSignalAbove050Seconds;
            props["degradation_signal_above_060_seconds"] = session.DegradationSignalAbove060Seconds;
            props["degradation_signal_above_070_seconds"] = session.DegradationSignalAbove070Seconds;
            props["degradation_signal_below_recovery_seconds"] = session.DegradationSignalBelowRecoverySeconds;
            return new TelemetryEvent(transition.EventKind == "degradation_start" ? "dda_degradation_start" : "dda_degradation_end", props);
        }

        public static TelemetryEvent BuildPlayerDeath(TelemetrySessionState session, CharacterBody body, DamageInfo damageInfo)
        {
            var props = BuildCommonProperties(session);
            props["event_kind"] = "player_death";
            props["player_deaths_count"] = session.PlayerDeathsCount;
            props["death_body_name"] = body != null ? body.GetDisplayName() : "";
            props["death_damage"] = damageInfo != null ? damageInfo.damage : 0f;
            props["death_damage_type"] = damageInfo != null ? damageInfo.damageType.ToString() : "";
            props["death_attacker_body"] = GetAttackerBodyName(damageInfo);
            AddPlayerBuildProperties(props, body);
            return new TelemetryEvent("dda_player_death", props);
        }

        public static TelemetryEvent BuildPostSessionSurvey(TelemetrySessionState session, int fairnessLikert, int continuityLikert, string comment)
        {
            var props = BuildCommonProperties(session);
            props["event_kind"] = "post_session_survey";
            props["fairness_likert_1_7"] = fairnessLikert;
            props["continuity_likert_1_7"] = continuityLikert;
            props["survey_comment"] = comment ?? "";
            return new TelemetryEvent("dda_post_session_survey", props);
        }

        public static TelemetryEvent BuildPostSessionSurveySkipped(TelemetrySessionState session, string comment)
        {
            var props = BuildCommonProperties(session);
            props["event_kind"] = "post_session_survey_skipped";
            props["survey_comment"] = comment ?? "";
            return new TelemetryEvent("dda_post_session_survey_skipped", props);
        }

        public static TelemetryEvent BuildSessionEnd(TelemetrySessionState session, string endReason)
        {
            var props = BuildCommonProperties(session);
            props["event_kind"] = "session_end";
            props["end_reason"] = endReason ?? "run_destroyed";
            props["duration_seconds"] = session.ElapsedSeconds;
            props["samples_count"] = session.SamplesCount;
            props["mean_abs_error_max_health"] = session.MeanAbsErrorMaxHealth;
            props["mean_abs_error_move_speed"] = session.MeanAbsErrorMoveSpeed;
            props["mean_abs_error_attack_speed"] = session.MeanAbsErrorAttackSpeed;
            props["mean_abs_error_attack_damage"] = session.MeanAbsErrorAttackDamage;
            props["jump_rate_all_axes"] = session.JumpRateAllAxes;
            props["smoothness_observations"] = session.SmoothnessObservations;
            props["mean_abs_delta_multiplier"] = session.MeanAbsDeltaMultiplier;
            props["mean_abs_delta_theta"] = session.MeanAbsDeltaTheta;
            props["mean_abs_relative_delta_multiplier"] = session.MeanAbsRelativeDeltaMultiplier;
            props["max_abs_delta_multiplier"] = session.MaxAbsDeltaMultiplier;
            props["mean_virtual_gap_abs"] = session.MeanVirtualGapAbs;
            props["h3_axis_mean_abs_error"] =
                (session.MeanAbsErrorMaxHealth +
                 session.MeanAbsErrorMoveSpeed +
                 session.MeanAbsErrorAttackSpeed +
                 session.MeanAbsErrorAttackDamage) * 0.25f;
            props["recovery_events_count"] = session.RecoveryEventsCount;
            props["degradation_events_count"] = session.DegradationEventsCount;
            props["mean_recovery_seconds"] = session.MeanRecoverySeconds;
            props["degradation_signal_above_050_seconds"] = session.DegradationSignalAbove050Seconds;
            props["degradation_signal_above_060_seconds"] = session.DegradationSignalAbove060Seconds;
            props["degradation_signal_above_070_seconds"] = session.DegradationSignalAbove070Seconds;
            props["degradation_signal_below_recovery_seconds"] = session.DegradationSignalBelowRecoverySeconds;
            props["player_deaths_count"] = session.PlayerDeathsCount;
            props["missed_sample_intervals"] = session.MissedSampleIntervals;
            props["minimum_session_seconds"] = ConfigManager.telemetryMinimumSessionSeconds.Value;
            props["is_quality_excluded_short_session"] = session.ElapsedSeconds < ConfigManager.telemetryMinimumSessionSeconds.Value;
            props["fairness_likert_1_7"] = session.SurveyFairnessLikert;
            props["continuity_likert_1_7"] = session.SurveyContinuityLikert;
            props["survey_comment"] = session.SurveyComment;
            return new TelemetryEvent("dda_session_end", props);
        }

        private static Dictionary<string, object> BuildCommonProperties(TelemetrySessionState session)
        {
            var snapshot = TelemetryDifficultySnapshot.Capture();
            var props = new Dictionary<string, object>
            {
                ["distinct_id"] = ConfigManager.telemetryAnonymousUserId.Value,
                ["$process_person_profile"] = false,
                ["session_id"] = session.SessionId,
                ["participant_id"] = GetParticipantId(),
                ["mod_version"] = GeneticsArtifactPlugin.ModVer,
                ["telemetry_schema_version"] = 3,
                ["experiment_id"] = ConfigManager.telemetryExperimentId.Value,
                ["condition_order"] = ConfigManager.telemetryConditionOrder.Value,
                ["run_attempt_index"] = ConfigManager.telemetryRunAttemptIndex.Value,
                ["configured_run_seed"] = ConfigManager.telemetryConfiguredRunSeed.Value,
                ["research_seed_cycle"] = ConfigManager.researchRunSeedCycle.Value,
                ["research_seed_index"] = ConfigManager.researchCurrentRunSeedIndex.Value,
                ["research_cycle_seed"] = ConfigManager.researchCurrentRunSeed.Value,
                ["research_seed_rotation_enabled"] = ConfigManager.researchAutoRotateRunSeeds.Value,
                ["runtime_run_seed"] = GetRuntimeRunSeed(),
                ["run_elapsed_seconds"] = session.ElapsedSeconds,
                ["stage_name"] = Stage.instance?.sceneDef?.baseSceneName ?? "",
                ["stage_index"] = Run.instance != null ? Run.instance.stageClearCount + 1 : 0,
                ["player_body"] = SgdSensorsRuntimeState.PlayerBodyName,
                ["dda_mode"] = snapshot.Mode,
                ["artifact_enabled"] = IsArtifactEnabled(),
                ["is_network_server"] = NetworkServer.active,
                ["queue_count"] = TelemetryEventQueue.Count,
                ["missed_sample_intervals"] = session.MissedSampleIntervals,
                ["sgd_total_steps_done"] = SgdDecisionRuntimeState.TotalStepsDone,
                ["sgd_applied_monsters_last"] = SgdDecisionRuntimeState.AppliedMonstersLast
            };

            AddExperimentConfigProperties(props);
            return props;
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
            string plane,
            float skill01,
            float challenge01,
            float error,
            float multiplier,
            float previousMultiplier,
            float previousSkill01,
            float previousChallenge01,
            bool hasPrevious,
            bool isJump)
        {
            string prefix = "axis_" + axis + "_";
            float safeMultiplier = Mathf.Max(0.0001f, multiplier);
            float safePreviousMultiplier = Mathf.Max(0.0001f, previousMultiplier);
            float deltaMultiplier = hasPrevious ? multiplier - previousMultiplier : 0f;
            float deltaTheta = hasPrevious ? Mathf.Log(safeMultiplier) - Mathf.Log(safePreviousMultiplier) : 0f;
            float relativeDeltaMultiplier = hasPrevious ? deltaMultiplier / safePreviousMultiplier : 0f;

            props[prefix + "plane"] = plane;
            props[prefix + "skill01"] = skill01;
            props[prefix + "challenge01"] = challenge01;
            props[prefix + "error"] = error;
            props[prefix + "abs_error"] = Mathf.Abs(error);
            props[prefix + "multiplier"] = multiplier;
            props[prefix + "previous_multiplier"] = previousMultiplier;
            props[prefix + "delta_multiplier"] = deltaMultiplier;
            props[prefix + "abs_delta_multiplier"] = Mathf.Abs(deltaMultiplier);
            props[prefix + "delta_theta"] = deltaTheta;
            props[prefix + "abs_delta_theta"] = Mathf.Abs(deltaTheta);
            props[prefix + "relative_delta_multiplier"] = relativeDeltaMultiplier;
            props[prefix + "abs_relative_delta_multiplier"] = Mathf.Abs(relativeDeltaMultiplier);
            props[prefix + "delta_skill01"] = hasPrevious ? skill01 - previousSkill01 : 0f;
            props[prefix + "delta_challenge01"] = hasPrevious ? challenge01 - previousChallenge01 : 0f;
            props[prefix + "is_jump"] = isJump;
        }

        private static void AddExperimentConfigProperties(Dictionary<string, object> props)
        {
            props["tau_jump"] = ConfigManager.telemetryJumpThreshold.Value;
            props["epsilon_v"] = ConfigManager.telemetryVirtualGapEpsilon.Value;
            props["epsilon_stable"] = ConfigManager.telemetryStableErrorEpsilon.Value;
            props["degradation_threshold"] = ConfigManager.telemetryDegradationThreshold.Value;
            props["recovery_threshold"] = ConfigManager.telemetryRecoveryThreshold.Value;
            props["sample_interval_seconds"] = ConfigManager.telemetrySampleIntervalSeconds.Value;
            props["minimum_session_seconds"] = ConfigManager.telemetryMinimumSessionSeconds.Value;

            props["sgd_step_seconds"] = SgdDecisionRuntimeState.StepSeconds;
            props["sgd_momentum"] = SgdDecisionDriver.DefaultMomentum;
            props["sgd_gradient_clip"] = SgdDecisionDriver.DefaultGradientClip;
            props["sgd_velocity_clip"] = SgdDecisionDriver.DefaultVelocityClip;
            props["sgd_error_dead_zone"] = SgdDecisionDriver.DefaultErrorDeadZone;
            props["sgd_hp_learning_rate"] = SgdDecisionDriver.HpLearningRate;
            props["sgd_ms_learning_rate"] = SgdDecisionDriver.MsLearningRate;
            props["sgd_as_learning_rate"] = SgdDecisionDriver.AsLearningRate;
            props["sgd_dmg_learning_rate"] = SgdDecisionDriver.DmgLearningRate;
            props["sgd_hp_max_delta_theta"] = SgdDecisionDriver.HpMaxDeltaTheta;
            props["sgd_ms_max_delta_theta"] = SgdDecisionDriver.MsMaxDeltaTheta;
            props["sgd_as_max_delta_theta"] = SgdDecisionDriver.AsMaxDeltaTheta;
            props["sgd_dmg_max_delta_theta"] = SgdDecisionDriver.DmgMaxDeltaTheta;
            props["sgd_hp_floor"] = ConfigManager.sgdHpFloor.Value;
            props["sgd_hp_cap"] = ConfigManager.sgdHpCap.Value;
            props["sgd_ms_floor"] = ConfigManager.sgdMsFloor.Value;
            props["sgd_ms_cap"] = ConfigManager.sgdMsCap.Value;
            props["sgd_as_floor"] = ConfigManager.sgdAsFloor.Value;
            props["sgd_as_cap"] = ConfigManager.sgdAsCap.Value;
            props["sgd_dmg_floor"] = ConfigManager.sgdDmgFloor.Value;
            props["sgd_dmg_cap"] = ConfigManager.sgdDmgCap.Value;

            props["ga_governor_type"] = ConfigManager.governorType.Value;
            props["ga_time_limit_seconds"] = ConfigManager.timeLimit.Value;
            props["ga_death_limit"] = ConfigManager.deathLimit.Value;
            props["ga_gene_variance_limit"] = ConfigManager.geneVarianceLimit.Value;
            props["ga_gene_cap"] = ConfigManager.geneCap.Value;
            props["ga_gene_floor"] = ConfigManager.geneFloor.Value;
            props["ga_gene_product_limit"] = ConfigManager.geneProductLimit.Value;
        }

        private static void AddPlayerBuildProperties(Dictionary<string, object> props, CharacterBody body)
        {
            if (body == null)
            {
                props["player_build_available"] = false;
                return;
            }

            props["player_build_available"] = true;
            props["player_body_name"] = body.GetDisplayName();
            props["player_level"] = body.level;
            props["player_damage"] = body.damage;
            props["player_attack_speed"] = body.attackSpeed;
            props["player_crit"] = body.crit;
            props["player_max_health"] = body.maxHealth;
            props["player_max_shield"] = body.maxShield;
            props["player_regen"] = body.regen;
            props["player_armor"] = body.armor;
            props["player_move_speed"] = body.moveSpeed;

            var raw = SgdVirtualPowerEstimator.ComputeRaw(body);
            props["virtual_power_raw_offense"] = raw.Offense;
            props["virtual_power_raw_defense"] = raw.Defense;
            props["virtual_power_raw_mobility"] = raw.Mobility;
            props["virtual_power_weight_offense"] = SgdVirtualPowerEstimator.WeightOffense;
            props["virtual_power_weight_defense"] = SgdVirtualPowerEstimator.WeightDefense;
            props["virtual_power_weight_mobility"] = SgdVirtualPowerEstimator.WeightMobility;
            props["virtual_power_regen_weight"] = SgdVirtualPowerEstimator.RegenWeight;
            props["player_unique_items"] = CountUniqueItems(body.inventory);
            props["player_item_stacks_total"] = CountItemStacks(body.inventory);
            props["player_items_compact"] = BuildItemSummary(body.inventory);
        }

        private static CharacterBody FindAnyPlayerBody()
        {
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                if (body != null && body.isPlayerControlled)
                {
                    return body;
                }
            }

            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                if (body != null && body.teamComponent != null && body.teamComponent.teamIndex == TeamIndex.Player)
                {
                    return body;
                }
            }

            return null;
        }

        private static string GetParticipantId()
        {
            string participantId = ConfigManager.telemetryParticipantId?.Value;
            return string.IsNullOrWhiteSpace(participantId)
                ? ConfigManager.telemetryAnonymousUserId.Value
                : participantId.Trim();
        }

        private static string GetRuntimeRunSeed()
        {
            var run = Run.instance;
            if (run == null) return "";

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = run.GetType();

            var field = type.GetField("seed", flags) ?? type.GetField("runSeed", flags);
            if (field != null)
            {
                object value = field.GetValue(run);
                return value != null ? value.ToString() : "";
            }

            var property = type.GetProperty("seed", flags) ?? type.GetProperty("runSeed", flags);
            if (property != null)
            {
                object value = property.GetValue(run, null);
                return value != null ? value.ToString() : "";
            }

            return "";
        }

        private static string GetAttackerBodyName(DamageInfo damageInfo)
        {
            if (damageInfo?.attacker == null) return "";

            var body = damageInfo.attacker.GetComponent<CharacterBody>();
            return body != null ? body.GetDisplayName() : damageInfo.attacker.name;
        }

        private static int CountUniqueItems(Inventory inventory)
        {
            int unique = 0;
            foreach (ItemIndex itemIndex in EnumerateAcquiredItems(inventory))
            {
                if (inventory.GetItemCount(itemIndex) > 0)
                {
                    unique++;
                }
            }

            return unique;
        }

        private static int CountItemStacks(Inventory inventory)
        {
            int total = 0;
            foreach (ItemIndex itemIndex in EnumerateAcquiredItems(inventory))
            {
                total += Mathf.Max(0, inventory.GetItemCount(itemIndex));
            }

            return total;
        }

        private static string BuildItemSummary(Inventory inventory)
        {
            if (inventory == null) return "";

            var sb = new StringBuilder(256);
            foreach (ItemIndex itemIndex in EnumerateAcquiredItems(inventory))
            {
                int count = inventory.GetItemCount(itemIndex);
                if (count <= 0) continue;

                if (sb.Length > 0) sb.Append("|");

                var itemDef = ItemCatalog.GetItemDef(itemIndex);
                string itemName = itemDef != null ? itemDef.name : itemIndex.ToString();
                sb.Append(itemName.Replace("|", "_").Replace(":", "_"));
                sb.Append(":");
                sb.Append(count);
            }

            return sb.ToString();
        }

        private static IEnumerable<ItemIndex> EnumerateAcquiredItems(Inventory inventory)
        {
            if (inventory == null) yield break;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var field = typeof(Inventory).GetField("itemAcquisitionOrder", flags);
            var acquiredItems = field?.GetValue(inventory) as System.Collections.IEnumerable;
            if (acquiredItems == null) yield break;

            foreach (object item in acquiredItems)
            {
                if (item is ItemIndex itemIndex)
                {
                    yield return itemIndex;
                }
            }
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
