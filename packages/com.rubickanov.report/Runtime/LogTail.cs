using System;
using System.IO;
using System.Text;

namespace Rubickanov.Report
{
    /// <summary>The end of the game's log as text for a report, with the player's own folders turned into <c>~</c>.</summary>
    public static class LogTail
    {
        /// <summary>
        /// The last <paramref name="bytes"/> of the file at <paramref name="path"/>, from the start of a line, every
        /// one of <paramref name="scrub"/> replaced by <c>~</c>; null when there is no such file.
        /// </summary>
        public static string Read(string path, int bytes, params string[] scrub)
        {
            return Read(path, bytes, new PathScrub(scrub));
        }

        /// <summary>The same with the folders already prepared.</summary>
        public static string Read(string path, int bytes, PathScrub scrub)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long from = Math.Max(0, file.Length - bytes);
                file.Seek(from, SeekOrigin.Begin);
                var buffer = new byte[file.Length - from];
                int read = 0;
                while (read < buffer.Length)
                {
                    int got = file.Read(buffer, read, buffer.Length - read);
                    if (got <= 0)
                    {
                        break;
                    }

                    read += got;
                }

                string text = Encoding.UTF8.GetString(buffer, 0, read);
                if (from > 0)
                {
                    int line = text.IndexOf('\n');
                    text = line >= 0 ? text.Substring(line + 1) : text;
                }

                return (scrub ?? PathScrub.None).Apply(text);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
