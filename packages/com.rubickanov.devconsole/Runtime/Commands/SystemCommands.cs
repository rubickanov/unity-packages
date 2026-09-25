using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Rubickanov.DevConsole.Commands
{
    internal static class SystemCommands
    {
        [ConsoleCommand("quit", "Quit the application", "System")]
        public static void Quit()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        [ConsoleCommand("echo", "Print a message to the console", "System")]
        public static void Echo([Remainder] string message) => ConsoleLog.Log(message);

        [ConsoleCommand("sysinfo", "Show application version, Unity version, platform and hardware", "System")]
        public static void Sysinfo()
        {
            ConsoleLog.Log($"Application: {Application.version}");
            ConsoleLog.Log($"Unity: {Application.unityVersion}");
            ConsoleLog.Log($"Platform: {Application.platform}");
            ConsoleLog.Log($"CPU: {SystemInfo.processorType} ({SystemInfo.processorCount} cores)");
            ConsoleLog.Log($"RAM: {SystemInfo.systemMemorySize} MB");
            ConsoleLog.Log($"GPU: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB)");
            ConsoleLog.Log($"OS: {SystemInfo.operatingSystem}");
        }
    }
}
