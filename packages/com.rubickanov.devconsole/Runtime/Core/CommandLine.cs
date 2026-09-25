using System.Collections.Generic;
using System.Text;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// One tokenized command line that remembers where each token came from in the raw text. The positions are what
    /// lets a <see cref="RemainderAttribute"/> parameter and an alias receive the rest of the line as it was typed,
    /// quotes included, instead of tokens glued back together with spaces.
    /// </summary>
    internal sealed class CommandLine
    {
        public readonly string Raw;
        public readonly List<string> Tokens = new();

        // Start and end (exclusive) of each token in Raw, quotes included
        private readonly List<int> _starts = new();
        private readonly List<int> _ends = new();

        public int Count => Tokens.Count;

        public CommandLine(string raw)
        {
            Raw = raw;
            Tokenize(raw, Tokens, _starts, _ends);
        }

        /// <summary>Where token <paramref name="index"/> starts in <see cref="Raw"/>.</summary>
        public int Start(int index) => _starts[index];

        /// <summary>Where token <paramref name="index"/> ends (exclusive) in <see cref="Raw"/>.</summary>
        public int End(int index) => _ends[index];

        /// <summary>Token <paramref name="index"/> as typed, quotes included.</summary>
        public string RawToken(int index) => Raw.Substring(_starts[index], _ends[index] - _starts[index]);

        /// <summary>Everything from token <paramref name="index"/> to the end as typed, or "" past the last token.</summary>
        public string RawTail(int index)
        {
            if (index >= Tokens.Count) return "";
            return Raw.Substring(_starts[index], _ends[Tokens.Count - 1] - _starts[index]);
        }

        /// <summary>
        /// The value of a remainder parameter starting at token <paramref name="index"/>: a lone token is taken
        /// unquoted, so the older <c>bind F5 "timescale 0.5"</c> form keeps working, and several tokens are taken as
        /// typed, so quotes inside a bound command survive.
        /// </summary>
        public string Remainder(int index)
        {
            if (index >= Tokens.Count) return "";
            return index == Tokens.Count - 1 ? Tokens[index] : RawTail(index);
        }

        /// <summary>
        /// Splits a line into the commands separated by <c>;</c> outside quotes, trimmed, empty ones dropped.
        /// </summary>
        public static void SplitStatements(string input, List<string> statements)
        {
            var inQuotes = false;
            var start = 0;
            for (int i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (c == '"') inQuotes = !inQuotes;
                else if (c == ';' && !inQuotes)
                {
                    Add(start, i);
                    start = i + 1;
                }
            }

            Add(start, input.Length);

            void Add(int from, int to)
            {
                var statement = input.Substring(from, to - from).Trim();
                if (statement.Length > 0) statements.Add(statement);
            }
        }

        /// <summary>Where the last <c>;</c>-separated command of <paramref name="input"/> begins.</summary>
        public static int LastStatementStart(string input)
        {
            var inQuotes = false;
            var start = 0;
            for (int i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (c == '"') inQuotes = !inQuotes;
                else if (c == ';' && !inQuotes) start = i + 1;
            }

            return start;
        }

        /// <summary>Splits input into tokens on spaces, a quoted part being one token without its quotes.</summary>
        public static void Tokenize(string input, List<string> tokens, List<int>? starts = null, List<int>? ends = null)
        {
            var current = new StringBuilder();
            var inQuotes = false;
            var inToken = false;
            var start = 0;

            for (int i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (c == ' ' && !inQuotes)
                {
                    if (inToken) Flush(i);
                    continue;
                }

                if (!inToken)
                {
                    inToken = true;
                    start = i;
                }

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                current.Append(c);
            }

            if (inToken) Flush(input.Length);

            void Flush(int end)
            {
                // A pair of quotes with nothing between them is not an argument, as before positions were kept
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    starts?.Add(start);
                    ends?.Add(end);
                }

                current.Clear();
                inToken = false;
            }
        }
    }
}
