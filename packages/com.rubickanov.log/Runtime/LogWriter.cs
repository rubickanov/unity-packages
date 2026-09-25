using System;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Rubickanov.Log
{
    /// <summary>
    /// Turns a message into a line of Unity's log. In the Editor's Console the channel stands out in its own
    /// colour; the Console has the time already. Player.log, which has neither, gets the time, the frame
    /// and a level letter, plain text for grep, and no stack trace under every Info and Warn.
    /// </summary>
    internal static class LogWriter
    {
        private const int NameWidth = 11;

#if !UNITY_EDITOR
        private static int _mainThread;
#endif

        [HideInCallstack]
        public static void Write(LogChannel channel, LogLevel level, string message, Object context)
        {
            Debug.unityLogger.Log(TypeOf(level), (object)Format(channel, level, message), context);
        }

        /// <summary>As Unity prints it, with a stack trace to click through; the channel only decides whether.</summary>
        [HideInCallstack]
        public static void WriteException(Exception exception, Object context) => Debug.LogException(exception, context);

        private static LogType TypeOf(LogLevel level)
        {
            return level switch
            {
                LogLevel.Warn => LogType.Warning,
                LogLevel.Error => LogType.Error,
                _ => LogType.Log,
            };
        }

        private static string Format(LogChannel channel, LogLevel level, string message)
        {
#if UNITY_EDITOR
            // Brackets inside the colour, so searching the Console for [Course] finds the channel and not the word.
            channel.Tag ??= $"<b><color=#{ColorOf(channel.Name)}>[{channel.Name}]</color></b> ";
            return level == LogLevel.Verbose
                ? $"{channel.Tag}<color=#8C8C8C>{message}</color>"
                : $"{channel.Tag}{message}";
#else
            // Time.frameCount is for the main thread only.
            string frame = Thread.CurrentThread.ManagedThreadId == _mainThread ? Time.frameCount.ToString() : "-";
            return $"{DateTime.Now:HH:mm:ss.fff} f{frame,-6} {LetterOf(level)} {channel.Name.PadRight(NameWidth)}{message}";
#endif
        }

        private static char LetterOf(LogLevel level)
        {
            return level switch
            {
                LogLevel.Verbose => 'V',
                LogLevel.Warn => 'W',
                LogLevel.Error => 'E',
                _ => 'I',
            };
        }

#if UNITY_EDITOR
        // The same colour for a channel every time, from its name, light on the dark skin and dark on the light.
        private static string ColorOf(string name)
        {
            uint hash = 2166136261;
            foreach (char c in name)
            {
                hash = (hash ^ char.ToLowerInvariant(c)) * 16777619;
            }

            bool dark = UnityEditor.EditorGUIUtility.isProSkin;
            Color color = Color.HSVToRGB(hash % 360 / 360f, dark ? 0.45f : 0.8f, dark ? 0.95f : 0.6f);
            return ColorUtility.ToHtmlStringRGB(color);
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
#if !UNITY_EDITOR
            _mainThread = Thread.CurrentThread.ManagedThreadId;

            // Every Debug.Log in a build writes where it was called from into Player.log: slow to capture
            // and most of the file. Errors and exceptions keep theirs.
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
#endif
        }
    }
}
