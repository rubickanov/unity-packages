using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.Audio
{
    /// <summary>The numbers behind <see cref="SoundKind"/>: pure functions of distance and direction.</summary>
    public static class SoundMath
    {
        public const float SoundSpeed = 343f;
        /// <summary>AudioLowPassFilter's custom cutoff curve: cutoff = y × this, Hz.</summary>
        public const float CutoffCurveScale = 22090f;
        public const float MinCutoff = 400f;
        public const float MaxCutoff = 22000f;
        // ISO 9613-1 above the relaxation bands (20 °C, 70 %): α ≈ 1.3 dB/km × f² [kHz].
        private const float AirDbPerMetreKHz2 = 1.3e-3f;
        // A 12 dB/oct low-pass is 3 dB down at its cutoff: put it where the air has taken 3 dB.
        private const float CutoffDb = 3f;

        /// <summary>
        /// Reference / max(d, reference), faded by a smoothstep to 0 over the last fadeShare of max (fadeShare 0: no
        /// fade, the law holds up to max).
        /// </summary>
        public static float DistanceGain(float d, float reference, float max, float fadeShare)
        {
            fadeShare = Mathf.Clamp(fadeShare, 0f, 0.5f);
            // No fade: the inverse law holds up to Max (a curve cannot step to silence).
            if (fadeShare <= 0f) return reference / Mathf.Clamp(d, reference, Mathf.Max(max, reference));
            if (d >= max) return 0f;
            float gain = reference / Mathf.Max(d, reference);
            float start = max * (1f - fadeShare);
            if (d <= start) return gain;
            float t = (d - start) / (max - start);
            return gain * (1f - t * t * (3f - 2f * t));
        }

        /// <summary>Low-pass cutoff (Hz) where the air has taken 3 dB over d metres, air × ISO 9613-1.</summary>
        public static float AirCutoff(float d, float air)
        {
            if (d <= 0f || air <= 0f) return MaxCutoff;
            float kHz = Mathf.Sqrt(CutoffDb / (AirDbPerMetreKHz2 * d * air));
            return Mathf.Clamp(kHz * 1000f, MinCutoff, MaxCutoff);
        }

        /// <summary>Spread in degrees: SpreadNear up to Reference, SpreadFar from SpreadFarAt, blended on log distance.</summary>
        public static float Spread(float d, SoundKind kind) =>
            Spread(d, kind.Reference, kind.SpreadNear, kind.SpreadFar, kind.SpreadFarAt);

        public static float Spread(float d, float reference, float near, float far, float farAt)
        {
            if (farAt <= reference) return d < farAt ? near : far;
            if (d <= reference) return near;
            if (d >= farAt) return far;
            float t = Mathf.Log(d / reference) / Mathf.Log(farAt / reference);
            return Mathf.Lerp(near, far, t);
        }

        // Unity's panner loses power as a mono source widens (measured on 6000.6, a source straight ahead):
        // spread 0, 30, …, 180 degrees → dB.
        private static readonly float[] SpreadLoss = { 0f, -0.18f, -0.64f, -1.28f, -2.08f, -2.75f, -3.04f };

        /// <summary>Gain that undoes Unity's level loss at this spread, so width never changes loudness.</summary>
        public static float SpreadCompensation(float spreadDegrees)
        {
            float x = Mathf.Clamp(spreadDegrees, 0f, 180f) / 30f;
            int i = Mathf.Min((int)x, SpreadLoss.Length - 2);
            float lossDb = Mathf.Lerp(SpreadLoss[i], SpreadLoss[i + 1], x - i);
            return DbToGain(-lossDb);
        }

        /// <summary>
        /// The source's rolloff at d: the distance law (flat for External) × the spread compensation, relative to the
        /// kind's widest spread (Unity clamps a rolloff curve to 1, so the compensation can only lower narrower spans).
        /// </summary>
        public static float Level(float d, SoundKind kind)
        {
            float law = kind.Law == DistanceLaw.External ? 1f : DistanceGain(d, kind.Reference, kind.Max, kind.FadeShare);
            float widest = SpreadCompensation(Mathf.Max(kind.SpreadNear, kind.SpreadFar));
            return law * SpreadCompensation(Spread(d, kind)) / widest;
        }

        /// <summary>
        /// Reverb-zone send: rises ∝ distance from Reference (the zone send is taken after the rolloff, so this keeps
        /// the wet level constant: a diffuse field), capped at ReverbMax.
        /// </summary>
        public static float ReverbSend(float d, SoundKind kind) =>
            ReverbSend(d, kind.Reference, kind.ReverbAtReference, kind.ReverbMax);

        public static float ReverbSend(float d, float reference, float atReference, float max) =>
            Mathf.Min(max, atReference * Mathf.Max(d, reference) / reference);

        /// <summary>0 anywhere in front of the listener, 1 straight behind.</summary>
        public static float BackAmount(Vector3 listenerForward, Vector3 toSource)
        {
            float a = listenerForward.sqrMagnitude, b = toSource.sqrMagnitude;
            if (a < 1e-12f || b < 1e-12f) return 0f;
            float cos = Vector3.Dot(listenerForward, toSource) / Mathf.Sqrt(a * b);
            return Mathf.Clamp01(-cos);
        }

        /// <summary>Points an occlusion check aims its rays at: the source's centre, then a half-ring round it.</summary>
        public const int OcclusionPoints = 8;

        /// <summary>
        /// Point <paramref name="index"/> of <see cref="OcclusionPoints"/>: 0 the source, the rest a half-ring of
        /// <paramref name="radius"/> across the line from the listener, from one side over the top to the other, never
        /// below the source (the ground under it blocks nothing). Sound bends round an obstacle smaller than the ring.
        /// </summary>
        public static Vector3 OcclusionPoint(Vector3 listener, Vector3 source, float radius, int index)
        {
            if (index <= 0 || radius <= 0f) return source;
            Vector3 along = source - listener;
            Vector3 side = Vector3.Cross(Vector3.up, along);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.right;
            side.Normalize();
            Vector3 up = Vector3.Cross(along, side).normalized;
            if (up.y < 0f) up = -up;
            float angle = (index - 1) * Mathf.PI / (OcclusionPoints - 2);
            return source + radius * (Mathf.Cos(angle) * side + Mathf.Sin(angle) * up);
        }

        public static float DbToGain(float db) => Mathf.Pow(10f, db / 20f);

        /// <summary>One-pole step towards target: 63 % of the way after tau seconds, never past it.</summary>
        public static float Smooth(float current, float target, float dt, float tau)
        {
            if (tau <= 0f || dt <= 0f) return tau <= 0f ? target : current;
            return current + (target - current) * (1f - Mathf.Exp(-dt / tau));
        }

        /// <summary><see cref="Smooth"/> with attack while rising and release while falling.</summary>
        public static float Smooth(float current, float target, float dt, float attack, float release) =>
            Smooth(current, target, dt, target > current ? attack : release);

        /// <summary>
        /// A curve over x = d / max for AudioSource custom curves and the low-pass cutoff curve: keys log-spaced from
        /// max/512 to max plus one at 0 and one at every break (kinks of f), tangents from f on each side.
        /// </summary>
        public static AnimationCurve Curve(Func<float, float> f, float max, int keys = 24, params float[] breaks)
        {
            var ds = new List<float>(keys + breaks.Length + 1) { 0f };
            float near = max * 1e-3f;
            foreach (float b in breaks)
                if (b > near && b < max - near) ds.Add(b);
            int fixedKeys = ds.Count;
            float lo = max / 512f;
            int n = Mathf.Max(2, keys);
            for (int i = 0; i < n; i++)
            {
                float d = i == n - 1 ? max : lo * Mathf.Pow(max / lo, i / (float)(n - 1));
                bool clash = false;
                for (int j = 1; j < fixedKeys; j++)
                    clash |= Mathf.Abs(ds[j] - d) < near;
                if (!clash) ds.Add(d);
            }
            ds.Sort();
            for (int i = ds.Count - 1; i > 0; i--)
                if (ds[i] - ds[i - 1] < near) ds.RemoveAt(ds[i] >= max ? i - 1 : i);

            var frames = new Keyframe[ds.Count];
            for (int i = 0; i < ds.Count; i++)
            {
                float d = ds[i];
                float gapL = i > 0 ? d - ds[i - 1] : float.MaxValue;
                float gapR = i < ds.Count - 1 ? ds[i + 1] - d : float.MaxValue;
                float h = Mathf.Max(Mathf.Min(gapL, gapR) * 1e-3f, 1e-7f);
                float y = f(d);
                float slopeL = i > 0 ? (y - f(d - h)) / h : float.NaN;
                float slopeR = i < ds.Count - 1 ? (f(d + h) - y) / h : float.NaN;
                if (float.IsNaN(slopeL)) slopeL = slopeR;
                if (float.IsNaN(slopeR)) slopeR = slopeL;
                // Slopes per metre → per unit of x = d / max.
                frames[i] = new Keyframe(d / max, y, slopeL * max, slopeR * max);
            }
            return new AnimationCurve(frames);
        }
    }
}
