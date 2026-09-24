using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// IMGUI frontend for the DevConsole package backend (CommandRegistry + ConsoleLog).
    /// Behaviorally mirrors <see cref="DevConsoleUIToolkit"/>: vertical autocomplete dropdown with
    /// descriptions, arrow-key suggestion/history navigation, token-aware completion and persisted
    /// command history. Zero setup — no UIDocument or UXML required.
    /// </summary>
    public class DevConsoleIMGUI : MonoBehaviour
    {
        private const string InputControlName = "DevConsoleInput";
        private const int MaxSuggestions = 10;

        private static DevConsoleIMGUI? _instance;

        public static DevConsoleIMGUI? Instance => _instance;
        public static event Action<bool>? Toggled;
        public static bool IsOpen => _instance != null && _instance._isOpen;

        private bool _isOpen;
        private bool _requestFocus;
        private bool _consumeNextChar;

        private CommandHistory _history = default!;

        private string _inputText = "";
        private string _prevInputText = "";
        private bool _moveCursorToEnd;
        private bool _suppressAutocomplete;
        private Vector2 _scrollPos;
        private bool _scrollToBottom;

        // Reused per frame so the measured total height matches what is drawn exactly.
        private readonly List<GUIContent> _logContents = new();
        private readonly List<float> _logHeights = new();

        // Autocomplete state
        private readonly List<string> _suggestions = new();
        private int _suggestionIndex = -1;
        private bool _applySuggestionRequested;
        private bool _pendingComplete;

        // IMGUI styles (lazy init)
        private bool _stylesInitialized;
        private GUIStyle _logStyle = default!;
        private GUIStyle _inputStyle = default!;
        private GUIStyle _promptStyle = default!;
        private GUIStyle _acNameStyle = default!;
        private GUIStyle _acDescStyle = default!;
        private Texture2D? _consoleBgTex;
        private Texture2D? _acBgTex;
        private Texture2D? _selectedBgTex;
        private Texture2D? _borderStrongTex;
        private Texture2D? _borderWeakTex;
        private Texture2D? _acBorderTex;
        private Texture2D? _clearTex;

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
            _history = new CommandHistory();
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

            DestroyTex(ref _consoleBgTex);
            DestroyTex(ref _acBgTex);
            DestroyTex(ref _selectedBgTex);
            DestroyTex(ref _borderStrongTex);
            DestroyTex(ref _borderWeakTex);
            DestroyTex(ref _acBorderTex);
            DestroyTex(ref _clearTex);
        }

        private void OnLogAdded(ConsoleLog.LogEntry entry) => _scrollToBottom = true;
        private void OnCleared() => _scrollToBottom = true;

        // Drive the built-in toggle from DevConsoleSettings, mirroring DevConsoleUIToolkit so the
        // two frontends honor the same Toggle Key / Use Built-in Toggle options. Polling the Input
        // System here (once per frame) rather than in OnGUI avoids the multi-event-per-frame
        // double-toggle that wasPressedThisFrame would cause inside OnGUI.
        private void Update()
        {
            var settings = DevConsoleSettings.GetOrCreate();
            var kb = Keyboard.current;
            if (kb == null) return;

            if (settings.UseBuiltInToggle && kb[settings.ToggleKey].wasPressedThisFrame)
                Toggle();

            // Detect Tab here rather than in OnGUI: IMGUI consumes Tab for built-in focus traversal
            // before our OnGUI event handler can reliably intercept it (especially with other OnGUI
            // surfaces in the scene). Polling the Input System sidesteps that entirely.
            if (_isOpen && kb.tabKey.wasPressedThisFrame)
                _pendingComplete = true;
        }

        /// <summary>Toggles the console open/closed. Call this when <c>UseBuiltInToggle</c> is disabled.</summary>
        public void Toggle() => SetOpen(!_isOpen);

        /// <summary>Opens or closes the console.</summary>
        public void SetOpen(bool open)
        {
            if (_isOpen == open) return;

            _isOpen = open;
            if (open)
            {
                _requestFocus = true;
                // Swallow the character the toggle key emits on the next OnGUI so it does not
                // land in the freshly focused input field.
                _consumeNextChar = true;
                _inputText = "";
                _prevInputText = "";
                _scrollToBottom = true;
            }

            HideAutocomplete();
            _history.ResetCursor();

            Toggled?.Invoke(_isOpen);
        }

        private void OnGUI()
        {
#if UNITY_SERVER
        return;
#endif
            var e = Event.current;

            // --- Consume the character produced by the toggle key ---
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

            if (!_isOpen) return;

            EnsureStyles();

            // Tab-completion request raised from Update() (see note there). Process it once per frame
            // on the Layout pass, before drawing, so the field reflects the completion this frame.
            if (_pendingComplete && e.type == EventType.Layout)
            {
                _pendingComplete = false;
                if (_suggestions.Count > 0)
                    ApplySelectedSuggestion();
            }

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
                        // Consume before the TextField is drawn so IMGUI does not treat Tab as
                        // focus-traversal or insert a control character into the field.
                        e.Use();
                        break;
                }

                // Tab/Enter can also arrive as a separate character event (keyCode == None).
                // Swallow those so they never land in the single-line command field.
                if (e.type == EventType.KeyDown &&
                    (e.character == '\t' || e.character == '\n' || e.character == '\r'))
                    e.Use();
            }

            // --- Layout (top → bottom: log, autocomplete, input), mirroring the UI Toolkit flex column ---
            float consoleHeight = Screen.height * DevConsoleSettings.GetOrCreate().ConsoleHeight;
            const float inputRowHeight = 28f;
            const float acRowHeight = 18f;
            int sugCount = _suggestions.Count;
            float acHeight = sugCount > 0 ? sugCount * acRowHeight + 4f : 0f;
            float logHeight = Mathf.Max(0f, consoleHeight - acHeight - inputRowHeight);

            GUI.DrawTexture(new Rect(0, 0, Screen.width, consoleHeight), _consoleBgTex!);

            DrawLogArea(logHeight);
            DrawSuggestions(logHeight, acHeight, acRowHeight);

            // Restore focus to the field before it is drawn so the named control adopts it this pass.
            // Re-focusing makes IMGUI select-all, so collapse the selection to the caret afterwards.
            if (_requestFocus)
            {
                GUI.FocusControl(InputControlName);
                _moveCursorToEnd = true;
            }

            DrawInput(logHeight + acHeight, inputRowHeight);

            // 2px accent border along the bottom edge of the console.
            GUI.DrawTexture(new Rect(0, consoleHeight - 2f, Screen.width, 2f), _borderStrongTex!);

            // --- Handle captured keys (after draw, so _inputText reflects this frame's typing) ---
            bool hasSuggestions = _suggestions.Count > 0;
            switch (capturedKey)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (hasSuggestions && _suggestionIndex >= 0)
                        ApplySelectedSuggestion();
                    else
                        SubmitInput();
                    break;

                // Tab is handled via the Input System poll in Update() → _pendingComplete; here we
                // only let the captured event fall through (already consumed above) to suppress
                // IMGUI's focus traversal and any stray tab character.

                case KeyCode.UpArrow:
                    if (hasSuggestions)
                        SelectSuggestion(_suggestionIndex - 1);
                    else
                    {
                        NavigateHistory(true);
                        _moveCursorToEnd = true;
                    }

                    break;

                case KeyCode.DownArrow:
                    if (hasSuggestions)
                        SelectSuggestion(_suggestionIndex + 1);
                    else
                    {
                        NavigateHistory(false);
                        _moveCursorToEnd = true;
                    }

                    break;

                case KeyCode.Escape:
                    if (hasSuggestions)
                        HideAutocomplete();
                    else
                        SetOpen(false);
                    break;
            }

            // Mouse click on a suggestion row (deferred out of the draw loop).
            if (_applySuggestionRequested)
            {
                _applySuggestionRequested = false;
                ApplySelectedSuggestion();
            }

            // --- Detect user typing and refresh autocomplete ---
            if (capturedKey == KeyCode.None && _inputText != _prevInputText)
            {
                _prevInputText = _inputText;
                if (!_suppressAutocomplete)
                {
                    _history.ResetCursor();
                    UpdateAutocomplete(_inputText);
                }
            }

            // Keep the command field focused while the console is open. Resolve the request only on
            // Repaint (when GUI.GetNameOfFocusedControl is reliable); re-request if focus drifted
            // away — e.g. after submitting, or clicking the log scrollbar.
            if (e.type == EventType.Repaint)
                _requestFocus = GUI.GetNameOfFocusedControl() != InputControlName;
        }

        // ── Log rendering ───────────────────────────────────────────

        private void DrawLogArea(float height)
        {
            const float padLeft = 10f;
            const float padRight = 10f;
            const float padTop = 8f;
            const float padBottom = 8f;
            const float scrollbarWidth = 16f;

            var entries = ConsoleLog.Entries;
            var position = new Rect(0, 0, Screen.width, height);

            // Reserve the scrollbar gutter up front so the wrap width used for measuring is the same
            // one used for drawing — otherwise CalcHeight and GUI.Label disagree and the scroll range
            // is wrong.
            float contentWidth = Screen.width - scrollbarWidth;
            float innerWidth = contentWidth - padLeft - padRight;

            _logContents.Clear();
            _logHeights.Clear();
            float contentHeight = padTop + padBottom;
            for (int i = 0; i < entries.Count; i++)
            {
                var content = new GUIContent(ColorizeEntry(entries[i]));
                float h = _logStyle.CalcHeight(content, innerWidth);
                _logContents.Add(content);
                _logHeights.Add(h);
                contentHeight += h;
            }

            var scrollContent = new Rect(0, 0, contentWidth, contentHeight);

            if (_scrollToBottom)
            {
                _scrollPos.y = Mathf.Max(0f, contentHeight - height);
                _scrollToBottom = false;
            }

            _scrollPos = GUI.BeginScrollView(position, _scrollPos, scrollContent);

            float y = padTop;
            for (int i = 0; i < _logContents.Count; i++)
            {
                GUI.Label(new Rect(padLeft, y, innerWidth, _logHeights[i]), _logContents[i], _logStyle);
                y += _logHeights[i];
            }

            GUI.EndScrollView();
        }

        private static string ColorizeEntry(ConsoleLog.LogEntry entry)
        {
            return entry.Type switch
            {
                ConsoleLog.LogType.Warning => $"<color=#ffd23c>{entry.Message}</color>",
                ConsoleLog.LogType.Error => $"<color=#ff5050>{entry.Message}</color>",
                ConsoleLog.LogType.Success => $"<color=#50dc64>{entry.Message}</color>",
                ConsoleLog.LogType.Input => $"<color=#a0a0aa>{entry.Message}</color>",
                _ => entry.Message
            };
        }

        // ── Suggestions ─────────────────────────────────────────────

        private void DrawSuggestions(float y, float totalHeight, float rowHeight)
        {
            if (_suggestions.Count == 0) return;

            var e = Event.current;
            var commands = CommandRegistry.Instance.Commands;

            GUI.DrawTexture(new Rect(0, y, Screen.width, totalHeight), _acBgTex!);

            float rowY = y + 2f;
            for (int i = 0; i < _suggestions.Count; i++)
            {
                var rowRect = new Rect(0, rowY, Screen.width, rowHeight);

                if (i == _suggestionIndex)
                    GUI.DrawTexture(rowRect, _selectedBgTex!);

                string name = _suggestions[i];
                GUI.Label(new Rect(10, rowY, 160, rowHeight), name, _acNameStyle);

                float nameWidth = _acNameStyle.CalcSize(new GUIContent(name)).x;
                float descX = 10 + Mathf.Max(160f, nameWidth + 10f);
                if (commands.TryGetValue(name, out var cmd) && !string.IsNullOrEmpty(cmd.Description))
                    GUI.Label(new Rect(descX, rowY, Screen.width - descX - 10, rowHeight), cmd.Description,
                        _acDescStyle);

                if (e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition))
                {
                    _suggestionIndex = i;
                    _applySuggestionRequested = true;
                    e.Use();
                }

                rowY += rowHeight;
            }

            // 1px accent border along the bottom edge of the dropdown.
            GUI.DrawTexture(new Rect(0, y + totalHeight - 1f, Screen.width, 1f), _acBorderTex!);
        }

        private void UpdateAutocomplete(string input)
        {
            _suggestions.Clear();
            _suggestionIndex = 0;

            if (string.IsNullOrEmpty(input))
            {
                HideAutocomplete();
                return;
            }

            CommandRegistry.Instance.GetSuggestions(input, _suggestions, MaxSuggestions);
            if (_suggestions.Count == 0)
                HideAutocomplete();
        }

        private void SelectSuggestion(int index)
        {
            if (_suggestions.Count == 0) return;

            if (index < 0) index = _suggestions.Count - 1;
            else if (index >= _suggestions.Count) index = 0;

            _suggestionIndex = index;
        }

        private void ApplySelectedSuggestion()
        {
            int idx = _suggestionIndex >= 0 && _suggestionIndex < _suggestions.Count
                ? _suggestionIndex
                : 0;
            if (idx >= _suggestions.Count) return;

            var suggestion = _suggestions[idx];
            var currentInput = _inputText;
            var tokens = CommandRegistry.Tokenize(currentInput);
            var endsWithSpace = currentInput.EndsWith(" ");

            string newValue;
            if (tokens.Length <= 1 && !endsWithSpace)
                // Completing the command name.
                newValue = suggestion + " ";
            else if (endsWithSpace)
                // Adding a new argument.
                newValue = currentInput + suggestion + " ";
            else
            {
                // Replacing the partially-typed argument.
                tokens[^1] = suggestion;
                newValue = string.Join(" ", tokens) + " ";
            }

            _inputText = newValue;
            _prevInputText = newValue;
            _moveCursorToEnd = true;
            HideAutocomplete();
            // Surface the next level of suggestions (subcommands / args) for the completed token.
            UpdateAutocomplete(newValue);
        }

        private void HideAutocomplete()
        {
            _suggestions.Clear();
            _suggestionIndex = -1;
        }

        // ── Input ───────────────────────────────────────────────────

        private void DrawInput(float y, float height)
        {
            // 1px accent border along the top edge of the input row.
            GUI.DrawTexture(new Rect(0, y, Screen.width, 1f), _borderWeakTex!);

            float promptWidth = 16f;
            GUI.Label(new Rect(10, y + 5, promptWidth, height), ">", _promptStyle);

            GUI.SetNextControlName(InputControlName);
            float inputX = 10 + promptWidth;
            _inputText = GUI.TextField(new Rect(inputX, y + 4, Screen.width - inputX - 10, height - 8),
                _inputText, _inputStyle);

            if (_moveCursorToEnd)
            {
                _moveCursorToEnd = false;
                MoveCursorToEnd();
            }
        }

        private void SubmitInput()
        {
            var text = _inputText.Trim();
            if (string.IsNullOrEmpty(text)) return;

            _history.Add(text);
            _history.ResetCursor();
            ExecuteInput(text);

            _inputText = "";
            _prevInputText = "";
            HideAutocomplete();
            _requestFocus = true;
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

        private void NavigateHistory(bool up)
        {
            var entry = up
                ? _history.NavigateUp(_inputText)
                : _history.NavigateDown();

            _suppressAutocomplete = true;
            if (entry != null)
                _inputText = entry;
            else if (!up)
                _inputText = "";
            _prevInputText = _inputText;
            _suppressAutocomplete = false;
        }

        private void MoveCursorToEnd()
        {
            if (GUIUtility.keyboardControl == 0) return;

            var editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            editor.text = _inputText;
            // cursorIndex == selectIndex ⇒ caret at the end with no selection.
            editor.cursorIndex = _inputText.Length;
            editor.selectIndex = _inputText.Length;
        }

        // ── Styles ──────────────────────────────────────────────────

        private void EnsureStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            // Colors mirror DevConsoleUI.uss so both frontends look identical.
            _consoleBgTex = MakeTex(new Color(0.059f, 0.059f, 0.078f, 0.92f));
            _acBgTex = MakeTex(new Color(0.078f, 0.078f, 0.110f, 0.95f));
            _selectedBgTex = MakeTex(new Color(0.235f, 0.353f, 0.549f, 0.5f));
            _borderStrongTex = MakeTex(new Color(0.314f, 0.627f, 1f, 0.6f));
            _borderWeakTex = MakeTex(new Color(0.314f, 0.627f, 1f, 0.25f));
            _acBorderTex = MakeTex(new Color(0.314f, 0.627f, 1f, 0.2f));
            _clearTex = MakeTex(new Color(0, 0, 0, 0));

            _logStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                wordWrap = true,
                fontSize = 14,
                normal = { textColor = new Color(0.863f, 0.863f, 0.863f) },
                padding = new RectOffset(0, 0, 1, 1)
            };

            _inputStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 14,
                border = new RectOffset(0, 0, 0, 0),
                normal = { textColor = new Color(0.902f, 0.902f, 0.902f), background = _clearTex },
                focused = { textColor = new Color(0.902f, 0.902f, 0.902f), background = _clearTex },
                hover = { textColor = new Color(0.902f, 0.902f, 0.902f), background = _clearTex }
            };

            _promptStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = new Color(0.314f, 0.627f, 1f, 0.9f) }
            };

            _acNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.784f, 0.824f, 0.902f) },
                padding = new RectOffset(0, 0, 0, 0)
            };

            _acDescStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.471f, 0.510f, 0.588f, 0.7f) },
                padding = new RectOffset(0, 0, 0, 0)
            };
        }

        private static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private static void DestroyTex(ref Texture2D? tex)
        {
            if (tex != null)
            {
                Destroy(tex);
                tex = null;
            }
        }
    }
}
