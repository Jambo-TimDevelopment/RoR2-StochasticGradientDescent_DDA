using RoR2;
using UnityEngine;
using RoR2.UI;
using UnityEngine.EventSystems;

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
            // IMPORTANT:
            // When SGD hooks are enabled, they already detour Run.Start and CharacterBody.OnDestroy.
            // Detouring the same methods from multiple subsystems has been observed to destabilize
            // repeat-run lobby startup (InvalidCastException in PauseStopController.Awake).
            //
            // So:
            // - Telemetry-only mode: we hook Run.Start and CharacterBody.OnDestroy ourselves.
            // - Telemetry+SGD mode: we DO NOT hook those methods; TelemetryRuntimeDriver is attached
            //   by SgdRuntimeDriver.Run_Start and player deaths are forwarded from SGD sensors.
            bool sgdHooksEnabled = ConfigManager.diagnosticsEnableSgdHooks != null && ConfigManager.diagnosticsEnableSgdHooks.Value;
            if (!sgdHooksEnabled)
            {
                On.RoR2.Run.BeginGameOver += Run_BeginGameOver;
                On.RoR2.Run.Start += Run_Start;
                On.RoR2.CharacterBody.OnDestroy += CharacterBody_OnDestroy;
            }
            // Keep the survey widget lazy-attached (TelemetrySurveyWidget.Show calls EnsureAttached).
            // Avoid creating persistent UI components on startup to reduce risk of repeat-run instability.

            On.RoR2.UI.MainMenu.BaseMainMenuScreen.OnEnter += BaseMainMenuScreen_OnEnter;
        }

        internal static void NotifyRunBeginGameOver(GameEndingDef gameEndingDef)
        {
            RequestSurvey(gameEndingDef != null && gameEndingDef.isWin ? "victory" : "game_over");
        }

        internal static void NotifyPlayerBodyDestroyed(CharacterBody body)
        {
            if (_activeDriver == null ||
                !ConfigManager.telemetryEnabled.Value ||
                body == null)
            {
                return;
            }

            if (!IsPlayerBody(body))
            {
                return;
            }

            // Collapse duplicates (scene transitions / multiple destroys).
            if (Time.time - _activeDriver._lastPlayerDeathAt < 1f)
            {
                return;
            }

            _activeDriver._lastPlayerDeathAt = Time.time;
            _activeDriver._session.RecordPlayerDeath();
            TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildPlayerDeath(_activeDriver._session, body, null));
            RequestSurvey("player_death");
            _activeDriver.StartFlushIfIdle();
        }

        public static bool HasActiveSession =>
            GetSurveySession() != null;

        public static bool HasSubmittedSurvey =>
            GetSurveySession()?.HasSurveyCompleted == true;

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

                // Ensure survey always has a session_end companion in the export (e.g. on immediate quit).
                if (!session.HasSessionEndQueued)
                {
                    session.MarkSessionEndQueued();
                    TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildSessionEnd(session, "post_session_survey_submitted"));
                }

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
            TelemetrySessionState session = GetSurveySession();
            if (session != null && !session.HasSurveyCompleted)
            {
                session.RecordSurveySkipped("ui_trigger=unknown");
                if (ConfigManager.telemetryEnabled.Value)
                {
                    TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildPostSessionSurveySkipped(session, session.SurveyComment));
                }
            }

            if (session != null && !session.HasSessionEndQueued)
            {
                session.MarkSessionEndQueued();
                TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildSessionEnd(session, "post_session_survey_skipped"));
            }

            CompletePendingSessionEndIfNeeded();
            StartAnyFlushIfPossible();
        }

        public static void SkipPendingSurvey(string comment)
        {
            TelemetrySessionState session = GetSurveySession();
            if (session != null && !session.HasSurveyCompleted)
            {
                session.RecordSurveySkipped(comment);
                if (ConfigManager.telemetryEnabled.Value)
                {
                    TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildPostSessionSurveySkipped(session, session.SurveyComment));
                }
            }

            if (session != null && !session.HasSessionEndQueued)
            {
                session.MarkSessionEndQueued();
                TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildSessionEnd(session, "post_session_survey_skipped"));
            }

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

        private static void BaseMainMenuScreen_OnEnter(
            On.RoR2.UI.MainMenu.BaseMainMenuScreen.orig_OnEnter orig,
            RoR2.UI.MainMenu.BaseMainMenuScreen self,
            RoR2.UI.MainMenu.MainMenuController mainMenuController)
        {
            orig(self, mainMenuController);

            EnsureMpEventSystemForMainMenu();

            // Force cursor usable in main menu. Some subsystems (and the game itself) can leave the
            // cursor hidden/locked after returning from a run, which blocks starting a new run.
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            TryShowPendingSurveyFromMainMenu();
        }

        private static void EnsureMpEventSystemForMainMenu()
        {
            try
            {
                MPEventSystem[] mp = UnityEngine.Object.FindObjectsOfType<MPEventSystem>();
                EventSystem[] es = UnityEngine.Object.FindObjectsOfType<EventSystem>();

                // Pick Player0 when available, otherwise first enabled/first found.
                MPEventSystem chosen = null;
                if (mp != null)
                {
                    foreach (MPEventSystem item in mp)
                    {
                        if (item != null && item.name == "MPEventSystem Player0")
                        {
                            chosen = item;
                            break;
                        }
                    }
                    if (chosen == null)
                    {
                        foreach (MPEventSystem item in mp)
                        {
                            if (item != null && item.enabled)
                            {
                                chosen = item;
                                break;
                            }
                        }
                    }
                    if (chosen == null && mp.Length > 0) chosen = mp[0];
                }

                if (chosen != null && !chosen.enabled)
                {
                    chosen.enabled = true;
                }

                // If a plain Unity EventSystem is enabled alongside MPEventSystem, RoR2 cursor/UI can break.
                // Disable plain EventSystem instances if we have an MPEventSystem.
                if (chosen != null && es != null)
                {
                    foreach (EventSystem item in es)
                    {
                        if (item == null) continue;
                        if (item is MPEventSystem) continue;
                        if (item.enabled)
                        {
                            item.enabled = false;
                        }
                    }
                }

            }
            catch (System.Exception ex)
            {
                GeneticsArtifactPlugin.geneticLogSource?.LogWarning("[DDA] EnsureMpEventSystemForMainMenu failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void TryShowPendingSurveyFromMainMenu()
        {
            if (!ConfigManager.telemetryEnabled.Value || _pendingSurveySession == null)
            {
                return;
            }

            if (_pendingSurveySession.HasSurveyCompleted)
            {
                return;
            }

            RequestSurvey("pending_survey_main_menu");
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
                    if (!_session.HasSessionEndQueued)
                    {
                        _session.MarkSessionEndQueued();
                        TelemetryEventQueue.Enqueue(TelemetrySampleBuilder.BuildSessionEnd(_session, "run_destroyed"));
                    }
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
                    // Do NOT show the survey UI during Run teardown. It can happen while UI/EventSystem
                    // is being rebuilt, and has been correlated with repeat-run lobby instability.
                    // The pending survey will be shown on main menu enter.
                }
            }

            if (_activeDriver == this)
            {
                _activeDriver = null;
            }
        }

        private static void CharacterBody_OnDestroy(On.RoR2.CharacterBody.orig_OnDestroy orig, CharacterBody self)
        {
            try
            {
                if (_activeDriver == null ||
                    !ConfigManager.telemetryEnabled.Value ||
                    self == null)
                {
                    return;
                }

                if (!IsPlayerBody(self))
                {
                    return;
                }

                // OnDestroy can be invoked during scene transitions; collapse duplicates.
                if (Time.time - _activeDriver._lastPlayerDeathAt < 1f)
                {
                    return;
                }

                NotifyPlayerBodyDestroyed(self);
            }
            finally
            {
                orig(self);
            }
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
