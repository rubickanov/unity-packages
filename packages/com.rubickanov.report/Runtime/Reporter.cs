using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Rubickanov.Log;
using Sentry;
using Sentry.Protocol;
using Sentry.Unity;
using UnityEngine;
using UnitySdk = Sentry.Unity.SentrySdk;

namespace Rubickanov.Report
{
    /// <summary>
    /// The face of bug reports over the Sentry Unity SDK: starts and stops it with the game's values, lets each kind of
    /// error through once a session (<see cref="OnceASession"/>) with the end of the log attached, tells the game what
    /// broke (<see cref="Broke"/>, on the main thread from <see cref="Tick"/>), sends feedback and the frame rate.
    /// Errors, exceptions and hangs reach the SDK by themselves, through Unity's log. One per app, the game's.
    /// </summary>
    public sealed class Reporter : IDisposable
    {
        /// <summary>The note of a feedback sent without one: the SDK takes no empty feedback.</summary>
        public const string NoNote = "(no note)";

        private static readonly LogChannel Log = LogChannel.Get("Report");

        private readonly ReportConfig _config;
        private readonly OnceASession _once;
        private readonly ConcurrentQueue<ReportProblem> _broke = new();
        private readonly Dictionary<string, string> _tags = new();
        private readonly object _lock = new();

        // Read on the main thread in Start: BeforeSend runs on the SDK's threads too, where Unity's API throws.
        private string _logPath;
        private PathScrub _scrub = PathScrub.None;
        private string _gpu;
        private string _os;

        private ReportProblem? _last;
        private volatile bool _on;
        private volatile bool _busy;
        private volatile bool _stopped;
        private bool _warnedCap;
        private bool _toldNoDsn;

        public Reporter(ReportConfig config)
        {
            _config = config ? config : throw new ArgumentNullException(nameof(config));
            _once = new OnceASession(config.PerSessionCap, config.FeedbackCap);
            Frames = new FrameSampler(config.MetricsInterval, Attributes);
            Application.quitting += OnQuitting;
        }

        /// <summary>Something broke and went to Sentry: a new kind this session. On the main thread.</summary>
        public event Action<ReportProblem> Broke;

        /// <summary>Whether reports are on: started and not stopped.</summary>
        public bool IsOn => _on;

        /// <summary>What it was last started with.</summary>
        public ReportStart Current { get; private set; }

        /// <summary>The last problem of any kind that went this session; a feedback is linked to it.</summary>
        public ReportProblem? LastProblem
        {
            get
            {
                lock (_lock)
                {
                    return _last;
                }
            }
        }

        /// <summary>The kinds of errors let through and the session's cap.</summary>
        public OnceASession Once => _once;

        /// <summary>The frame rate, fed by the game every frame.</summary>
        public FrameSampler Frames { get; }

        /// <summary>Starts the SDK with the game's values; nothing when already on.</summary>
        public bool Start(ReportStart start)
        {
            if (_on)
            {
                return true;
            }

            Cache();
            SentryUnityOptions options = ScriptableSentryUnityOptions.LoadSentryUnityOptions();
            if (options == null)
            {
                Log.Warn("No Sentry options (Resources/Sentry/SentryOptions): reports stay off");
                return false;
            }

            if (string.IsNullOrWhiteSpace(options.Dsn))
            {
                if (!_toldNoDsn)
                {
                    _toldNoDsn = true;
                    Log.Info("No Sentry DSN in the options: reports stay off");
                }

                return false;
            }

            Current = start;
            Apply(options, start);
            _stopped = false;
            UnitySdk.Init(options);
            if (!UnitySdk.IsEnabled)
            {
                Log.Warn("The Sentry SDK did not start: reports stay off");
                return false;
            }

            _on = true;
            UnitySdk.ConfigureScope(scope => scope.User = new SentryUser { Id = start.UserId });
            foreach (KeyValuePair<string, string> tag in start.Tags)
            {
                _tags[tag.Key] = tag.Value;
            }

            foreach (KeyValuePair<string, string> tag in _tags)
            {
                UnitySdk.SetTag(tag.Key, tag.Value);
            }

            Log.Info($"On: {start.Environment}, {start.Release}, user {start.UserId}, cache {start.CacheDir}");
            return true;
        }

        /// <summary>Sends the frame rate so far and closes the SDK: nothing more goes until the next start.</summary>
        public void Stop()
        {
            if (!_on)
            {
                return;
            }

            Frames.Emit();
            _on = false;
            _stopped = true;
            UnitySdk.Close();
            Log.Info("Off");
        }

        /// <summary>Hands the game what broke since the last tick. Main thread.</summary>
        public void Tick()
        {
            while (_broke.TryDequeue(out ReportProblem problem))
            {
                Broke?.Invoke(problem);
            }
        }

        /// <summary>A tag on every event and metric from now on, kept across stops.</summary>
        public void Tag(string key, string value)
        {
            if (_tags.TryGetValue(key, out string was) && was == value)
            {
                return;
            }

            _tags[key] = value;
            if (_on)
            {
                UnitySdk.SetTag(key, value);
            }
        }

        /// <summary>A message of level info, a kind of its own each call: proof that reports reach Sentry.</summary>
        public SentryId Test(string text) => _on ? UnitySdk.CaptureMessage(text, SentryLevel.Info) : SentryId.Empty;

        /// <summary>The main thread is rightly busy (a scene loads): a hang now is no problem.</summary>
        public void Busy(bool busy) => _busy = busy;

        /// <summary>
        /// Sends a feedback: the note (or <see cref="NoNote"/>), the shot and the end of the log, linked to
        /// <see cref="LastProblem"/>; waits for it to go and says whether it went.
        /// </summary>
        public async Task<(SentryId Id, SendResult Result)> SendFeedback(string note, byte[] shot)
        {
            if (!_on)
            {
                return (SentryId.Empty, SendResult.Off);
            }

            if (!_once.TakeFeedback())
            {
                Log.Warn($"Feedback cap of {_once.FeedbackCap} this session reached");
                return (SentryId.Empty, SendResult.Refused);
            }

            SentryHint hint = FeedbackHint(shot);
            Log.Info($"Feedback with {string.Join(", ", NamesOf(hint))}");
            SentryFeedback feedback = FeedbackOf(note);
            SentryId id = UnitySdk.CaptureFeedback(feedback, out CaptureFeedbackResult result, (Scope)null, hint);
            if (result != CaptureFeedbackResult.Success)
            {
                Log.Warn($"Feedback not taken: {result}");
                return (id, SendResult.Refused);
            }

            await UnitySdk.FlushAsync(TimeSpan.FromSeconds(_config.FlushSeconds));
            SendResult sent = ReportCache.Holds(Current?.CacheDir, id) ? SendResult.Queued : SendResult.Sent;
            Log.Info($"Feedback {id}: {sent}{(feedback.AssociatedEventId is SentryId linked ? $", on {linked}" : string.Empty)}");
            return (id, sent);
        }

        /// <summary>
        /// Tries a queued feedback <paramref name="id"/> again: the SDK sends what waits in its cache, then whether this
        /// one is still there says whether it went.
        /// </summary>
        public async Task<SendResult> Resend(SentryId id)
        {
            if (!_on)
            {
                return SendResult.Off;
            }

            await UnitySdk.FlushAsync(TimeSpan.FromSeconds(_config.FlushSeconds));
            SendResult sent = ReportCache.Holds(Current?.CacheDir, id) ? SendResult.Queued : SendResult.Sent;
            Log.Info($"Feedback {id} again: {sent}");
            return sent;
        }

        /// <summary>What a feedback says now: the note and the problem it is linked to.</summary>
        internal SentryFeedback FeedbackOf(string note)
        {
            string message = string.IsNullOrWhiteSpace(note) ? NoNote : note.Trim();
            return new SentryFeedback(message, null, null, null, null, LastProblem?.Id);
        }

        /// <summary>Lines for the console: on or off, who, which build, the session's counts, the cache.</summary>
        public string Status()
        {
            var text = new StringBuilder();
            ReportStart start = Current;
            text.AppendLine($"on {_on}, sdk {UnitySdk.IsEnabled}, environment {start?.Environment ?? "-"}, release {start?.Release ?? "-"}");
            text.AppendLine($"user {start?.UserId ?? "-"}, kinds {_once.Count}/{_once.Cap}, frame points {Frames.Sent}");
            text.AppendLine($"last {(LastProblem?.ToString() ?? "none")}");
            text.AppendLine($"tags {string.Join(", ", _tags)}");
            text.Append($"cache {start?.CacheDir ?? "-"} ({ReportCache.Count(start?.CacheDir)} waiting)");
            return text.ToString();
        }

        public void Dispose()
        {
            Application.quitting -= OnQuitting;
            Stop();
        }

        /// <summary>
        /// The SDK's last look at an error before it goes, on any thread: drops a hang while busy, a kind already sent
        /// this session and kinds over the cap; attaches the end of the log; remembers what went. Never throws: the
        /// SDK would send what it holds as it is.
        /// </summary>
        internal SentryEvent BeforeSend(SentryEvent evt, SentryHint hint)
        {
            string print;
            ProblemKind kind;
            try
            {
                (print, kind) = Fingerprint.Of(evt);
            }
            catch (Exception)
            {
                (print, kind) = ("unknown", ProblemKind.Error);
            }

            // Stopped (a hang noticed while closing): nothing goes. A hang while busy is no problem.
            if (_stopped || kind == ProblemKind.Hang && _busy)
            {
                return null;
            }

            switch (_once.Check(print))
            {
                case OnceASession.Verdict.Seen:
                    return null;
                case OnceASession.Verdict.OverCap:
                    if (!_warnedCap)
                    {
                        _warnedCap = true;
                        Log.Warn($"Cap of {_once.Cap} kinds of errors this session reached: no more go");
                    }

                    return null;
            }

            string title = print;
            try
            {
                Anonymise(evt);
                if (kind == ProblemKind.Error && evt.Level is not (SentryLevel.Info or SentryLevel.Debug))
                {
                    // An error line: titled without the logger's tag, grouped in Sentry as it is deduped here.
                    evt.Message = new SentryMessage { Message = Fingerprint.Clean(evt.Message?.Formatted ?? evt.Message?.Message) };
                    evt.Fingerprint = new[] { _scrub.Apply(print) };
                }

                title = Fingerprint.TitleOf(evt);
                if (hint != null)
                {
                    AttachLog(hint);
                }
            }
            catch (Exception)
            {
                // The event still goes, without what failed.
            }

            // A message sent on purpose (report test) goes, but is no problem to tell or to link a feedback to.
            if (evt.Level is SentryLevel.Info or SentryLevel.Debug)
            {
                return evt;
            }

            var problem = new ReportProblem(evt.EventId, kind, title);
            lock (_lock)
            {
                _last = problem;
            }

            _broke.Enqueue(problem);
            return evt;
        }

        private SentryEvent BeforeSendFeedback(SentryEvent evt, SentryHint hint)
        {
            try
            {
                Anonymise(evt);
            }
            catch (Exception)
            {
                // The feedback still goes.
            }

            return evt;
        }

        // The machine's name: .NET's server name and the device context's name (the host name). The player's folders
        // (the user name in them) out of the event's message and exception texts.
        private void Anonymise(SentryEvent evt)
        {
            evt.ServerName = null;
            evt.Contexts.Device.Name = null;
            if (evt.Message != null)
            {
                evt.Message.Message = _scrub.Apply(evt.Message.Message);
                evt.Message.Formatted = _scrub.Apply(evt.Message.Formatted);
            }

            if (evt.SentryExceptions != null)
            {
                foreach (SentryException exception in evt.SentryExceptions)
                {
                    exception.Value = _scrub.Apply(exception.Value);
                }
            }
        }

        // Every log line is a breadcrumb: the player's folders out of it as out of the log tail. A crumb comes here as it
        // is made, so the copy's own time is the line's.
        private Breadcrumb BeforeBreadcrumb(Breadcrumb crumb, SentryHint hint)
        {
            string message = _scrub.Apply(crumb.Message);
            return message == crumb.Message
                ? crumb
                : new Breadcrumb(message, crumb.Type, crumb.Data, crumb.Category, crumb.Level);
        }

        // IL2CPP checks for null in code, so breakpad's SIGSEGV handler never meets a managed null dereference.
#if ENABLE_IL2CPP
        private const bool Il2Cpp = true;
#else
        private const bool Il2Cpp = false;
#endif

        private void Apply(SentryUnityOptions options, ReportStart start)
        {
            bool verbose = start.Environment == ReportEnvironment.Agent || Application.isEditor;
            options.Enabled = true;
            options.CaptureInEditor = true;
            options.Environment = start.Environment;
            options.Release = start.Release;
            options.SendDefaultPii = false;
            options.IsEnvironmentUser = false;
            options.ServerName = string.Empty;
            // Without it the SDK's user is SystemInfo.deviceUniqueIdentifier, a hardware fingerprint; it is internal.
            typeof(SentryUnityOptions).GetProperty("DefaultUserId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.SetValue(options, start.UserId);
            options.AttachStacktrace = true;
            options.AttachScreenshot = false;
            options.AttachViewHierarchy = false;
            options.AutoSessionTracking = true;
            options.TracesSampleRate = 0;
            options.AutoFrameMetrics = false;
            options.AutoMemoryMetrics = false;
            options.AutoGcMetrics = false;
            options.AutoNetworkMetrics = false;
            options.AnrTimeout = TimeSpan.FromSeconds(_config.HangSeconds);
            if (!start.LinuxNative && !Il2Cpp)
            {
                options.LinuxNativeSupportEnabled = false;
            }

            // The machine's name goes to the native crash handler's scope once at start, out of BeforeSend's reach.
            typeof(SentryUnityOptions).Assembly.GetType("Sentry.Unity.MainThreadData")
                ?.GetProperty("DeviceName", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            if (!string.IsNullOrEmpty(start.CacheDir))
            {
                options.CacheDirectoryPath = start.CacheDir;
            }

            if (!string.IsNullOrEmpty(start.Proxy))
            {
                options.HttpProxy = new WebProxy("http://" + start.Proxy);
            }

            options.Debug = verbose;
            options.DiagnosticLevel = verbose ? SentryLevel.Debug : SentryLevel.Warning;
            options.SetBeforeSend(BeforeSend);
            options.SetBeforeBreadcrumb(BeforeBreadcrumb);
            options.SetBeforeSendFeedback(BeforeSendFeedback);
        }

        private void Cache()
        {
            _logPath = Application.consoleLogPath;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _scrub = new PathScrub(Application.persistentDataPath, Application.dataPath, home);
            _gpu = SystemInfo.graphicsDeviceName;
            _os = SystemInfo.operatingSystem;
        }

        /// <summary>
        /// Files a feedback carries besides the shot and the log, each asked as the feedback goes: a name, its bytes and
        /// its type, or null for nothing now.
        /// </summary>
        public List<Func<FeedbackFile?>> FeedbackFiles { get; } = new();

        /// <summary>The names of the files a feedback would carry now, sent or not (a dry send).</summary>
        public List<string> FeedbackNames(byte[] shot) => NamesOf(FeedbackHint(shot));

        private SentryHint FeedbackHint(byte[] shot)
        {
            var hint = new SentryHint();
            if (shot != null)
            {
                hint.AddAttachment(shot, "screenshot.jpg", AttachmentType.Default, "image/jpeg");
            }

            AttachLog(hint);
            foreach (Func<FeedbackFile?> source in FeedbackFiles)
            {
                FeedbackFile? file;
                try
                {
                    file = source();
                }
                catch (Exception e)
                {
                    Log.Warn($"A feedback file failed: {e.Message}");
                    continue;
                }

                if (file is FeedbackFile f && f.Data != null)
                {
                    hint.AddAttachment(f.Data, f.Name, AttachmentType.Default, f.Type);
                }
            }

            return hint;
        }

        private static List<string> NamesOf(SentryHint hint)
        {
            var names = new List<string>();
            foreach (SentryAttachment each in hint.Attachments)
            {
                names.Add(each.FileName);
            }

            return names;
        }

        private void AttachLog(SentryHint hint)
        {
            string tail = LogTail.Read(_logPath, _config.LogTailBytes, _scrub);
            if (tail != null)
            {
                hint.AddAttachment(Encoding.UTF8.GetBytes(tail), "log.txt", AttachmentType.Default, "text/plain");
            }
        }

        private List<KeyValuePair<string, object>> Attributes()
        {
            var attributes = new List<KeyValuePair<string, object>>
            {
                new("gpu", _gpu ?? SystemInfo.graphicsDeviceName),
                new("os", _os ?? SystemInfo.operatingSystem),
            };
            foreach (string key in new[] { "scene", "players", "role", "build" })
            {
                if (_tags.TryGetValue(key, out string value))
                {
                    attributes.Add(new KeyValuePair<string, object>(key, value));
                }
            }

            return attributes;
        }

        private void OnQuitting()
        {
            if (_on)
            {
                Frames.Emit();
            }
        }
    }

    /// <summary>A file a feedback carries (<see cref="Reporter.FeedbackFiles"/>).</summary>
    public readonly struct FeedbackFile
    {
        public FeedbackFile(string name, byte[] data, string type)
        {
            Name = name;
            Data = data;
            Type = type;
        }

        public string Name { get; }
        public byte[] Data { get; }
        public string Type { get; }
    }
}
