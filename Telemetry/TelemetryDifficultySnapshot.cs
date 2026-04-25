using GeneticsArtifact.CheatManager;
using GeneticsArtifact.SgdEngine.Actuators;
using System.Collections.Generic;
using UnityEngine;

namespace GeneticsArtifact.Telemetry
{
    internal readonly struct TelemetryDifficultySnapshot
    {
        public readonly string Mode;
        public readonly float MaxHealth;
        public readonly float MoveSpeed;
        public readonly float AttackSpeed;
        public readonly float AttackDamage;

        public TelemetryDifficultySnapshot(string mode, float maxHealth, float moveSpeed, float attackSpeed, float attackDamage)
        {
            Mode = mode;
            MaxHealth = Sanitize(maxHealth);
            MoveSpeed = Sanitize(moveSpeed);
            AttackSpeed = Sanitize(attackSpeed);
            AttackDamage = Sanitize(attackDamage);
        }

        public static TelemetryDifficultySnapshot Capture()
        {
            string mode = DdaAlgorithmState.GetTelemetryMode();
            if (mode == "SGD")
            {
                return new TelemetryDifficultySnapshot(
                    mode,
                    SgdActuatorsRuntimeState.MaxHealthMultiplier,
                    SgdActuatorsRuntimeState.MoveSpeedMultiplier,
                    SgdActuatorsRuntimeState.AttackSpeedMultiplier,
                    SgdActuatorsRuntimeState.AttackDamageMultiplier);
            }

            if (mode == "GA")
            {
                return CaptureGeneticAverages();
            }

            return new TelemetryDifficultySnapshot(mode, 1f, 1f, 1f, 1f);
        }

        public float GetMultiplier(GeneStat stat)
        {
            switch (stat)
            {
                case GeneStat.MaxHealth:
                    return MaxHealth;
                case GeneStat.MoveSpeed:
                    return MoveSpeed;
                case GeneStat.AttackSpeed:
                    return AttackSpeed;
                case GeneStat.AttackDamage:
                    return AttackDamage;
                default:
                    return 1f;
            }
        }

        private static TelemetryDifficultySnapshot CaptureGeneticAverages()
        {
            float hp = 0f;
            float ms = 0f;
            float attackSpeed = 0f;
            float attackDamage = 0f;
            int count = 0;

            if (GeneEngineDriver.masterGenes != null)
            {
                for (int i = 0; i < GeneEngineDriver.masterGenes.Count; i++)
                {
                    var master = GeneEngineDriver.masterGenes[i];
                    if (master?.templateGenes == null) continue;
                    AddGenes(master.templateGenes, ref hp, ref ms, ref attackSpeed, ref attackDamage, ref count);
                }
            }

            if (count == 0 && GeneEngineDriver.livingGenes != null)
            {
                for (int i = 0; i < GeneEngineDriver.livingGenes.Count; i++)
                {
                    var monster = GeneEngineDriver.livingGenes[i];
                    if (monster?.currentGenes == null) continue;
                    AddGenes(monster.currentGenes, ref hp, ref ms, ref attackSpeed, ref attackDamage, ref count);
                }
            }

            if (count <= 0)
            {
                return new TelemetryDifficultySnapshot("GA", 1f, 1f, 1f, 1f);
            }

            return new TelemetryDifficultySnapshot("GA", hp / count, ms / count, attackSpeed / count, attackDamage / count);
        }

        private static void AddGenes(
            Dictionary<GeneStat, float> genes,
            ref float hp,
            ref float ms,
            ref float attackSpeed,
            ref float attackDamage,
            ref int count)
        {
            hp += GetGeneOrDefault(genes, GeneStat.MaxHealth);
            ms += GetGeneOrDefault(genes, GeneStat.MoveSpeed);
            attackSpeed += GetGeneOrDefault(genes, GeneStat.AttackSpeed);
            attackDamage += GetGeneOrDefault(genes, GeneStat.AttackDamage);
            count++;
        }

        private static float GetGeneOrDefault(Dictionary<GeneStat, float> genes, GeneStat stat)
        {
            return genes != null && genes.TryGetValue(stat, out float value) ? Sanitize(value) : 1f;
        }

        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp(value, 0.0001f, 100f);
        }
    }
}
