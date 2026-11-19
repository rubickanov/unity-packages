using System;
using System.IO;
using Sentry;

namespace Rubickanov.Report
{
    /// <summary>
    /// The SDK's envelopes waiting on disk (offline caching): one still there after a flush did not go and goes on a
    /// later start. An envelope's first line is its header, with the event's id.
    /// </summary>
    public static class ReportCache
    {
        /// <summary>Whether an envelope of this event waits in <paramref name="cacheDir"/>.</summary>
        public static bool Holds(string cacheDir, SentryId id)
        {
            string hex = id.ToString();
            foreach (string file in Envelopes(cacheDir))
            {
                string header = FirstLine(file);
                if (header != null && header.IndexOf(hex, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>How many envelopes wait.</summary>
        public static int Count(string cacheDir)
        {
            int count = 0;
            foreach (string _ in Envelopes(cacheDir))
            {
                count++;
            }

            return count;
        }

        private static string[] Envelopes(string cacheDir)
        {
            if (string.IsNullOrEmpty(cacheDir) || !Directory.Exists(cacheDir))
            {
                return Array.Empty<string>();
            }

            try
            {
                return Directory.GetFiles(cacheDir, "*.envelope", SearchOption.AllDirectories);
            }
            catch (IOException)
            {
                return Array.Empty<string>();
            }
        }

        private static string FirstLine(string path)
        {
            try
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(file);
                return reader.ReadLine();
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
