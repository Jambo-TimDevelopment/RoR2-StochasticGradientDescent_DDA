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
                if (_textComponent != null) _textComponent.raycastTarget = false;
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
            _textComponent.fontSize = 14;
            _textComponent.color = Color.white;
            _textComponent.raycastTarget = false;

            var rectTransform = _textComponent.rectTransform;
            rectTransform.anchorMin = new Vector2(0.02f, 0.98f);
            rectTransform.anchorMax = new Vector2(0.98f, 0.98f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.sizeDelta = new Vector2(-40, 440);
            rectTransform.anchoredPosition = Vector2.zero;

            _instance = _overlayRoot.AddComponent<DebugOverlayBehaviour>();
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
                    $"Step: {SgdDecisionRuntimeState.StepSeconds:F1}s, combatTimer: {SgdDecisionRuntimeState.CombatSecondsSinceLastStep:F1}s (next in {SgdDecisionRuntimeState.CombatSecondsUntilNextStep:F1}s)\n" +
                    $"AS axis: {(SgdDecisionRuntimeState.IsAttackSpeedAdaptationEnabled ? "ENABLED" : "DISABLED")}, steps: {SgdDecisionRuntimeState.TotalStepsDone}\n" +
                    $"AS.skill: {SgdDecisionRuntimeState.AttackSpeedSkill01Last:F2}, challenge: {SgdDecisionRuntimeState.AttackSpeedChallenge01Last:F2}, error: {SgdDecisionRuntimeState.AttackSpeedErrorLast:F2}\n" +
                    $"AS.mult(last): {SgdDecisionRuntimeState.AttackSpeedMultiplierLast:F2}, grad: {SgdDecisionRuntimeState.AttackSpeedGradientLast:F3}, dθ: {SgdDecisionRuntimeState.AttackSpeedDeltaThetaLast:F4}\n" +
                    $"AS.appliedMonsters: {SgdDecisionRuntimeState.AttackSpeedAppliedMonstersLast}\n";

                if (SgdRuntimeState.HasVirtualPower)
                {
                    var vp = SgdRuntimeState.VirtualPower;

                    string sensorsText;
                    if (SgdSensorsRuntimeState.HasSample)
                    {
                        var s = SgdSensorsRuntimeState.Sample;
                        sensorsText =
                            $"Sensors:\n" +
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

                    _textComponent.text =
                        $"[DDA Debug]\n" +
                        $"Time: {Time.time:F1}s\n" +
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
                        "V_p: N/A (enable SGD or start a run)\n\n" +
                        actuatorsText + "\n" +
                        decisionText;
                }
            }
        }
    }
}
