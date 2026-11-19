using System;
using System.Collections.Generic;

namespace Rubickanov.Report
{
    /// <summary>
    /// The player's own folders (home, install, saves), each turned into <c>~</c> in text that leaves the machine:
    /// the user name sits in them. Prepared once, applied to every log line on its way to a report.
    /// </summary>
    public sealed class PathScrub
    {
        public static readonly PathScrub None = new();

        // Each folder in both slashes, the longest first, so a save folder inside home goes whole.
        private readonly string[] _folders;

        public PathScrub(params string[] folders)
        {
            var list = new List<string>();
            if (folders != null)
            {
                foreach (string folder in folders)
                {
                    string trimmed = folder?.TrimEnd('/', '\\');
                    if (trimmed == null || trimmed.Length < 2)
                    {
                        continue;
                    }

                    Add(list, trimmed.Replace('\\', '/'));
                    Add(list, trimmed.Replace('/', '\\'));
                }
            }

            list.Sort((a, b) => b.Length.CompareTo(a.Length));
            _folders = list.ToArray();
        }

        /// <summary><paramref name="text"/> with every folder replaced by <c>~</c>; null stays null.</summary>
        public string Apply(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            foreach (string folder in _folders)
            {
                if (text.IndexOf(folder, StringComparison.Ordinal) >= 0)
                {
                    text = text.Replace(folder, "~");
                }
            }

            return text;
        }

        private static void Add(List<string> list, string folder)
        {
            if (!list.Contains(folder))
            {
                list.Add(folder);
            }
        }
    }
}
