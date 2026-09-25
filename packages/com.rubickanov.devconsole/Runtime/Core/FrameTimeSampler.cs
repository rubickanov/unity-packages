using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// Remembers the unscaled duration of recent frames, so <c>fps</c> can report the last second instead of the one
    /// frame the command ran in, which is the frame the console spent handling Enter.
    /// </summary>
    internal static class FrameTimeSampler
    {
        // Enough for a second at 1000 fps
        private const int Capacity = 1024;

        // The player loop system is found again by this type, so a second registration replaces the first.
        private struct SampleSystem { }

        private static readonly float[] Durations = new float[Capacity];
        private static int _next;
        private static int _count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            Clear();
            InsertSampleSystem();
        }

        internal static void Clear()
        {
            _next = 0;
            _count = 0;
        }

        internal static void Record(float duration)
        {
            Durations[_next] = duration;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        /// <summary>
        /// Frame rate over the most recent frames adding up to <paramref name="window"/> seconds: the average, and the
        /// slowest and fastest single frame. False before any frame was recorded.
        /// </summary>
        internal static bool TryGetStats(float window, out float average, out float min, out float max, out int frames)
        {
            average = min = max = 0f;
            frames = 0;
            var total = 0f;
            var longest = 0f;
            var shortest = float.MaxValue;

            for (int i = 1; i <= _count && total < window; i++)
            {
                var duration = Durations[(_next - i + Capacity) % Capacity];
                if (duration <= 0f) continue;
                total += duration;
                longest = Mathf.Max(longest, duration);
                shortest = Mathf.Min(shortest, duration);
                frames++;
            }

            if (frames == 0) return false;
            average = frames / total;
            min = 1f / longest;
            max = 1f / shortest;
            return true;
        }

        private static void InsertSampleSystem()
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
                    if (system.type != typeof(SampleSystem))
                        systems.Add(system);
                }

                systems.Add(new PlayerLoopSystem
                {
                    type = typeof(SampleSystem),
                    updateDelegate = () => Record(Time.unscaledDeltaTime)
                });
                phases[i].subSystemList = systems.ToArray();
                root.subSystemList = phases;
                PlayerLoop.SetPlayerLoop(root);
                return;
            }
        }
    }
}
