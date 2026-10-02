using System;
using System.IO;

namespace Rubickanov.DevConsole.Tests
{
    /// <summary>
    /// Points the console config at a fresh temporary folder, with an empty config.cfg so no PlayerPrefs save of the
    /// sandbox is migrated. A registry made after this reads its aliases, bindings and history from there.
    /// </summary>
    internal static class TestConfig
    {
        public static string Use()
        {
            var dir = Path.Combine(Path.GetTempPath(), "devconsole-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ConsoleConfig.ConfigFileName), "");
            ConsoleConfig.DirectoryOverride = dir;
            return dir;
        }

        public static void Release(string dir)
        {
            ConsoleConfig.DirectoryOverride = null;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
