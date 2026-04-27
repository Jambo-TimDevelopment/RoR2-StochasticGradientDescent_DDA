using UnityEngine;
using UnityEngine.EventSystems;
using RoR2.UI;

namespace GeneticsArtifact.Telemetry
{
    internal sealed class TelemetrySurveyWidget : MonoBehaviour
    {
        private const int WindowId = 453216;
        private const float OverlayOpacity = 0.86f;

        private sealed class SurveyText
        {
            public readonly string WindowTitle;
            public readonly string Intro;
            public readonly string FairnessQuestion;
            public readonly string ContinuityQuestion;
            public readonly string CommentLabel;
            public readonly string SubmitButton;
            public readonly string CloseButton;
            public readonly string SkipAndQuitButton;
            public readonly string MissingAnswers;
            public readonly string[] FairnessOptions;
            public readonly string[] ContinuityOptions;

            public SurveyText(
                string windowTitle,
                string intro,
                string fairnessQuestion,
                string continuityQuestion,
                string commentLabel,
                string submitButton,
                string closeButton,
                string skipAndQuitButton,
                string missingAnswers,
                string[] fairnessOptions,
                string[] continuityOptions)
            {
                WindowTitle = windowTitle;
                Intro = intro;
                FairnessQuestion = fairnessQuestion;
                ContinuityQuestion = continuityQuestion;
                CommentLabel = commentLabel;
                SubmitButton = submitButton;
                CloseButton = closeButton;
                SkipAndQuitButton = skipAndQuitButton;
                MissingAnswers = missingAnswers;
                FairnessOptions = fairnessOptions;
                ContinuityOptions = continuityOptions;
            }
        }

        private static readonly SurveyText RuText = new SurveyText(
            "Опрос после забега DDA",
            "Пожалуйста, оцените последний забег. Выберите один вариант в каждом вопросе.",
            "H5. Насколько справедливой ощущалась сложность?",
            "H6. Насколько плавной и непрерывной ощущалась динамика сложности?",
            "Комментарий, необязательно:",
            "Отправить ответы",
            "Закрыть без отправки",
            "Пропустить и выйти",
            "Для отправки нужно выбрать ответ на оба вопроса.",
            new[]
        {
            "1 - совсем несправедливо: сложность казалась случайной или наказующей",
            "2 - скорее несправедливо: часто было ощущение, что игра давит без причины",
            "3 - немного несправедливо: были заметные спорные моменты сложности",
            "4 - нейтрально: сложность не казалась ни честной, ни нечестной",
            "5 - немного справедливо: в основном вызов соответствовал моим действиям",
            "6 - скорее справедливо: сложность почти всегда ощущалась заслуженной",
            "7 - полностью справедливо: вызов стабильно соответствовал моей игре"
            },
            new[]
        {
            "1 - очень рвано: сложность менялась скачками и выбивала из темпа",
            "2 - рвано: резкие изменения были частыми и заметными",
            "3 - скорее рвано: иногда динамика сложности ощущалась неестественно",
            "4 - нейтрально: плавность изменений трудно оценить",
            "5 - скорее плавно: изменения в основном были постепенными",
            "6 - плавно: кривая сложности почти не нарушала темп игры",
            "7 - очень плавно: сложность ощущалась как единая непрерывная кривая"
            });

        private static readonly SurveyText EnText = new SurveyText(
            "DDA Post-Run Survey",
            "Please rate the last run. Select exactly one answer for each question.",
            "H5. How fair did the difficulty feel?",
            "H6. How smooth and continuous did the difficulty curve feel?",
            "Comment, optional:",
            "Submit answers",
            "Close without submitting",
            "Skip and quit",
            "Please select an answer for both questions before submitting.",
            new[]
            {
                "1 - completely unfair: difficulty felt random or punitive",
                "2 - mostly unfair: the game often felt harsh without a clear reason",
                "3 - slightly unfair: there were noticeable questionable difficulty moments",
                "4 - neutral: difficulty felt neither fair nor unfair",
                "5 - slightly fair: challenge mostly matched my actions",
                "6 - mostly fair: difficulty almost always felt earned",
                "7 - completely fair: challenge consistently matched my play"
            },
            new[]
            {
                "1 - very abrupt: difficulty changed in jumps and broke the flow",
                "2 - abrupt: sharp changes were frequent and noticeable",
                "3 - slightly abrupt: difficulty dynamics sometimes felt unnatural",
                "4 - neutral: smoothness was hard to judge",
                "5 - slightly smooth: changes were mostly gradual",
                "6 - smooth: the difficulty curve almost never disrupted pacing",
                "7 - very smooth: difficulty felt like one continuous curve"
            });

        private static TelemetrySurveyWidget _instance;
        private static bool _visible;
        private static bool _quitAfterClose;
        private static bool _allowQuit;
        private static string _triggerReason = "";

        private int _fairnessLikert;
        private int _continuityLikert;
        private string _comment = "";
        private Rect _windowRect;
        private Vector2 _scroll;
        private Texture2D _overlayTexture;
        private GUIStyle _windowStyle;
        private GUIStyle _introStyle;
        private GUIStyle _questionStyle;
        private GUIStyle _optionStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _textAreaStyle;
        private GUIStyle _missingStyle;
        private bool _savedCursorState;
        private bool _previousCursorVisible;
        private CursorLockMode _previousCursorLockMode;
        private bool _savedEventSystemState;
        private bool _previousEventSystemEnabled;
        private EventSystem _previousEventSystem;
        private bool _isWaitingForQuitFlush;

        public static void EnsureAttached()
        {
            if (_instance != null)
            {
                return;
            }

            if (GeneticsArtifactPlugin.Instance == null)
            {
                return;
            }

            _instance = GeneticsArtifactPlugin.Instance.gameObject.GetComponent<TelemetrySurveyWidget>();
            if (_instance == null)
            {
                _instance = GeneticsArtifactPlugin.Instance.gameObject.AddComponent<TelemetrySurveyWidget>();
            }
        }

        public static void Show(string triggerReason, bool quitAfterClose = false)
        {
            EnsureAttached();
            if (_instance == null || TelemetryRuntimeDriver.HasSubmittedSurvey)
            {
                return;
            }

            // If we show the survey in a menu context (non-MP EventSystem), do NOT restore the
            // in-run cursor state afterwards. Otherwise the main menu can end up with hidden cursor.
            if (EventSystem.current != null && !(EventSystem.current is MPEventSystem))
            {
                _instance._savedCursorState = true;
                _instance._previousCursorVisible = true;
                _instance._previousCursorLockMode = CursorLockMode.None;
            }

            _triggerReason = string.IsNullOrWhiteSpace(triggerReason) ? "manual" : triggerReason;
            _quitAfterClose = quitAfterClose;
            _visible = true;
            _instance.ResetAnswers();
            _instance.RememberCursorState();
            _instance.DisableBackgroundEventSystem();
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void Awake()
        {
            _instance = this;
            _windowRect = new Rect(0f, 0f, 760f, 620f);
            Application.wantsToQuit += WantsToQuit;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            RestoreBackgroundEventSystem();
            Application.wantsToQuit -= WantsToQuit;
        }

        private static bool WantsToQuit()
        {
            if (_allowQuit || !TelemetryRuntimeDriver.HasActiveSession || TelemetryRuntimeDriver.HasSubmittedSurvey)
            {
                return true;
            }

            Show("exit_attempt", quitAfterClose: true);
            return false;
        }

        private void OnGUI()
        {
            if (!_visible)
            {
                return;
            }

            // RoR2 UI can override cursor state (pause/menu logic). Keep it visible while the survey is open.
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            EnsureStyles();
            DrawDimmedOverlay();

            float width = Mathf.Min(980f, Screen.width - 56f);
            float height = Mathf.Min(780f, Screen.height - 56f);
            _windowRect.width = width;
            _windowRect.height = height;
            _windowRect.x = (Screen.width - width) * 0.5f;
            _windowRect.y = (Screen.height - height) * 0.5f;

            SurveyText text = GetText();
            GUI.ModalWindow(WindowId, _windowRect, DrawWindow, text.WindowTitle, _windowStyle);
        }

        private void DrawWindow(int id)
        {
            SurveyText text = GetText();
            GUILayout.BeginVertical();
            GUILayout.Space(18f);
            GUILayout.Label(text.Intro, _introStyle);
            GUILayout.Space(14f);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            GUILayout.Label(text.FairnessQuestion, _questionStyle);
            _fairnessLikert = DrawLikertOptions(text.FairnessOptions, _fairnessLikert);

            GUILayout.Space(22f);
            GUILayout.Label(text.ContinuityQuestion, _questionStyle);
            _continuityLikert = DrawLikertOptions(text.ContinuityOptions, _continuityLikert);

            GUILayout.Space(22f);
            GUILayout.Label(text.CommentLabel, _questionStyle);
            _comment = GUILayout.TextArea(_comment ?? "", _textAreaStyle, GUILayout.MinHeight(76f));
            GUILayout.EndScrollView();

            GUILayout.Space(14f);
            GUILayout.BeginHorizontal();
            GUI.enabled = !_isWaitingForQuitFlush && _fairnessLikert > 0 && _continuityLikert > 0;
            if (GUILayout.Button(text.SubmitButton, _buttonStyle, GUILayout.Height(48f)))
            {
                SubmitSurvey();
            }
            GUI.enabled = !_isWaitingForQuitFlush;

            if (GUILayout.Button(_quitAfterClose ? text.SkipAndQuitButton : text.CloseButton, _buttonStyle, GUILayout.Height(48f)))
            {
                string comment = "ui_trigger=" + _triggerReason;
                CloseWidget();
                TelemetryRuntimeDriver.SkipPendingSurvey(comment);
                if (_quitAfterClose)
                {
                    BeginQuitAfterFlush();
                }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (_fairnessLikert <= 0 || _continuityLikert <= 0)
            {
                GUILayout.Space(6f);
                GUILayout.Label(text.MissingAnswers, _missingStyle);
            }
            else if (_isWaitingForQuitFlush)
            {
                GUILayout.Space(6f);
                GUILayout.Label(Application.systemLanguage == SystemLanguage.Russian ? "Отправка телеметрии перед выходом..." : "Sending telemetry before quitting...", _missingStyle);
            }

            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 24f));
            GUILayout.EndVertical();
        }

        private int DrawLikertOptions(string[] options, int selected)
        {
            for (int i = 0; i < options.Length; i++)
            {
                bool wasSelected = selected == i + 1;
                bool isSelected = GUILayout.Toggle(wasSelected, options[i], _optionStyle, GUILayout.MinHeight(34f));
                if (isSelected && !wasSelected)
                {
                    selected = i + 1;
                }
            }

            return selected;
        }

        private static SurveyText GetText()
        {
            return Application.systemLanguage == SystemLanguage.Russian ? RuText : EnText;
        }

        private void SubmitSurvey()
        {
            string comment = string.IsNullOrWhiteSpace(_comment)
                ? "ui_trigger=" + _triggerReason
                : _comment.Trim() + " | ui_trigger=" + _triggerReason;

            if (TelemetryRuntimeDriver.RecordPostSessionSurvey(_fairnessLikert, _continuityLikert, comment))
            {
                if (_quitAfterClose)
                {
                    CloseWidget();
                    BeginQuitAfterFlush();
                    return;
                }

                CloseWidget();
            }
        }

        private void BeginQuitAfterFlush()
        {
            if (_isWaitingForQuitFlush)
            {
                return;
            }

            _isWaitingForQuitFlush = true;
            StartCoroutine(QuitAfterFlushRoutine());
        }

        private System.Collections.IEnumerator QuitAfterFlushRoutine()
        {
            // Best-effort: try to flush everything that is queued right now.
            yield return PostHogBatchClient.FlushAllQueuedEvents(maxSeconds: 10f, maxBatches: 128);

            _allowQuit = true;
            Application.Quit();
        }

        private void CloseWidget()
        {
            _visible = false;
            RestoreCursorState();
            RestoreBackgroundEventSystem();
        }

        private void ResetAnswers()
        {
            _fairnessLikert = 0;
            _continuityLikert = 0;
            _comment = "";
            _scroll = Vector2.zero;
        }

        private void RememberCursorState()
        {
            if (_savedCursorState)
            {
                return;
            }

            _previousCursorVisible = Cursor.visible;
            _previousCursorLockMode = Cursor.lockState;
            _savedCursorState = true;
        }

        private void RestoreCursorState()
        {
            if (!_savedCursorState)
            {
                return;
            }

            Cursor.visible = _previousCursorVisible;
            Cursor.lockState = _previousCursorLockMode;
            _savedCursorState = false;
        }

        private void DisableBackgroundEventSystem()
        {
            if (_savedEventSystemState)
            {
                return;
            }

            _previousEventSystem = EventSystem.current;
            if (_previousEventSystem != null)
            {
                _previousEventSystemEnabled = _previousEventSystem.enabled;
                _previousEventSystem.enabled = false;
            }

            _savedEventSystemState = true;
        }

        private void RestoreBackgroundEventSystem()
        {
            if (!_savedEventSystemState)
            {
                return;
            }

            // Only restore if it's still the active EventSystem (or there is no active EventSystem).
            // Restoring an old MPEventSystem after returning to main menu can create multiple active
            // EventSystems and destabilize the next lobby start.
            if (_previousEventSystem != null &&
                (EventSystem.current == null || EventSystem.current == _previousEventSystem))
            {
                _previousEventSystem.enabled = _previousEventSystemEnabled;
            }

            _previousEventSystem = null;
            _savedEventSystemState = false;
        }

        private void DrawDimmedOverlay()
        {
            if (_overlayTexture == null)
            {
                _overlayTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _overlayTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, OverlayOpacity));
                _overlayTexture.Apply();
            }

            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _overlayTexture, ScaleMode.StretchToFill);
        }

        private void EnsureStyles()
        {
            if (_windowStyle != null)
            {
                return;
            }

            _windowStyle = new GUIStyle(GUI.skin.window)
            {
                fontSize = 24,
                padding = new RectOffset(26, 26, 30, 24)
            };
            _windowStyle.normal.textColor = Color.white;

            _introStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                wordWrap = true,
                richText = false
            };
            _introStyle.normal.textColor = Color.white;

            _questionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 21,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            _questionStyle.normal.textColor = Color.white;

            _optionStyle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 18,
                wordWrap = true,
                padding = new RectOffset(28, 8, 7, 7)
            };
            _optionStyle.normal.textColor = Color.white;
            _optionStyle.onNormal.textColor = Color.white;
            _optionStyle.hover.textColor = Color.white;
            _optionStyle.onHover.textColor = Color.white;

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 19,
                fontStyle = FontStyle.Bold
            };

            _textAreaStyle = new GUIStyle(GUI.skin.textArea)
            {
                fontSize = 18,
                wordWrap = true
            };

            _missingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            _missingStyle.normal.textColor = new Color(1f, 0.78f, 0.35f, 1f);
        }
    }
}
