using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Object = UnityEngine.Object;

namespace Rubickanov.Log
{
    /// <summary>
    /// <c>$"..."</c> passed to <see cref="LogChannel.Info(ref InfoMessage, Object)"/>. The compiler asks the
    /// channel first and skips every hole when it would not write the message, so a silent channel costs a
    /// level check and nothing else.
    /// </summary>
    [InterpolatedStringHandler]
    public ref struct InfoMessage
    {
        private MessageBuilder _builder;

        public InfoMessage(int literalLength, int formattedCount, LogChannel channel, out bool enabled)
        {
            enabled = channel.Writes(LogLevel.Info);
            _builder = new MessageBuilder(enabled);
        }

        public bool Enabled => _builder.Enabled;

        public void AppendLiteral(string value) => _builder.AppendLiteral(value);

        public void AppendFormatted<T>(T value) => _builder.AppendFormatted(value, null);

        public void AppendFormatted<T>(T value, string format) => _builder.AppendFormatted(value, format);

        public string ToStringAndClear() => _builder.ToStringAndClear();
    }

    /// <summary><c>$"..."</c> passed to <see cref="LogChannel.Verbose(ref VerboseMessage, Object)"/>, as <see cref="InfoMessage"/>.</summary>
    [InterpolatedStringHandler]
    public ref struct VerboseMessage
    {
        private MessageBuilder _builder;

        public VerboseMessage(int literalLength, int formattedCount, LogChannel channel, out bool enabled)
        {
            enabled = channel.Writes(LogLevel.Verbose);
            _builder = new MessageBuilder(enabled);
        }

        public bool Enabled => _builder.Enabled;

        public void AppendLiteral(string value) => _builder.AppendLiteral(value);

        public void AppendFormatted<T>(T value) => _builder.AppendFormatted(value, null);

        public void AppendFormatted<T>(T value, string format) => _builder.AppendFormatted(value, format);

        public string ToStringAndClear() => _builder.ToStringAndClear();
    }

    /// <summary>
    /// The text of one message, written into a StringBuilder the thread reuses. Holes read as a person would
    /// want them: numbers the same in every locale and Unity objects by name.
    /// </summary>
    internal struct MessageBuilder
    {
        [ThreadStatic]
        private static StringBuilder _cached;

        private StringBuilder _text;

        public MessageBuilder(bool enabled)
        {
            _text = null;
            if (enabled)
            {
                // Taken, not shared: a ToString in a hole that logs itself gets a builder of its own.
                _text = _cached ?? new StringBuilder(128);
                _cached = null;
            }
        }

        public bool Enabled => _text != null;

        public void AppendLiteral(string value) => _text?.Append(value);

        public void AppendFormatted<T>(T value, string format)
        {
            if (_text == null)
            {
                return;
            }

            switch (value)
            {
                case null:
                    _text.Append("null");
                    break;
                case string text:
                    _text.Append(text);
                    break;
                // A destroyed object is not null to C# but is to Unity, and reading its name would throw.
                case Object unityObject:
                    _text.Append(unityObject != null ? unityObject.name : "null");
                    break;
                case IFormattable formattable:
                    _text.Append(formattable.ToString(format, CultureInfo.InvariantCulture));
                    break;
                default:
                    _text.Append(value);
                    break;
            }
        }

        public string ToStringAndClear()
        {
            if (_text == null)
            {
                return string.Empty;
            }

            string message = _text.ToString();
            _text.Clear();
            // One that grew huge on a giant message is let go rather than kept for good.
            if (_text.Capacity <= 1024)
            {
                _cached = _text;
            }
            _text = null;
            return message;
        }
    }
}
