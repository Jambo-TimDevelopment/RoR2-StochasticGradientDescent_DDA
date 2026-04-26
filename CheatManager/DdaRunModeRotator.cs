using RoR2;
using UnityEngine.Networking;

namespace GeneticsArtifact.CheatManager
{
    /// <summary>
    /// Hidden research rotator: each new run uses the next DDA mode in a stable cycle.
    /// </summary>
    public static class DdaRunModeRotator
    {
        public static void RegisterHooks()
        {
            On.RoR2.Run.Start += Run_Start;
        }

        private static void Run_Start(On.RoR2.Run.orig_Start orig, Run self)
        {
            if (NetworkServer.active && ConfigManager.researchAutoRotateDdaAlgorithms.Value)
            {
                DdaAlgorithmType next = GetNextAlgorithm(ConfigManager.researchLastRunDdaAlgorithm.Value);
                DdaAlgorithmState.Activate(next);

                string telemetryMode = DdaAlgorithmState.GetTelemetryMode();
                ConfigManager.researchLastRunDdaAlgorithm.Value = telemetryMode;
                ConfigManager.Save();

                GeneticsArtifactPlugin.geneticLogSource?.LogInfo("[DDA] Research run mode selected: " + telemetryMode);
            }

            orig(self);
        }

        private static DdaAlgorithmType GetNextAlgorithm(string last)
        {
            switch (Normalize(last))
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
    }
}
