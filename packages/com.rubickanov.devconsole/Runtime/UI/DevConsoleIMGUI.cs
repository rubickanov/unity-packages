using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.DevConsole
{
    /// <summary>IMGUI frontend for the DevConsole package backend (CommandRegistry + ConsoleLog).</summary>
    public class DevConsoleIMGUI : MonoBehaviour
    {
        private const int MaxHistory = 64;
        private const float ConsoleHeightRatio = 0.5f;
        private const string InputControlName = "DevConsoleInput";

        private static DevConsoleIMGUI? _instance;

        public static DevConsoleIMGUI? Instance => _instance;
        public static event Action<bool>? Toggled;
        public static bool IsOpen => _instance != null && _instance._isOpen;

        private bool _isOpen;
        private bool _requestFocus;
        private bool _consumeNextChar;
        private readonly List<string> _history = new();
        private int _historyIndex = -1;
        private string _savedInput = "";
        private string _inputText = "";
        private string _prevInputText = "";
        private bool _moveCursorToEnd;
        private Vector2 _scrollPos;
        private readonly List<string> _suggestions = new();
        private int _suggestionIndex = -1;
        private bool _scrollToBottom;

        // IMGUI styles (lazy init)
        private bool _stylesInitialized;
        private GUIStyle _logStyle = default!;
        private GUIStyle _inputStyle = default!;
        private GUIStyle _suggestionStyle = default!;
        private GUIStyle _suggestionActiveStyle = default!;
        private Texture2D? _bgTexture;
        private Texture2D? _inputBgTexture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            Toggled = null;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            CommandRegistry.Instance.Initialize();

            ConsoleLog.OnLogAdded += OnLogAdded;
            ConsoleLog.OnCleared += OnCleared;
        }

        private void OnDestroy()
        {
            ConsoleLog.OnLogAdded -= OnLogAdded;
            ConsoleLog.OnCleared -= OnCleared;

            if (_instance == this)
                _instance = null;

            if (_bgTexture != null)
                Destroy(_bgTexture);
            if (_inputBgTexture != null)
                Destroy(_inputBgTexture);
        }

        private void OnLogAdded(ConsoleLog.LogEntry entry)
        {
            _scrollToBottom = true;
        }

        private void OnCleared()
        {
            _scrollToBottom = true;
        }

        private void OnGUI()
        {
#if UNITY_SERVER
        return;
#endif
            var e = Event.current;

            // --- Consume the character produced by the backtick physical key ---
            if (_consumeNextChar)
            {
                if (e.type == EventType.KeyDown && e.character != '\0' && e.keyCode == KeyCode.None)
                {
                    _consumeNextChar = false;
                    e.Use();
                    return;
                }

                if (e.type == EventType.Repaint)
                    _consumeNextChar = false;
            }

            // --- Backtick toggle ---
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.BackQuote)
            {
                _isOpen = !_isOpen;
                _requestFocus = _isOpen;
                _consumeNextChar = true;
                if (!_isOpen)
                {
                    _suggestions.Clear();
                    _suggestionIndex = -1;
                }

                Toggled?.Invoke(_isOpen);
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.BackQuote && e.type == EventType.KeyUp)
            {
                e.Use();
                return;
            }

            if (!_isOpen) return;

            EnsureStyles();

            KeyCode capturedKey = KeyCode.None;
            if (e.type == EventType.KeyDown)
            {
                switch (e.keyCode)
                {
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                    case KeyCode.Tab:
                    case KeyCode.UpArrow:
                    case KeyCode.DownArrow:
                    case KeyCode.Escape:
                        capturedKey = e.keyCode;
                        e.Use();
                        break;
                }
            }

            // --- Draw UI ---
            float consoleHeight = Screen.height * ConsoleHeightRatio;
            float suggestionRowHeight = 20f;
            float inputRowHeight = 28f;
            float logHeight = consoleHeight - suggestionRowHeight - inputRowHeight;

            GUI.DrawTexture(new Rect(0, 0, Screen.width, consoleHeight), _bgTexture!);

            DrawLogArea(logHeight);
            DrawSuggestions(logHeight, suggestionRowHeight);
            DrawInput(logHeight + suggestionRowHeight, inputRowHeight);

            switch (capturedKey)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (!string.IsNullOrWhiteSpace(_inputText))
                    {
                        string input = _inputText.Trim();
                        _inputText = "";
                        _suggestions.Clear();
                        _suggestionIndex = -1;
                        AddToHistory(input);
                        _historyIndex = -1;
                        _savedInput = "";
                        ExecuteInput(input);
                    }

                    break;

                case KeyCode.Tab:
                    HandleTab();
                    _moveCursorToEnd = true;
                    break;

                case KeyCode.UpArrow:
                    NavigateHistory(1);
                    _moveCursorToEnd = true;
                    break;

                case KeyCode.DownArrow:
                    NavigateHistory(-1);
                    _moveCursorToEnd = true;
                    break;

                case KeyCode.Escape:
                    _isOpen = false;
                    _suggestions.Clear();
                    _suggestionIndex = -1;
                    Toggled?.Invoke(false);
                    break;
            }

            if (capturedKey == KeyCode.None && _inputText != _prevInputText)
            {
                _prevInputText = _inputText;
                _suggestionIndex = -1;
                UpdateSuggestions();
            }

            if (_requestFocus)
            {
                GUI.FocusControl(InputControlName);
                _requestFocus = false;
            }
        }

        // ── Log rendering ───────────────────────────────────────────

        private void DrawLogArea(float height)
        {
            var entries = ConsoleLog.Entries;
            var viewRect = new Rect(4, 0, Screen.width - 8, height);
            float contentHeight = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                string colored = ColorizeEntry(entries[i]);
                float h = _logStyle.CalcHeight(new GUIContent(colored), viewRect.width - 16);
                contentHeight += h;
            }

            var scrollContent = new Rect(0, 0, viewRect.width - 16, contentHeight);

            if (_scrollToBottom && contentHeight > height)
            {
                _scrollPos.y = contentHeight - height;
                _scrollToBottom = false;
            }

            _scrollPos = GUI.BeginScrollView(viewRect, _scrollPos, scrollContent);

            float y = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                string colored = ColorizeEntry(entries[i]);
                var content = new GUIContent(colored);
                float h = _logStyle.CalcHeight(content, scrollContent.width);
                GUI.Label(new Rect(0, y, scrollContent.width, h), content, _logStyle);
                y += h;
            }

            GUI.EndScrollView();
        }

        private static string ColorizeEntry(ConsoleLog.LogEntry entry)
        {
            return entry.Type switch
            {
                ConsoleLog.LogType.Warning => $"<color=#ffcc44>{entry.Message}</color>",
                ConsoleLog.LogType.Error => $"<color=#ff4444>{entry.Message}</color>",
                ConsoleLog.LogType.Success => $"<color=#44ff44>{entry.Message}</color>",
                ConsoleLog.LogType.Input => $"<color=#aaaaaa>{entry.Message}</color>",
                _ => entry.Message
            };
        }

        // ── Suggestions ─────────────────────────────────────────────

        private void DrawSuggestions(float y, float height)
        {
            if (_suggestions.Count == 0) return;

            var e = Event.current;
            float x = 4;

            for (int i = 0; i < _suggestions.Count && i < 12; i++)
            {
                var activeStyle = i == _suggestionIndex ? _suggestionActiveStyle : _suggestionStyle;

                string label = _suggestions[i];
                float w = activeStyle.CalcSize(new GUIContent(label)).x + 12;
                var rect = new Rect(x, y + 2, w, height - 4);

                GUI.Label(rect, label, activeStyle);

                if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
                {
                    _inputText = _suggestions[i] + " ";
                    _prevInputText = _inputText;
                    _suggestions.Clear();
                    _suggestionIndex = -1;
                    _moveCursorToEnd = true;
                    e.Use();
                    return;
                }

                x += w + 4;
                if (x > Screen.width - 20) break;
            }
        }

        private void UpdateSuggestions()
        {
            _suggestions.Clear();

            if (string.IsNullOrEmpty(_inputText))
                return;

            CommandRegistry.Instance.GetSuggestions(_inputText, _suggestions, 12);

            // Don't show if the only match is exactly what's typed
            if (_suggestions.Count == 1 &&
                string.Equals(_suggestions[0], _inputText.Trim(), StringComparison.OrdinalIgnoreCase))
                _suggestions.Clear();
        }

        private void HandleTab()
        {
            if (string.IsNullOrEmpty(_inputText)) return;

            if (_suggestions.Count > 0)
            {
                _suggestionIndex = (_suggestionIndex + 1) % _suggestions.Count;
                _inputText = _suggestions[_suggestionIndex] + " ";
            }
            else
            {
                UpdateSuggestions();

                if (_suggestions.Count > 0)
                {
                    _suggestionIndex = 0;
                    _inputText = _suggestions[0] + " ";

                    if (_suggestions.Count == 1)
                        _suggestions.Clear();
                }
            }

            _prevInputText = _inputText;
        }

        // ── Input ───────────────────────────────────────────────────

        private void DrawInput(float y, float height)
        {
            var inputRect = new Rect(0, y, Screen.width, height);
            GUI.DrawTexture(inputRect, _inputBgTexture!);

            GUI.Label(new Rect(6, y + 4, 14, height), ">", _logStyle);

            GUI.SetNextControlName(InputControlName);
            _inputText = GUI.TextField(new Rect(20, y + 3, Screen.width - 28, height - 6), _inputText, _inputStyle);

            if (_moveCursorToEnd)
            {
                _moveCursorToEnd = false;
                MoveCursorToEnd();
            }
        }

        private void ExecuteInput(string input)
        {
            ConsoleLog.LogInput(input);

            var result = CommandRegistry.Instance.Execute(input);

            if (!string.IsNullOrEmpty(result.Message))
            {
                if (result.Success)
                    ConsoleLog.Log(result.Message);
                else
                    ConsoleLog.LogError(result.Message);
            }
        }

        // ── History ─────────────────────────────────────────────────

        private void NavigateHistory(int direction)
        {
            if (_history.Count == 0) return;

            if (_historyIndex == -1)
                _savedInput = _inputText;

            _historyIndex += direction;
            _historyIndex = Mathf.Clamp(_historyIndex, -1, _history.Count - 1);

            _inputText = _historyIndex == -1
                ? _savedInput
                : _history[_history.Count - 1 - _historyIndex];

            _suggestions.Clear();
            _suggestionIndex = -1;
            _prevInputText = _inputText;
        }

        private void AddToHistory(string input)
        {
            if (_history.Count > 0 && _history[_history.Count - 1] == input)
                return;

            _history.Add(input);
            if (_history.Count > MaxHistory)
                _history.RemoveAt(0);
        }

        private static void MoveCursorToEnd()
        {
            var editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            editor.MoveTextEnd();
        }

        // ── Styles ──────────────────────────────────────────────────

        private void EnsureStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _bgTexture = MakeTex(1, 1, new Color(0.05f, 0.05f, 0.08f, 0.85f));
            _inputBgTexture = MakeTex(1, 1, new Color(0.1f, 0.1f, 0.14f, 0.95f));

            _logStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                wordWrap = true,
                fontSize = 14,
                normal = { textColor = Color.white },
                padding = new RectOffset(4, 4, 1, 1)
            };

            _inputStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 14,
                normal =
                {
                    textColor = Color.white,
                    background = _inputBgTexture
                },
                focused =
                {
                    textColor = Color.white,
                    background = _inputBgTexture
                }
            };

            _suggestionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) },
                padding = new RectOffset(6, 6, 2, 2)
            };

            _suggestionActiveStyle = new GUIStyle(_suggestionStyle)
            {
                normal = { textColor = new Color(1f, 0.85f, 0.2f) }
            };
        }

        private static Texture2D MakeTex(int width, int height, Color color)
        {
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}