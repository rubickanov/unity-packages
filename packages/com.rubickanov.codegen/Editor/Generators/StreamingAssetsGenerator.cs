using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Rubickanov.Codegen.Editor.Generators
{
    /// <summary>
    /// Emits a string constant for every file under <c>Assets/StreamingAssets/</c>, keyed by its
    /// path relative to the StreamingAssets root (extension kept), to be combined with
    /// <see cref="Application.streamingAssetsPath"/> at load time. Scans the file system directly,
    /// so files without a Unity importer (raw .json, .bytes, ...) are included too.
    /// </summary>
    public sealed class StreamingAssetsGenerator : BuiltInConstantsGenerator
    {
        public const string GeneratorId = "streamingAssets";

        public override string Id => GeneratorId;
        public override string DisplayName => "Streaming Assets";
        protected override string DefaultClassName => "StreamingAssets";

        protected override void Collect(List<ConstMember> rootMembers, List<ConstGroup> groups)
        {
            var root = Application.streamingAssetsPath;
            if (!Directory.Exists(root))
                return;

            var relativePaths = new List<string>();

            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                // Skip Unity sidecars and OS dotfiles (.meta, .DS_Store, ...).
                if (name.StartsWith(".", StringComparison.Ordinal) || name.EndsWith(".meta", StringComparison.Ordinal))
                    continue;

                relativePaths.Add(ToRelativePath(root, file));
            }

            // Deterministic order so unchanged contents produce an identical file.
            relativePaths.Sort(StringComparer.Ordinal);

            foreach (var relative in relativePaths)
                rootMembers.Add(new ConstMember(relative, "string", Str(relative)));
        }

        public override bool HandlesAssetChange(string[] importedAssets, string[] deletedAssets, string[] movedAssets)
            => TouchesStreamingAssets(importedAssets) || TouchesStreamingAssets(deletedAssets) || TouchesStreamingAssets(movedAssets);

        /// <summary>
        /// Returns <paramref name="file"/> relative to <paramref name="root"/> with forward slashes.
        /// Pure string logic — unit-testable without file-system state.
        /// </summary>
        public static string ToRelativePath(string root, string file)
        {
            var normalizedRoot = root.Replace('\\', '/').TrimEnd('/');
            var normalizedFile = file.Replace('\\', '/');

            if (normalizedFile.StartsWith(normalizedRoot + "/", StringComparison.Ordinal))
                return normalizedFile.Substring(normalizedRoot.Length + 1);

            return normalizedFile;
        }

        private static bool TouchesStreamingAssets(string[] paths)
        {
            foreach (var path in paths)
            {
                if (path.StartsWith("Assets/StreamingAssets/", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
