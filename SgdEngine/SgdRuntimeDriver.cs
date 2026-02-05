using GeneticsArtifact.CheatManager;
using GeneticsArtifact.SgdEngine.Decision;
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
            SgdSensorsHooks.RegisterHooks();
        }

        private static void Run_Start(On.RoR2.Run.orig_Start orig, Run self)
        {
            orig(self);

            // Attach only once per Run. We keep it lightweight and gate work in Update().
            if (self != null && self.gameObject != null && self.gameObject.GetComponent<SgdRuntimeDriver>() == null)
            {
                self.gameObject.AddComponent<SgdRuntimeDriver>();
            }
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

            // Hard gate: do nothing unless SGD is selected or overlay is enabled.
            if (!isSgdActive && !DdaAlgorithmState.IsDebugOverlayEnabled)
            {
                return;
            }

            CharacterBody body = FindAnyPlayerBody();
            if (body == null)
            {
                SgdRuntimeState.Clear();
                SgdSensorsRuntimeState.Clear();
                SgdDecisionRuntimeState.Reset();
                _trackedBody = null;
                _vpEstimator.Reset();
                return;
            }

            if (_trackedBody != body)
            {
                _trackedBody = body;
                _vpEstimator.Reset();
                SgdDecisionRuntimeState.Reset();
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

