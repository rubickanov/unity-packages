using System.Linq;
using Sentry;
using Sentry.Protocol;

namespace Rubickanov.Report.Tests
{
    // Events as the SDK hands them to BeforeSend.
    internal static class ReportTestEvents
    {
        public static SentryEvent Message(string text) => new() { Message = new SentryMessage { Message = text } };

        // Frames from the outermost call to the break, each "Module.Function".
        public static SentryEvent Exception(string type, string value, params string[] frames)
        {
            var trace = new SentryStackTrace();
            foreach (string frame in frames)
            {
                int dot = frame.LastIndexOf('.');
                trace.Frames.Add(new SentryStackFrame { Module = frame.Substring(0, dot), Function = frame.Substring(dot + 1) });
            }

            return new SentryEvent
            {
                SentryExceptions = new[] { new SentryException { Type = type, Value = value, Stacktrace = trace } }.ToList(),
            };
        }
    }
}
