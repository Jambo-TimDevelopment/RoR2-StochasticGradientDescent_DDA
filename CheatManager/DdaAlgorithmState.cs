namespace GeneticsArtifact.CheatManager
{
    /// <summary>
    /// Holds runtime state for DDA (Dynamic Difficulty Adaptation) algorithms.
    /// </summary>
    public static class DdaAlgorithmState
    {
        /// <summary>
        /// Whether the genetic algorithm is enabled. Default: false.
        /// </summary>
        public static bool IsGeneticAlgorithmEnabled { get; set; }

        /// <summary>
        /// Currently active difficulty adaptation algorithm.
        /// </summary>
        public static DdaAlgorithmType ActiveAlgorithm { get; set; } = DdaAlgorithmType.Sgd;

        /// <summary>
        /// Whether the debug overlay is visible on screen.
        /// </summary>
        public static bool IsDebugOverlayEnabled { get; set; }

        /// <summary>
        /// Whether the telemetry overlay is visible on screen.
        /// Shows the last telemetry payload enqueued/sent to PostHog.
        /// </summary>
        public static bool IsTelemetryOverlayEnabled { get; set; }

        public static void Activate(DdaAlgorithmType algorithm)
        {
            ActiveAlgorithm = algorithm;
            IsGeneticAlgorithmEnabled = algorithm == DdaAlgorithmType.Genetic;
        }

        public static string GetTelemetryMode()
        {
            if (ActiveAlgorithm == DdaAlgorithmType.Fixed)
            {
                return "FLS";
            }

            if (ActiveAlgorithm == DdaAlgorithmType.Sgd)
            {
                return "SGD";
            }

            if (ActiveAlgorithm == DdaAlgorithmType.Genetic && IsGeneticAlgorithmEnabled)
            {
                return "GA";
            }

            return "FLS";
        }

        public static bool ShouldRunGeneticEngine()
        {
            return ActiveAlgorithm == DdaAlgorithmType.Genetic && IsGeneticAlgorithmEnabled;
        }
    }

    public enum DdaAlgorithmType
    {
        Fixed,
        Genetic,
        Sgd
    }
}
