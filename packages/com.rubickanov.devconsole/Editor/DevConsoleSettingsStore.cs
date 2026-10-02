#nullable enable
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.DevConsole.Editor
{
    /// <summary>
    /// Writes <see cref="DevConsoleSettings"/> to the JSON file in <c>Resources</c> that builds carry, and moves a
    /// settings file of devconsole 3.x out of <c>ProjectSettings</c>, where only the editor could read it.
    /// </summary>
    public static class DevConsoleSettingsStore
    {
        /// <summary>Where the file is created when the project has none yet.</summary>
        public const string DefaultAssetPath = "Assets/Resources/" + DevConsoleSettings.ResourceName + ".json";

        private const string LegacyPath = "ProjectSettings/DevConsoleSettings.json";

        /// <summary>The settings file the project has, in any Resources folder, or null.</summary>
        public static string? FindAssetPath()
        {
            var file = Resources.Load<TextAsset>(DevConsoleSettings.ResourceName);
            return file != null ? AssetDatabase.GetAssetPath(file) : null;
        }

        /// <summary>Writes the settings to the project's file, creating <see cref="DefaultAssetPath"/> if there is none.</summary>
        public static void Save(DevConsoleSettings settings) => Save(settings, FindAssetPath() ?? DefaultAssetPath);

        /// <summary>Writes the settings to <paramref name="assetPath"/>, which must be in a Resources folder to ship.</summary>
        public static void Save(DevConsoleSettings settings, string assetPath) => Write(settings.ToJson(), assetPath);

        private static void Write(string json, string assetPath)
        {
            var folder = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(assetPath, json);
            AssetDatabase.ImportAsset(assetPath);
        }

        // Without this a project upgraded from 3.x would lose its settings in the editor too, since nothing reads the
        // old file any more
        [InitializeOnLoadMethod]
        private static void MoveLegacy()
        {
            if (!File.Exists(LegacyPath)) return;
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(LegacyPath)) return;
                if (FindAssetPath() == null)
                {
                    Write(File.ReadAllText(LegacyPath), DefaultAssetPath);
                    Debug.Log($"[DevConsole] Settings moved from {LegacyPath} to {DefaultAssetPath}, so builds carry them.");
                }

                File.Delete(LegacyPath);
                DevConsoleSettings.ResetStatics();
            };
        }
    }
}
