using System;
using System.Collections.Generic;
using System.Text;
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
        private static readonly int LogSelectionHint = "DevConsoleLogSelection".GetHashCode();

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
        // _logSlots[i] is the cache slot of ConsoleLog.Entries[i].
        private readonly List<int> _logSlots = new();
        private readonly List<float> _logHeights = new();

        // Per entry: the text as drawn (colour tags only) and as copied (no tags). Slot = entry number % capacity.
        private long[] _lineNumbers = Array.Empty<long>();
        private GUIContent[] _lineDrawn = Array.Empty<GUIContent>();
        private GUIContent[] _linePlain = Array.Empty<GUIContent>();

        // Log geometry of the last pass, for mouse hit tests outside the scroll view.
        private Rect _logView;
        private float _logInnerWidth;
        private float _logContentHeight;
        private const float LogPadLeft = 10f;
        private const float LogPadTop = 8f;

        // Text selected in the log, from the anchor (where the press was) to the caret (where the mouse is).
        // Positions are entry numbers, not buffer indices, so dropping old entries does not move the selection.
        private LogPos _selAnchor;
        private LogPos _selCaret;
        private bool _hasSelection;
        private int _selectionControl;

        private readonly struct LogPos
        {
            public readonly long Entry;
            public readonly int Char;

            public LogPos(long entry, int ch)
            {
                Entry = entry;
                Char = ch;
            }

            public bool Before(LogPos other) => Entry < other.Entry || (Entry == other.Entry && Char < other.Char);
            public bool Same(LogPos other) => Entry == other.Entry && Char == other.Char;
        }

        // Autocomplete state
        private readonly List<string> _suggestions = new();
        private int _suggestionIndex = -1;
        private bool _applySuggestionRequested;
        private bool _pendingComplete;

        // IMGUI styles (lazy init)
        private bool _stylesInitialized;
        private GUIStyle _logStyle = default!;
        private GUIStyle _logPlainStyle = default!;
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

        // While text is selected the log stays where it is, so the selection does not scroll away.
        private void OnLogAdded(ConsoleLog.LogEntry entry) => _scrollToBottom |= !_hasSelection;

        private void OnCleared()
        {
            ClearSelection();
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

            // Copy the log selection before the command field is drawn: the field would take the
            // shortcut for its own, usually empty, selection.
            if (_hasSelection && IsCopyEvent(e))
            {
                if (e.type != EventType.ValidateCommand)
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
                    else if (_hasSelection)
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
            const float padRight = 10f;
            const float padBottom = 8f;
            const float scrollbarWidth = 16f;

            var entries = ConsoleLog.Entries;
            long first = ConsoleLog.FirstNumber;
            var position = new Rect(0, 0, Screen.width, height);

            // Reserve the scrollbar gutter up front so the wrap width used for measuring is the same
            // one used for drawing — otherwise CalcHeight and GUI.Label disagree and the scroll range
            // is wrong.
            float contentWidth = Screen.width - scrollbarWidth;
            float innerWidth = contentWidth - LogPadLeft - padRight;

            _logSlots.Clear();
            _logHeights.Clear();
            float contentHeight = LogPadTop + padBottom;
            for (int i = 0; i < entries.Count; i++)
            {
                int slot = CacheLine(first + i, entries[i]);
                float h = _logStyle.CalcHeight(_lineDrawn[slot], innerWidth);
                _logSlots.Add(slot);
                _logHeights.Add(h);
                contentHeight += h;
            }

            _logView = position;
            _logInnerWidth = innerWidth;
            _logContentHeight = contentHeight;

            DropEvictedSelection(first, entries.Count);
            HandleLogMouse(contentWidth);

            var scrollContent = new Rect(0, 0, contentWidth, contentHeight);

            if (_scrollToBottom)
            {
                _scrollPos.y = Mathf.Max(0f, contentHeight - height);
                _scrollToBottom = false;
            }

            _scrollPos = GUI.BeginScrollView(position, _scrollPos, scrollContent);

            bool drawSelection = _hasSelection && Event.current.type == EventType.Repaint;
            float y = LogPadTop;
            for (int i = 0; i < _logSlots.Count; i++)
            {
                var rect = new Rect(LogPadLeft, y, innerWidth, _logHeights[i]);
                if (drawSelection)
                    DrawLineSelection(first + i, _logSlots[i], rect);

                GUI.Label(rect, _lineDrawn[_logSlots[i]], _logStyle);
                y += _logHeights[i];
            }

            GUI.EndScrollView();
        }

        /// <summary>Fills the cache slot of an entry if it holds another one; returns the slot.</summary>
        private int CacheLine(long number, in ConsoleLog.LogEntry entry)
        {
            if (_lineNumbers.Length != ConsoleLog.Capacity)
            {
                _lineNumbers = new long[ConsoleLog.Capacity];
                Array.Fill(_lineNumbers, -1L);
                _lineDrawn = new GUIContent[ConsoleLog.Capacity];
                _linePlain = new GUIContent[ConsoleLog.Capacity];
            }

            int slot = (int)(number % _lineNumbers.Length);
            if (_lineNumbers[slot] != number)
            {
                _lineNumbers[slot] = number;
                _lineDrawn[slot] = new GUIContent(ColorizeEntry(entry.Type, RichText.KeepColor(entry.Message)));
                _linePlain[slot] = new GUIContent(RichText.Strip(entry.Message));
            }

            return slot;
        }

        private static string ColorizeEntry(ConsoleLog.LogType type, string message)
        {
            return type switch
            {
                ConsoleLog.LogType.Warning => $"<color=#ffd23c>{message}</color>",
                ConsoleLog.LogType.Error => $"<color=#ff5050>{message}</color>",
                ConsoleLog.LogType.Success => $"<color=#50dc64>{message}</color>",
                ConsoleLog.LogType.Input => $"<color=#a0a0aa>{message}</color>",
                _ => message
            };
        }

        // ── Log selection ───────────────────────────────────────────

        // Press and drag to select, shift-press to extend, double-press for a word, triple for the whole entry.
        // Handled in screen space before the scroll view, so the scrollbar keeps its own clicks and the
        // command field keeps keyboard focus.
        private void HandleLogMouse(float contentWidth)
        {
            var e = Event.current;
            int id = GUIUtility.GetControlID(LogSelectionHint, FocusType.Passive);
            _selectionControl = id;

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                {
                    if (e.button != 0) return;

                    bool onText = _logView.Contains(e.mousePosition) && e.mousePosition.x < _logView.x + contentWidth;
                    if (!onText)
                    {
                        // A press on the command field or a suggestion drops the selection; the scrollbar keeps it.
                        if (!_logView.Contains(e.mousePosition))
                            ClearSelection();
                        return;
                    }

                    if (_logSlots.Count == 0) return;

                    var pos = PosAt(e.mousePosition);
                    if (e.shift && _hasSelection)
                    {
                        _selCaret = pos;
                    }
                    else if (e.clickCount == 2)
                    {
                        SelectWord(pos);
                    }
                    else if (e.clickCount >= 3)
                    {
                        _selAnchor = new LogPos(pos.Entry, 0);
                        _selCaret = new LogPos(pos.Entry, PlainOf(pos.Entry).Length);
                    }
                    else
                    {
                        _selAnchor = pos;
                        _selCaret = pos;
                    }

                    _hasSelection = true;
                    GUIUtility.hotControl = id;
                    e.Use();
                    break;
                }

                case EventType.MouseDrag:
                {
                    if (GUIUtility.hotControl != id) return;

                    // Dragging past the top or bottom edge scrolls towards it, faster the further out.
                    float maxScroll = Mathf.Max(0f, _logContentHeight - _logView.height);
                    if (e.mousePosition.y < _logView.yMin)
                        _scrollPos.y = Mathf.Max(0f, _scrollPos.y - (_logView.yMin - e.mousePosition.y));
                    else if (e.mousePosition.y > _logView.yMax)
                        _scrollPos.y = Mathf.Min(maxScroll, _scrollPos.y + (e.mousePosition.y - _logView.yMax));

                    if (_logSlots.Count > 0)
                        _selCaret = PosAt(e.mousePosition);
                    e.Use();
                    break;
                }

                case EventType.MouseUp:
                {
                    if (GUIUtility.hotControl != id) return;

                    GUIUtility.hotControl = 0;
                    // A plain click without a drag selects nothing.
                    if (_selAnchor.Same(_selCaret))
                        ClearSelection();
                    e.Use();
                    break;
                }
            }
        }

        /// <summary>The entry and character under a screen point, clamped to the log text.</summary>
        private LogPos PosAt(Vector2 screen)
        {
            long first = ConsoleLog.FirstNumber;
            var content = new Vector2(screen.x - _logView.x + _scrollPos.x, screen.y - _logView.y + _scrollPos.y);

            if (content.y < LogPadTop)
                return new LogPos(first, 0);

            float y = LogPadTop;
            for (int i = 0; i < _logSlots.Count; i++)
            {
                float h = _logHeights[i];
                if (content.y < y + h)
                {
                    var rect = new Rect(LogPadLeft, y, _logInnerWidth, h);
                    var plain = _linePlain[_logSlots[i]];
                    int ch = _logPlainStyle.GetCursorStringIndex(rect, plain, content);
                    return new LogPos(first + i, Mathf.Clamp(ch, 0, plain.text.Length));
                }

                y += h;
            }

            int last = _logSlots.Count - 1;
            return new LogPos(first + last, _linePlain[_logSlots[last]].text.Length);
        }

        private void SelectWord(LogPos pos)
        {
            string text = PlainOf(pos.Entry);
            int at = Mathf.Min(pos.Char, text.Length - 1);
            if (at < 0)
            {
                _selAnchor = _selCaret = pos;
                return;
            }

            // A run of word characters, a run of spaces, or a single other character.
            int from = at, to = at + 1;
            if (IsWordChar(text[at]))
            {
                while (from > 0 && IsWordChar(text[from - 1])) from--;
                while (to < text.Length && IsWordChar(text[to])) to++;
            }
            else if (char.IsWhiteSpace(text[at]))
            {
                while (from > 0 && char.IsWhiteSpace(text[from - 1]) && text[from - 1] != '\n') from--;
                while (to < text.Length && char.IsWhiteSpace(text[to]) && text[to] != '\n') to++;
            }

            _selAnchor = new LogPos(pos.Entry, from);
            _selCaret = new LogPos(pos.Entry, to);
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>Highlights the selected part of one entry, line by line where it wraps.</summary>
        private void DrawLineSelection(long number, int slot, Rect rect)
        {
            OrderedSelection(out var start, out var end);
            if (number < start.Entry || number > end.Entry) return;

            var plain = _linePlain[slot];
            int length = plain.text.Length;
            int from = number == start.Entry ? Mathf.Min(start.Char, length) : 0;
            int to = number == end.Entry ? Mathf.Min(end.Char, length) : length;

            // The line break after an entry is part of the selection when the next entry is too.
            float breakWidth = number < end.Entry ? 6f : 0f;
            if (from >= to && breakWidth == 0f) return;

            Vector2 a = _logPlainStyle.GetCursorPixelPosition(rect, plain, from);
            Vector2 b = _logPlainStyle.GetCursorPixelPosition(rect, plain, to);
            float lineHeight = _logPlainStyle.lineHeight;

            if (Mathf.Abs(a.y - b.y) < 1f)
            {
                FillSelection(a.x, a.y, b.x - a.x + breakWidth, lineHeight);
                return;
            }

            FillSelection(a.x, a.y, rect.xMax - a.x, lineHeight);
            if (b.y - a.y > lineHeight + 1f)
                FillSelection(rect.x, a.y + lineHeight, rect.width, b.y - a.y - lineHeight);
            FillSelection(rect.x, b.y, b.x - rect.x + breakWidth, lineHeight);
        }

        private void FillSelection(float x, float y, float width, float height)
        {
            if (width > 0f)
                GUI.DrawTexture(new Rect(x, y, width, height), _selectedBgTex!);
        }

        /// <summary>The selected text without rich text tags, entries separated by line breaks.</summary>
        private string SelectedText()
        {
            OrderedSelection(out var start, out var end);
            long first = ConsoleLog.FirstNumber;
            long last = first + ConsoleLog.Entries.Count - 1;

            var sb = new StringBuilder();
            for (long n = Math.Max(start.Entry, first); n <= Math.Min(end.Entry, last); n++)
            {
                string text = PlainOf(n);
                int from = n == start.Entry ? Mathf.Min(start.Char, text.Length) : 0;
                int to = n == end.Entry ? Mathf.Min(end.Char, text.Length) : text.Length;
                if (to > from)
                    sb.Append(text, from, to - from);
                if (n < end.Entry)
                    sb.Append('\n');
            }

            return sb.ToString();
        }

        private string PlainOf(long number)
        {
            long first = ConsoleLog.FirstNumber;
            var entries = ConsoleLog.Entries;
            if (number < first || number >= first + entries.Count) return "";

            return _linePlain[CacheLine(number, entries[(int)(number - first)])].text;
        }

        private void OrderedSelection(out LogPos start, out LogPos end)
        {
            bool forward = !_selCaret.Before(_selAnchor);
            start = forward ? _selAnchor : _selCaret;
            end = forward ? _selCaret : _selAnchor;
        }

        // The ring buffer drops its oldest entries; a selection that starts in them starts at the first one left.
        private void DropEvictedSelection(long first, int count)
        {
            if (!_hasSelection) return;

            OrderedSelection(out var start, out var end);
            if (count == 0 || end.Entry < first)
            {
                ClearSelection();
                return;
            }

            if (start.Entry < first)
            {
                var clamped = new LogPos(first, 0);
                if (_selAnchor.Before(_selCaret))
                    _selAnchor = clamped;
                else
                    _selCaret = clamped;
            }
        }

        private void ClearSelection()
        {
            _hasSelection = false;
            if (_selectionControl != 0 && GUIUtility.hotControl == _selectionControl)
                GUIUtility.hotControl = 0;
        }

        // Ctrl+C, Cmd+C on macOS; the editor may send the Copy command instead of the key.
        private static bool IsCopyEvent(Event e)
        {
            if (e.type == EventType.KeyDown)
            {
                if (!e.control && !e.command) return false;
                return e.keyCode == KeyCode.C || e.character == 'c' || e.character == 'C' || e.character == '\u0003';
            }

            return (e.type == EventType.ValidateCommand || e.type == EventType.ExecuteCommand) &&
                   e.commandName == "Copy";
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

            // Lets the command's output scroll into view.
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

            // Lays out the tag-free text exactly like _logStyle lays out the coloured one, for selection.
            _logPlainStyle = new GUIStyle(_logStyle) { richText = false };

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
