#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Rubickanov.Save.Data;

namespace Rubickanov.Save.World
{
    /// <summary>
    /// Golden files of a piece kind, as <see cref="SaveFixtures"/> for documents: in a folder per kind, <c>v&lt;N&gt;.json</c> is
    /// one record a build of version N really wrote and <c>v&lt;N&gt;.expected.json</c> what the kind's chain must make of
    /// it; the current version's file must read back as itself.
    /// </summary>
    public static class PieceFixtures
    {
        private static readonly UTF8Encoding Utf8 = new(false);

        /// <summary>The problems found, none when every version migrates as expected; a missing expected file is written and reported.</summary>
        public static IReadOnlyList<string> Check(string folder, PieceKind kind)
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
                else if (n > kind.Version)
                {
                    problems.Add($"{name}: {kind.Kind} is only at v{kind.Version}");
                }
            }

            for (int v = 1; v <= kind.Version; v++)
            {
                string path = Path.Combine(folder, $"v{v}.json");
                if (!File.Exists(path))
                {
                    problems.Add($"v{v}.json: missing; every released version stays as a file");
                    continue;
                }

                if (!SaveJson.TryRead(Text(path), out SaveNode record, out string error) || !record.IsObject)
                {
                    problems.Add($"v{v}.json: {error ?? "not an object"}");
                    continue;
                }

                SaveNode version = record[SavePieces.VersionKey];
                SaveNode kindName = record[SavePieces.KindKey];
                if (version == null || version.Kind != SaveNodeKind.Number || version.Long != v ||
                    kindName == null || kindName.Kind != SaveNodeKind.String || kindName.String != kind.Kind)
                {
                    problems.Add($"v{v}.json: not a {kind.Kind} record of v{v}");
                    continue;
                }

                string written;
                try
                {
                    written = SaveJson.Write(kind.Migrate(record, v), problems.Add);
                }
                catch (System.Exception e)
                {
                    problems.Add($"v{v}.json: its migration failed: {e.Message}");
                    continue;
                }

                string expectedPath = v == kind.Version ? path : Path.Combine(folder, $"v{v}.expected.json");
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
                    problems.Add($"v{v}.json: migrates to\n{written}but {expectedName} is\n{expected}");
                }
            }

            return problems;
        }

        private static string Text(string path) => File.ReadAllText(path, Utf8).Replace("\r\n", "\n").TrimStart('﻿');
    }
}
#endif
