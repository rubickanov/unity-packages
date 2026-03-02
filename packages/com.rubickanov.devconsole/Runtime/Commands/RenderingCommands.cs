using System;
using UnityEngine;

namespace Rubickanov.DevConsole.Commands
{
    internal static class RenderingCommands
    {
        [ConsoleCommand("resolution", "Get or set screen resolution", "Rendering")]
        public static void Resolution(int width = 0, int height = 0, FullScreenMode mode = (FullScreenMode)(-1))
        {
            if (width == 0 || height == 0)
            {
                ConsoleLog.Log($"Resolution: {Screen.width}x{Screen.height} ({Screen.fullScreenMode})");
                return;
            }

            var actualMode = (int)mode == -1 ? Screen.fullScreenMode : mode;
            Screen.SetResolution(width, height, actualMode);
            ConsoleLog.Log($"Resolution set to {width}x{height} ({actualMode})");
        }

        [ConsoleCommand("fullscreen", "Get or set fullscreen mode", "Rendering")]
        public static void Fullscreen(FullScreenMode mode = (FullScreenMode)(-1))
        {
            if ((int)mode == -1)
            {
                ConsoleLog.Log($"Fullscreen mode: {Screen.fullScreenMode}");
                return;
            }

            Screen.fullScreenMode = mode;
            ConsoleLog.Log($"Fullscreen mode set to {mode}");
        }

        [ConsoleCommand("quality", "Get or set quality level by name or index", "Rendering")]
        [AutoComplete(0, typeof(QualityLevelProvider))]
        public static void Quality(string level = "")
        {
            if (string.IsNullOrEmpty(level))
            {
                var names = QualitySettings.names;
                var current = QualitySettings.GetQualityLevel();
                ConsoleLog.Log($"Quality: {names[current]} (index {current})");
                return;
            }

            // Try parse as index first
            if (int.TryParse(level, out var index))
            {
                var names = QualitySettings.names;
                if (index >= 0 && index < names.Length)
                {
                    QualitySettings.SetQualityLevel(index, true);
                    ConsoleLog.Log($"Quality set to {names[index]} (index {index})");
                }
                else
                {
                    ConsoleLog.LogError($"Quality index {index} out of range (0..{names.Length - 1})");
                }
                return;
            }

            // Match by name (case-insensitive)
            var qualityNames = QualitySettings.names;
            for (int i = 0; i < qualityNames.Length; i++)
            {
                if (string.Equals(qualityNames[i], level, StringComparison.OrdinalIgnoreCase))
                {
                    QualitySettings.SetQualityLevel(i, true);
                    ConsoleLog.Log($"Quality set to {qualityNames[i]} (index {i})");
                    return;
                }
            }

            ConsoleLog.LogError($"Unknown quality level '{level}'. Available: {string.Join(", ", qualityNames)}");
        }
    }
}
