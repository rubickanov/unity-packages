using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Rubickanov.Log
{
    /// <summary>
    /// A named stream of log messages, the thing code logs through. Declared where it is used and shared
    /// by name, so two files that ask for "Course" write to the same channel:
    /// <code>private static readonly LogChannel Log = LogChannel.Get("Course");</code>
    /// Everything ends up in Unity's log, so the Console, clicking through to the caller, pinging the context
    /// object and Player.log work as with Debug.Log. Info and Verbose messages are built only when the
    /// channel writes them; Verbose calls are compiled out of release builds altogether.
    /// </summary>
    public sealed class LogChannel
    {
        private static readonly Dictionary<string, LogChannel> ByName = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<LogChannel> Channels = new();

        private LogLevel _level;

        private LogChannel(string name)
        {
            Name = name;
            _level = LogSettings.LevelFor(name);
        }

        public string Name { get; }

        /// <summary>How the Console shows the name, made on the first message.</summary>
        internal string Tag;

        /// <summary>The lowest level this channel writes. Start with <c>-log Course=Verbose</c>, or change it in the console.</summary>
        public LogLevel Level
        {
            get => _level;
            set
            {
                if (_level != value)
                {
                    _level = value;
                    LevelChanged?.Invoke(this);
                }
            }
        }

        /// <summary>For code with a log of its own to follow the channel, such as a third-party library's.</summary>
        public event Action<LogChannel> LevelChanged;

        /// <summary>Every channel so far. A channel appears once the class that declares it is first used.</summary>
        public static IReadOnlyList<LogChannel> All => Channels;

        public static LogChannel Get(string name)
        {
            // Static fields are made on whichever thread first uses their class.
            lock (ByName)
            {
                if (!ByName.TryGetValue(name, out LogChannel channel))
                {
                    channel = new LogChannel(name);
                    ByName.Add(name, channel);
                    Channels.Add(channel);
                }
                return channel;
            }
        }

        public bool Writes(LogLevel level) => level >= Level && level != LogLevel.Off;

        /// <summary>In the Editor and debug builds, or any build with LOG_VERBOSE; silent until the channel is set to Verbose.</summary>
        [HideInCallstack]
        [Conditional("UNITY_EDITOR"), Conditional("DEBUG"), Conditional("LOG_VERBOSE")]
        public void Verbose([InterpolatedStringHandlerArgument("")] ref VerboseMessage message, Object context = null)
        {
            if (message.Enabled)
            {
                LogWriter.Write(this, LogLevel.Verbose, message.ToStringAndClear(), context);
            }
        }

        [HideInCallstack]
        [Conditional("UNITY_EDITOR"), Conditional("DEBUG"), Conditional("LOG_VERBOSE")]
        public void Verbose(string message, Object context = null) => Write(LogLevel.Verbose, message, context);

        [HideInCallstack]
        public void Info([InterpolatedStringHandlerArgument("")] ref InfoMessage message, Object context = null)
        {
            if (message.Enabled)
            {
                LogWriter.Write(this, LogLevel.Info, message.ToStringAndClear(), context);
            }
        }

        [HideInCallstack]
        public void Info(string message, Object context = null) => Write(LogLevel.Info, message, context);

        [HideInCallstack]
        public void Warn(string message, Object context = null) => Write(LogLevel.Warn, message, context);

        [HideInCallstack]
        public void Error(string message, Object context = null) => Write(LogLevel.Error, message, context);

        /// <summary>Logs the exception with its own stack trace, as Debug.LogException does.</summary>
        [HideInCallstack]
        public void Exception(System.Exception exception, Object context = null)
        {
            if (Writes(LogLevel.Error))
            {
                LogWriter.WriteException(exception, context);
            }
        }

        public override string ToString() => Name;

        [HideInCallstack]
        private void Write(LogLevel level, string message, Object context)
        {
            if (Writes(level))
            {
                LogWriter.Write(this, level, message, context);
            }
        }

        // Without a domain reload the last play session's levels would still be set; the channels
        // themselves stay, since the static fields that hold them are not made again.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLevels()
        {
            LogSettings.Load();
            foreach (LogChannel channel in Channels)
            {
                channel.Level = LogSettings.LevelFor(channel.Name);
            }
        }
    }
}
