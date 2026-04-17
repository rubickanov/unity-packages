using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.DevConsole
{
    /// <summary>Static log buffer for the dev console. Ring buffer with configurable capacity.</summary>
    public static class ConsoleLog
    {
        public enum LogType { Info, Warning, Error, Success, Input }

        public struct LogEntry
        {
            public string Message;
            public LogType Type;
            public DateTime Timestamp;
        }

        private static readonly LogEntry[] Buffer = new LogEntry[MaxEntries];
        private static int _head;
        private static int _count;
        private const int MaxEntries = 1000;

        /// <summary>Read-only view of all current log entries.</summary>
        public static RingBufferView Entries => new(Buffer, _head, _count);

        /// <summary>Fired when a new entry is added.</summary>
        public static event Action<LogEntry>? OnLogAdded;

        /// <summary>Fired when the log is cleared.</summary>
        public static event Action? OnCleared;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _head = 0;
            _count = 0;
            Array.Clear(Buffer, 0, Buffer.Length);
            OnLogAdded = null;
            OnCleared = null;
        }

        /// <summary>Logs a message to the console.</summary>
        public static void Log(string message, LogType type = LogType.Info)
        {
            var entry = new LogEntry { Message = message, Type = type, Timestamp = DateTime.Now };

            if (_count < MaxEntries)
            {
                Buffer[_count] = entry;
                _count++;
            }
            else
            {
                Buffer[_head] = entry;
                _head = (_head + 1) % MaxEntries;
            }

            OnLogAdded?.Invoke(entry);
        }

        public static void LogWarning(string message) => Log(message, LogType.Warning);
        public static void LogError(string message) => Log(message, LogType.Error);
        public static void LogSuccess(string message) => Log(message, LogType.Success);
        public static void LogInput(string message) => Log($"> {message}", LogType.Input);

        /// <summary>Clears all log entries.</summary>
        public static void Clear()
        {
            _head = 0;
            _count = 0;
            OnCleared?.Invoke();
        }

        public struct RingBufferView : IReadOnlyList<LogEntry>
        {
            private readonly LogEntry[] _buffer;
            private readonly int _head;
            private readonly int _count;

            public RingBufferView(LogEntry[] buffer, int head, int count)
            {
                _buffer = buffer;
                _head = head;
                _count = count;
            }

            public int Count => _count;

            public LogEntry this[int index]
            {
                get
                {
                    if (index < 0 || index >= _count)
                        throw new ArgumentOutOfRangeException(nameof(index));
                    return _buffer[(_head + index) % _buffer.Length];
                }
            }

            public IEnumerator<LogEntry> GetEnumerator()
            {
                for (int i = 0; i < _count; i++)
                    yield return _buffer[(_head + i) % _buffer.Length];
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
