using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Rubickanov.Save.Data
{
    /// <summary>
    /// JSON text of a <see cref="SaveNode"/> tree. The written text is canonical, so a diff reads well and the same tree
    /// always gives the same bytes: two-space indent, '\n', object keys in ordinal order, an array of scalars on one line,
    /// a newline at the end; a number has the fewest digits that read back exactly (a float as a float: 0.1, not
    /// 0.100000001490116), negative zero is "-0.0".
    /// The reader takes what <see cref="JsonUtility"/> writes as well, NaN and Infinity read as null with a note.
    /// </summary>
    public static class SaveJson
    {
        private const int MaxDepth = 256;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly double NegativeZero = BitConverter.Int64BitsToDouble(long.MinValue);
        private static readonly string[] SinglePrecision = { "G6", "G7", "G8", "G9" };
        private static readonly string[] DoublePrecision = { "G15", "G16", "G17" };

        /// <summary>
        /// The canonical text of <paramref name="node"/>. NaN and infinities have no JSON: they are written as null and
        /// reported with their key path to <paramref name="logError"/> (Unity's error log when null).
        /// </summary>
        public static string Write(SaveNode node, Action<string> logError = null)
        {
            var text = new StringBuilder(256);
            var path = new List<string>();
            WriteNode(text, node ?? SaveNode.Null(), 0, path, logError ?? Debug.LogError);
            text.Append('\n');
            return text.ToString();
        }

        /// <summary>Reads JSON text; on failure <paramref name="error"/> says where ("line 4, col 12: ...").</summary>
        public static bool TryRead(string text, out SaveNode node, out string error) => TryRead(text, out node, out error, null);

        /// <param name="notes">Where values read leniently go (a NaN read as null), with their key path.</param>
        public static bool TryRead(string text, out SaveNode node, out string error, List<string> notes)
        {
            var reader = new Reader(text ?? string.Empty, notes);
            try
            {
                node = reader.ReadDocument();
                error = null;
                return true;
            }
            catch (FormatException e)
            {
                node = null;
                error = e.Message;
                return false;
            }
        }

        /// <summary>The text of a number node; null for NaN and infinities.</summary>
        internal static string NumberText(SaveNode node)
        {
            if (node.IsInteger)
            {
                return node.Long.ToString(Invariant);
            }

            double d = node.RawDouble;
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                return null;
            }

            if (d == 0 && IsNegative(d))
            {
                return "-0.0"; // "-0" would read back as the integer 0
            }

            if (node.IsSingle)
            {
                // The fewest digits that read back as the same float (Mono's "R" gives nine where fewer do), from six
                // up, the digits a float always keeps, so 120 stays 120 and is not 1.2E+02.
                float f = (float)d;
                for (int i = 0; i < SinglePrecision.Length - 1; i++)
                {
                    string single = f.ToString(SinglePrecision[i], Invariant);
                    if (float.TryParse(single, NumberStyles.Float, Invariant, out float back) && back.Equals(f))
                    {
                        return single;
                    }
                }

                return f.ToString("G9", Invariant);
            }

            for (int i = 0; i < DoublePrecision.Length - 1; i++)
            {
                string s = d.ToString(DoublePrecision[i], Invariant);
                if (double.TryParse(s, NumberStyles.Float, Invariant, out double back) && back.Equals(d))
                {
                    return s;
                }
            }

            return d.ToString("G17", Invariant);
        }

        private static bool IsNegative(double d) => BitConverter.DoubleToInt64Bits(d) < 0;

        private static void WriteNode(StringBuilder text, SaveNode node, int depth, List<string> path, Action<string> logError)
        {
            switch (node.Kind)
            {
                case SaveNodeKind.Null:
                    text.Append("null");
                    break;
                case SaveNodeKind.Bool:
                    text.Append(node.Bool ? "true" : "false");
                    break;
                case SaveNodeKind.Number:
                    string number = NumberText(node);
                    if (number == null)
                    {
                        logError($"{JoinPath(path)}: {node.RawDouble.ToString(Invariant)} has no JSON, written as null");
                        number = "null";
                    }

                    text.Append(number);
                    break;
                case SaveNodeKind.String:
                    WriteString(text, node.String);
                    break;
                case SaveNodeKind.Array:
                    WriteArray(text, node, depth, path, logError);
                    break;
                default:
                    WriteObject(text, node, depth, path, logError);
                    break;
            }
        }

        private static void WriteArray(StringBuilder text, SaveNode array, int depth, List<string> path, Action<string> logError)
        {
            if (array.Count == 0)
            {
                text.Append("[]");
                return;
            }

            bool scalars = true;
            for (int i = 0; i < array.Count && scalars; i++)
            {
                scalars = !array[i].IsArray && !array[i].IsObject;
            }

            text.Append('[');
            for (int i = 0; i < array.Count; i++)
            {
                if (scalars)
                {
                    text.Append(i == 0 ? "" : ", ");
                }
                else
                {
                    text.Append(i == 0 ? "\n" : ",\n");
                    Indent(text, depth + 1);
                }

                path.Add(i.ToString(Invariant));
                WriteNode(text, array[i], depth + 1, path, logError);
                path.RemoveAt(path.Count - 1);
            }

            if (!scalars)
            {
                text.Append('\n');
                Indent(text, depth);
            }

            text.Append(']');
        }

        private static void WriteObject(StringBuilder text, SaveNode obj, int depth, List<string> path, Action<string> logError)
        {
            IReadOnlyList<string> keys = obj.Keys;
            if (keys.Count == 0)
            {
                text.Append("{}");
                return;
            }

            text.Append('{');
            for (int i = 0; i < keys.Count; i++)
            {
                text.Append(i == 0 ? "\n" : ",\n");
                Indent(text, depth + 1);
                WriteString(text, keys[i]);
                text.Append(": ");
                path.Add(keys[i]);
                WriteNode(text, obj[keys[i]], depth + 1, path, logError);
                path.RemoveAt(path.Count - 1);
            }

            text.Append('\n');
            Indent(text, depth);
            text.Append('}');
        }

        private static void Indent(StringBuilder text, int depth) => text.Append(' ', depth * 2);

        private static void WriteString(StringBuilder text, string s)
        {
            text.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    default:
                        if (c < ' ')
                        {
                            text.Append("\\u").Append(((int)c).ToString("x4", Invariant));
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

        private static string JoinPath(List<string> path) => path.Count == 0 ? "(root)" : string.Join("/", path);

        private sealed class Reader
        {
            private readonly string _text;
            private readonly List<string> _notes;
            private readonly List<string> _path = new();
            private int _at;

            public Reader(string text, List<string> notes)
            {
                _text = text;
                _notes = notes;
                _at = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
            }

            public SaveNode ReadDocument()
            {
                SkipSpace();
                if (_at >= _text.Length)
                {
                    throw Fail("no value, the text is empty");
                }

                SaveNode node = ReadValue(0);
                SkipSpace();
                if (_at < _text.Length)
                {
                    throw Fail($"'{_text[_at]}' after the end of the value");
                }

                return node;
            }

            private SaveNode ReadValue(int depth)
            {
                if (depth > MaxDepth)
                {
                    throw Fail($"nested deeper than {MaxDepth}");
                }

                SkipSpace();
                if (_at >= _text.Length)
                {
                    throw Fail("the text ends where a value was expected");
                }

                char c = _text[_at];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return SaveNode.Of(ReadString());
                    case 't': Word("true"); return SaveNode.Of(true);
                    case 'f': Word("false"); return SaveNode.Of(false);
                    case 'n': Word("null"); return SaveNode.Null();
                    case 'N': return NotANumber("NaN");
                    case 'I': return NotANumber("Infinity");
                    default:
                        if (c == '-' && _at + 1 < _text.Length && _text[_at + 1] == 'I')
                        {
                            return NotANumber("-Infinity");
                        }

                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return ReadNumber();
                        }

                        throw Fail($"'{c}' where a value was expected");
                }
            }

            private SaveNode ReadObject(int depth)
            {
                SaveNode obj = SaveNode.NewObject();
                _at++;
                SkipSpace();
                if (Peek() == '}')
                {
                    _at++;
                    return obj;
                }

                while (true)
                {
                    SkipSpace();
                    if (Peek() != '"')
                    {
                        throw Fail("a key in quotes expected");
                    }

                    string key = ReadString();
                    SkipSpace();
                    Expect(':');
                    _path.Add(key);
                    obj.Put(key, ReadValue(depth + 1));
                    _path.RemoveAt(_path.Count - 1);
                    SkipSpace();
                    char c = Peek();
                    _at++;
                    if (c == '}')
                    {
                        return obj;
                    }

                    if (c != ',')
                    {
                        _at--;
                        throw Fail("',' or '}' expected");
                    }
                }
            }

            private SaveNode ReadArray(int depth)
            {
                SaveNode array = SaveNode.NewArray();
                _at++;
                SkipSpace();
                if (Peek() == ']')
                {
                    _at++;
                    return array;
                }

                while (true)
                {
                    _path.Add(array.Count.ToString(Invariant));
                    array.Add(ReadValue(depth + 1));
                    _path.RemoveAt(_path.Count - 1);
                    SkipSpace();
                    char c = Peek();
                    _at++;
                    if (c == ']')
                    {
                        return array;
                    }

                    if (c != ',')
                    {
                        _at--;
                        throw Fail("',' or ']' expected");
                    }
                }
            }

            private string ReadString()
            {
                _at++; // the opening quote
                var s = new StringBuilder();
                while (true)
                {
                    if (_at >= _text.Length)
                    {
                        throw Fail("the text ends inside a string");
                    }

                    char c = _text[_at++];
                    if (c == '"')
                    {
                        return s.ToString();
                    }

                    if (c != '\\')
                    {
                        s.Append(c);
                        continue;
                    }

                    if (_at >= _text.Length)
                    {
                        throw Fail("the text ends inside a string");
                    }

                    char e = _text[_at++];
                    switch (e)
                    {
                        case '"': s.Append('"'); break;
                        case '\\': s.Append('\\'); break;
                        case '/': s.Append('/'); break;
                        case 'b': s.Append('\b'); break;
                        case 'f': s.Append('\f'); break;
                        case 'n': s.Append('\n'); break;
                        case 'r': s.Append('\r'); break;
                        case 't': s.Append('\t'); break;
                        case 'u':
                            if (_at + 4 > _text.Length
                                || !int.TryParse(_text.Substring(_at, 4), NumberStyles.AllowHexSpecifier, Invariant, out int code))
                            {
                                _at -= 2;
                                throw Fail("\\u needs four hex digits");
                            }

                            s.Append((char)code);
                            _at += 4;
                            break;
                        default:
                            _at -= 2;
                            throw Fail($"unknown escape \\{e}");
                    }
                }
            }

            // Integer text (an optional '-', then digits only) that fits a long is a long; any other number a double.
            private SaveNode ReadNumber()
            {
                int start = _at;
                bool integer = true;
                if (_text[_at] == '-')
                {
                    _at++;
                }

                int digits = _at;
                while (_at < _text.Length)
                {
                    char c = _text[_at];
                    if (c >= '0' && c <= '9')
                    {
                        _at++;
                    }
                    else if (c == '.' || c == 'e' || c == 'E' || ((c == '+' || c == '-') && (_text[_at - 1] == 'e' || _text[_at - 1] == 'E')))
                    {
                        integer = false;
                        _at++;
                    }
                    else
                    {
                        break;
                    }
                }

                string number = _text.Substring(start, _at - start);
                if (_at == digits || !(_text[digits] >= '0' && _text[digits] <= '9'))
                {
                    _at = start;
                    throw Fail($"\"{number}\" is not a number");
                }

                if (integer && long.TryParse(number, NumberStyles.AllowLeadingSign, Invariant, out long l))
                {
                    return SaveNode.Of(l);
                }

                if (!double.TryParse(number, NumberStyles.Float, Invariant, out double d))
                {
                    _at = start;
                    throw Fail($"\"{number}\" is not a number");
                }

                if (d == 0 && number[0] == '-')
                {
                    d = NegativeZero; // Mono reads "-0.0" as +0
                }

                if (double.IsInfinity(d))
                {
                    Note($"{number} is out of range, read as null");
                    return SaveNode.Null();
                }

                return SaveNode.Of(d);
            }

            private SaveNode NotANumber(string word)
            {
                Word(word);
                Note($"{word} read as null");
                return SaveNode.Null();
            }

            private void Note(string what) => _notes?.Add($"{JoinPath(_path)}: {what}");

            private void Word(string word)
            {
                if (string.CompareOrdinal(_text, _at, word, 0, word.Length) != 0)
                {
                    throw Fail($"'{_text[_at]}' where a value was expected");
                }

                _at += word.Length;
            }

            private void Expect(char c)
            {
                if (Peek() != c)
                {
                    throw Fail($"'{c}' expected");
                }

                _at++;
            }

            private char Peek() => _at < _text.Length ? _text[_at] : '\0';

            private void SkipSpace()
            {
                while (_at < _text.Length)
                {
                    char c = _text[_at];
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                    {
                        return;
                    }

                    _at++;
                }
            }

            private FormatException Fail(string what)
            {
                int line = 1;
                int col = 1;
                for (int i = 0; i < _at && i < _text.Length; i++)
                {
                    if (_text[i] == '\n')
                    {
                        line++;
                        col = 1;
                    }
                    else
                    {
                        col++;
                    }
                }

                return new FormatException($"line {line}, col {col}: {what}");
            }
        }
    }
}
