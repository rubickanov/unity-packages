using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.LowLevel;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// Copies every Unity log message into <see cref="ConsoleLog"/>, from the first moment scripts run, on any
    /// thread. Subscribed at <c>SubsystemRegistration</c> rather than by a frontend, so nothing logged before the
    /// console window exists is lost. <see cref="ConsoleLog"/> is main-thread only: a message from another thread
    /// waits in a queue that is emptied at the start of the next frame, or earlier by the next main-thread message,
    /// which keeps the order they were logged in.
    /// </summary>
    internal static class UnityLogForwarder
    {
        private const string Prefix = "[Unity] ";

        private readonly struct Pending
        {
            public readonly string Condition;
            public readonly string StackTrace;
            public readonly LogType Type;

            public Pending(string condition, string stackTrace, LogType type)
            {
                Condition = condition;
                StackTrace = stackTrace;
                Type = type;
            }
        }

        // The player loop system is found again by this type, so a second registration replaces the first.
        private struct DrainSystem { }

        private static readonly ConcurrentQueue<Pending> Queue = new();
        private static int _mainThreadId;
        private static bool _subscribed;
        private static bool _forwarding;

        /// <summary>Whether messages reach the console. On by default; <c>log_unity</c> switches it.</summary>
        public static bool Enabled { get; set; } = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            Uninstall();
            Reset();
            Application.logMessageReceivedThreaded += Receive;
            Application.quitting += Uninstall;
            _subscribed = true;
            InsertDrainSystem();
        }

        // In the editor quitting is leaving play mode. Without it a play session with domain reload off would keep
        // filling the queue from editor threads with nobody draining it.
        private static void Uninstall()
        {
            if (!_subscribed) return;
            Application.logMessageReceivedThreaded -= Receive;
            Application.quitting -= Uninstall;
            _subscribed = false;
        }

        /// <summary>Back to the start state, on the calling thread as the main one. Leaves the subscription alone.</summary>
        internal static void Reset()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            while (Queue.TryDequeue(out _)) { }
            _forwarding = false;
            Enabled = true;
        }

        /// <summary>The <c>logMessageReceivedThreaded</c> handler.</summary>
        internal static void Receive(string condition, string stackTrace, LogType type)
        {
            if (!Enabled) return;

            if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            {
                Queue.Enqueue(new Pending(condition, stackTrace, type));
                return;
            }

            // A console subscriber that logs would otherwise recurse forever; its message is still in the Unity log.
            if (_forwarding) return;
            _forwarding = true;
            try
            {
                DrainQueue();
                Write(condition, stackTrace, type);
            }
            finally
            {
                _forwarding = false;
            }
        }

        /// <summary>Moves the messages from other threads into the console. Main thread only.</summary>
        internal static void Drain()
        {
            if (_forwarding) return;
            _forwarding = true;
            try
            {
                DrainQueue();
            }
            finally
            {
                _forwarding = false;
            }
        }

        private static void DrainQueue()
        {
            while (Queue.TryDequeue(out var pending))
                Write(pending.Condition, pending.StackTrace, pending.Type);
        }

        private static void Write(string condition, string stackTrace, LogType type)
        {
            switch (type)
            {
                // Without the trace an exception in the console says what broke but never where
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    var trace = stackTrace?.TrimEnd();
                    ConsoleLog.LogError(string.IsNullOrEmpty(trace)
                        ? Prefix + condition
                        : Prefix + condition + "\n" + trace);
                    break;
                case LogType.Warning:
                    ConsoleLog.LogWarning(Prefix + condition);
                    break;
                default:
                    ConsoleLog.Log(Prefix + condition);
                    break;
            }
        }

        private static void InsertDrainSystem()
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();
            var phases = root.subSystemList;
            if (phases == null) return;

            for (int i = 0; i < phases.Length; i++)
            {
                if (phases[i].type != typeof(UnityEngine.PlayerLoop.PreUpdate)) continue;

                var old = phases[i].subSystemList ?? Array.Empty<PlayerLoopSystem>();
                var systems = new List<PlayerLoopSystem>(old.Length + 1);
                foreach (var system in old)
                {
                    if (system.type != typeof(DrainSystem))
                        systems.Add(system);
                }

                systems.Add(new PlayerLoopSystem { type = typeof(DrainSystem), updateDelegate = Drain });
                phases[i].subSystemList = systems.ToArray();
                root.subSystemList = phases;
                PlayerLoop.SetPlayerLoop(root);
                return;
            }
        }
    }
}
