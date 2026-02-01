using R2API.Utils;
using RoR2;
using UnityEngine;

namespace GeneticsArtifact.CheatManager
{
    /// <summary>
    /// Registers and manages DDA-related console commands.
    /// Uses [ConCommand] attributes - CommandHelper.AddToConsoleWhenReady scans the assembly.
    /// </summary>
    public static class DdaCheatManager
    {
        public static void Init()
        {
            CommandHelper.AddToConsoleWhenReady();
            GeneticsArtifactPlugin.geneticLogSource?.LogInfo("DDA CheatManager: Console commands registered.");
        }

        [ConCommand(commandName = "dda_genetics", helpText = "Toggle genetic algorithm. Usage: dda_genetics [0|1]")]
        private static void OnGeneticsToggle(ConCommandArgs args)
        {
            if (args.Count > 0)
            {
                if (int.TryParse(args[0], out int value))
                {
                    DdaAlgorithmState.IsGeneticAlgorithmEnabled = value != 0;
                }
            }
            else
            {
                DdaAlgorithmState.IsGeneticAlgorithmEnabled = !DdaAlgorithmState.IsGeneticAlgorithmEnabled;
            }

            Debug.Log($"[DDA] Genetic algorithm: {(DdaAlgorithmState.IsGeneticAlgorithmEnabled ? "ENABLED" : "DISABLED")}");
        }

        [ConCommand(commandName = "dda_algorithm", helpText = "Switch DDA algorithm. Usage: dda_algorithm [genetic|sgd]")]
        private static void OnAlgorithmSwitch(ConCommandArgs args)
        {
            if (args.Count > 0)
            {
                string arg = args[0].ToLowerInvariant();
                if (arg == "genetic")
                {
                    DdaAlgorithmState.ActiveAlgorithm = DdaAlgorithmType.Genetic;
                    Debug.Log("[DDA] Algorithm set to: Genetic");
                }
                else if (arg == "sgd")
                {
                    DdaAlgorithmState.ActiveAlgorithm = DdaAlgorithmType.Sgd;
                    Debug.Log("[DDA] Algorithm set to: SGD (not yet implemented)");
                }
                else
                {
                    Debug.Log("[DDA] Unknown algorithm. Use: genetic, sgd");
                }
            }
            else
            {
                Debug.Log($"[DDA] Current algorithm: {DdaAlgorithmState.ActiveAlgorithm}");
            }
        }

        [ConCommand(commandName = "dda_debug_overlay", helpText = "Toggle debug overlay. Usage: dda_debug_overlay [0|1]")]
        private static void OnDebugOverlayToggle(ConCommandArgs args)
        {
            if (args.Count > 0)
            {
                if (int.TryParse(args[0], out int value))
                {
                    DdaAlgorithmState.IsDebugOverlayEnabled = value != 0;
                }
            }
            else
            {
                DdaAlgorithmState.IsDebugOverlayEnabled = !DdaAlgorithmState.IsDebugOverlayEnabled;
            }

            DebugOverlayBehaviour.UpdateVisibility();
            Debug.Log($"[DDA] Debug overlay: {(DdaAlgorithmState.IsDebugOverlayEnabled ? "ENABLED" : "DISABLED")}");
        }

        [ConCommand(commandName = "dda_param", helpText = "Change DDA parameters (placeholder). Usage: dda_param [param_name] [value]")]
        private static void OnParamCommand(ConCommandArgs args)
        {
            Debug.Log("[DDA] dda_param: Not yet implemented. Use config file for now.");
        }
    }
}
