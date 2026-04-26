using RoR2;
using UnityEngine;

namespace GeneticsArtifact.Telemetry
{
    internal sealed class TelemetryRuntimeDriver : MonoBehaviour
    {
        private static TelemetryRuntimeDriver _activeDriver;
        private static TelemetrySessionState _pendingSurveySession;
        private static string _pendingSurveyEndReason = "";
        private static bool _pendingSessionEndQueued;

        private readonly TelemetrySessionState _session = new TelemetrySessionState();
        private float _sampleTimer;
        private float _flushTimer;
        private float _lastPlayerDeathAt = -1000f;
        private bool _isFlushing;

        public static void RegisterHooks()
        {
            On.RoR2.Run.Start += Run_Start;
            On.RoR2.Run.BeginGameOver += Run_BeginGameOver;
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            TelemetrySurveyWidget.EnsureAttached();
        }

        public static bool HasActiveSession =>
            GetSurveySession() != null;

        public static bool HasSubmittedSurvey =>
            GetSurveySession()?.HasSurvey == true;

        public static bool RecordPostSessionSurvey(int fairnessLikert, int continuityLikert, string comment)
        {
            TelemetrySessionState session = GetSurveySession();
            if (session == null)
            {
                return false;
            }

            session.RecordSurvey(fairnessLikert, continuityLikert, comment);
            if (ConfigManager.telemetryEnabled.Value)
            {
                TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildPostSessionSurvey(
                    session,
                    session.SurveyFairnessLikert,
                    session.SurveyContinuityLikert,
                    session.SurveyComment));
                CompletePendingSessionEndIfNeeded();
                StartAnyFlushIfPossible();
            }

            return true;
        }

        public static void RequestSurvey(string triggerReason)
        {
            if (!ConfigManager.telemetryEnabled.Value || !HasActiveSession || HasSubmittedSurvey)
            {
                return;
            }

            TelemetrySurveyWidget.Show(triggerReason);
        }

        public static void SkipPendingSurvey()
        {
            CompletePendingSessionEndIfNeeded();
            StartAnyFlushIfPossible();
        }

        private static void Run_Start(On.RoR2.Run.orig_Start orig, Run self)
        {
            orig(self);

            if (self != null && self.gameObject != null && self.gameObject.GetComponent<TelemetryRuntimeDriver>() == null)
            {
                self.gameObject.AddComponent<TelemetryRuntimeDriver>();
            }
        }

        private static void Run_BeginGameOver(On.RoR2.Run.orig_BeginGameOver orig, Run self, GameEndingDef gameEndingDef)
        {
            RequestSurvey(gameEndingDef != null && gameEndingDef.isWin ? "victory" : "game_over");
            orig(self, gameEndingDef);
        }

        private void Awake()
        {
            _activeDriver = this;
            _session.StartNewRun();
            _sampleTimer = 0f;
            _flushTimer = 0f;
            _lastPlayerDeathAt = -1000f;

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
                float elapsedSinceLastSample = _sampleTimer;
                int elapsedIntervals = Mathf.Max(1, Mathf.FloorToInt(_sampleTimer / sampleInterval));
                _sampleTimer -= elapsedIntervals * sampleInterval;
                _session.RecordMissedSampleIntervals(elapsedIntervals - 1);

                TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildSample(_session, elapsedSinceLastSample));

                if (_session.TryConsumeRecoveryEvent(out float recoverySeconds))
                {
                    TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildRecovery(_session, recoverySeconds));
                }

                while (_session.TryConsumeDegradationTransition(out TelemetryDegradationTransition transition))
                {
                    TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildDegradationTransition(_session, transition));
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
                if (_session.HasSurvey)
                {
                    TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildSessionEnd(_session, "run_destroyed"));
                    if (GeneticsArtifactPlugin.Instance != null)
                    {
                        GeneticsArtifactPlugin.Instance.StartCoroutine(PostHogBatchClient.FlushQueuedEvents());
                    }
                }
                else
                {
                    _pendingSurveySession = _session;
                    _pendingSurveyEndReason = "run_destroyed";
                    _pendingSessionEndQueued = false;
                    RequestSurvey("run_destroyed");
                }
            }

            if (_activeDriver == this)
            {
                _activeDriver = null;
            }
        }

        private static void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            orig(self, damageInfo);

            if (_activeDriver == null ||
                !ConfigManager.telemetryEnabled.Value ||
                self == null ||
                damageInfo == null)
            {
                return;
            }

            CharacterBody victimBody = self.body;
            if (!IsPlayerBody(victimBody))
            {
                return;
            }

            if (self.alive || self.health > 0f)
            {
                return;
            }

            // TakeDamage can be invoked multiple times around lethal damage; collapse duplicates.
            if (Time.time - _activeDriver._lastPlayerDeathAt < 1f)
            {
                return;
            }

            _activeDriver._lastPlayerDeathAt = Time.time;
            _activeDriver._session.RecordPlayerDeath();
            TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildPlayerDeath(_activeDriver._session, victimBody, damageInfo));
            RequestSurvey("player_death");
            _activeDriver.StartFlushIfIdle();
        }

        private static bool IsPlayerBody(CharacterBody body)
        {
            return body != null &&
                   (body.isPlayerControlled ||
                    (body.teamComponent != null && body.teamComponent.teamIndex == TeamIndex.Player));
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

        private static TelemetrySessionState GetSurveySession()
        {
            if (_activeDriver != null && !string.IsNullOrEmpty(_activeDriver._session.SessionId))
            {
                return _activeDriver._session;
            }

            return _pendingSurveySession != null && !string.IsNullOrEmpty(_pendingSurveySession.SessionId)
                ? _pendingSurveySession
                : null;
        }

        private static void CompletePendingSessionEndIfNeeded()
        {
            if (_pendingSurveySession == null || _pendingSessionEndQueued)
            {
                return;
            }

            TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildSessionEnd(_pendingSurveySession, _pendingSurveyEndReason));
            _pendingSessionEndQueued = true;
            _pendingSurveySession = null;
            _pendingSurveyEndReason = "";
        }

        private static void StartAnyFlushIfPossible()
        {
            if (_activeDriver != null)
            {
                _activeDriver.StartFlushIfIdle();
                return;
            }

            if (GeneticsArtifactPlugin.Instance != null)
            {
                GeneticsArtifactPlugin.Instance.StartCoroutine(PostHogBatchClient.FlushQueuedEvents());
            }
        }
    }
}
