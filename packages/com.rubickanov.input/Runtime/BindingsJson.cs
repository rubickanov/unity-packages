using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Rubickanov.Input
{
    /// <summary>
    /// The player's keys as text: <c>{ "format": 1, "keys": { "jump": "&lt;Keyboard&gt;/f" } }</c>, slot id to binding
    /// path, only the keys that differ from the defaults. Reads any JSON and keeps those two fields, so the package needs
    /// no JSON library.
    /// </summary>
    internal static class BindingsJson
    {
        public const int CurrentFormat = 1;

        public static string Write(IReadOnlyList<KeyValuePair<string, string>> keys)
        {
            var text = new StringBuilder();
            text.Append("{\n  \"format\": ").Append(CurrentFormat.ToString(CultureInfo.InvariantCulture)).Append(",\n  \"keys\": {");
            for (int i = 0; i < keys.Count; i++)
            {
                text.Append(i == 0 ? "\n    " : ",\n    ");
                Quote(text, keys[i].Key);
                text.Append(": ");
                Quote(text, keys[i].Value);
            }

            text.Append(keys.Count > 0 ? "\n  }\n}\n" : "}\n}\n");
            return text.ToString();
        }

        /// <summary>The keys in <paramref name="json"/>; throws <see cref="FormatException"/> for anything else.</summary>
        public static Dictionary<string, string> Read(string json)
        {
            var reader = new Reader(json);
            if (reader.Value() is not Dictionary<string, object?> root)
            {
                throw new FormatException("not an object");
            }

            reader.End();
            if (!root.TryGetValue("format", out object? format) || format is not double number ||
                number != CurrentFormat)
            {
                throw new FormatException($"format {format ?? "none"}, this build reads {CurrentFormat}");
            }

            var keys = new Dictionary<string, string>();
            if (root.TryGetValue("keys", out object? found) && found != null)
            {
                if (found is not Dictionary<string, object?> map)
                {
                    throw new FormatException("keys is not an object");
                }

                foreach (KeyValuePair<string, object?> key in map)
                {
                    keys[key.Key] = key.Value as string ?? throw new FormatException($"{key.Key} is not a path");
                }
            }

            return keys;
        }

        private static void Quote(StringBuilder text, string value)
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

        // A plain recursive-descent JSON reader: objects, arrays, strings, numbers, true, false, null.
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

                char c = _text[_at];
                switch (c)
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

            private Dictionary<string, object?> Object()
            {
                var map = new Dictionary<string, object?>();
                _at++;
                Skip();
                if (Peek() == '}')
                {
                    _at++;
                    return map;
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
                    map[name] = Value();
                    Skip();
                    if (Peek() == ',')
                    {
                        _at++;
                        continue;
                    }

                    Expect('}');
                    return map;
                }
            }

            private List<object?> Array()
            {
                var list = new List<object?>();
                _at++;
                Skip();
                if (Peek() == ']')
                {
                    _at++;
                    return list;
                }

                while (true)
                {
                    list.Add(Value());
                    Skip();
                    if (Peek() == ',')
                    {
                        _at++;
                        continue;
                    }

                    Expect(']');
                    return list;
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
