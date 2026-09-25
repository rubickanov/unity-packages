using System;
using System.IO;

namespace Rubickanov.DevConsole.Tests
{
    /// <summary>
    /// Points the console config at a fresh temporary folder, with an empty config.cfg so no PlayerPrefs save of the
    /// sandbox is migrated, and gives the alias and binding registries a clean start.
    /// </summary>
    internal static class TestConfig
    {
        public static string Use()
        {
            var dir = Path.Combine(Path.GetTempPath(), "devconsole-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ConsoleConfig.ConfigFileName), "");
            ConsoleConfig.DirectoryOverride = dir;
            AliasRegistry.ResetStatics();
            BindingRegistry.ResetStatics();
            return dir;
        }

        public static void Release(string dir)
        {
            ConsoleConfig.DirectoryOverride = null;
            AliasRegistry.ResetStatics();
            BindingRegistry.ResetStatics();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
