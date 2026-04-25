using BepInEx.Configuration;
using System;
using GeneticsArtifact.Telemetry;

namespace GeneticsArtifact
{
    public class ConfigManager
    {
        public static ConfigEntry<int> timeLimit, deathLimit, governorType;

        public static ConfigEntry<float> geneVarianceLimit, geneCap, geneFloor, geneProductLimit;

        public static ConfigEntry<bool> maintainIfDisabled, enableGeneLimitOverrides;

        public static ConfigEntry<string> geneLimitOverrides;

        // SGD / sensors normalization parameters
        public static ConfigEntry<float> sgdNormTargetTimeToDieSeconds;
        public static ConfigEntry<float> sgdNormTargetTtkSeconds;
        public static ConfigEntry<float> sgdNormHitRateScalePerSecond;
        public static ConfigEntry<float> sgdHpFloor, sgdHpCap;
        public static ConfigEntry<float> sgdMsFloor, sgdMsCap;
        public static ConfigEntry<float> sgdAsFloor, sgdAsCap;
        public static ConfigEntry<float> sgdDmgFloor, sgdDmgCap;

        // Research telemetry / PostHog ingestion.
        public static ConfigEntry<bool> telemetryEnabled;
        public static ConfigEntry<string> telemetryAnonymousUserId;
        public static ConfigEntry<string> telemetryExperimentId;
        public static ConfigEntry<float> telemetrySampleIntervalSeconds;
        public static ConfigEntry<float> telemetryFlushIntervalSeconds;
        public static ConfigEntry<int> telemetryMaxQueueSize;
        public static ConfigEntry<float> telemetryJumpThreshold;
        public static ConfigEntry<float> telemetryDegradationThreshold;
        public static ConfigEntry<float> telemetryRecoveryThreshold;

        public static void Init(ConfigFile configFile)
        {
            governorType = configFile.Bind<int>(new ConfigDefinition("GeneEngineDriver Variables", "Learning Governor Type"), 0, new ConfigDescription("How the algorithm decides when to learn: 0 - Default, 1 - Time Only, 2 - Death Count Only", new AcceptableValueRange<int>(0, 2)));
            timeLimit = configFile.Bind<int>(new ConfigDefinition("GeneEngineDriver Variables", "Time Limit"), 60, new ConfigDescription("How many seconds between learnings:", new AcceptableValueRange<int>(5, 300))); // 5 seconds to 5 minutes
            deathLimit = configFile.Bind<int>(new ConfigDefinition("GeneEngineDriver Variables", "Death Limit"), 40, new ConfigDescription("How many monster deaths between learnings:", new AcceptableValueRange<int>(10, 100)));
            maintainIfDisabled = configFile.Bind<bool>(new ConfigDefinition("GeneEngineDriver Variables", "Keep Mutations While Disabled"), false, new ConfigDescription("Should the stat mods still be applied if the artifact is disabled mid-run:", new AcceptableValueList<bool>(true, false)));

            geneCap = configFile.Bind<float>(new ConfigDefinition("Mutation Variables", "Gene Value Cap"), 10.00f, new ConfigDescription("Maximum multiplier for any stat:", new AcceptableValueRange<float>(1f, 50f)));
            geneFloor = configFile.Bind<float>(new ConfigDefinition("Mutation Variables", "Gene Value Floor"), 0.01f, new ConfigDescription("Minimum multiplier for any stat:", new AcceptableValueRange<float>(0.01f, 1f)));
            geneProductLimit = configFile.Bind<float>(new ConfigDefinition("Mutation Variables", "Gene Product Cap"), 1.5f, new ConfigDescription("Maximum product of all stat multipliers:", new AcceptableValueRange<float>(1f, 10f)));
            geneVarianceLimit = configFile.Bind<float>(new ConfigDefinition("Mutation Variables", "Gene Variation Limit"), 0.1f, new ConfigDescription("How much a monster can differ from it`s master as a percent: 0.1 is 10% (Bulwark will be 5x this)", new AcceptableValueRange<float>(0.01f, 1f)));

            enableGeneLimitOverrides = configFile.Bind<bool>(new ConfigDefinition("Mutation Override Variables", "Enable Mutation Overrides"), false, new ConfigDescription("Should the mutation overrides be applied, use with caution", new AcceptableValueList<bool>(true, false)));
            geneLimitOverrides = configFile.Bind<string>(new ConfigDefinition("Mutation Override Variables", "Gene Limit Overrides"), "MoveSpeed,0.5,2|InvalidName,0.8,NaN", new ConfigDescription("Format is as follows: GeneName1,Floor1,Cap1|GeneName2,Floor2,Cap2 where GeneName is in (MaxHealth,MoveSpeed,AttackSpeed,AttackDamage) and Floor and Cap are parseable numerics"));

            // --- SGD / sensors normalization ---
            // These targets are used to compress sensor values into stable [0..1] signals via Norm01(x)=1-exp(-x).
            sgdNormTargetTimeToDieSeconds = configFile.Bind<float>(
                new ConfigDefinition("SGD Sensor Normalization", "Target Time To Die (seconds)"),
                10f,
                new ConfigDescription("Target survival horizon used for normalizing incoming DPS against V_p(defense). Higher => less sensitive.", new AcceptableValueRange<float>(1f, 60f)));

            sgdNormTargetTtkSeconds = configFile.Bind<float>(
                new ConfigDefinition("SGD Sensor Normalization", "Target Time To Kill (seconds)"),
                8f,
                new ConfigDescription("Target TTK used for normalizing AvgTTK. Higher => less sensitive.", new AcceptableValueRange<float>(1f, 60f)));

            sgdNormHitRateScalePerSecond = configFile.Bind<float>(
                new ConfigDefinition("SGD Sensor Normalization", "Hit Rate Scale (per second)"),
                1.5f,
                new ConfigDescription("Scale for normalizing hit rate (hits/sec) into [0..1]. Higher => less sensitive.", new AcceptableValueRange<float>(0.1f, 10f)));

            // --- SGD / per-axis multiplier limits ---
            // Keep defaults aligned with global mutation defaults to preserve behavior unless overridden.
            sgdHpFloor = configFile.Bind<float>(
                new ConfigDefinition("SGD Axis Limits", "HP Floor"),
                0.01f,
                new ConfigDescription("Minimum SGD multiplier for MaxHealth axis.", new AcceptableValueRange<float>(0.01f, 1f)));
            sgdHpCap = configFile.Bind<float>(
                new ConfigDefinition("SGD Axis Limits", "HP Cap"),
                10f,
                new ConfigDescription("Maximum SGD multiplier for MaxHealth axis.", new AcceptableValueRange<float>(1f, 50f)));

            sgdMsFloor = configFile.Bind<float>(
                new ConfigDefinition("SGD Axis Limits", "MoveSpeed Floor"),
                0.01f,
                new ConfigDescription("Minimum SGD multiplier for MoveSpeed axis.", new AcceptableValueRange<float>(0.01f, 1f)));
            sgdMsCap = configFile.Bind<float>(
                new ConfigDefinition("SGD Axis Limits", "MoveSpeed Cap"),
                10f,
                new ConfigDescription("Maximum SGD multiplier for MoveSpeed axis.", new AcceptableValueRange<float>(1f, 50f)));

            sgdAsFloor = configFile.Bind<float>(
                new ConfigDefinition("SGD Axis Limits", "AttackSpeed Floor"),
                0.01f,
                new ConfigDescription("Minimum SGD multiplier for AttackSpeed axis.", new AcceptableValueRange<float>(0.01f, 1f)));
            sgdAsCap = configFile.Bind<float>(
                new ConfigDefinition("SGD Axis Limits", "AttackSpeed Cap"),
                10f,
                new ConfigDescription("Maximum SGD multiplier for AttackSpeed axis.", new AcceptableValueRange<float>(1f, 50f)));

            sgdDmgFloor = configFile.Bind<float>(
                new ConfigDefinition("SGD Axis Limits", "AttackDamage Floor"),
                0.01f,
                new ConfigDescription("Minimum SGD multiplier for AttackDamage axis.", new AcceptableValueRange<float>(0.01f, 1f)));
            sgdDmgCap = configFile.Bind<float>(
                new ConfigDefinition("SGD Axis Limits", "AttackDamage Cap"),
                10f,
                new ConfigDescription("Maximum SGD multiplier for AttackDamage axis.", new AcceptableValueRange<float>(1f, 50f)));

            // --- Research telemetry ---
            telemetryEnabled = configFile.Bind<bool>(
                new ConfigDefinition("Research Telemetry", "Telemetry Enabled"),
                true,
                new ConfigDescription("Send anonymous DDA research telemetry to the configured PostHog project. Disable to opt out."));

            telemetryAnonymousUserId = configFile.Bind<string>(
                new ConfigDefinition("Research Telemetry", "Anonymous User Id"),
                "",
                new ConfigDescription("Anonymous UUID for grouping runs from the same installation. Generated automatically if empty."));

            if (string.IsNullOrWhiteSpace(telemetryAnonymousUserId.Value))
            {
                telemetryAnonymousUserId.Value = "anon-" + Guid.NewGuid().ToString("N");
                configFile.Save();
            }

            telemetryExperimentId = configFile.Bind<string>(
                new ConfigDefinition("Research Telemetry", "Experiment Id"),
                "thesis_v1",
                new ConfigDescription("Experiment label used to segment telemetry in a single PostHog project."));

            telemetrySampleIntervalSeconds = configFile.Bind<float>(
                new ConfigDefinition("Research Telemetry", "Sample Interval Seconds"),
                10f,
                new ConfigDescription("How often to sample runtime DDA telemetry.", new AcceptableValueRange<float>(1f, 120f)));

            telemetryFlushIntervalSeconds = configFile.Bind<float>(
                new ConfigDefinition("Research Telemetry", "Flush Interval Seconds"),
                20f,
                new ConfigDescription("How often to flush queued telemetry events to PostHog.", new AcceptableValueRange<float>(5f, 300f)));

            telemetryMaxQueueSize = configFile.Bind<int>(
                new ConfigDefinition("Research Telemetry", "Max Queue Size"),
                512,
                new ConfigDescription("Maximum in-memory telemetry events kept before oldest events are dropped.", new AcceptableValueRange<int>(32, 5000)));

            telemetryJumpThreshold = configFile.Bind<float>(
                new ConfigDefinition("Research Telemetry", "Jump Threshold"),
                0.10f,
                new ConfigDescription("Absolute multiplier delta treated as a sharp difficulty jump.", new AcceptableValueRange<float>(0.01f, 1f)));

            telemetryDegradationThreshold = configFile.Bind<float>(
                new ConfigDefinition("Research Telemetry", "Degradation Threshold"),
                0.70f,
                new ConfigDescription("Stress signal threshold that starts a degradation episode.", new AcceptableValueRange<float>(0.1f, 1f)));

            telemetryRecoveryThreshold = configFile.Bind<float>(
                new ConfigDefinition("Research Telemetry", "Recovery Threshold"),
                0.35f,
                new ConfigDescription("Stress signal threshold that ends a degradation episode.", new AcceptableValueRange<float>(0f, 0.9f)));
        }
    }

    public enum GovernorType
    {
        Default,
        TimeOnly,
        DeathsOnly
    }
}
