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
            rectTransform.sizeDelta = new Vector2(-40, 440);
            rectTransform.anchoredPosition = Vector2.zero;

            _instance = _overlayRoot.AddComponent<DebugOverlayBehaviour>();
        }

        private static void ApplyTextStyle(Text text)
        {
            if (text == null) return;

            // Smaller font + higher contrast for dense debug output.
            text.fontSize = 11;
            text.color = new Color(0.85f, 1.00f, 0.85f, 1f);

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

                string headerLine = $"[DDA Debug] t={Time.time:F1}s alg={DdaAlgorithmState.ActiveAlgorithm}";
                string bodyLine = $"Body: {SgdRuntimeState.VirtualPowerBodyName}";

                string vpLine = "V_p: N/A";
                if (SgdRuntimeState.HasVirtualPower)
                {
                    var vp = SgdRuntimeState.VirtualPower;
                    vpLine = $"V_p: total={vp.Total:F3} (o={vp.Offense:F3}, d={vp.Defense:F3}, m={vp.Mobility:F3})";
                }

                string actuatorsLine =
                    $"Actuators: HP={SgdActuatorsRuntimeState.MaxHealthMultiplier:F2} " +
                    $"MS={SgdActuatorsRuntimeState.MoveSpeedMultiplier:F2} " +
                    $"AS={SgdActuatorsRuntimeState.AttackSpeedMultiplier:F2} " +
                    $"DMG={SgdActuatorsRuntimeState.AttackDamageMultiplier:F2}";

                string decisionMetaLine =
                    $"SGD: step={SgdDecisionRuntimeState.StepSeconds:F1}s " +
                    $"combat={SgdDecisionRuntimeState.CombatSecondsSinceLastStep:F1}/{SgdDecisionRuntimeState.StepSeconds:F1} " +
                    $"next={SgdDecisionRuntimeState.CombatSecondsUntilNextStep:F1}s " +
                    $"steps={SgdDecisionRuntimeState.TotalStepsDone} " +
                    $"applied={SgdDecisionRuntimeState.AppliedMonstersLast}";

                string axesLine =
                    $"Axes: HP={(SgdDecisionRuntimeState.IsMaxHealthAdaptationEnabled ? "on" : "off")} " +
                    $"MS={(SgdDecisionRuntimeState.IsMoveSpeedAdaptationEnabled ? "on" : "off")} " +
                    $"AS={(SgdDecisionRuntimeState.IsAttackSpeedAdaptationEnabled ? "on" : "off")} " +
                    $"DMG={(SgdDecisionRuntimeState.IsAttackDamageAdaptationEnabled ? "on" : "off")}";

                string hpLine =
                    $"HP:  mult={SgdDecisionRuntimeState.MaxHealthMultiplierLast:F2} " +
                    $"s={SgdDecisionRuntimeState.MaxHealthSkill01Last:F2} " +
                    $"c={SgdDecisionRuntimeState.MaxHealthChallenge01Last:F2} " +
                    $"e={SgdDecisionRuntimeState.MaxHealthErrorLast:F2} " +
                    $"dθ={SgdDecisionRuntimeState.MaxHealthDeltaThetaLast:F4}";

                string msLine =
                    $"MS:  mult={SgdDecisionRuntimeState.MoveSpeedMultiplierLast:F2} " +
                    $"s={SgdDecisionRuntimeState.MoveSpeedSkill01Last:F2} " +
                    $"c={SgdDecisionRuntimeState.MoveSpeedChallenge01Last:F2} " +
                    $"e={SgdDecisionRuntimeState.MoveSpeedErrorLast:F2} " +
                    $"dθ={SgdDecisionRuntimeState.MoveSpeedDeltaThetaLast:F4}";

                string asLine =
                    $"AS:  mult={SgdDecisionRuntimeState.AttackSpeedMultiplierLast:F2} " +
                    $"s={SgdDecisionRuntimeState.AttackSpeedSkill01Last:F2} " +
                    $"c={SgdDecisionRuntimeState.AttackSpeedChallenge01Last:F2} " +
                    $"e={SgdDecisionRuntimeState.AttackSpeedErrorLast:F2} " +
                    $"dθ={SgdDecisionRuntimeState.AttackSpeedDeltaThetaLast:F4}";

                string dmgLine =
                    $"DMG: mult={SgdDecisionRuntimeState.AttackDamageMultiplierLast:F2} " +
                    $"s={SgdDecisionRuntimeState.AttackDamageSkill01Last:F2} " +
                    $"c={SgdDecisionRuntimeState.AttackDamageChallenge01Last:F2} " +
                    $"e={SgdDecisionRuntimeState.AttackDamageErrorLast:F2} " +
                    $"dθ={SgdDecisionRuntimeState.AttackDamageDeltaThetaLast:F4}";

                string sensorsLine = "Sensors: N/A";
                if (SgdSensorsRuntimeState.HasSample)
                {
                    var s = SgdSensorsRuntimeState.Sample;
                    sensorsLine =
                        $"Sensors: in={s.IncomingDamageRate:F1}(n={s.IncomingDamageNorm01:F2}) " +
                        $"out={s.OutgoingDamageRate:F1}(n={s.OutgoingDamageNorm01:F2}) " +
                        $"hit={s.HitRateOnPlayer:F2}/s(n={s.HitRateOnPlayerNorm01:F2}) " +
                        $"low={s.LowHealthUptime:P0} deaths={s.DeathsPerWindow:F0}(n={s.DeathsPerWindowNorm01:F2}) " +
                        $"ttk={s.AvgTtkSeconds:F1}s(n={s.AvgTtkSecondsNorm01:F2})";
                }

                _textComponent.text =
                    headerLine + "\n" +
                    bodyLine + "\n" +
                    vpLine + "\n" +
                    actuatorsLine + "\n" +
                    decisionMetaLine + "\n" +
                    axesLine + "\n" +
                    hpLine + "\n" +
                    msLine + "\n" +
                    asLine + "\n" +
                    dmgLine + "\n" +
                    sensorsLine;
            }
        }
    }
}
