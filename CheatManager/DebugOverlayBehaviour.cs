using UnityEngine;
using UnityEngine.UI;
using GeneticsArtifact.SgdEngine;
using GeneticsArtifact.SgdEngine.Decision;
using GeneticsArtifact.SgdEngine.Actuators;

namespace GeneticsArtifact.CheatManager
{
    /// <summary>
    /// Displays debug overlay on screen when enabled.
    /// </summary>
    public class DebugOverlayBehaviour : MonoBehaviour
    {
        private static DebugOverlayBehaviour _instance;
        private static GameObject _overlayRoot;
        private static Text _textComponent;
        private static float _updateTimer;

        public static void UpdateVisibility()
        {
            if (DdaAlgorithmState.IsDebugOverlayEnabled)
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

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) return font;

            return Font.CreateDynamicFontFromOSFont("Arial", 14);
        }

        private static void EnsureOverlayExists()
        {
            if (_overlayRoot != null)
            {
                // Be defensive: if the overlay was created by an older version, ensure it stays click-through.
                MakeOverlayClickThrough(_overlayRoot);
                if (_textComponent != null)
                {
                    ApplyTextStyle(_textComponent);
                    _textComponent.raycastTarget = false;
                }
                return;
            }

            _overlayRoot = new GameObject("DdaDebugOverlay");
            DontDestroyOnLoad(_overlayRoot);

            var canvas = _overlayRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            _overlayRoot.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            MakeOverlayClickThrough(_overlayRoot);

            var textObj = new GameObject("DebugText");
            textObj.transform.SetParent(_overlayRoot.transform, false);

            _textComponent = textObj.AddComponent<Text>();
            _textComponent.font = GetFontForOverlay();
            ApplyTextStyle(_textComponent);
            _textComponent.raycastTarget = false;

            var rectTransform = _textComponent.rectTransform;
            rectTransform.anchorMin = new Vector2(0.02f, 0.98f);
            rectTransform.anchorMax = new Vector2(0.98f, 0.98f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.sizeDelta = new Vector2(-40, 520);
            rectTransform.anchoredPosition = Vector2.zero;

            _instance = _overlayRoot.AddComponent<DebugOverlayBehaviour>();
        }

        private static void ApplyTextStyle(Text text)
        {
            if (text == null) return;

            // Smaller font + higher contrast for dense debug output.
            text.fontSize = 11;
            text.color = new Color(0.85f, 1.00f, 0.85f, 1f);
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            // Improve readability on bright backgrounds.
            var outline = text.GetComponent<Outline>() ?? text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
        }

        private static void MakeOverlayClickThrough(GameObject overlayRoot)
        {
            if (overlayRoot == null) return;

            // IMPORTANT: the debug overlay must never block UI interactions (menus, HUD, etc).
            var raycaster = overlayRoot.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
            {
                raycaster.enabled = false;
            }

            var canvasGroup = overlayRoot.GetComponent<CanvasGroup>() ?? overlayRoot.AddComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.ignoreParentGroups = true;
        }

        private void Update()
        {
            if (!DdaAlgorithmState.IsDebugOverlayEnabled || _textComponent == null) return;

            _updateTimer += Time.deltaTime;
            if (_updateTimer >= 0.5f)
            {
                _updateTimer = 0f;

                string actuatorsText =
                    "Actuators:\n" +
                    $"HP (MaxHealth): {SgdActuatorsRuntimeState.MaxHealthMultiplier:F2}\n" +
                    $"MS (MoveSpeed): {SgdActuatorsRuntimeState.MoveSpeedMultiplier:F2}\n" +
                    $"AS (AttackSpeed): {SgdActuatorsRuntimeState.AttackSpeedMultiplier:F2}\n" +
                    $"DMG (AttackDamage): {SgdActuatorsRuntimeState.AttackDamageMultiplier:F2}\n";

                string decisionText =
                    "Decision (SGD):\n" +
                    $"Step: {SgdDecisionRuntimeState.StepSeconds:F1}s\n" +
                    $"Combat timer: {SgdDecisionRuntimeState.CombatSecondsSinceLastStep:F1}s (next in {SgdDecisionRuntimeState.CombatSecondsUntilNextStep:F1}s)\n" +
                    $"Applied to monsters (last step): {SgdDecisionRuntimeState.AppliedMonstersLast}\n" +
                    $"Axes enabled:\n" +
                    $"  HP: {(SgdDecisionRuntimeState.IsMaxHealthAdaptationEnabled ? "ENABLED" : "DISABLED")}\n" +
                    $"  MS: {(SgdDecisionRuntimeState.IsMoveSpeedAdaptationEnabled ? "ENABLED" : "DISABLED")}\n" +
                    $"  AS: {(SgdDecisionRuntimeState.IsAttackSpeedAdaptationEnabled ? "ENABLED" : "DISABLED")}\n" +
                    $"  DMG: {(SgdDecisionRuntimeState.IsAttackDamageAdaptationEnabled ? "ENABLED" : "DISABLED")}\n" +
                    $"Steps done: total={SgdDecisionRuntimeState.TotalStepsDone}, HP={SgdDecisionRuntimeState.MaxHealthStepsDone}, MS={SgdDecisionRuntimeState.MoveSpeedStepsDone}, AS={SgdDecisionRuntimeState.AttackSpeedStepsDone}, DMG={SgdDecisionRuntimeState.AttackDamageStepsDone}\n" +
                    "Axis telemetry (last step):\n" +
                    $"HP (MaxHealth):\n" +
                    $"  Multiplier: {SgdDecisionRuntimeState.MaxHealthMultiplierLast:F2}\n" +
                    $"  Skill: {SgdDecisionRuntimeState.MaxHealthSkill01Last:F2}, Challenge: {SgdDecisionRuntimeState.MaxHealthChallenge01Last:F2}, Error: {SgdDecisionRuntimeState.MaxHealthErrorLast:F2}\n" +
                    $"  Gradient: {SgdDecisionRuntimeState.MaxHealthGradientLast:F3}, Δθ: {SgdDecisionRuntimeState.MaxHealthDeltaThetaLast:F4}\n" +
                    $"MS (MoveSpeed):\n" +
                    $"  Multiplier: {SgdDecisionRuntimeState.MoveSpeedMultiplierLast:F2}\n" +
                    $"  Skill: {SgdDecisionRuntimeState.MoveSpeedSkill01Last:F2}, Challenge: {SgdDecisionRuntimeState.MoveSpeedChallenge01Last:F2}, Error: {SgdDecisionRuntimeState.MoveSpeedErrorLast:F2}\n" +
                    $"  Gradient: {SgdDecisionRuntimeState.MoveSpeedGradientLast:F3}, Δθ: {SgdDecisionRuntimeState.MoveSpeedDeltaThetaLast:F4}\n" +
                    $"AS (AttackSpeed):\n" +
                    $"  Multiplier: {SgdDecisionRuntimeState.AttackSpeedMultiplierLast:F2}\n" +
                    $"  Skill: {SgdDecisionRuntimeState.AttackSpeedSkill01Last:F2}, Challenge: {SgdDecisionRuntimeState.AttackSpeedChallenge01Last:F2}, Error: {SgdDecisionRuntimeState.AttackSpeedErrorLast:F2}\n" +
                    $"  Gradient: {SgdDecisionRuntimeState.AttackSpeedGradientLast:F3}, Δθ: {SgdDecisionRuntimeState.AttackSpeedDeltaThetaLast:F4}\n" +
                    $"DMG (AttackDamage):\n" +
                    $"  Multiplier: {SgdDecisionRuntimeState.AttackDamageMultiplierLast:F2}\n" +
                    $"  Skill: {SgdDecisionRuntimeState.AttackDamageSkill01Last:F2}, Challenge: {SgdDecisionRuntimeState.AttackDamageChallenge01Last:F2}, Error: {SgdDecisionRuntimeState.AttackDamageErrorLast:F2}\n" +
                    $"  Gradient: {SgdDecisionRuntimeState.AttackDamageGradientLast:F3}, Δθ: {SgdDecisionRuntimeState.AttackDamageDeltaThetaLast:F4}\n";

                string sensorsText;
                if (SgdSensorsRuntimeState.HasSample)
                {
                    var s = SgdSensorsRuntimeState.Sample;
                    sensorsText =
                        "Sensors:\n" +
                        $"IncomingDPS: {s.IncomingDamageRate:F1} (n={s.IncomingDamageNorm01:F2})\n" +
                        $"OutgoingDPS: {s.OutgoingDamageRate:F1} (n={s.OutgoingDamageNorm01:F2})\n" +
                        $"HitRate: {s.HitRateOnPlayer:F2}/s (n={s.HitRateOnPlayerNorm01:F2})\n" +
                        $"CombatUptime: {s.CombatUptime:P0}\n" +
                        $"LowHPUptime: {s.LowHealthUptime:P0}\n" +
                        $"Deaths/W: {s.DeathsPerWindow:F0} (n={s.DeathsPerWindowNorm01:F2})\n" +
                        $"AvgTTK: {s.AvgTtkSeconds:F2}s (n={s.AvgTtkSecondsNorm01:F2})\n";
                }
                else
                {
                    sensorsText = "Sensors: N/A\n";
                }

                if (SgdRuntimeState.HasVirtualPower)
                {
                    var vp = SgdRuntimeState.VirtualPower;

                    _textComponent.text =
                        $"[DDA Debug]\n" +
                        $"Time: {Time.time:F1}s\n" +
                        $"Algorithm: {DdaAlgorithmState.ActiveAlgorithm}\n" +
                        $"Body: {SgdRuntimeState.VirtualPowerBodyName}\n" +
                        $"V_p.offense: {vp.Offense:F3}\n" +
                        $"V_p.defense: {vp.Defense:F3}\n" +
                        $"V_p.mobility: {vp.Mobility:F3}\n" +
                        $"V_p.total: {vp.Total:F3}\n\n" +
                        actuatorsText + "\n" +
                        decisionText + "\n" +
                        sensorsText;
                }
                else
                {
                    _textComponent.text =
                        $"[DDA Debug]\n" +
                        $"Time: {Time.time:F1}s\n" +
                        $"Algorithm: {DdaAlgorithmState.ActiveAlgorithm}\n" +
                        "V_p: N/A (enable SGD or start a run)\n\n" +
                        actuatorsText + "\n" +
                        decisionText + "\n" +
                        sensorsText;
                }
            }
        }
    }
}
