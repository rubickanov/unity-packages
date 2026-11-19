using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sentry;
using Sentry.Protocol;
using Sentry.Unity.Integrations;

namespace Rubickanov.Report
{
    /// <summary>
    /// What makes two errors the same kind for <see cref="OnceASession"/>: an exception's type and the first frame of
    /// the game's own code; an error line's text with its numbers, hex and ids folded; a hang is one kind.
    /// </summary>
    public static class Fingerprint
    {
        /// <summary>A hang's fingerprint.</summary>
        public const string Hang = "anr";

        private const int MaxLength = 200;
        private const int MaxTitle = 120;

        // Frames of these are the logger, the SDK and the engine, never where the game went wrong.
        private static readonly string[] NotOurs = { "Rubickanov.Log", "Sentry", "UnityEngine", "UnityEditor", "System", "Mono.", "mscorlib" };

        // The channel's tag in the Editor's Console: <b><color=#RRGGBB>[Name]</color></b>.
        private static readonly Regex EditorTag = new(@"^<b><color=#[0-9A-Fa-f]{6}>\[[^\]]*\]</color></b>\s*", RegexOptions.Compiled);

        // A Player.log line: time, frame, level letter, channel.
        private static readonly Regex PlayerTag = new(@"^\d{2}:\d{2}:\d{2}\.\d{3} f\S*\s+[VIWE] \S+\s+", RegexOptions.Compiled);

        // Who was speaking: [Host], [Client 2].
        private static readonly Regex Speaker = new(@"^\[[^\]]{1,32}\]\s+", RegexOptions.Compiled);

        private static readonly Regex Ids = new(
            @"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}|\b[0-9A-Fa-f]{16,}\b|0x[0-9A-Fa-f]+|\d+(?:[.,]\d+)*",
            RegexOptions.Compiled);

        /// <summary>The event's fingerprint and kind.</summary>
        public static (string Print, ProblemKind Kind) Of(SentryEvent evt)
        {
            if (evt.Exception is ApplicationNotRespondingException)
            {
                return (Hang, ProblemKind.Hang);
            }

            SentryException last = evt.SentryExceptions?.LastOrDefault();
            if (last != null)
            {
                if (last.Type != null && last.Type.EndsWith(nameof(ApplicationNotRespondingException)))
                {
                    return (Hang, ProblemKind.Hang);
                }

                return (Cut($"{last.Type}|{OurFrame(last.Stacktrace?.Frames) ?? Fold(Clean(last.Value))}"), ProblemKind.Exception);
            }

            if (evt.Exception != null)
            {
                string type = evt.Exception.GetType().FullName;
                return (Cut($"{type}|{OurFrame(evt.Exception) ?? Fold(Clean(evt.Exception.Message))}"), ProblemKind.Exception);
            }

            // Info and below never come from the log (those lines are breadcrumbs): a message sent on purpose.
            if (evt.Level is SentryLevel.Info or SentryLevel.Debug)
            {
                return ($"message|{evt.EventId}", ProblemKind.Error);
            }

            return (Cut(Fold(Clean(MessageOf(evt)))), ProblemKind.Error);
        }

        /// <summary>A short title for the event: the exception's type and message, or the error line.</summary>
        public static string TitleOf(SentryEvent evt)
        {
            SentryException last = evt.SentryExceptions?.LastOrDefault();
            string title = last != null ? $"{ShortType(last.Type)}: {last.Value}"
                : evt.Exception != null ? $"{evt.Exception.GetType().Name}: {evt.Exception.Message}"
                : Clean(MessageOf(evt));
            title = (title ?? string.Empty).Split('\n')[0].Trim();
            return title.Length > MaxTitle ? title.Substring(0, MaxTitle) : title;
        }

        /// <summary>A log line without the logger's channel tag and speaker in front.</summary>
        public static string Clean(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            string text = EditorTag.Replace(message, string.Empty, 1);
            text = PlayerTag.Replace(text, string.Empty, 1);
            return Speaker.Replace(text, string.Empty, 1).Trim();
        }

        /// <summary>Numbers, hex and ids folded into <c>#</c>.</summary>
        public static string Fold(string text) => Ids.Replace(text ?? string.Empty, "#");

        private static string MessageOf(SentryEvent evt) => evt.Message?.Formatted ?? evt.Message?.Message ?? string.Empty;

        // Frames run from the outermost call to where it broke; the game's own nearest to the break wins.
        private static string OurFrame(IList<SentryStackFrame> frames)
        {
            if (frames == null)
            {
                return null;
            }

            for (int i = frames.Count - 1; i >= 0; i--)
            {
                SentryStackFrame frame = frames[i];
                string module = frame.Module ?? string.Empty;
                if (module.Length > 0 && !IsNotOurs(module))
                {
                    return $"{module}.{frame.Function}";
                }
            }

            return null;
        }

        private static string OurFrame(System.Exception exception)
        {
            var trace = new System.Diagnostics.StackTrace(exception, false);
            for (int i = 0; i < trace.FrameCount; i++)
            {
                System.Reflection.MethodBase method = trace.GetFrame(i)?.GetMethod();
                string module = method?.DeclaringType?.FullName;
                if (module != null && !IsNotOurs(module))
                {
                    return $"{module}.{method.Name}";
                }
            }

            return null;
        }

        private static bool IsNotOurs(string module) => NotOurs.Any(prefix => module.StartsWith(prefix, System.StringComparison.Ordinal));

        private static string ShortType(string type)
        {
            if (string.IsNullOrEmpty(type))
            {
                return "Exception";
            }

            int dot = type.LastIndexOf('.');
            return dot >= 0 ? type.Substring(dot + 1) : type;
        }

        private static string Cut(string text) => text.Length > MaxLength ? text.Substring(0, MaxLength) : text;
    }
}
