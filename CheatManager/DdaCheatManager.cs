using R2API.Utils;
using RoR2;
using System.Globalization;
using GeneticsArtifact.SgdEngine.Actuators;
using UnityEngine;
using UnityEngine.Networking;

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

        [ConCommand(commandName = "dda_sgd_hp", helpText = "Set SGD actuator: monster MaxHealth multiplier. Applies to existing monsters on level and future spawns. Usage: dda_sgd_hp <multiplier>")]
        private static void OnSgdHpCommand(ConCommandArgs args)
        {
            if (args.Count <= 0)
            {
                Debug.Log($"[DDA] SGD HP multiplier: {SgdActuatorsRuntimeState.MaxHealthMultiplier:F2}");
                return;
            }

            if (!TryParseFloat(args[0], out float mult))
            {
                Debug.Log("[DDA] Usage: dda_sgd_hp <multiplier>. Example: dda_sgd_hp 1.50");
                return;
            }

            SgdActuatorsRuntimeState.SetMaxHealthMultiplier(mult);

            if (!NetworkServer.active)
            {
                Debug.Log($"[DDA] SGD actuator set: MaxHealth multiplier = {SgdActuatorsRuntimeState.MaxHealthMultiplier:F2}. (Not applied now: NetworkServer is not active)");
                return;
            }

            int applied = SgdActuatorsApplier.ApplyToAllLivingMonsters();
            Debug.Log($"[DDA] SGD actuator set: MaxHealth multiplier = {SgdActuatorsRuntimeState.MaxHealthMultiplier:F2}. Applied to {applied} existing monsters; will apply to future spawns. Tip: run 'dda_genetics 0' to avoid genetic engine interference.");
        }

        private static bool TryParseFloat(string s, out float value)
        {
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            // Common locale fallback (comma decimal separator).
            s = s?.Replace(',', '.');
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
