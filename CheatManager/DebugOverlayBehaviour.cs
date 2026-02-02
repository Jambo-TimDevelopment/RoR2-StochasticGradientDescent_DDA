using UnityEngine;
using UnityEngine.UI;

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
            if (_overlayRoot != null) return;

            _overlayRoot = new GameObject("DdaDebugOverlay");
            DontDestroyOnLoad(_overlayRoot);

            var canvas = _overlayRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            _overlayRoot.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _overlayRoot.AddComponent<GraphicRaycaster>();

            var textObj = new GameObject("DebugText");
            textObj.transform.SetParent(_overlayRoot.transform, false);

            _textComponent = textObj.AddComponent<Text>();
            _textComponent.font = GetFontForOverlay();
            _textComponent.fontSize = 14;
            _textComponent.color = Color.white;

            var rectTransform = _textComponent.rectTransform;
            rectTransform.anchorMin = new Vector2(0.02f, 0.98f);
            rectTransform.anchorMax = new Vector2(0.98f, 0.98f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.sizeDelta = new Vector2(-40, 200);
            rectTransform.anchoredPosition = Vector2.zero;

            _instance = _overlayRoot.AddComponent<DebugOverlayBehaviour>();
        }

        private void Update()
        {
            if (!DdaAlgorithmState.IsDebugOverlayEnabled || _textComponent == null) return;

            _updateTimer += Time.deltaTime;
            if (_updateTimer >= 0.5f)
            {
                _updateTimer = 0f;
                _textComponent.text = $"[DDA Debug] Random: {Random.Range(0, 100)}\nTime: {Time.time:F1}s";
            }
        }
    }
}
