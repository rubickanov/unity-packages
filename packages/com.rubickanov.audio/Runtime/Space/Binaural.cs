using System;
using System.Runtime.InteropServices;
using AOT;
using Rubickanov.Log;
using UnityEngine;

namespace Rubickanov.Audio
{
    /// <summary>
    /// HRTF for kinds marked <see cref="SoundKind.Binaural"/>: Steam Audio's spatializer, direct path only. Distance
    /// stays on the kind's rolloff curve, air and occlusion on its low-pass, reverb on the zones; Steam Audio only
    /// places the sound around the head. Off until a game calls <see cref="Start"/>.
    /// </summary>
    public static class Binaural
    {
        public const string PluginName = "Steam Audio Spatializer";

        private static readonly LogChannel Channel = LogChannel.Get("Audio");

        private static IntPtr _context;
        private static IntPtr _hrtf;
        private static bool _on = true;

        /// <summary>Steam Audio is up: its context and HRTF are handed to the spatializer.</summary>
        public static bool Ready { get; private set; }

        /// <summary>What <see cref="Start"/> failed on, or null.</summary>
        public static string? Error { get; private set; }

        /// <summary>The A/B switch: false puts every kind back on Unity's panner and head shadow.</summary>
        public static bool On
        {
            get => _on;
            set
            {
                if (_on == value) return;
                _on = value;
                SoundSpace.Reshape();
            }
        }

        /// <summary>The kind gets HRTF now.</summary>
        public static bool Applies(SoundKind kind) => Ready && _on && kind.Binaural;

        /// <summary>
        /// Loads Steam Audio with its default HRTF and hands both to its spatializer. Safe to call again; false (and
        /// <see cref="Error"/>) when the platform or the libraries are missing, in which case every kind stays panned.
        /// The project must pick the spatializer (Audio settings, Spatializer Plugin): a player cannot, and reports no
        /// name early, so it is not checked here.
        /// </summary>
        public static bool Start()
        {
            if (Ready) return true;
            try
            {
                var settings = new Native.ContextSettings
                {
                    Version = Native.Version,
                    Log = Log,
                    Simd = Native.SimdLevel.Avx2,
                };
                if (Native.iplContextCreate(ref settings, out _context) != Native.Status.Success)
                    return Fail("context");

                AudioSettings.GetDSPBufferSize(out int frame, out _);
                var audio = new Native.AudioSettings { SamplingRate = AudioSettings.outputSampleRate, FrameSize = frame };
                var hrtf = new Native.HrtfSettings { Type = Native.HrtfType.Default, Volume = 1f };
                if (Native.iplHRTFCreate(_context, ref audio, ref hrtf, out _hrtf) != Native.Status.Success)
                {
                    Native.iplContextRelease(ref _context);
                    return Fail("HRTF");
                }

                Native.iplUnityInitialize(_context);
                Native.iplUnitySetHRTF(_hrtf);
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                return Fail(e.Message);
            }

            Ready = true;
            Error = null;
            SoundSpace.Reshape();
            return true;
        }

        /// <summary><see cref="Start"/> for a scope's life: disposing it calls <see cref="Stop"/>.</summary>
        public static IDisposable Begin()
        {
            Start();
            return new Scope();
        }

        /// <summary>Back to Unity's panner for every kind and Steam Audio released.</summary>
        public static void Stop()
        {
            if (!Ready) return;
            Ready = false;
            SoundSpace.Reshape();
            Native.iplUnityTerminate();
            Native.iplHRTFRelease(ref _hrtf);
            Native.iplContextRelease(ref _context);
        }

        /// <summary>The spatializer's parameters on a binaural source: HRTF on the direct path, nothing else.</summary>
        internal static void Configure(AudioSource source)
        {
            source.SetSpatializerFloat(Native.ApplyAirAbsorption, 0f);
            source.SetSpatializerFloat(Native.ApplyDistanceAttenuation, 1f);
            source.SetSpatializerFloat(Native.UseUnityCurve, 1f);
            source.SetSpatializerFloat(Native.Interpolation, 1f);
            source.SetSpatializerFloat(Native.DirectBinaural, 1f);
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose() => Stop();
        }

        private static bool Fail(string what)
        {
            Error = what;
            Channel.Warn("Binaural off: " + what);
            return false;
        }

        [MonoPInvokeCallback(typeof(Native.LogCallback))]
        private static void Log(Native.LogLevel level, string message)
        {
            if (level == Native.LogLevel.Error) Channel.Error("Steam Audio: " + message);
            else if (level == Native.LogLevel.Warning) Channel.Warn("Steam Audio: " + message);
        }

        // Steam Audio 4.8's C API, only what the direct path needs.
        private static class Native
        {
            public const uint Version = (4u << 16) | (8u << 8) | 1u;

            // The spatializer's parameter indices (spatialize_effect.cpp, Params).
            public const int ApplyDistanceAttenuation = 0;
            public const int ApplyAirAbsorption = 1;
            public const int Interpolation = 7;
            public const int UseUnityCurve = 9;
            public const int DirectBinaural = 29;

            public enum Status { Success, Failure, OutOfMemory, Initialization }
            public enum LogLevel { Info, Warning, Error, Debug }
            public enum SimdLevel { Sse2, Sse4, Avx, Avx2, Avx512 }
            public enum HrtfType { Default, Sofa }

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            public delegate void LogCallback(LogLevel level, string message);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            public delegate IntPtr AllocateCallback(UIntPtr size, UIntPtr alignment);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            public delegate void FreeCallback(IntPtr block);

            [StructLayout(LayoutKind.Sequential)]
            public struct ContextSettings
            {
                public uint Version;
                public LogCallback Log;
                public AllocateCallback? Allocate;
                public FreeCallback? Free;
                public SimdLevel Simd;
                public int Flags;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct AudioSettings
            {
                public int SamplingRate;
                public int FrameSize;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct HrtfSettings
            {
                public HrtfType Type;
                public string? SofaFileName;
                public IntPtr SofaData;
                public int SofaDataSize;
                public float Volume;
                public int NormType;
            }

            [DllImport("phonon")]
            public static extern Status iplContextCreate(ref ContextSettings settings, out IntPtr context);

            [DllImport("phonon")]
            public static extern void iplContextRelease(ref IntPtr context);

            [DllImport("phonon")]
            public static extern Status iplHRTFCreate(IntPtr context, ref AudioSettings audio, ref HrtfSettings settings, out IntPtr hrtf);

            [DllImport("phonon")]
            public static extern void iplHRTFRelease(ref IntPtr hrtf);

            [DllImport("audioplugin_phonon")]
            public static extern void iplUnityInitialize(IntPtr context);

            [DllImport("audioplugin_phonon")]
            public static extern void iplUnitySetHRTF(IntPtr hrtf);

            [DllImport("audioplugin_phonon")]
            public static extern void iplUnityTerminate();
        }
    }
}
