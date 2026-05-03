using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using GeneticsArtifact.CheatManager;
using GeneticsArtifact.SgdEngine;
using GeneticsArtifact.SgdEngine.Actuators;
using GeneticsArtifact.Telemetry;
using R2API.Utils;
using System.Reflection;
using UnityEngine;

namespace GeneticsArtifact
{
    [BepInPlugin(ModGuid, ModName, ModVer)]
    #region [BepInDeps]
    [BepInDependency("com.bepis.r2api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api" + ".artifactcode", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api" + ".content_management", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api" + ".items", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api" + ".language", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api" + ".recalculatestats", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api" + ".commandhelper", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]
    #endregion
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
    public class GeneticsArtifactPlugin : BaseUnityPlugin
    {
        public const string ModVer = "4.5.7";
        public const string ModName = "PainGradient: Suffering Descent";
        public const string ModGuid = "com.RicoValdezio.ArtifactOfGenetics";
        public static GeneticsArtifactPlugin Instance;
        public static ManualLogSource geneticLogSource;
        public static AssetBundle geneticAssetBundle;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            geneticLogSource = Instance.Logger;
            geneticAssetBundle = AssetBundle.LoadFromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream("GeneticsArtifact.ArtifactResources.genetics"));

            ConfigManager.Init(Config);
            DdaCheatManager.Init();

            ArtifactOfGenetics.Init();
            GeneTokens.Init();

            geneticLogSource?.LogInfo(
                "[DDA] Diagnostics flags: " +
                "GeneTokenCalc=" + ConfigManager.diagnosticsEnableGeneTokenCalcHooks.Value +
                ", GeneticEngine=" + ConfigManager.diagnosticsEnableGeneticEngineHooks.Value +
                ", SGD=" + ConfigManager.diagnosticsEnableSgdHooks.Value +
                ", SgdActuators=" + ConfigManager.diagnosticsEnableSgdActuatorsHooks.Value +
                ", Telemetry=" + ConfigManager.diagnosticsEnableTelemetryHooks.Value +
                ", Rotator=" + ConfigManager.diagnosticsEnableRunModeRotatorHooks.Value);

            if (ConfigManager.diagnosticsEnableGeneTokenCalcHooks.Value)
            {
                GeneTokenCalc.RegisterHooks();
            }
            if (ConfigManager.diagnosticsEnableGeneticEngineHooks.Value)
            {
                GeneEngineDriver.RegisterHooks();
            }
            if (ConfigManager.diagnosticsEnableSgdHooks.Value)
            {
                SgdRuntimeDriver.RegisterHooks();
            }
            if (ConfigManager.diagnosticsEnableSgdActuatorsHooks.Value)
            {
                SgdActuatorsHooks.RegisterHooks();
            }
            if (ConfigManager.diagnosticsEnableTelemetryHooks.Value)
            {
                TelemetryRuntimeDriver.RegisterHooks();
            }
            if (ConfigManager.diagnosticsEnableRunModeRotatorHooks.Value)
            {
                DdaRunModeRotator.RegisterHooks();
            }

            foreach (PluginInfo plugin in Chainloader.PluginInfos.Values) { if (plugin.Metadata.GUID.Equals("com.rune580.riskofoptions")) { RiskOfOptionsCompat.Init(); break; } }
        }
    }
}
