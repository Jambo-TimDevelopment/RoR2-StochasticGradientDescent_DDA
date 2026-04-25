using RoR2;
using UnityEngine;

namespace GeneticsArtifact.Telemetry
{
    internal sealed class TelemetryRuntimeDriver : MonoBehaviour
    {
        private readonly TelemetrySessionState _session = new TelemetrySessionState();
        private float _sampleTimer;
        private float _flushTimer;
        private bool _isFlushing;

        public static void RegisterHooks()
        {
            On.RoR2.Run.Start += Run_Start;
        }

        private static void Run_Start(On.RoR2.Run.orig_Start orig, Run self)
        {
            orig(self);

            if (self != null && self.gameObject != null && self.gameObject.GetComponent<TelemetryRuntimeDriver>() == null)
            {
                self.gameObject.AddComponent<TelemetryRuntimeDriver>();
            }
        }

        private void Awake()
        {
            _session.StartNewRun();
            _sampleTimer = 0f;
            _flushTimer = 0f;

            if (ConfigManager.telemetryEnabled.Value)
            {
                TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildSessionStart(_session));
            }
        }

        private void Update()
        {
            if (!ConfigManager.telemetryEnabled.Value)
            {
                return;
            }

            float dt = Time.deltaTime;
            _sampleTimer += dt;
            _flushTimer += dt;

            float sampleInterval = Mathf.Max(1f, ConfigManager.telemetrySampleIntervalSeconds.Value);
            if (_sampleTimer >= sampleInterval)
            {
                _sampleTimer = 0f;
                TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildSample(_session, dt));

                if (_session.TryConsumeRecoveryEvent(out float recoverySeconds))
                {
                    TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildRecovery(_session, recoverySeconds));
                }
            }

            float flushInterval = Mathf.Max(5f, ConfigManager.telemetryFlushIntervalSeconds.Value);
            if (_flushTimer >= flushInterval)
            {
                _flushTimer = 0f;

                StartFlushIfIdle();
            }
        }

        private void OnDestroy()
        {
            if (ConfigManager.telemetryEnabled.Value && !string.IsNullOrEmpty(_session.SessionId))
            {
                TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildSessionEnd(_session));
                if (GeneticsArtifactPlugin.Instance != null)
                {
                    GeneticsArtifactPlugin.Instance.StartCoroutine(PostHogBatchClient.FlushQueuedEvents());
                }
            }
        }

        private void StartFlushIfIdle()
        {
            if (_isFlushing)
            {
                return;
            }

            StartCoroutine(FlushRoutine());
        }

        private System.Collections.IEnumerator FlushRoutine()
        {
            _isFlushing = true;
            yield return PostHogBatchClient.FlushQueuedEvents();
            _isFlushing = false;
        }
    }
}
