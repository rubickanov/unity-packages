using System;
using UnityEngine;

namespace Rubickanov.DevConsole.Commands
{
    internal static class RenderingCommands
    {
        [ConsoleCommand("resolution", "Get or set screen resolution", "Rendering")]
        public static void Resolution(int? width = null, int? height = null, FullScreenMode? mode = null)
        {
            if (!width.HasValue)
            {
                ConsoleLog.Log($"Resolution: {Screen.width}x{Screen.height} ({Screen.fullScreenMode})");
                return;
            }

            if (!height.HasValue)
                throw new CommandException("Usage: resolution <width> <height> [mode]");
            if (width <= 0 || height <= 0)
                throw new CommandException("Width and height must be positive.");

            var actualMode = mode ?? Screen.fullScreenMode;
            Screen.SetResolution(width.Value, height.Value, actualMode);
            ConsoleLog.Log($"Resolution set to {width}x{height} ({actualMode})");
        }

        [ConsoleCommand("resolution_list", "List the resolutions the display supports", "Rendering")]
        public static void ResolutionList()
        {
            var resolutions = Screen.resolutions;
            if (resolutions.Length == 0)
                throw new CommandException("The display reports no resolutions (windowed or editor).");

            foreach (var r in resolutions)
            {
                var current = r.width == Screen.width && r.height == Screen.height ? " (current)" : "";
                ConsoleLog.Log($"  {r.width}x{r.height} @ {r.refreshRateRatio.value:0.##} Hz{current}");
            }
        }

        [ConsoleCommand("fullscreen", "Get or set fullscreen mode", "Rendering")]
        public static void Fullscreen(FullScreenMode? mode = null)
        {
            if (!mode.HasValue)
            {
                ConsoleLog.Log($"Fullscreen mode: {Screen.fullScreenMode}");
                return;
            }

            Screen.fullScreenMode = mode.Value;
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
