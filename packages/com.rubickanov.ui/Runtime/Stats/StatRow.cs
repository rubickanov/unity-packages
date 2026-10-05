using System;
using System.Globalization;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// One line of a <see cref="StatPanel"/>: a label and a value read from the game on each refresh, a number shown in
    /// its format and unit and coloured by its limits, a text, or a fixed text such as the build's version. Its label
    /// changes only when the shown text does, so a refresh of an unchanged value makes nothing.
    /// </summary>
    public sealed class StatRow
    {
        public const string ClassName = "stat-row";
        public const string LabelClassName = ClassName + "__label";
        public const string ValueClassName = ClassName + "__value";

        private const int Room = 64;
        private const string Unknown = "-";

        private static readonly string[] LevelClassNames =
        {
            ValueClassName + "--plain",
            ValueClassName + "--good",
            ValueClassName + "--warn",
            ValueClassName + "--bad",
        };

        private readonly Func<double>? _number;
        private readonly Func<string?>? _text;
        private readonly string _format;
        private readonly string _unit;
        private readonly StatLimits _limits;
        private readonly StatPanelOptions _options;
        private readonly Action _shownChanged;
        private readonly Label _value;
        private readonly char[] _chars = new char[Room];
        private int _length = -1;
        private bool _shown = true;

        internal StatRow(string id, string label, Func<double>? number, Func<string?>? text, string format, string unit,
            StatLimits limits, StatPanelOptions options, Action shownChanged)
        {
            Id = id;
            Label = label;
            _number = number;
            _text = text;
            _format = format;
            _unit = unit;
            _limits = limits;
            _options = options;
            _shownChanged = shownChanged;

            Element = new VisualElement { name = id, pickingMode = PickingMode.Ignore };
            Element.AddToClassList(ClassName);
            Element.style.flexDirection = FlexDirection.Row;
            if (label.Length > 0)
            {
                var name = NewLabel(label, LabelClassName);
                name.style.color = options.Plain;
                name.style.marginRight = options.Gap;
                Element.Add(name);
            }

            _value = NewLabel("", ValueClassName);
            Element.Add(_value);
            SetLevel(StatLevel.Plain);
        }

        /// <summary>The name the game and its console know the row by.</summary>
        public string Id { get; }

        /// <summary>The words before the value; empty for none.</summary>
        public string Label { get; }

        /// <summary>Hidden rows are not read and take no room; the panel hides itself when no row is shown.</summary>
        public bool Shown
        {
            get => _shown;
            set
            {
                if (_shown == value)
                {
                    return;
                }

                _shown = value;
                Element.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                if (value)
                {
                    Refresh();
                }

                _shownChanged();
            }
        }

        /// <summary>The number last read; NaN for a text row, or a number not known yet.</summary>
        public double Value { get; private set; } = double.NaN;

        /// <summary>The last value's level against the row's limits.</summary>
        public StatLevel Level { get; private set; } = StatLevel.Plain;

        /// <summary>What the value shows now.</summary>
        public string Text => _value.text;

        public VisualElement Element { get; }

        /// <summary>Reads the value now, shown or not.</summary>
        public void Refresh()
        {
            if (_number != null)
            {
                Value = _number();
                Write(Value);
                SetLevel(_limits.Judge(Value));
            }
            else if (_text != null)
            {
                string text = _text() ?? Unknown;
                if (text != _value.text)
                {
                    _value.text = text;
                }
            }
        }

        internal void SetFixed(string text) => _value.text = text;

        private void Write(double value)
        {
            Span<char> chars = stackalloc char[Room];
            int length;
            if (double.IsNaN(value))
            {
                Unknown.AsSpan().CopyTo(chars);
                length = Unknown.Length;
            }
            else if (!value.TryFormat(chars, out length, _format, CultureInfo.InvariantCulture) ||
                     length + _unit.Length > Room)
            {
                SetText(value.ToString(_format, CultureInfo.InvariantCulture) + _unit);
                return;
            }
            else
            {
                _unit.AsSpan().CopyTo(chars[length..]);
                length += _unit.Length;
            }

            if (length == _length && chars[..length].SequenceEqual(_chars.AsSpan(0, length)))
            {
                return;
            }

            chars[..length].CopyTo(_chars);
            _length = length;
            _value.text = new string(_chars, 0, length);
        }

        private void SetText(string text)
        {
            _length = -1;
            if (text != _value.text)
            {
                _value.text = text;
            }
        }

        private void SetLevel(StatLevel level)
        {
            if (level == Level && _value.ClassListContains(LevelClassNames[(int)level]))
            {
                return;
            }

            _value.RemoveFromClassList(LevelClassNames[(int)Level]);
            Level = level;
            _value.AddToClassList(LevelClassNames[(int)level]);
            _value.style.color = _options.Colour(level);
        }

        private static Label NewLabel(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            label.style.marginLeft = 0f;
            label.style.marginRight = 0f;
            label.style.marginTop = 0f;
            label.style.marginBottom = 0f;
            label.style.paddingLeft = 0f;
            label.style.paddingRight = 0f;
            label.style.paddingTop = 0f;
            label.style.paddingBottom = 0f;
            return label;
        }
    }
}
