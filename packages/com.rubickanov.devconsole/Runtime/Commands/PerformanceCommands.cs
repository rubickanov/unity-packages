using System;
using UnityEngine;
using UnityEngine.Profiling;

namespace Rubickanov.DevConsole.Commands
{
    internal static class PerformanceCommands
    {
        [ConsoleCommand("fps", "Show current FPS", "Performance")]
        public static void Fps()
        {
            var fps = 1f / Time.unscaledDeltaTime;
            ConsoleLog.Log($"FPS: {fps:F1}");
        }

        [ConsoleCommand("target_fps", "Get or set target frame rate (-1 = unlimited)", "Performance")]
        public static void TargetFps(int value = -2)
        {
            if (value != -2)
                Application.targetFrameRate = value;
            ConsoleLog.Log($"Target FPS: {Application.targetFrameRate}");
        }

        [ConsoleCommand("vsync", "Get or set VSync count (0=off, 1=every vblank, 2=every second)", "Performance")]
        public static void Vsync(int count = -1)
        {
            if (count != -1)
                QualitySettings.vSyncCount = Mathf.Clamp(count, 0, 4);
            ConsoleLog.Log($"VSync: {QualitySettings.vSyncCount}");
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

        [ConsoleCommand("timescale", "Get or set Time.timeScale", "Performance")]
        public static void Timescale(float value = -1f)
        {
            if (value >= 0f)
                Time.timeScale = value;
            ConsoleLog.Log($"Time scale: {Time.timeScale}");
        }
    }
}
