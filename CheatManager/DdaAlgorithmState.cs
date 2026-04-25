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

        public static string GetTelemetryMode()
        {
            if (ActiveAlgorithm == DdaAlgorithmType.Sgd)
            {
                return "SGD";
            }

            if (ActiveAlgorithm == DdaAlgorithmType.Genetic && IsGeneticAlgorithmEnabled)
            {
                return "GA";
            }

            return "FixedDisabled";
        }
    }

    public enum DdaAlgorithmType
    {
        Genetic,
        Sgd
    }
}
