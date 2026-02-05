using R2API.Utils;
using RoR2;
using System;
using System.Globalization;
using GeneticsArtifact.SgdEngine.Decision;
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

                    // Avoid interference: the genetic engine does not check ActiveAlgorithm and will run if enabled.
                    DdaAlgorithmState.IsGeneticAlgorithmEnabled = false;

                    Debug.Log("[DDA] Algorithm set to: SGD (genetic engine disabled to avoid interference)");
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

        [ConCommand(commandName = "dda_sgd_step_time", helpText = "Set SGD gradient step time (seconds). Counts only combat time. Usage: dda_sgd_step_time [seconds]")]
        private static void OnSgdStepTimeCommand(ConCommandArgs args)
        {
            if (args.Count <= 0)
            {
                Debug.Log($"[DDA] SGD step time: {SgdDecisionRuntimeState.StepSeconds:F1}s (combat time only)");
                return;
            }

            if (!TryParseFloat(args[0], out float seconds))
            {
                Debug.Log("[DDA] Usage: dda_sgd_step_time [seconds]. Example: dda_sgd_step_time 10");
                return;
            }

            // Convenience: step configuration implies we want SGD active.
            DdaAlgorithmState.ActiveAlgorithm = DdaAlgorithmType.Sgd;
            SgdDecisionRuntimeState.SetStepSeconds(seconds);

            Debug.Log($"[DDA] SGD step time set to: {SgdDecisionRuntimeState.StepSeconds:F1}s (combat time only)");
        }

        [ConCommand(commandName = "dda_actuator_hp", helpText = "Set SGD actuator: monster MaxHealth multiplier. Applies to existing monsters on level and future spawns. Usage: dda_actuator_hp <multiplier>")]
        private static void OnSgdHpCommand(ConCommandArgs args)
        {
            HandleSgdActuatorFloatCommand(
                args,
                commandName: "dda_actuator_hp",
                statDisplayName: "HP (MaxHealth)",
                getValue: () => SgdActuatorsRuntimeState.MaxHealthMultiplier,
                setValue: SgdActuatorsRuntimeState.SetMaxHealthMultiplier);
        }

        [ConCommand(commandName = "dda_actuator_ms", helpText = "Set SGD actuator: monster MoveSpeed multiplier. Applies to existing monsters on level and future spawns. Usage: dda_actuator_ms <multiplier>")]
        private static void OnSgdMoveSpeedCommand(ConCommandArgs args)
        {
            HandleSgdActuatorFloatCommand(
                args,
                commandName: "dda_actuator_ms",
                statDisplayName: "MS (MoveSpeed)",
                getValue: () => SgdActuatorsRuntimeState.MoveSpeedMultiplier,
                setValue: SgdActuatorsRuntimeState.SetMoveSpeedMultiplier);
        }

        [ConCommand(commandName = "dda_actuator_as", helpText = "Set SGD actuator: monster AttackSpeed multiplier. Applies to existing monsters on level and future spawns. Usage: dda_actuator_as <multiplier>")]
        private static void OnSgdAttackSpeedCommand(ConCommandArgs args)
        {
            HandleSgdActuatorFloatCommand(
                args,
                commandName: "dda_actuator_as",
                statDisplayName: "AS (AttackSpeed)",
                getValue: () => SgdActuatorsRuntimeState.AttackSpeedMultiplier,
                setValue: SgdActuatorsRuntimeState.SetAttackSpeedMultiplier);
        }

        [ConCommand(commandName = "dda_actuator_dmg", helpText = "Set SGD actuator: monster AttackDamage multiplier. Applies to existing monsters on level and future spawns. Usage: dda_actuator_dmg <multiplier>")]
        private static void OnSgdAttackDamageCommand(ConCommandArgs args)
        {
            HandleSgdActuatorFloatCommand(
                args,
                commandName: "dda_actuator_dmg",
                statDisplayName: "DMG (AttackDamage)",
                getValue: () => SgdActuatorsRuntimeState.AttackDamageMultiplier,
                setValue: SgdActuatorsRuntimeState.SetAttackDamageMultiplier);
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

        private static void HandleSgdActuatorFloatCommand(
            ConCommandArgs args,
            string commandName,
            string statDisplayName,
            Func<float> getValue,
            Action<float> setValue)
        {
            if (args.Count <= 0)
            {
                Debug.Log($"[DDA] SGD {statDisplayName} multiplier: {getValue():F2}");
                return;
            }

            if (!TryParseFloat(args[0], out float mult))
            {
                Debug.Log($"[DDA] Usage: {commandName} <multiplier>. Example: {commandName} 1.50");
                return;
            }

            // Convenience: these commands imply we want SGD behavior active.
            DdaAlgorithmState.ActiveAlgorithm = DdaAlgorithmType.Sgd;
            setValue(mult);

            float clamped = getValue();
            if (!NetworkServer.active)
            {
                Debug.Log($"[DDA] SGD actuator set: {statDisplayName} multiplier = {clamped:F2}. (Not applied now: NetworkServer is not active)");
                return;
            }

            int applied = SgdActuatorsApplier.ApplyToAllLivingMonsters();
            Debug.Log($"[DDA] SGD actuator set: {statDisplayName} multiplier = {clamped:F2}. Applied to {applied} existing monsters; will apply to future spawns. Tip: run 'dda_genetics 0' to avoid genetic engine interference.");
        }

        [ConCommand(commandName = "dda_show_monster_hp", helpText = "Toggle HP numbers above monsters (client-side). Usage: dda_show_monster_hp [0|1]")]
        private static void OnMonsterHpOverlay(ConCommandArgs args)
        {
            if (args.Count > 0)
            {
                if (int.TryParse(args[0], out int value))
                {
                    MonsterHpOverheadDriver.SetEnabled(value != 0);
                }
                else
                {
                    Debug.Log("[DDA] Usage: dda_show_monster_hp [0|1]");
                    return;
                }
            }
            else
            {
                MonsterHpOverheadDriver.SetEnabled(!MonsterHpOverheadDriver.IsEnabled);
            }

            Debug.Log($"[DDA] Monster HP overhead: {(MonsterHpOverheadDriver.IsEnabled ? "ENABLED" : "DISABLED")}");
        }
    }
}
