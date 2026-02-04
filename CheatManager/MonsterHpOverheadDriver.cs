using RoR2;
using System.Collections.Generic;
using UnityEngine;

namespace GeneticsArtifact.CheatManager
{
    /// <summary>
    /// Client-side debug helper: shows current HP numbers above each monster.
    /// Enabled via console command (see DdaCheatManager).
    /// </summary>
    public sealed class MonsterHpOverheadDriver : MonoBehaviour
    {
        private const float ScanIntervalSeconds = 0.50f;
        private const float TextUpdateIntervalSeconds = 0.20f;

        private static MonsterHpOverheadDriver _instance;

        public static bool IsEnabled { get; private set; }

        private readonly Dictionary<int, HpLabel> _labelsByBodyId = new Dictionary<int, HpLabel>(256);
        private float _nextScanTime;
        private float _nextTextUpdateTime;
        private Camera _camera;

        public static void SetEnabled(bool enabled)
        {
            IsEnabled = enabled;

            if (enabled)
            {
                EnsureInstance();
                if (_instance != null) _instance.enabled = true;
            }
            else
            {
                if (_instance != null)
                {
                    _instance.ClearAll();
                    _instance.enabled = false;
                }
            }
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;

            var root = new GameObject("DdaMonsterHpOverhead");
            DontDestroyOnLoad(root);
            _instance = root.AddComponent<MonsterHpOverheadDriver>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            if (!IsEnabled) return;

            if (_camera == null)
            {
                _camera = Camera.main;
            }

            float now = Time.time;

            if (now >= _nextScanTime)
            {
                _nextScanTime = now + ScanIntervalSeconds;
                ResyncBodies();
            }

            if (now >= _nextTextUpdateTime)
            {
                _nextTextUpdateTime = now + TextUpdateIntervalSeconds;
                UpdateAllTexts();
            }

            UpdateAllTransforms();
        }

        private void ResyncBodies()
        {
            // Mark all existing labels as "not seen".
            var seen = new HashSet<int>();

            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                if (body == null) continue;
                if (body.teamComponent == null || body.teamComponent.teamIndex != TeamIndex.Monster) continue;
                if (body.healthComponent == null) continue;

                int id = body.gameObject != null ? body.gameObject.GetInstanceID() : 0;
                if (id == 0) continue;
                seen.Add(id);

                if (!_labelsByBodyId.TryGetValue(id, out var label) || label == null)
                {
                    _labelsByBodyId[id] = CreateLabel(body);
                }
                else
                {
                    label.Body = body;
                }
            }

            // Remove labels for bodies that no longer exist.
            if (_labelsByBodyId.Count == 0) return;

            var toRemove = new List<int>();
            foreach (var kvp in _labelsByBodyId)
            {
                if (!seen.Contains(kvp.Key) || kvp.Value == null || kvp.Value.Body == null)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                RemoveLabel(toRemove[i]);
            }
        }

        private void UpdateAllTexts()
        {
            foreach (var kvp in _labelsByBodyId)
            {
                var label = kvp.Value;
                if (label == null) continue;
                label.RefreshText();
            }
        }

        private void UpdateAllTransforms()
        {
            foreach (var kvp in _labelsByBodyId)
            {
                var label = kvp.Value;
                if (label == null) continue;
                label.RefreshTransform(_camera);
            }
        }

        private HpLabel CreateLabel(CharacterBody body)
        {
            var go = new GameObject("DdaMonsterHpLabel");
            var label = go.AddComponent<HpLabel>();
            label.Initialize(body);
            return label;
        }

        private void RemoveLabel(int bodyId)
        {
            if (_labelsByBodyId.TryGetValue(bodyId, out var label))
            {
                if (label != null)
                {
                    Destroy(label.gameObject);
                }
            }

            _labelsByBodyId.Remove(bodyId);
        }

        private void ClearAll()
        {
            foreach (var kvp in _labelsByBodyId)
            {
                if (kvp.Value != null)
                {
                    Destroy(kvp.Value.gameObject);
                }
            }
            _labelsByBodyId.Clear();
        }

        private sealed class HpLabel : MonoBehaviour
        {
            // Lift HP numbers above the vanilla overhead healthbar.
            private const float BaseHeightOffset = 0.80f;
            private const float CharacterSize = 0.08f;

            public CharacterBody Body { get; set; }

            private TextMesh _text;

            public void Initialize(CharacterBody body)
            {
                Body = body;

                _text = gameObject.AddComponent<TextMesh>();
                _text.anchor = TextAnchor.MiddleCenter;
                _text.alignment = TextAlignment.Center;
                _text.fontSize = 48;
                _text.characterSize = CharacterSize;
                _text.color = new Color32(0, 255, 0, 255);
                _text.richText = false;

                RefreshText();
            }

            public void RefreshText()
            {
                if (_text == null) return;
                if (Body == null || Body.healthComponent == null)
                {
                    _text.text = "";
                    return;
                }

                float cur = Body.healthComponent.combinedHealth;
                float max = Body.healthComponent.fullCombinedHealth;
                if (float.IsNaN(cur) || float.IsInfinity(cur) || cur < 0f) cur = 0f;
                if (float.IsNaN(max) || float.IsInfinity(max) || max <= 0f) max = 0f;

                int curInt = Mathf.CeilToInt(cur);
                int maxInt = Mathf.CeilToInt(max);
                _text.text = maxInt > 0 ? $"{curInt}/{maxInt}" : $"{curInt}";
            }

            public void RefreshTransform(Camera cam)
            {
                if (Body == null)
                {
                    return;
                }

                var t = transform;
                Vector3 basePos = Body.corePosition;
                float y = BaseHeightOffset + Mathf.Max(0.2f, Body.radius);
                t.position = basePos + Vector3.up * y;

                if (cam != null)
                {
                    // Face the camera (use -toCam so text is not mirrored/backwards).
                    Vector3 toCam = cam.transform.position - t.position;
                    if (toCam.sqrMagnitude > 0.0001f)
                    {
                        t.rotation = Quaternion.LookRotation(-toCam);
                    }
                }
            }
        }
    }
}

