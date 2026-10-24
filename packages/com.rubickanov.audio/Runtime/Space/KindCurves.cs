using UnityEngine;

namespace Rubickanov.Audio
{
    // A kind's four curves, built once and reused by every source of the kind (rebuilt after an Inspector edit).
    internal sealed class KindCurves
    {
        // Builds so far: tests check plays reuse them.
        internal static int Built;

        public readonly AnimationCurve Rolloff;
        public readonly AnimationCurve Spread;
        public readonly AnimationCurve Reverb;
        public readonly AnimationCurve Cutoff;

        public static KindCurves Of(SoundKind kind) => kind.Curves ??= new KindCurves(kind);

        private KindCurves(SoundKind kind)
        {
            Built++;
            float max = kind.Max, reference = Mathf.Min(kind.Reference, max);
            float start = max * (1f - kind.FadeShare), w = max - start;
            Rolloff = SoundMath.Curve(d => SoundMath.Level(d, kind), max, 32, reference, kind.SpreadFarAt,
                start, start + 0.25f * w, start + 0.5f * w, start + 0.75f * w);
            Spread = SoundMath.Curve(d => SoundMath.Spread(d, kind) / 360f, max, 16, reference, kind.SpreadFarAt);
            float cap = kind.ReverbAtReference > 0f ? reference * kind.ReverbMax / kind.ReverbAtReference : max;
            Reverb = SoundMath.Curve(d => SoundMath.ReverbSend(d, kind), max, 16, reference, cap);
            float air = Mathf.Max(kind.Air, 1e-6f);
            // Where the cutoff leaves its clamps.
            float top = 3f / (1.3e-3f * air * 22f * 22f), bottom = 3f / (1.3e-3f * air * 0.4f * 0.4f);
            Cutoff = SoundMath.Curve(d => SoundMath.AirCutoff(d, kind.Air) / SoundMath.CutoffCurveScale, max, 24, top, bottom);
        }
    }
}
