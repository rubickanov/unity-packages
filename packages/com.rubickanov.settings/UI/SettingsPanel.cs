using System;
using System.Collections.Generic;
using R3;
using UnityEngine.UIElements;

namespace Rubickanov.Settings.UI
{
    /// <summary>
    /// A <see cref="SettingsMenu"/> drawn as a UI Toolkit tree with BEM classes and no style of its own: the section
    /// tabs, the rows in a scroll view (the focused one kept in sight), the line about the focused row and the question
    /// over the rows. The pointer goes to the menu: a move over a row focuses it (a move, not an enter, so a row drawn
    /// anew under a resting pointer does not take the focus from the keys), a click on a step, an arrow, an option, a
    /// key, a tab or an answer does what it shows. Draws itself again on the menu's <see cref="SettingsMenu.Changed"/>.
    /// The look is the game's stylesheet; a game whose screen is not a tree draws its own from the same menu.
    /// </summary>
    /// <example><code>
    /// var panel = new SettingsPanel(menu, words);
    /// panel.Draw&lt;MyRow&gt;((row, menu, text) =&gt; new MyRowView(row, menu, text));   // a row kind of the game's
    /// root.Add(panel);
    /// </code></example>
    public sealed class SettingsPanel : VisualElement, IDisposable
    {
        public const string Block = "settings";
        public const string SectionsClass = "settings__sections";
        public const string SectionClass = "settings__section";
        public const string CurrentSectionClass = "settings__section--current";
        public const string RowsClass = "settings__rows";
        public const string AboutClass = "settings__about";
        public const string QuestionClass = "settings__question";
        public const string QuestionTextClass = "settings__question-text";
        public const string AnswersClass = "settings__answers";
        public const string AnswerClass = "settings__answer";
        public const string ChosenAnswerClass = "settings__answer--chosen";
        public const string CountdownClass = "settings__countdown";

        private readonly SettingsMenu _menu;
        private readonly ISettingsText _text;
        private readonly Dictionary<Type, Func<SettingsRow, SettingsMenu, ISettingsText, SettingsRowView>> _makers = new();
        private readonly List<SettingsRowView> _views = new();
        private readonly List<Label> _tabs = new();
        private readonly VisualElement _sections;
        private readonly ScrollView _rows;
        private readonly Label _about;
        private readonly VisualElement _question;
        private readonly Label _questionText;
        private readonly Label _countdown;
        private readonly Label _yes;
        private readonly Label _no;
        private IDisposable? _changed;
        private SettingsRowView? _scrolledTo;

        public SettingsPanel(SettingsMenu menu, ISettingsText text)
        {
            _menu = menu ?? throw new ArgumentNullException(nameof(menu));
            _text = text ?? throw new ArgumentNullException(nameof(text));
            AddToClassList(Block);

            _sections = Element(SectionsClass, this);
            _rows = new ScrollView(ScrollViewMode.Vertical);
            _rows.AddToClassList(RowsClass);
            // A focused row may step aside (a translate in the game's style): never a sideways scroll for it.
            _rows.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            Add(_rows);
            _about = new Label();
            _about.AddToClassList(AboutClass);
            Add(_about);

            _question = Element(QuestionClass, this);
            _questionText = new Label();
            _questionText.AddToClassList(QuestionTextClass);
            _question.Add(_questionText);
            _countdown = new Label();
            _countdown.AddToClassList(CountdownClass);
            _question.Add(_countdown);
            VisualElement answers = Element(AnswersClass, _question);
            _yes = Answer(answers, true);
            _no = Answer(answers, false);

            _changed = menu.Changed.Subscribe(_ => Render());
            Render();
        }

        /// <summary>A key's look (a glyph, a cap); null draws its text as a label. Setting it draws the rows again.</summary>
        public Func<KeysRow, int, VisualElement>? KeyCap
        {
            get => _keyCap;
            set
            {
                _keyCap = value;
                _views.Clear();
                _rows.Clear();
                Render();
            }
        }

        private Func<KeysRow, int, VisualElement>? _keyCap;

        /// <summary>The view of the rows of <typeparamref name="TRow"/> (and those deriving from it): a game's own kind, or another look of one of the package's.</summary>
        public void Draw<TRow>(Func<TRow, SettingsMenu, ISettingsText, SettingsRowView> make) where TRow : SettingsRow
        {
            if (make == null) throw new ArgumentNullException(nameof(make));
            _makers[typeof(TRow)] = (row, menu, text) => make((TRow)row, menu, text);
            _views.Clear();
            _rows.Clear();
            Render();
        }

        public void Render()
        {
            RenderSections();
            RenderRows();
            _about.text = _menu.Description;
            RenderQuestion();
        }

        public void Dispose()
        {
            _changed?.Dispose();
            _changed = null;
        }

        private void RenderSections()
        {
            IReadOnlyList<SettingsSection> sections = _menu.Sections;
            if (_tabs.Count != sections.Count)
            {
                _sections.Clear();
                _tabs.Clear();
                for (int i = 0; i < sections.Count; i++)
                {
                    int section = i;
                    var tab = new Label(_text.Get("section." + sections[i].Id));
                    tab.AddToClassList(SectionClass);
                    tab.RegisterCallback<PointerDownEvent>(_ => _menu.ClickSection(section));
                    _sections.Add(tab);
                    _tabs.Add(tab);
                }
            }

            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].EnableInClassList(CurrentSectionClass, i == _menu.SectionIndex);
            }
        }

        private void RenderRows()
        {
            IReadOnlyList<SettingsRow> rows = _menu.Rows;
            bool same = _views.Count == rows.Count;
            for (int i = 0; same && i < rows.Count; i++)
            {
                same = ReferenceEquals(_views[i].Row, rows[i]);
            }

            if (!same)
            {
                _rows.Clear();
                _views.Clear();
                _scrolledTo = null;
                for (int i = 0; i < rows.Count; i++)
                {
                    SettingsRowView view = Make(rows[i]);
                    view.Index = i;
                    _views.Add(view);
                    _rows.Add(view);
                }
            }

            SettingsRowView? focused = null;
            for (int i = 0; i < _views.Count; i++)
            {
                SettingsRowView view = _views[i];
                SettingsRow row = view.Row;
                bool focus = i == _menu.Focus;
                view.EnableInClassList(SettingsRowView.FocusedClass, focus);
                view.EnableInClassList(SettingsRowView.DisabledClass, !row.IsEnabled);
                view.EnableInClassList(SettingsRowView.HeldClass, row.IsHeld);
                view.Render();
                if (focus) focused = view;
            }

            if (focused != null && focused != _scrolledTo)
            {
                _scrolledTo = focused;
                ScrollToFocused(focused);
            }
        }

        // The focused row into view, with the headings right above it (the first row of a group shows its heading).
        // A row just made has no layout yet: it is scrolled to once it has one.
        private void ScrollToFocused(SettingsRowView focused)
        {
            VisualElement top = focused;
            for (int i = focused.Index - 1; i >= 0 && i < _views.Count && _views[i].Row is HeadingRow; i--)
            {
                top = _views[i];
            }

            if (Laid(focused) && Laid(top))
            {
                Scroll(top, focused);
                return;
            }

            void OnLaid(GeometryChangedEvent _)
            {
                focused.UnregisterCallback<GeometryChangedEvent>(OnLaid);
                if (_scrolledTo == focused) _rows.schedule.Execute(() => Scroll(top, focused));
            }

            focused.RegisterCallback<GeometryChangedEvent>(OnLaid);
        }

        private void Scroll(VisualElement top, VisualElement focused)
        {
            if (focused.parent == null) return;
            if (top != focused && top.parent != null) _rows.ScrollTo(top);
            _rows.ScrollTo(focused);
        }

        private static bool Laid(VisualElement element) =>
            !float.IsNaN(element.layout.height) && element.layout.height > 0f;

        private void RenderQuestion()
        {
            SettingsQuestion? question = _menu.Question;
            _question.style.display = question != null ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList("settings--asking", question != null);
            if (question == null) return;

            _questionText.text = _menu.QuestionText;
            _countdown.text = question.Seconds > 0f ? _menu.QuestionSeconds.ToString() : "";
            _countdown.style.display = question.Seconds > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            _yes.text = _text.Get(question.YesKey);
            _no.text = _text.Get(question.NoKey);
            _yes.EnableInClassList(ChosenAnswerClass, question.Choice == 0);
            _no.EnableInClassList(ChosenAnswerClass, question.Choice == 1);
        }

        private SettingsRowView Make(SettingsRow row)
        {
            for (Type? type = row.GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                if (_makers.TryGetValue(type, out var make)) return make(row, _menu, _text);
            }

            return row switch
            {
                StepsRow steps => new StepsRowView(steps, _menu, _text),
                ToggleRow toggle => new ToggleRowView(toggle, _menu, _text),
                ChoiceRow choice => new ChoiceRowView(choice, _menu, _text),
                HeadingRow heading => new HeadingRowView(heading, _menu, _text),
                KeysRow keys => new KeysRowView(keys, _menu, _text, this),
                _ => new ActionRowView(row, _menu, _text),
            };
        }

        internal VisualElement Cap(KeysRow row, int key)
        {
            VisualElement? cap = _keyCap?.Invoke(row, key);
            return cap ?? new Label(row.KeyText(key));
        }

        private Label Answer(VisualElement parent, bool yes)
        {
            var answer = new Label();
            answer.AddToClassList(AnswerClass);
            answer.AddToClassList(yes ? AnswerClass + "--yes" : AnswerClass + "--no");
            answer.RegisterCallback<PointerMoveEvent>(_ => _menu.PointAnswer(yes));
            answer.RegisterCallback<PointerDownEvent>(_ => _menu.Answer(yes));
            parent.Add(answer);
            return answer;
        }

        internal static VisualElement Element(string className, VisualElement parent)
        {
            var element = new VisualElement();
            element.AddToClassList(className);
            parent.Add(element);
            return element;
        }
    }

    /// <summary>
    /// One row of a <see cref="SettingsPanel"/>: the label on the left, the value on the right
    /// (<see cref="Value"/>). A move of the pointer over it focuses it. <see cref="Render"/> shows the row as it is now;
    /// the panel sets the focused and off classes.
    /// </summary>
    public abstract class SettingsRowView : VisualElement
    {
        public const string RowClass = "settings__row";
        public const string FocusedClass = "settings__row--focused";
        public const string DisabledClass = "settings__row--disabled";
        public const string HeldClass = "settings__row--held";
        public const string HeadingClass = "settings__row--heading";
        public const string LabelClass = "settings__label";
        public const string ValueClass = "settings__value";

        protected SettingsRowView(SettingsRow row, SettingsMenu menu, ISettingsText text)
        {
            Row = row ?? throw new ArgumentNullException(nameof(row));
            Menu = menu ?? throw new ArgumentNullException(nameof(menu));
            Text = text ?? throw new ArgumentNullException(nameof(text));
            AddToClassList(RowClass);
            AddToClassList(RowClass + "--" + KindName(row));
            Label = new Label(text.Get(row.Id));
            Label.AddToClassList(LabelClass);
            Add(Label);
            Value = SettingsPanel.Element(ValueClass, this);
            RegisterCallback<PointerMoveEvent>(_ => Menu.Point(Index));
        }

        public SettingsRow Row { get; }

        /// <summary>The row's place in <see cref="SettingsMenu.Rows"/>.</summary>
        public int Index { get; internal set; }

        protected SettingsMenu Menu { get; }

        protected ISettingsText Text { get; }

        protected Label Label { get; }

        /// <summary>The right side, where the value is drawn.</summary>
        protected VisualElement Value { get; }

        public abstract void Render();

        protected static Label AddLabel(VisualElement parent, string className, string text = "")
        {
            var label = new Label(text);
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        private static string KindName(SettingsRow row) => row switch
        {
            StepsRow => "steps",
            ToggleRow => "toggle",
            ChoiceRow => "choice",
            HeadingRow => "heading",
            KeysRow => "keys",
            ActionRow => "action",
            ApplyRow => "apply",
            _ => "custom",
        };
    }

    /// <summary>A meter of steps, one element a step from the first above the least; a click on one sets it.</summary>
    public sealed class StepsRowView : SettingsRowView
    {
        public const string StepsClass = "settings-steps";
        public const string StepClass = "settings-steps__step";
        public const string OnClass = "settings-steps__step--on";
        public const string NumberClass = "settings-steps__number";

        private readonly StepsRow _steps;
        private readonly List<VisualElement> _cells = new();
        private readonly Label _number;

        public StepsRowView(StepsRow row, SettingsMenu menu, ISettingsText text) : base(row, menu, text)
        {
            _steps = row;
            VisualElement meter = SettingsPanel.Element(StepsClass, Value);
            for (int value = row.Min + 1; value <= row.Max; value++)
            {
                int step = value;
                VisualElement cell = SettingsPanel.Element(StepClass, meter);
                cell.RegisterCallback<PointerDownEvent>(_ => Menu.SetStep(Index, step));
                _cells.Add(cell);
            }

            _number = AddLabel(Value, NumberClass);
        }

        public override void Render()
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                _cells[i].EnableInClassList(OnClass, _steps.Min + 1 + i <= _steps.Value);
            }

            _number.text = _steps.Value.ToString();
        }
    }

    /// <summary>ON and OFF side by side, the one that is so marked; a click on either sets it.</summary>
    public sealed class ToggleRowView : SettingsRowView
    {
        public const string OptionClass = "settings-toggle__option";
        public const string OnClass = "settings-toggle__option--on";

        private readonly ToggleRow _toggle;
        private readonly Label _on;
        private readonly Label _off;

        public ToggleRowView(ToggleRow row, SettingsMenu menu, ISettingsText text) : base(row, menu, text)
        {
            _toggle = row;
            // As the keys go: left is on, right is off.
            _on = AddLabel(Value, OptionClass, text.Get("settings.on"));
            _on.RegisterCallback<PointerDownEvent>(_ => Menu.Nudge(Index, -1));
            _off = AddLabel(Value, OptionClass, text.Get("settings.off"));
            _off.RegisterCallback<PointerDownEvent>(_ => Menu.Nudge(Index, 1));
        }

        public override void Render()
        {
            _on.EnableInClassList(OnClass, _toggle.Value);
            _off.EnableInClassList(OnClass, !_toggle.Value);
        }
    }

    /// <summary>‹ OPTION ›: the arrows step (off at the ends), a click on the option takes the next one round.</summary>
    public sealed class ChoiceRowView : SettingsRowView
    {
        public const string ArrowClass = "settings-choice__arrow";
        public const string LeftClass = "settings-choice__arrow--left";
        public const string RightClass = "settings-choice__arrow--right";
        public const string EndClass = "settings-choice__arrow--end";
        public const string OptionClass = "settings-choice__option";

        private readonly ChoiceRow _choice;
        private readonly Label _left;
        private readonly Label _option;
        private readonly Label _right;

        public ChoiceRowView(ChoiceRow row, SettingsMenu menu, ISettingsText text) : base(row, menu, text)
        {
            _choice = row;
            _left = AddLabel(Value, ArrowClass, "‹");
            _left.AddToClassList(LeftClass);
            _left.RegisterCallback<PointerDownEvent>(_ => Menu.Nudge(Index, -1));
            _option = AddLabel(Value, OptionClass);
            _option.RegisterCallback<PointerDownEvent>(_ => Menu.Nudge(Index, 0));
            _right = AddLabel(Value, ArrowClass, "›");
            _right.AddToClassList(RightClass);
            _right.RegisterCallback<PointerDownEvent>(_ => Menu.Nudge(Index, 1));
        }

        public override void Render()
        {
            int index = _choice.Index;
            _option.text = index >= 0 ? _choice.OptionText(index, Text) : "";
            _left.EnableInClassList(EndClass, index <= 0);
            _right.EnableInClassList(EndClass, index < 0 || index >= _choice.Count - 1);
        }
    }

    /// <summary>A row that does something on Enter or a click.</summary>
    public sealed class ActionRowView : SettingsRowView
    {
        public ActionRowView(SettingsRow row, SettingsMenu menu, ISettingsText text) : base(row, menu, text)
        {
            RegisterCallback<PointerDownEvent>(_ => Menu.Click(Index));
        }

        public override void Render()
        {
        }
    }

    /// <summary>A heading between rows: its label only.</summary>
    public sealed class HeadingRowView : SettingsRowView
    {
        public HeadingRowView(HeadingRow row, SettingsMenu menu, ISettingsText text) : base(row, menu, text)
        {
            AddToClassList(HeadingClass);
            Value.style.display = DisplayStyle.None;
        }

        public override void Render()
        {
        }
    }

    /// <summary>
    /// An action's keys, a cap each (<see cref="SettingsPanel.KeyCap"/>, else its text): the one picked on the focused
    /// row, the changed ones and the one listened for are marked; a click on one listens for it.
    /// </summary>
    public sealed class KeysRowView : SettingsRowView
    {
        public const string KeyClass = "settings-keys__key";
        public const string KeyFocusedClass = "settings-keys__key--focused";
        public const string ChangedClass = "settings-keys__key--changed";
        public const string ListeningClass = "settings-keys__key--listening";

        private readonly KeysRow _keys;
        private readonly SettingsPanel _panel;
        private readonly List<VisualElement> _caps = new();
        private string _drawn = "";

        public KeysRowView(KeysRow row, SettingsMenu menu, ISettingsText text, SettingsPanel panel)
            : base(row, menu, text)
        {
            _keys = row;
            _panel = panel;
        }

        public override void Render()
        {
            // Caps are made again only when a key's text or their number changed.
            var drawn = new System.Text.StringBuilder();
            for (int k = 0; k < _keys.Count; k++) drawn.Append(_keys.KeyText(k)).Append('\n');
            if (drawn.ToString() != _drawn || _caps.Count != _keys.Count)
            {
                _drawn = drawn.ToString();
                Value.Clear();
                _caps.Clear();
                for (int k = 0; k < _keys.Count; k++)
                {
                    int key = k;
                    VisualElement cap = _panel.Cap(_keys, k);
                    cap.AddToClassList(KeyClass);
                    cap.RegisterCallback<PointerDownEvent>(_ => Menu.ClickKey(Index, key));
                    Value.Add(cap);
                    _caps.Add(cap);
                }
            }

            bool focused = Menu.Focused == _keys;
            bool listening = Menu.Listening == _keys.Id;
            for (int k = 0; k < _caps.Count; k++)
            {
                bool picked = focused && k == _keys.KeyFocus;
                _caps[k].EnableInClassList(KeyFocusedClass, picked && !listening);
                _caps[k].EnableInClassList(ListeningClass, picked && listening);
                _caps[k].EnableInClassList(ChangedClass, !_keys.IsDefault(k));
            }
        }
    }
}
