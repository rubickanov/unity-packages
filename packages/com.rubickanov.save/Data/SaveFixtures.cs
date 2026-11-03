#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Rubickanov.Save.Data
{
    /// <summary>
    /// Golden files: every released version of a document stays as a file forever and must still read. In a folder per
    /// format, <c>v&lt;N&gt;.json</c> is the text a build of version N really wrote and <c>v&lt;N&gt;.expected.json</c> what it
    /// must become at the current version (written with build <see cref="Build"/>); the current version's file must
    /// come out as itself, byte for byte.
    /// </summary>
    public static class SaveFixtures
    {
        /// <summary>The build written into expected files, so no build changes them.</summary>
        public const string Build = "fixture";

        private static readonly UTF8Encoding Utf8 = new(false);

        /// <summary>
        /// The problems found, none when every version reads as expected. A missing expected file is written beside its
        /// version and reported, to be reviewed as a diff and committed (an approval test).
        /// </summary>
        public static IReadOnlyList<string> Check(string folder, SaveFormat format)
        {
            var problems = new List<string>();
            if (!Directory.Exists(folder))
            {
                problems.Add($"{folder}: no such folder");
                return problems;
            }

            foreach (string file in Directory.GetFiles(folder, "v*.json"))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".expected.json"))
                {
                    continue;
                }

                string number = name.Substring(1, name.Length - "v.json".Length);
                if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1)
                {
                    problems.Add($"{name}: not v<N>.json");
                }
                else if (n > format.Version)
                {
                    problems.Add($"{name}: {format.Kind} is only at v{format.Version}");
                }
            }

            for (int v = 1; v <= format.Version; v++)
            {
                string path = Path.Combine(folder, $"v{v}.json");
                if (!File.Exists(path))
                {
                    problems.Add($"v{v}.json: missing; every released version stays as a file");
                    continue;
                }

                SaveRead read = format.Read(Text(path));
                if (!read.IsOk)
                {
                    problems.Add($"v{v}.json: {read.Status}: {read.Error}");
                    continue;
                }

                if (read.From != v)
                {
                    problems.Add($"v{v}.json: its header says v{read.From}");
                    continue;
                }

                var errors = new List<string>();
                bool current = v == format.Version;
                string written = format.Write(read.Data, current ? read.Build : Build, errors.Add);
                for (int i = 0; i < errors.Count; i++)
                {
                    problems.Add($"v{v}.json: {errors[i]}");
                }

                string expectedPath = current ? path : Path.Combine(folder, $"v{v}.expected.json");
                string expectedName = Path.GetFileName(expectedPath);
                if (!File.Exists(expectedPath))
                {
                    File.WriteAllText(expectedPath, written, Utf8);
                    problems.Add($"{expectedName}: written, review the diff and commit");
                    continue;
                }

                string expected = Text(expectedPath);
                if (expected != written)
                {
                    problems.Add($"v{v}.json: reads as\n{written}but {expectedName} is\n{expected}");
                }
            }

            return problems;
        }

        // A checkout may turn '\n' into "\r\n" and add a byte order mark; neither is the document's.
        private static string Text(string path) => File.ReadAllText(path, Utf8).Replace("\r\n", "\n").TrimStart('﻿');
    }
}
#endif
