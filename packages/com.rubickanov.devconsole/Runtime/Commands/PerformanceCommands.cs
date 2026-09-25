using System;
using UnityEngine;
using UnityEngine.Profiling;

namespace Rubickanov.DevConsole.Commands
{
    internal static class PerformanceCommands
    {
        private static float? _pausedTimeScale;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _pausedTimeScale = null;

        [ConsoleCommand("fps", "Show the frame rate over the last second: average, slowest and fastest frame", "Performance")]
        public static void Fps()
        {
            if (FrameTimeSampler.TryGetStats(1f, out var average, out var min, out var max, out var frames))
                ConsoleLog.Log($"FPS: {average:F1} avg, {min:F1} min, {max:F1} max ({frames} frames)");
            else
                ConsoleLog.Log($"FPS: {1f / Time.unscaledDeltaTime:F1}");
        }

        [ConsoleCommand("fps target", "Get or set target frame rate (-1 = unlimited)", "Performance")]
        public static void TargetFps(int? value = null)
        {
            if (value.HasValue)
                Application.targetFrameRate = value.Value;
            ConsoleLog.Log($"Target FPS: {Application.targetFrameRate}");
            if (QualitySettings.vSyncCount > 0)
                ConsoleLog.LogWarning($"VSync is {QualitySettings.vSyncCount}, so the target is ignored; vsync 0 turns it off.");
        }

        [ConsoleCommand("vsync", "Get or set VSync count (0=off, 1=every vblank, 2=every second)", "Performance")]
        public static void Vsync(int? count = null)
        {
            if (count.HasValue)
            {
                var clamped = Mathf.Clamp(count.Value, 0, 4);
                if (clamped != count.Value)
                    ConsoleLog.LogWarning($"VSync count must be 0..4, using {clamped}.");
                QualitySettings.vSyncCount = clamped;
            }

            ConsoleLog.Log($"VSync: {QualitySettings.vSyncCount}");
            if (QualitySettings.vSyncCount > 0)
                ConsoleLog.Log("  While VSync is on, fps target is ignored.");
        }

        [ConsoleCommand("memory", "Show memory usage", "Performance")]
        public static void Memory()
        {
            var gcMemory = GC.GetTotalMemory(false);
            var totalReserved = Profiler.GetTotalReservedMemoryLong();
            var totalAllocated = Profiler.GetTotalAllocatedMemoryLong();
            var monoUsed = Profiler.GetMonoUsedSizeLong();

            ConsoleLog.Log($"GC Heap: {gcMemory / (1024f * 1024f):F1} MB");
            ConsoleLog.Log($"Total Reserved: {totalReserved / (1024f * 1024f):F1} MB");
            ConsoleLog.Log($"Total Allocated: {totalAllocated / (1024f * 1024f):F1} MB");
            ConsoleLog.Log($"Mono Used: {monoUsed / (1024f * 1024f):F1} MB");
        }

        [ConsoleCommand("gc", "Force garbage collection", "Performance")]
        public static void Gc()
        {
            var before = GC.GetTotalMemory(false);
            GC.Collect();
            var after = GC.GetTotalMemory(true);
            ConsoleLog.Log($"GC collected. Before: {before / (1024f * 1024f):F1} MB → After: {after / (1024f * 1024f):F1} MB");
        }

        [ConsoleCommand("timescale", "Get or set Time.timeScale", "Time")]
        public static void Timescale(float? value = null)
        {
            if (value < 0f)
                throw new CommandException("Time scale cannot be negative.");
            if (value.HasValue)
            {
                Time.timeScale = value.Value;
                _pausedTimeScale = null;
            }

            ConsoleLog.Log($"Time scale: {Time.timeScale}");
        }

        [ConsoleCommand("pause", "Stop time, or give back the time scale it had before", "Time")]
        public static void Pause()
        {
            if (_pausedTimeScale.HasValue)
            {
                Time.timeScale = _pausedTimeScale.Value;
                _pausedTimeScale = null;
                ConsoleLog.Log($"Resumed, time scale {Time.timeScale}.");
                return;
            }

            _pausedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            ConsoleLog.Log("Paused. pause again to resume.");
        }
    }
}
