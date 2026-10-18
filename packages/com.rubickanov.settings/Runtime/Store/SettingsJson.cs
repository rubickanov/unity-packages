using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Rubickanov.Settings
{
    /// <summary>
    /// The settings file as text: nested objects of scalars by dotted id, <c>"format"</c> first, two-space indents.
    /// Reads any JSON; arrays and empty objects are kept as raw text. Needs no JSON library.
    /// </summary>
    internal static class SettingsJson
    {
        public const string FormatField = "format";

        /// <summary>
        /// The file's values by dotted id into <paramref name="values"/> and its format (0 without one); throws
        /// <see cref="FormatException"/> for text that is not a JSON object or a format that is not a whole number.
        /// </summary>
        public static int Read(string text, SettingsValues values)
        {
            var reader = new Reader(text);
            if (reader.Value() is not List<KeyValuePair<string, object?>> root)
            {
                throw new FormatException("not an object");
            }

            reader.End();
            int format = 0;
            foreach (KeyValuePair<string, object?> field in root)
            {
                if (field.Key != FormatField) continue;
                if (field.Value is not double number || number != Math.Floor(number) || number < 0 || number > int.MaxValue)
                {
                    throw new FormatException($"format {Show(field.Value)} is not a whole number");
                }

                format = (int)number;
            }

            foreach (KeyValuePair<string, object?> field in root)
            {
                if (field.Key != FormatField) Flatten(field.Key, field.Value, values);
            }

            return format;
        }

        /// <summary>The file for <paramref name="values"/> in their order; an id under one already a value is left out.</summary>
        public static string Write(int format, IReadOnlyList<KeyValuePair<string, SettingsJsonValue>> values)
        {
            var root = new Node();
            for (int i = 0; i < values.Count; i++)
            {
                root.Put(values[i].Key, 0, values[i].Value);
            }

            var text = new StringBuilder();
            text.Append("{\n  \"").Append(FormatField).Append("\": ").Append(format.ToString(CultureInfo.InvariantCulture));
            root.WriteFields(text, 1, first: false);
            text.Append("\n}\n");
            return text.ToString();
        }

        public static void Quote(StringBuilder text, string value)
        {
            text.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':
                        text.Append("\\\"");
                        break;
                    case '\\':
                        text.Append("\\\\");
                        break;
                    case '\n':
                        text.Append("\\n");
                        break;
                    case '\r':
                        text.Append("\\r");
                        break;
                    case '\t':
                        text.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            text.Append(c);
                        }

                        break;
                }
            }

            text.Append('"');
        }

        public static void WriteNumber(StringBuilder text, double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                text.Append('0');
            }
            else if (number == Math.Floor(number) && Math.Abs(number) < 1e15)
            {
                text.Append(((long)number).ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                text.Append(number.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        private static void WriteValue(StringBuilder text, SettingsJsonValue value)
        {
            switch (value.Kind)
            {
                case SettingsJsonKind.Number:
                    WriteNumber(text, value.Number);
                    break;
                case SettingsJsonKind.Bool:
                    text.Append(value.Bool ? "true" : "false");
                    break;
                case SettingsJsonKind.String:
                    Quote(text, value.String!);
                    break;
                case SettingsJsonKind.Raw:
                    text.Append(value.Raw);
                    break;
                default:
                    text.Append("null");
                    break;
            }
        }

        private static void Flatten(string id, object? value, SettingsValues values)
        {
            switch (value)
            {
                case List<KeyValuePair<string, object?>> { Count: > 0 } fields:
                    foreach (KeyValuePair<string, object?> field in fields)
                    {
                        Flatten(id + "." + field.Key, field.Value, values);
                    }

                    break;
                case List<KeyValuePair<string, object?>>:
                case List<object?>:
                    var raw = new StringBuilder();
                    WriteCompact(raw, value);
                    values.Set(id, SettingsJsonValue.OfRaw(raw.ToString()));
                    break;
                case double number:
                    values.Set(id, SettingsJsonValue.Of(number));
                    break;
                case bool flag:
                    values.Set(id, SettingsJsonValue.Of(flag));
                    break;
                case string text:
                    values.Set(id, SettingsJsonValue.Of(text));
                    break;
                default:
                    values.Set(id, SettingsJsonValue.Null);
                    break;
            }
        }

        private static void WriteCompact(StringBuilder text, object? value)
        {
            switch (value)
            {
                case List<KeyValuePair<string, object?>> fields:
                    text.Append('{');
                    for (int i = 0; i < fields.Count; i++)
                    {
                        if (i > 0) text.Append(',');
                        Quote(text, fields[i].Key);
                        text.Append(':');
                        WriteCompact(text, fields[i].Value);
                    }

                    text.Append('}');
                    break;
                case List<object?> items:
                    text.Append('[');
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (i > 0) text.Append(',');
                        WriteCompact(text, items[i]);
                    }

                    text.Append(']');
                    break;
                case double number:
                    WriteNumber(text, number);
                    break;
                case bool flag:
                    text.Append(flag ? "true" : "false");
                    break;
                case string s:
                    Quote(text, s);
                    break;
                default:
                    text.Append("null");
                    break;
            }
        }

        private static string Show(object? value)
        {
            var text = new StringBuilder();
            WriteCompact(text, value);
            return text.ToString();
        }

        // One object of the file being written: its fields in the order they first came.
        private sealed class Node
        {
            private readonly List<string> _names = new();
            private readonly Dictionary<string, object> _fields = new();

            public void Put(string id, int start, SettingsJsonValue value)
            {
                int dot = id.IndexOf('.', start);
                string name = dot < 0 ? id.Substring(start) : id.Substring(start, dot - start);
                if (name.Length == 0 || name == FormatField && start == 0) return;
                _fields.TryGetValue(name, out object? field);
                if (dot < 0)
                {
                    if (field != null) return;
                    _names.Add(name);
                    _fields[name] = value;
                    return;
                }

                if (field == null)
                {
                    field = new Node();
                    _names.Add(name);
                    _fields[name] = field;
                }

                if (field is Node node) node.Put(id, dot + 1, value);
            }

            public void WriteFields(StringBuilder text, int depth, bool first)
            {
                foreach (string name in _names)
                {
                    text.Append(first ? "\n" : ",\n");
                    first = false;
                    text.Append(' ', depth * 2);
                    Quote(text, name);
                    text.Append(": ");
                    object field = _fields[name];
                    if (field is Node node)
                    {
                        text.Append('{');
                        node.WriteFields(text, depth + 1, first: true);
                        text.Append('\n').Append(' ', depth * 2).Append('}');
                    }
                    else
                    {
                        WriteValue(text, (SettingsJsonValue)field);
                    }
                }
            }
        }

        // A plain recursive-descent JSON reader: objects (fields in order), arrays, strings, numbers, true, false, null.
        private struct Reader
        {
            private readonly string _text;
            private int _at;

            public Reader(string text)
            {
                _text = text ?? throw new FormatException("no text");
                _at = 0;
            }

            public object? Value()
            {
                Skip();
                if (_at >= _text.Length)
                {
                    throw Error("the text ends");
                }

                switch (_text[_at])
                {
                    case '{':
                        return Object();
                    case '[':
                        return Array();
                    case '"':
                        return String();
                    case 't':
                        Word("true");
                        return true;
                    case 'f':
                        Word("false");
                        return false;
                    case 'n':
                        Word("null");
                        return null;
                    default:
                        return Number();
                }
            }

            public void End()
            {
                Skip();
                if (_at < _text.Length)
                {
                    throw Error("text after the end");
                }
            }

            private List<KeyValuePair<string, object?>> Object()
            {
                var fields = new List<KeyValuePair<string, object?>>();
                _at++;
                Skip();
                if (Peek() == '}')
                {
                    _at++;
                    return fields;
                }

                while (true)
                {
                    Skip();
                    if (Peek() != '"')
                    {
                        throw Error("a name expected");
                    }

                    string name = String();
                    Skip();
                    Expect(':');
                    fields.Add(new KeyValuePair<string, object?>(name, Value()));
                    Skip();
                    if (Peek() == ',')
                    {
                        _at++;
                        continue;
                    }

                    Expect('}');
                    return fields;
                }
            }

            private List<object?> Array()
            {
                var items = new List<object?>();
                _at++;
                Skip();
                if (Peek() == ']')
                {
                    _at++;
                    return items;
                }

                while (true)
                {
                    items.Add(Value());
                    Skip();
                    if (Peek() == ',')
                    {
                        _at++;
                        continue;
                    }

                    Expect(']');
                    return items;
                }
            }

            private string String()
            {
                _at++;
                var text = new StringBuilder();
                while (_at < _text.Length)
                {
                    char c = _text[_at++];
                    if (c == '"')
                    {
                        return text.ToString();
                    }

                    if (c != '\\')
                    {
                        text.Append(c);
                        continue;
                    }

                    if (_at >= _text.Length)
                    {
                        break;
                    }

                    char escaped = _text[_at++];
                    switch (escaped)
                    {
                        case 'n':
                            text.Append('\n');
                            break;
                        case 'r':
                            text.Append('\r');
                            break;
                        case 't':
                            text.Append('\t');
                            break;
                        case 'b':
                            text.Append('\b');
                            break;
                        case 'f':
                            text.Append('\f');
                            break;
                        case 'u':
                            if (_at + 4 > _text.Length ||
                                !int.TryParse(_text.Substring(_at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                                    out int code))
                            {
                                throw Error("a bad \\u escape");
                            }

                            text.Append((char)code);
                            _at += 4;
                            break;
                        default:
                            text.Append(escaped);
                            break;
                    }
                }

                throw Error("a string does not end");
            }

            private double Number()
            {
                int start = _at;
                while (_at < _text.Length && "+-0123456789.eE".IndexOf(_text[_at]) >= 0)
                {
                    _at++;
                }

                if (_at == start || !double.TryParse(_text.Substring(start, _at - start), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double number))
                {
                    throw Error("a value expected");
                }

                return number;
            }

            private void Word(string word)
            {
                if (string.CompareOrdinal(_text, _at, word, 0, word.Length) != 0)
                {
                    throw Error($"{word} expected");
                }

                _at += word.Length;
            }

            private void Expect(char c)
            {
                if (Peek() != c)
                {
                    throw Error($"'{c}' expected");
                }

                _at++;
            }

            private char Peek() => _at < _text.Length ? _text[_at] : '\0';

            private void Skip()
            {
                while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
                {
                    _at++;
                }
            }

            private FormatException Error(string what) => new($"{what} at {_at}");
        }
    }
}
