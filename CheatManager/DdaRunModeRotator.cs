using RoR2;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Networking;

namespace GeneticsArtifact.CheatManager
{
    /// <summary>
    /// Hidden research rotator: each new run uses the next DDA mode in a stable cycle.
    /// </summary>
    public static class DdaRunModeRotator
    {
        private const string LogPrefix = "[DDA][Seed] ";
        private const string DefaultRunSeedCycle = "8459684015804115075,8821573197706646788,2559340200192868678,97399012779323199,1444060760769064427";

        public static void RegisterHooks()
        {
            On.RoR2.PreGameController.StartRun += PreGameController_StartRun;
            GeneticsArtifactPlugin.geneticLogSource?.LogInfo("[DDA] Research run mode rotator enabled (PreGameController.StartRun).");
        }

        private static void PreGameController_StartRun(On.RoR2.PreGameController.orig_StartRun orig, PreGameController self)
        {
            try
            {
                if (NetworkServer.active && ConfigManager.researchAutoRotateDdaAlgorithms.Value)
                {
                    string lastAlgorithm = Normalize(ConfigManager.researchLastRunDdaAlgorithm.Value);
                    DdaAlgorithmType next = GetNextAlgorithm(lastAlgorithm);
                    DdaAlgorithmState.Activate(next);

                    string telemetryMode = DdaAlgorithmState.GetTelemetryMode();
                    string configuredSeed = SelectConfiguredSeed(lastAlgorithm, telemetryMode);

                    ConfigManager.researchLastRunDdaAlgorithm.Value = telemetryMode;
                    ConfigManager.telemetryConfiguredRunSeed.Value = configuredSeed;
                    ConfigManager.Save();

                    LogSeedSelection(lastAlgorithm, telemetryMode, configuredSeed);

                    if (!string.IsNullOrWhiteSpace(configuredSeed))
                    {
                        ApplyConfiguredRunSeed(self, configuredSeed);
                    }

                    GeneticsArtifactPlugin.geneticLogSource?.LogInfo(
                        "[DDA] Research run mode selected (StartRun): " + telemetryMode +
                        (string.IsNullOrWhiteSpace(configuredSeed) ? "" : ", planned_seed=" + configuredSeed));
                }
            }
            catch (Exception ex)
            {
                GeneticsArtifactPlugin.geneticLogSource?.LogWarning(
                    "[DDA] PreGameController.StartRun rotator hook failed; falling back to vanilla StartRun. Error: " +
                    ex.GetType().Name + ": " + ex.Message);
            }

            orig(self);
        }

        private static void Run_Start(On.RoR2.Run.orig_Start orig, Run self)
        {
            try
            {
                if (NetworkServer.active && ConfigManager.researchAutoRotateDdaAlgorithms.Value)
                {
                    string lastAlgorithm = Normalize(ConfigManager.researchLastRunDdaAlgorithm.Value);
                    DdaAlgorithmType next = GetNextAlgorithm(lastAlgorithm);
                    DdaAlgorithmState.Activate(next);

                    string telemetryMode = DdaAlgorithmState.GetTelemetryMode();
                    string configuredSeed = SelectConfiguredSeed(lastAlgorithm, telemetryMode);

                    ConfigManager.researchLastRunDdaAlgorithm.Value = telemetryMode;
                    ConfigManager.telemetryConfiguredRunSeed.Value = configuredSeed;
                    ConfigManager.Save();

                    LogSeedSelection(lastAlgorithm, telemetryMode, configuredSeed);

                    GeneticsArtifactPlugin.geneticLogSource?.LogInfo(
                        "[DDA] Research run mode selected (pre-run): " + telemetryMode +
                        (string.IsNullOrWhiteSpace(configuredSeed) ? "" : ", planned_seed=" + configuredSeed) +
                        " (seed forcing disabled)");
                }
            }
            catch (Exception ex)
            {
                GeneticsArtifactPlugin.geneticLogSource?.LogWarning(
                    "[DDA] Run.Start rotator hook failed; falling back to vanilla Run.Start. Error: " +
                    ex.GetType().Name + ": " + ex.Message);
            }

            orig(self);
        }

        private static DdaAlgorithmType GetNextAlgorithm(string normalizedLast)
        {
            switch (normalizedLast)
            {
                case "FLS":
                    return DdaAlgorithmType.Genetic;
                case "GA":
                    return DdaAlgorithmType.Sgd;
                case "SGD":
                    return DdaAlgorithmType.Fixed;
                default:
                    return DdaAlgorithmType.Fixed;
            }
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "None";

            string normalized = value.Trim().ToUpperInvariant();
            if (normalized == "FIXED" || normalized == "FIXEDDISABLED") return "FLS";
            if (normalized == "GENETIC") return "GA";
            return normalized;
        }

        private static string SelectConfiguredSeed(string normalizedLastAlgorithm, string nextTelemetryMode)
        {
            if (!ConfigManager.researchAutoRotateRunSeeds.Value)
            {
                return ConfigManager.telemetryConfiguredRunSeed.Value ?? "";
            }

            var seeds = ParseSeedCycle(GetSeedCycleRaw());
            if (seeds.Count <= 0)
            {
                ConfigManager.researchCurrentRunSeed.Value = "";
                return "";
            }

            int index = ClampSeedIndex(ConfigManager.researchCurrentRunSeedIndex.Value, seeds.Count);

            // One seed is shared by a full FLS -> GA -> SGD cycle. Advance only when
            // starting a new cycle after the previous run was SGD and the next one is FLS.
            bool startsNewCycle = normalizedLastAlgorithm == "SGD" && nextTelemetryMode == "FLS";
            if (startsNewCycle)
            {
                index = (index + 1) % seeds.Count;
                ConfigManager.researchCurrentRunSeedIndex.Value = index;
            }

            string seed = seeds[index];
            ConfigManager.researchCurrentRunSeed.Value = seed;
            return seed;
        }

        private static string GetSeedCycleRaw()
        {
            string configured = ConfigManager.researchRunSeedCycle.Value;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            return DefaultRunSeedCycle;
        }

        private static string GetTelemetryMode(DdaAlgorithmType algorithm)
        {
            if (algorithm == DdaAlgorithmType.Fixed) return "FLS";
            if (algorithm == DdaAlgorithmType.Sgd) return "SGD";
            return "GA";
        }

        private static List<string> ParseSeedCycle(string raw)
        {
            var seeds = new List<string>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return seeds;
            }

            string[] parts = raw.Split(new[] { ',', ';', '|', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string seed = parts[i].Trim();
                if (seed.Length > 0)
                {
                    seeds.Add(seed);
                }
            }

            return seeds;
        }

        private static int ClampSeedIndex(int index, int count)
        {
            if (count <= 0) return 0;
            if (index < 0) return 0;
            return index % count;
        }

        private static void ApplyConfiguredRunSeed(PreGameController preGame, string configuredSeed)
        {
            if (preGame == null || string.IsNullOrWhiteSpace(configuredSeed))
            {
                return;
            }

            if (!ulong.TryParse(configuredSeed.Trim(), out ulong seed))
            {
                GeneticsArtifactPlugin.geneticLogSource?.LogWarning(
                    "[DDA] Research run seed was not applied: seed is not an unsigned integer: " + configuredSeed);
                return;
            }

            // PreGameController typically holds the runSeed used to start the run.
            string beforeRunSeed = TryDescribeSeedMember(preGame, "runSeed");
            string beforeSeed = TryDescribeSeedMember(preGame, "seed");

            bool appliedRunSeed = TrySetSeedMemberWithLog(preGame, "runSeed", seed);
            bool appliedSeed = !appliedRunSeed && TrySetSeedMemberWithLog(preGame, "seed", seed);

            string afterRunSeed = TryDescribeSeedMember(preGame, "runSeed");
            string afterSeed = TryDescribeSeedMember(preGame, "seed");

            GeneticsArtifactPlugin.geneticLogSource?.LogInfo(
                LogPrefix + "PreGameController seed members before: runSeed=" + beforeRunSeed + ", seed=" + beforeSeed +
                " | after: runSeed=" + afterRunSeed + ", seed=" + afterSeed);

            if (appliedRunSeed || appliedSeed)
            {
                GeneticsArtifactPlugin.geneticLogSource?.LogInfo(LogPrefix + "Applied configured seed=" + seed + " to PreGameController (StartRun)");
                return;
            }

            GeneticsArtifactPlugin.geneticLogSource?.LogWarning(
                "[DDA] Research run seed could not be applied to PreGameController via reflection; it will still be sent as configured_run_seed.");
        }

        private static void ApplyConfiguredRunSeed(Run run, string configuredSeed)
        {
            if (run == null || string.IsNullOrWhiteSpace(configuredSeed))
            {
                return;
            }

            if (!ulong.TryParse(configuredSeed.Trim(), out ulong seed))
            {
                GeneticsArtifactPlugin.geneticLogSource?.LogWarning(
                    "[DDA] Research run seed was not applied: seed is not an unsigned integer: " + configuredSeed);
                return;
            }

            if (TrySetSeedMember(run, "seed", seed) || TrySetSeedMember(run, "runSeed", seed))
            {
                GeneticsArtifactPlugin.geneticLogSource?.LogInfo("[DDA] Research run seed applied: " + seed);
                return;
            }

            GeneticsArtifactPlugin.geneticLogSource?.LogWarning(
                "[DDA] Research run seed could not be applied to Run via reflection; it will still be sent as configured_run_seed.");
        }

        private static bool TrySetSeedMember(object instance, string memberName, ulong seed)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (instance == null) return false;
            Type type = instance.GetType();

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
            {
                return TrySetSeedValue(instance, field.FieldType, value => field.SetValue(instance, value), seed);
            }

            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null && property.CanWrite)
            {
                return TrySetSeedValue(instance, property.PropertyType, value => property.SetValue(instance, value, null), seed);
            }

            return false;
        }

        private static bool TrySetSeedMemberWithLog(object instance, string memberName, ulong seed)
        {
            if (instance == null) return false;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = instance.GetType();

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
            {
                bool ok = TrySetSeedValue(instance, field.FieldType, value => field.SetValue(instance, value), seed);
                GeneticsArtifactPlugin.geneticLogSource?.LogInfo(
                    LogPrefix + "TrySet " + type.Name + "." + memberName + " (field:" + field.FieldType.Name + ") => " + (ok ? "OK" : "FAIL"));
                return ok;
            }

            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null)
            {
                if (!property.CanWrite)
                {
                    GeneticsArtifactPlugin.geneticLogSource?.LogInfo(
                        LogPrefix + "TrySet " + type.Name + "." + memberName + " (property:" + property.PropertyType.Name + ") => SKIP (no setter)");
                    return false;
                }

                bool ok = TrySetSeedValue(instance, property.PropertyType, value => property.SetValue(instance, value, null), seed);
                GeneticsArtifactPlugin.geneticLogSource?.LogInfo(
                    LogPrefix + "TrySet " + type.Name + "." + memberName + " (property:" + property.PropertyType.Name + ") => " + (ok ? "OK" : "FAIL"));
                return ok;
            }

            GeneticsArtifactPlugin.geneticLogSource?.LogInfo(
                LogPrefix + "TrySet " + type.Name + "." + memberName + " => MISS (no field/property)");
            return false;
        }

        private static string TryDescribeSeedMember(object instance, string memberName)
        {
            if (instance == null) return "null";

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = instance.GetType();

            try
            {
                FieldInfo field = type.GetField(memberName, flags);
                if (field != null)
                {
                    object v = field.GetValue(instance);
                    return (v == null ? "null" : v + " (" + field.FieldType.Name + ")");
                }

                PropertyInfo prop = type.GetProperty(memberName, flags);
                if (prop != null && prop.CanRead)
                {
                    object v = prop.GetValue(instance, null);
                    return (v == null ? "null" : v + " (" + prop.PropertyType.Name + ")");
                }

                return "<missing>";
            }
            catch (Exception ex)
            {
                return "<error:" + ex.GetType().Name + ">";
            }
        }

        private static void LogSeedSelection(string normalizedLastAlgorithm, string nextTelemetryMode, string configuredSeed)
        {
            if (GeneticsArtifactPlugin.geneticLogSource == null) return;

            bool rotate = ConfigManager.researchAutoRotateRunSeeds.Value;
            int idx = ConfigManager.researchCurrentRunSeedIndex.Value;
            string cycle = GetSeedCycleRaw();
            string current = ConfigManager.researchCurrentRunSeed.Value ?? "";

            GeneticsArtifactPlugin.geneticLogSource.LogInfo(
                LogPrefix + "Select seed: last=" + normalizedLastAlgorithm +
                ", next=" + nextTelemetryMode +
                ", rotate=" + rotate +
                ", idx=" + idx +
                ", selected=" + (string.IsNullOrWhiteSpace(configuredSeed) ? "<empty>" : configuredSeed) +
                ", current=" + (string.IsNullOrWhiteSpace(current) ? "<empty>" : current) +
                ", cycle_raw_len=" + cycle.Length);
        }

        private static bool TrySetSeedValue(object target, Type memberType, Action<object> setValue, ulong seed)
        {
            try
            {
                if (memberType == typeof(ulong))
                {
                    setValue(seed);
                    return true;
                }
                if (memberType == typeof(long))
                {
                    setValue(unchecked((long)seed));
                    return true;
                }
                if (memberType == typeof(uint))
                {
                    setValue(unchecked((uint)seed));
                    return true;
                }
                if (memberType == typeof(int))
                {
                    setValue(unchecked((int)seed));
                    return true;
                }
                if (memberType == typeof(string))
                {
                    setValue(seed.ToString());
                    return true;
                }
            }
            catch (Exception ex)
            {
                GeneticsArtifactPlugin.geneticLogSource?.LogWarning(
                    "[DDA] Research run seed assignment failed: " + ex.Message);
            }

            return false;
        }
    }
}
