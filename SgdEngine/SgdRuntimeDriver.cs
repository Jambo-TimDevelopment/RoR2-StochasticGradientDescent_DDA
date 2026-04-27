using GeneticsArtifact.CheatManager;
using GeneticsArtifact.DdaDebug;
using GeneticsArtifact.SgdEngine.Actuators;
using GeneticsArtifact.SgdEngine.Decision;
using GeneticsArtifact.Telemetry;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace GeneticsArtifact.SgdEngine
{
    /// <summary>
    /// Minimal runtime driver that continuously computes V_p(t) for the local player.
    /// Exists primarily to validate stability of the V_p formula and to feed debug overlay.
    /// </summary>
    public sealed class SgdRuntimeDriver : MonoBehaviour
    {
        private static SgdRuntimeDriver _instance;

        private readonly SgdVirtualPowerEstimator _vpEstimator = new SgdVirtualPowerEstimator();
        private CharacterBody _trackedBody;
        private bool _wasSgdActiveLastFrame;

        public static void RegisterHooks()
        {
            On.RoR2.Run.Start += Run_Start;
            On.RoR2.Run.BeginGameOver += Run_BeginGameOver;
            SgdSensorsHooks.RegisterHooks();

            // #region agent log
            DdaDebugLog.Write("H1", "SgdRuntimeDriver.cs:RegisterHooks", "Registered SGD hooks");
            // #endregion
        }

        private static void Run_Start(On.RoR2.Run.orig_Start orig, Run self)
        {
            // #region agent log
            DdaDebugLog.Write("H2", "SgdRuntimeDriver.cs:Run_Start:pre", "Run.Start entered", data: self != null ? ("run=" + self.name) : "run=null");
            // #endregion

            orig(self);

            // New run started: reset SGD axes and actuators to defaults (not per-stage).
            // This keeps debugging deterministic and matches the expected "new run resets" behavior.
            SgdDecisionRuntimeState.Reset();
            SgdActuatorsRuntimeState.Reset();
            SgdActuatorsApplier.ApplyToAllLivingMonsters();

            // Attach only once per Run. We keep it lightweight and gate work in Update().
            if (self != null && self.gameObject != null && self.gameObject.GetComponent<SgdRuntimeDriver>() == null)
            {
                self.gameObject.AddComponent<SgdRuntimeDriver>();
            }

            // When Telemetry hooks are enabled together with SGD hooks, TelemetryRuntimeDriver does NOT
            // detour Run.Start itself to avoid double detours. Attach it here instead.
            if (ConfigManager.diagnosticsEnableTelemetryHooks != null &&
                ConfigManager.diagnosticsEnableTelemetryHooks.Value &&
                ConfigManager.telemetryEnabled.Value &&
                self != null &&
                self.gameObject != null &&
                self.gameObject.GetComponent<TelemetryRuntimeDriver>() == null)
            {
                self.gameObject.AddComponent<TelemetryRuntimeDriver>();
            }

            // #region agent log
            DdaDebugLog.Write(
                "H2",
                "SgdRuntimeDriver.cs:Run_Start:post",
                "Run.Start completed",
                data: "attachedSgd=" + (self != null && self.gameObject != null && self.gameObject.GetComponent<SgdRuntimeDriver>() != null) +
                      "; attachedTelemetry=" + (self != null && self.gameObject != null && self.gameObject.GetComponent<TelemetryRuntimeDriver>() != null));
            // #endregion
        }

        private static void Run_BeginGameOver(On.RoR2.Run.orig_BeginGameOver orig, Run self, GameEndingDef gameEndingDef)
        {
            // #region agent log
            DdaDebugLog.Write(
                "H2",
                "SgdRuntimeDriver.cs:Run_BeginGameOver:pre",
                "Run.BeginGameOver entered",
                data: (gameEndingDef != null ? ("ending=" + gameEndingDef.cachedName + "; isWin=" + gameEndingDef.isWin) : "ending=null"));
            // #endregion

            if (ConfigManager.diagnosticsEnableTelemetryHooks != null &&
                ConfigManager.diagnosticsEnableTelemetryHooks.Value &&
                ConfigManager.telemetryEnabled.Value)
            {
                TelemetryRuntimeDriver.NotifyRunBeginGameOver(gameEndingDef);
            }

            orig(self, gameEndingDef);

            // #region agent log
            DdaDebugLog.Write("H2", "SgdRuntimeDriver.cs:Run_BeginGameOver:post", "Run.BeginGameOver completed");
            // #endregion
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            SgdRuntimeState.Clear();
            SgdDecisionRuntimeState.Reset();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            bool isSgdActive = DdaAlgorithmState.ActiveAlgorithm == DdaAlgorithmType.Sgd;
            if (isSgdActive && !_wasSgdActiveLastFrame)
            {
                // Reset on activation to make debugging easier and avoid stale momentum/timers.
                SgdDecisionRuntimeState.Reset();
            }
            _wasSgdActiveLastFrame = isSgdActive;

            CharacterBody body = FindAnyPlayerBody();
            if (body == null)
            {
                SgdRuntimeState.Clear();
                SgdSensorsRuntimeState.Clear();
                _trackedBody = null;
                _vpEstimator.Reset();
                return;
            }

            if (_trackedBody != body)
            {
                _trackedBody = body;
                _vpEstimator.Reset();
            }

            var sample = _vpEstimator.ComputeSmoothed(body, Time.deltaTime);
            SgdRuntimeState.SetVirtualPower(sample, body);

            SgdSensorsHooks.Tick(body, Time.deltaTime, sample);

            if (isSgdActive && NetworkServer.active)
            {
                SgdDecisionDriver.Tick(body, Time.deltaTime);
            }
        }

        private static CharacterBody FindAnyPlayerBody()
        {
            // Prefer player-controlled bodies when available.
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                if (body != null && body.isPlayerControlled)
                {
                    return body;
                }
            }

            // Fallback: any player team body.
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                if (body != null && body.teamComponent != null && body.teamComponent.teamIndex == TeamIndex.Player)
                {
                    return body;
                }
            }

            return null;
        }
    }
}

