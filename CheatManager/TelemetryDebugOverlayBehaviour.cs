using GeneticsArtifact.Telemetry;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace GeneticsArtifact.CheatManager
{
    /// <summary>
    /// Universal telemetry overlay: shows the last "dda_sample" payload enqueued for PostHog.
    /// Intended for validating that values shown on screen match what's exported from PostHog JSONL.
    /// </summary>
    public sealed class TelemetryDebugOverlayBehaviour : MonoBehaviour
    {
        private static TelemetryDebugOverlayBehaviour _instance;
        private static GameObject _overlayRoot;
        private static Text _textComponent;
        private static float _updateTimer;

        public static void UpdateVisibility()
        {
            if (DdaAlgorithmState.IsTelemetryOverlayEnabled)
            {
                EnsureOverlayExists();
                if (_instance != null)
                {
                    _instance.gameObject.SetActive(true);
                }
            }
            else
            {
                if (_instance != null)
                {
                    _instance.gameObject.SetActive(false);
                }
            }
        }

        private static Font GetFontForOverlay()
        {
            var font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null) return font;
            return Font.CreateDynamicFontFromOSFont("Arial", 14);
        }

        private static void EnsureOverlayExists()
        {
            if (_overlayRoot != null)
            {
                MakeOverlayClickThrough(_overlayRoot);
                if (_textComponent != null)
                {
                    ApplyTextStyle(_textComponent);
                    _textComponent.raycastTarget = false;
                }
                return;
            }

            _overlayRoot = new GameObject("DdaTelemetryOverlay");
            DontDestroyOnLoad(_overlayRoot);

            var canvas = _overlayRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1001;

            _overlayRoot.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            MakeOverlayClickThrough(_overlayRoot);

            var textObj = new GameObject("TelemetryText");
            textObj.transform.SetParent(_overlayRoot.transform, false);

            _textComponent = textObj.AddComponent<Text>();
            _textComponent.font = GetFontForOverlay();
            ApplyTextStyle(_textComponent);
            _textComponent.raycastTarget = false;

            var rectTransform = _textComponent.rectTransform;
            rectTransform.anchorMin = new Vector2(0.02f, 0.02f);
            rectTransform.anchorMax = new Vector2(0.98f, 0.02f);
            rectTransform.pivot = new Vector2(0.5f, 0f);
            rectTransform.sizeDelta = new Vector2(-40, 560);
            rectTransform.anchoredPosition = Vector2.zero;

            _instance = _overlayRoot.AddComponent<TelemetryDebugOverlayBehaviour>();
        }

        private static void ApplyTextStyle(Text text)
        {
            if (text == null) return;
            text.fontSize = 9;
            text.color = new Color(0.90f, 0.90f, 1.00f, 1f);
            text.alignment = TextAnchor.LowerLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var outline = text.GetComponent<Outline>() ?? text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
        }

        private static void MakeOverlayClickThrough(GameObject overlayRoot)
        {
            if (overlayRoot == null) return;
            var raycaster = overlayRoot.GetComponent<GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = false;

            var canvasGroup = overlayRoot.GetComponent<CanvasGroup>() ?? overlayRoot.AddComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.ignoreParentGroups = true;
        }

        private void Update()
        {
            if (!DdaAlgorithmState.IsTelemetryOverlayEnabled || _textComponent == null) return;

            _updateTimer += Time.deltaTime;
            if (_updateTimer < 0.5f) return;
            _updateTimer = 0f;

            TelemetryEvent evt = TelemetryEventQueue.LastEnqueuedSample ?? TelemetryEventQueue.LastEnqueuedEvent;
            if (evt == null)
            {
                _textComponent.text =
                    "[Telemetry Debug]\n" +
                    $"Time: {Time.time:F1}s\n" +
                    $"Algorithm: {DdaAlgorithmState.ActiveAlgorithm}\n" +
                    "Last event: N/A (no telemetry enqueued yet)\n";
                return;
            }

            var p = evt.Properties ?? new Dictionary<string, object>();
            string sessionId = GetString(p, "session_id");
            string mode = GetString(p, "dda_mode");
            float runElapsed = GetFloat(p, "run_elapsed_seconds");
            int queueCount = (int)GetFloat(p, "queue_count");
            float sampleInterval = GetFloat(p, "sample_interval_seconds");

            var sb = new StringBuilder(2048);
            sb.AppendLine("[Telemetry Debug]");
            sb.AppendLine($"Time: {Time.time:F1}s");
            sb.AppendLine($"Algorithm: {DdaAlgorithmState.ActiveAlgorithm} (telemetry dda_mode={mode})");
            sb.AppendLine($"Last enqueued: {evt.EventName} @ {evt.TimestampUtc:O}");
            sb.AppendLine($"Session: {sessionId}");
            sb.AppendLine($"Run elapsed: {runElapsed:F1}s  SampleInterval: {sampleInterval:F1}s  Queue: {queueCount}");
            sb.AppendLine();

            AppendAxis(sb, p, "max_health", "HP (MaxHealth)");
            AppendAxis(sb, p, "move_speed", "MS (MoveSpeed)");
            AppendAxis(sb, p, "attack_speed", "AS (AttackSpeed)");
            AppendAxis(sb, p, "attack_damage", "DMG (AttackDamage)");

            sb.AppendLine();
            sb.AppendLine($"Degradation: is_degraded={GetBool(p, "is_degraded")} signal={GetFloat(p, "degradation_signal"):F3}");
            sb.AppendLine($"Virtual total: Vp={GetFloat(p, "virtual_power_total"):F3} Vc={GetFloat(p, "virtual_challenge_total"):F3} gap={GetFloat(p, "virtual_gap_abs"):F3}");
            sb.AppendLine($"Virtual axes gap: hp={GetFloat(p, "virtual_gap_hp_abs"):F3} ms={GetFloat(p, "virtual_gap_move_speed_abs"):F3} as={GetFloat(p, "virtual_gap_attack_speed_abs"):F3} dmg={GetFloat(p, "virtual_gap_attack_damage_abs"):F3}");

            _textComponent.text = sb.ToString();
        }

        private static void AppendAxis(StringBuilder sb, Dictionary<string, object> p, string axis, string label)
        {
            string prefix = "axis_" + axis + "_";

            float mult = GetFloat(p, prefix + "multiplier");
            float prev = GetFloat(p, prefix + "previous_multiplier");
            float dmult = GetFloat(p, prefix + "delta_multiplier");
            bool isJump = GetBool(p, prefix + "is_jump");

            float skill = GetFloat(p, prefix + "skill01");
            float chall = GetFloat(p, prefix + "challenge01");
            float err = GetFloat(p, prefix + "error");
            float absErr = GetFloat(p, prefix + "abs_error");

            sb.AppendLine($"{label}:");
            sb.AppendLine($"  Mult: {mult:F3} (prev {prev:F3})  Δmult: {dmult:F3}  jump={isJump}");
            sb.AppendLine($"  Skill: {skill:F3}  Challenge: {chall:F3}  Error: {err:F3}  |err|: {absErr:F3}");
        }

        private static string GetString(Dictionary<string, object> p, string key)
        {
            if (p == null || string.IsNullOrEmpty(key)) return "";
            if (!p.TryGetValue(key, out object v) || v == null) return "";
            return Convert.ToString(v) ?? "";
        }

        private static float GetFloat(Dictionary<string, object> p, string key)
        {
            if (p == null || string.IsNullOrEmpty(key)) return 0f;
            if (!p.TryGetValue(key, out object v) || v == null) return 0f;
            try { return Convert.ToSingle(v); } catch { return 0f; }
        }

        private static bool GetBool(Dictionary<string, object> p, string key)
        {
            if (p == null || string.IsNullOrEmpty(key)) return false;
            if (!p.TryGetValue(key, out object v) || v == null) return false;
            try { return Convert.ToBoolean(v); } catch { return false; }
        }
    }
}

