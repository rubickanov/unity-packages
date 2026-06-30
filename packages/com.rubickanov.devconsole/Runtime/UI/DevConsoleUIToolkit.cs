using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Rubickanov.DevConsole
{
    /// <summary>UI Toolkit-based dev console. Attach to a GameObject with UIDocument.</summary>
    [RequireComponent(typeof(UIDocument))]
    public class DevConsoleUIToolkit : MonoBehaviour
    {
        /// <summary>Singleton instance. Null if console is not in the scene.</summary>
        public static DevConsoleUIToolkit? Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private VisualElement _root = default!;
        private ScrollView _logScroll = default!;
        private VisualElement _logContainer = default!;
        private TextField _commandInput = default!;
        private VisualElement _autocompleteContainer = default!;

        private CommandHistory _history = default!;
        private bool _isVisible;

        /// <summary>Whether the console is currently visible.</summary>
        public bool IsVisible => _isVisible;

        private bool _needsScrollToBottom;

        // Autocomplete state
        private readonly List<string> _suggestions = new();
        private int _selectedSuggestion = -1;
        private bool _suppressAutocomplete;

        // Row pool for autocomplete UI
        private readonly List<VisualElement> _rowPool = new();

        private static readonly Dictionary<ConsoleLog.LogType, string> LogTypeClasses = new()
        {
            { ConsoleLog.LogType.Info, "log-info" },
            { ConsoleLog.LogType.Warning, "log-warning" },
            { ConsoleLog.LogType.Error, "log-error" },
            { ConsoleLog.LogType.Success, "log-success" },
            { ConsoleLog.LogType.Input, "log-input" },
        };

        private const int MaxSuggestions = 10;

        /// <summary>Toggles console visibility.</summary>
        public void Toggle()
        {
            if (_isVisible) Hide();
            else Show();
        }

        /// <summary>Shows the console and focuses the input field.</summary>
        public void Show()
        {
            if (_isVisible) return;
            _isVisible = true;
            _root.style.display = DisplayStyle.Flex;
            _root.pickingMode = PickingMode.Position;
            // Delay 2 frames: skip the current key event that opened the console
            _commandInput.schedule.Execute(() =>
                _commandInput.schedule.Execute(() =>
                {
                    _commandInput.Focus();
                    _commandInput.value = "";
                }));
            ScrollToBottom();
        }

        /// <summary>Hides the console and clears autocomplete state.</summary>
        public void Hide()
        {
            if (!_isVisible && _root.style.display == DisplayStyle.None) return;
            _isVisible = false;
            _root.style.display = DisplayStyle.None;
            _root.pickingMode = PickingMode.Ignore;
            _commandInput.Blur();
            HideAutocomplete();
            _history.ResetCursor();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[DevConsole] Duplicate DevConsoleUI detected, destroying this one.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            _history = new CommandHistory();
            CommandRegistry.Instance.Initialize();

            var doc = GetComponent<UIDocument>();
            if (doc.visualTreeAsset == null)
            {
                var uxml = Resources.Load<VisualTreeAsset>("UI/DevConsoleUI");
                if (uxml == null)
                {
                    Debug.LogError("[DevConsole] Could not load DevConsoleUI.uxml from Resources/UI/");
                    return;
                }
                doc.visualTreeAsset = uxml;
            }

            _root = doc.rootVisualElement.Q("dev-console-root");

            _logScroll = _root.Q<ScrollView>("log-scroll");
            _logContainer = _root.Q("log-container");
            _commandInput = _root.Q<TextField>("command-input");
            _autocompleteContainer = _root.Q("autocomplete-container");

            ApplySettings();

            ConsoleLog.OnLogAdded += OnLogAdded;
            ConsoleLog.OnCleared += OnCleared;

            foreach (var entry in ConsoleLog.Entries)
                AppendLogLabel(entry);

            _commandInput.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _commandInput.RegisterValueChangedCallback(OnInputChanged);

            Hide();
        }

        private void OnDestroy()
        {
            ConsoleLog.OnLogAdded -= OnLogAdded;
            ConsoleLog.OnCleared -= OnCleared;

            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            var settings = DevConsoleSettings.GetOrCreate();
            if (settings.UseBuiltInToggle && Keyboard.current != null &&
                Keyboard.current[settings.ToggleKey].wasPressedThisFrame)
                Toggle();

            if (_needsScrollToBottom)
            {
                _needsScrollToBottom = false;
                _logScroll.scrollOffset = new Vector2(0, _logScroll.contentContainer.layout.height);
            }
        }

        private void ApplySettings()
        {
            var settings = DevConsoleSettings.GetOrCreate();
            _root.style.height = Length.Percent(settings.ConsoleHeight * 100f);
        }

        // ── Input handling ──────────────────────────────────────────

        private void OnKeyDown(KeyDownEvent evt)
        {
            bool hasSuggestions = _suggestions.Count > 0;

            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    evt.StopPropagation();
                    _commandInput.focusController.IgnoreEvent(evt);
                    if (hasSuggestions && _selectedSuggestion >= 0)
                        ApplySelectedSuggestion();
                    else
                        ExecuteInput();
                    break;

                case KeyCode.Tab:
                    evt.StopPropagation();
                    _commandInput.focusController.IgnoreEvent(evt);
                    if (hasSuggestions)
                        ApplySelectedSuggestion();
                    break;

                case KeyCode.UpArrow:
                    evt.StopPropagation();
                    _commandInput.focusController.IgnoreEvent(evt);
                    if (hasSuggestions)
                        SelectSuggestion(_selectedSuggestion - 1);
                    else
                        NavigateHistory(true);
                    break;

                case KeyCode.DownArrow:
                    evt.StopPropagation();
                    _commandInput.focusController.IgnoreEvent(evt);
                    if (hasSuggestions)
                        SelectSuggestion(_selectedSuggestion + 1);
                    else
                        NavigateHistory(false);
                    break;

                case KeyCode.Escape:
                    evt.StopPropagation();
                    _commandInput.focusController.IgnoreEvent(evt);
                    if (hasSuggestions)
                        HideAutocomplete();
                    else
                        Hide();
                    break;
            }
        }

        private void ExecuteInput()
        {
            var text = _commandInput.value.Trim();
            if (string.IsNullOrEmpty(text)) return;

            _history.Add(text);
            _history.ResetCursor();
            ConsoleLog.LogInput(text);

            var result = CommandRegistry.Instance.Execute(text);
            if (!string.IsNullOrEmpty(result.Message))
            {
                if (result.Success)
                    ConsoleLog.Log(result.Message);
                else
                    ConsoleLog.LogError(result.Message);
            }

            _suppressAutocomplete = true;
            _commandInput.value = "";
            _suppressAutocomplete = false;
            HideAutocomplete();
            _commandInput.schedule.Execute(() => _commandInput.Focus());
        }

        private void NavigateHistory(bool up)
        {
            var current = _commandInput.value;
            var entry = up
                ? _history.NavigateUp(current)
                : _history.NavigateDown();

            _suppressAutocomplete = true;
            if (entry != null)
                _commandInput.SetValueWithoutNotify(entry);
            else if (!up)
                _commandInput.SetValueWithoutNotify("");
            _suppressAutocomplete = false;

            _commandInput.schedule.Execute(() => MoveCursorToEnd());
        }

        // ── Autocomplete ────────────────────────────────────────────

        private void OnInputChanged(ChangeEvent<string> evt)
        {
            if (_suppressAutocomplete) return;
            _history.ResetCursor();
            UpdateAutocomplete(evt.newValue);
        }

        private void UpdateAutocomplete(string input)
        {
            _suggestions.Clear();
            _selectedSuggestion = 0;

            if (string.IsNullOrEmpty(input))
            {
                HideAutocomplete();
                return;
            }

            CommandRegistry.Instance.GetSuggestions(input, _suggestions, MaxSuggestions);
            if (_suggestions.Count == 0)
            {
                HideAutocomplete();
                return;
            }

            RebuildSuggestionUI();
        }

        private void SelectSuggestion(int index)
        {
            if (_suggestions.Count == 0) return;

            // Wrap around
            if (index < 0) index = _suggestions.Count - 1;
            else if (index >= _suggestions.Count) index = 0;

            _selectedSuggestion = index;
            RefreshSuggestionHighlight();
        }

        private void ApplySelectedSuggestion()
        {
            int idx = _selectedSuggestion >= 0 && _selectedSuggestion < _suggestions.Count
                ? _selectedSuggestion
                : 0;
            if (idx >= _suggestions.Count) return;

            var suggestion = _suggestions[idx];
            var currentInput = _commandInput.value;
            var tokens = CommandRegistry.Tokenize(currentInput);
            var endsWithSpace = currentInput.EndsWith(" ");

            string newValue;
            if (tokens.Length <= 1 && !endsWithSpace)
            {
                // Completing command name
                newValue = suggestion + " ";
            }
            else if (endsWithSpace)
            {
                // Adding new argument
                newValue = currentInput + suggestion + " ";
            }
            else
            {
                // Replacing partial argument
                tokens[^1] = suggestion;
                newValue = string.Join(" ", tokens) + " ";
            }

            _suppressAutocomplete = true;
            _commandInput.SetValueWithoutNotify(newValue);
            _suppressAutocomplete = false;
            HideAutocomplete();
            _commandInput.schedule.Execute(() =>
            {
                MoveCursorToEnd();
                UpdateAutocomplete(newValue);
            });
        }

        private VisualElement GetOrCreatePooledRow(int index)
        {
            if (index < _rowPool.Count)
                return _rowPool[index];

            var row = new VisualElement();
            row.AddToClassList("ac-row");

            var nameLabel = new Label();
            nameLabel.AddToClassList("ac-name");
            row.Add(nameLabel);

            var descLabel = new Label();
            descLabel.AddToClassList("ac-desc");
            row.Add(descLabel);

            _rowPool.Add(row);
            _autocompleteContainer.Add(row);
            return row;
        }

        private void RebuildSuggestionUI()
        {
            var commands = CommandRegistry.Instance.Commands;

            for (int i = 0; i < _suggestions.Count; i++)
            {
                var name = _suggestions[i];
                var row = GetOrCreatePooledRow(i);

                row.style.display = DisplayStyle.Flex;

                if (i == _selectedSuggestion)
                    row.AddToClassList("ac-row-selected");
                else
                    row.RemoveFromClassList("ac-row-selected");

                var nameLabel = (Label)row[0];
                nameLabel.text = name;

                var descLabel = (Label)row[1];
                if (commands.TryGetValue(name, out var cmd) && !string.IsNullOrEmpty(cmd.Description))
                {
                    descLabel.text = cmd.Description;
                    descLabel.style.display = DisplayStyle.Flex;
                }
                else
                {
                    descLabel.text = "";
                    descLabel.style.display = DisplayStyle.None;
                }
            }

            // Hide unused pooled rows
            for (int i = _suggestions.Count; i < _rowPool.Count; i++)
                _rowPool[i].style.display = DisplayStyle.None;

            _autocompleteContainer.style.display = DisplayStyle.Flex;
        }

        private void RefreshSuggestionHighlight()
        {
            for (int i = 0; i < _autocompleteContainer.childCount; i++)
            {
                var row = _autocompleteContainer[i];
                if (i == _selectedSuggestion)
                    row.AddToClassList("ac-row-selected");
                else
                    row.RemoveFromClassList("ac-row-selected");
            }
        }

        private void HideAutocomplete()
        {
            _suggestions.Clear();
            _selectedSuggestion = -1;
            _autocompleteContainer.style.display = DisplayStyle.None;

            // Hide all pooled rows instead of clearing the container
            for (int i = 0; i < _rowPool.Count; i++)
                _rowPool[i].style.display = DisplayStyle.None;
        }

        // ── Log ─────────────────────────────────────────────────────

        private void OnLogAdded(ConsoleLog.LogEntry entry)
        {
            AppendLogLabel(entry);
            ScrollToBottom();
        }

        private void OnCleared()
        {
            _logContainer.Clear();
        }

        private void AppendLogLabel(ConsoleLog.LogEntry entry)
        {
            var label = new Label(entry.Message);
            label.enableRichText = true;
            label.AddToClassList("log-entry");

            if (LogTypeClasses.TryGetValue(entry.Type, out var typeClass))
                label.AddToClassList(typeClass);

            _logContainer.Add(label);
        }

        private void MoveCursorToEnd()
        {
            var len = _commandInput.value.Length;
            _commandInput.SelectRange(len, len);
        }

        private void ScrollToBottom()
        {
            _needsScrollToBottom = true;
        }
    }
}
