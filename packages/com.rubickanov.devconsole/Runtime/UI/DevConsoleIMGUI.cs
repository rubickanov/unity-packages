using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
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
        private static readonly int LogSelectHint = "DevConsoleLogSelect".GetHashCode();

        // The tags IMGUI rich text understands; stripped from copied lines so the clipboard gets plain text.
        private static readonly Regex RichTextTag =
            new(@"</?(b|i|size|color|material|quad)(=[^>]*)?>", RegexOptions.Compiled);

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

        // Selected log lines, whole lines only: rich text makes per-character hit testing unreliable in
        // IMGUI. Anchor is where the drag started, end is where it is now; -1 for no selection.
        private int _selAnchor = -1;
        private int _selEnd = -1;
        private int _lastLogCount;
        private int _dragControl;

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

            _lastLogCount = ConsoleLog.Entries.Count;
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

        private void OnLogAdded(ConsoleLog.LogEntry entry)
        {
            // A full ring buffer drops its oldest line, so the selected lines move up by one.
            int count = ConsoleLog.Entries.Count;
            if (count == _lastLogCount && HasSelection)
            {
                _selAnchor--;
                _selEnd--;
                if (_selAnchor < 0 && _selEnd < 0)
                {
                    ClearSelection();
                }
                else
                {
                    _selAnchor = Mathf.Max(0, _selAnchor);
                    _selEnd = Mathf.Max(0, _selEnd);
                }
            }

            _lastLogCount = count;

            // Jumping to the bottom would scroll the lines being copied out from under the cursor.
            if (!HasSelection)
                _scrollToBottom = true;
        }

        private void OnCleared()
        {
            ClearSelection();
            _lastLogCount = 0;
            _scrollToBottom = true;
        }

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
            ClearSelection();

            // Closed mid-drag: nothing is left to take the mouse-up, so IMGUI would stay captured
            if (_dragControl != 0 && GUIUtility.hotControl == _dragControl)
                GUIUtility.hotControl = 0;
            _dragControl = 0;
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

            // Copy the selected log lines. Checked before the command field is drawn, which would
            // otherwise take the shortcut for its own, usually empty, selection.
            if (HasSelection && IsCopyEvent(e))
            {
                GUIUtility.systemCopyBuffer = SelectedText();
                e.Use();
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
                    else if (HasSelection)
                        ClearSelection();
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

            HandleLogMouse(position, contentWidth, padTop);

            if (_scrollToBottom)
            {
                _scrollPos.y = Mathf.Max(0f, contentHeight - height);
                _scrollToBottom = false;
            }

            _scrollPos = GUI.BeginScrollView(position, _scrollPos, scrollContent);

            int selFrom = Mathf.Min(_selAnchor, _selEnd);
            int selTo = Mathf.Max(_selAnchor, _selEnd);
            bool repaint = Event.current.type == EventType.Repaint;

            float y = padTop;
            for (int i = 0; i < _logContents.Count; i++)
            {
                if (repaint && HasSelection && i >= selFrom && i <= selTo)
                    GUI.DrawTexture(new Rect(0, y, contentWidth, _logHeights[i]), _selectedBgTex!);

                GUI.Label(new Rect(padLeft, y, innerWidth, _logHeights[i]), _logContents[i], _logStyle);
                y += _logHeights[i];
            }

            GUI.EndScrollView();
        }

        // Press on a line to select it, drag across lines to extend, shift-press to extend from the anchor.
        // Handled in screen space before the scroll view so the scrollbar keeps its own clicks.
        private void HandleLogMouse(Rect area, float contentWidth, float padTop)
        {
            var e = Event.current;
            int id = GUIUtility.GetControlID(LogSelectHint, FocusType.Passive);
            float contentY = e.mousePosition.y - area.y + _scrollPos.y;

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !area.Contains(e.mousePosition) || e.mousePosition.x >= area.x + contentWidth)
                        return;

                    int hit = LineAt(contentY, padTop);
                    if (hit < 0)
                    {
                        // Empty space under the last line
                        ClearSelection();
                    }
                    else
                    {
                        if (!e.shift || !HasSelection)
                            _selAnchor = hit;
                        _selEnd = hit;
                        GUIUtility.hotControl = id;
                        _dragControl = id;
                    }

                    // Used so the command field keeps keyboard focus and the caret
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id) return;

                    int line = LineAt(contentY, padTop);
                    _selEnd = line < 0 ? _logHeights.Count - 1 : line;
                    ScrollLineIntoView(_selEnd, area.height, padTop);
                    e.Use();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) return;

                    GUIUtility.hotControl = 0;
                    _dragControl = 0;
                    e.Use();
                    break;
            }
        }

        /// <summary>The line at a y in scroll content space; the first above it, -1 below the last.</summary>
        private int LineAt(float contentY, float padTop)
        {
            if (_logHeights.Count == 0) return -1;

            float y = padTop;
            for (int i = 0; i < _logHeights.Count; i++)
            {
                y += _logHeights[i];
                if (contentY < y) return i;
            }

            return -1;
        }

        private void ScrollLineIntoView(int index, float viewHeight, float padTop)
        {
            if (index < 0 || index >= _logHeights.Count) return;

            float top = padTop;
            for (int i = 0; i < index; i++)
                top += _logHeights[i];
            float bottom = top + _logHeights[index];

            if (top < _scrollPos.y)
                _scrollPos.y = top;
            else if (bottom > _scrollPos.y + viewHeight)
                _scrollPos.y = bottom - viewHeight;
        }

        private bool HasSelection => _selAnchor >= 0;

        private void ClearSelection()
        {
            _selAnchor = -1;
            _selEnd = -1;
        }

        // Ctrl+C in a player, Cmd+C on macOS; the editor may send the Copy command instead of the key.
        private static bool IsCopyEvent(Event e)
        {
            if (e.type == EventType.KeyDown)
                return e.keyCode == KeyCode.C && (e.control || e.command);

            return (e.type == EventType.ValidateCommand || e.type == EventType.ExecuteCommand) &&
                   e.commandName == "Copy";
        }

        private string SelectedText()
        {
            var entries = ConsoleLog.Entries;
            int from = Mathf.Max(0, Mathf.Min(_selAnchor, _selEnd));
            int to = Mathf.Min(entries.Count - 1, Mathf.Max(_selAnchor, _selEnd));

            var sb = new StringBuilder();
            for (int i = from; i <= to; i++)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(RichTextTag.Replace(entries[i].Message, ""));
            }

            return sb.ToString();
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

            // Lets the command's output scroll into view
            ClearSelection();

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
