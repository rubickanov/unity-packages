using System;
using System.IO;
using UnityEditor;

namespace Rubickanov.Codegen.Editor
{
    /// <summary>
    /// Writes generated source to disk idempotently: the directory is created on demand and the
    /// file is only rewritten (and reimported) when its content actually changed, so a no-op
    /// regeneration does not trigger a needless script recompile.
    /// </summary>
    public static class GeneratedFileWriter
    {
        /// <summary>
        /// Writes <paramref name="content"/> to <paramref name="assetPath"/>. Returns true if the
        /// file was created or its content changed, false if it was already up to date and left
        /// untouched. Paths under <c>Assets/</c> are reimported so Unity picks up the change.
        /// </summary>
        public static bool Write(string assetPath, string content)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("Output path must be non-empty.", nameof(assetPath));

            var directory = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(assetPath) && File.ReadAllText(assetPath) == content)
                return false;

            File.WriteAllText(assetPath, content);

            // ImportAsset only makes sense for project-relative paths; skip it for scratch paths
            // (e.g. tests writing to a temp directory) to avoid bogus "path is not in Assets" logs.
            if (assetPath.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal))
                AssetDatabase.ImportAsset(assetPath);

            return true;
        }
    }
}
