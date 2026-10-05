using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// A column of stats in a corner of its host: the build's version, frames a second, ping, loss, whatever the game
    /// adds, each read by a function of the game's and coloured by its limits. The panel knows no stat itself; which rows
    /// there are, which a player may turn on and what keeps that choice are the game's. It takes no pointer events and
    /// hides itself while no row is shown. Fed <see cref="Tick"/> once a frame, it reads the shown rows every
    /// <see cref="StatPanelOptions.Interval"/> seconds.
    /// </summary>
    public sealed class StatPanel : IDisposable
    {
        public const string ClassName = "stat-panel";

        private readonly List<StatRow> _rows = new();
        private readonly StatPanelOptions _options;
        private readonly Action _shownChanged;
        private float _sinceRefresh = float.PositiveInfinity;

        public StatPanel(VisualElement host, StatPanelOptions? options = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            _options = options ?? new StatPanelOptions();
            _shownChanged = UpdateDisplay;

            Root = new VisualElement { name = ClassName, pickingMode = PickingMode.Ignore };
            Root.AddToClassList(ClassName);
            Place(Root.style, _options);
            host.Add(Root);
            UpdateDisplay();
        }

        public VisualElement Root { get; }

        public IReadOnlyList<StatRow> Rows => _rows;

        /// <summary>
        /// A number in <paramref name="format"/> (as <c>double.ToString</c>, invariant culture) followed by
        /// <paramref name="unit"/>, coloured by <paramref name="limits"/>; NaN shows a dash.
        /// </summary>
        public StatRow Add(string id, string label, Func<double> value, string format = "0", string unit = "",
            StatLimits limits = default)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return Append(id, label, value, null, format, unit ?? "", limits);
        }

        /// <summary>A text read on each refresh; null shows a dash.</summary>
        public StatRow AddText(string id, string label, Func<string?> text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return Append(id, label, null, text, "", "", default);
        }

        /// <summary>A text that does not change, such as the build's version.</summary>
        public StatRow AddFixed(string id, string label, string text)
        {
            StatRow row = Append(id, label, null, null, "", "", default);
            row.SetFixed(text ?? "");
            return row;
        }

        /// <summary>The row named <paramref name="id"/>; null for none.</summary>
        public StatRow? Find(string id)
        {
            foreach (StatRow row in _rows)
            {
                if (row.Id == id)
                {
                    return row;
                }
            }

            return null;
        }

        /// <summary>Takes a row out, as when what it reads goes away; false if the panel did not have it.</summary>
        public bool Remove(StatRow row)
        {
            if (row == null || !_rows.Remove(row))
            {
                return false;
            }

            row.Element.RemoveFromHierarchy();
            UpdateDisplay();
            return true;
        }

        /// <summary>Counts unscaled frame time and reads the shown rows when the interval is up.</summary>
        public void Tick(float unscaledDeltaTime)
        {
            _sinceRefresh += unscaledDeltaTime;
            if (_sinceRefresh >= _options.Interval)
            {
                Refresh();
            }
        }

        /// <summary>Reads every shown row now.</summary>
        public void Refresh()
        {
            _sinceRefresh = 0f;
            foreach (StatRow row in _rows)
            {
                if (row.Shown)
                {
                    row.Refresh();
                }
            }
        }

        public void Dispose()
        {
            _rows.Clear();
            Root.RemoveFromHierarchy();
        }

        private StatRow Append(string id, string label, Func<double>? number, Func<string?>? text, string format,
            string unit, StatLimits limits)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A row needs an id.", nameof(id));
            if (Find(id) != null) throw new ArgumentException($"The panel already has a row '{id}'.", nameof(id));

            var row = new StatRow(id, label ?? "", number, text, format, unit, limits, _options, _shownChanged);
            _rows.Add(row);
            Root.Add(row.Element);
            row.Refresh();
            UpdateDisplay();
            return row;
        }

        private void UpdateDisplay()
        {
            bool any = false;
            foreach (StatRow row in _rows)
            {
                any |= row.Shown;
            }

            Root.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void Place(IStyle style, StatPanelOptions options)
        {
            style.position = Position.Absolute;
            style.flexDirection = FlexDirection.Column;
            bool top = options.Corner is StatCorner.TopLeft or StatCorner.TopRight;
            bool left = options.Corner is StatCorner.TopLeft or StatCorner.BottomLeft;
            if (top)
            {
                style.top = options.Margin;
            }
            else
            {
                style.bottom = options.Margin;
            }

            if (left)
            {
                style.left = options.Margin;
            }
            else
            {
                style.right = options.Margin;
            }

            style.alignItems = left ? Align.FlexStart : Align.FlexEnd;
            if (options.FontSize > 0f)
            {
                style.fontSize = options.FontSize;
            }

            if (options.Shadow.a > 0f)
            {
                style.textShadow = new TextShadow
                {
                    offset = new Vector2(options.ShadowOffset, options.ShadowOffset),
                    color = options.Shadow,
                };
            }
        }
    }
}
