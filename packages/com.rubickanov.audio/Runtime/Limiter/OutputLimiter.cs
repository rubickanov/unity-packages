using System;
using UnityEngine;

namespace Rubickanov.Audio
{
    /// <summary>The true-peak limiter on the listener's output (see <see cref="OutputLimiter"/>).</summary>
    [Serializable]
    public sealed class OutputLimiterSettings
    {
        [Tooltip("A true-peak limiter on the active AudioListener: after every mixer group, before the reverb-zone wet.")]
        public bool Enabled = true;
        [Tooltip("The highest true peak let through, dBTP. Below 0 so the reverb-zone wet, which sums after it, has room.")]
        public float CeilingDb = -1f;
        [Tooltip("How far ahead the gain sees a peak, ms: the attack ramp, and the delay added to everything heard.")]
        public float LookaheadMs = 1.5f;
        [Tooltip("Time constant of the gain coming back after a peak, ms.")]
        public float ReleaseMs = 100f;
    }

    /// <summary>
    /// <see cref="TruePeakLimiter"/> in <c>OnAudioFilterRead</c> on an <see cref="AudioListener"/>'s object: it gets the
    /// whole mix after the mixer (every group and effect), so it catches the peaks a mixer compressor without
    /// lookahead lets through. Unity adds the <see cref="AudioReverbZone"/> wet after it. Put it before other filters
    /// on the listener (a capture sees it then). <see cref="UnityAudioService"/> keeps one on the active listener.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OutputLimiter : MonoBehaviour
    {
        private TruePeakLimiter? _limiter;
        private OutputLimiterSettings _settings = new();
        private volatile bool _on = true;
        private int _rate;

        /// <summary>The limiter in use, null before the first audio block of its rate.</summary>
        public TruePeakLimiter? Limiter => _limiter;

        /// <summary>Off passes the mix through untouched (for an A/B); back on starts from silence for one lookahead.</summary>
        public bool On
        {
            get => _on;
            set
            {
                if (value && !_on) _limiter = null;
                _on = value;
                Build();
            }
        }

        /// <summary>The lowest gain since the last call, dB (0 = idle), then resets.</summary>
        public float TakeReductionDb()
        {
            TruePeakLimiter? l = _limiter;
            return l == null ? 0f : 20f * Mathf.Log10(Mathf.Max(1e-6f, l.TakeLowestGain()));
        }

        /// <summary>Takes new settings; a new lookahead or rate builds a new limiter (one block of silence).</summary>
        public void Configure(OutputLimiterSettings settings)
        {
            _settings = settings ?? new OutputLimiterSettings();
            On = _settings.Enabled;
        }

        private void OnEnable()
        {
            AudioSettings.OnAudioConfigurationChanged += OnAudioChanged;
            Build();
        }

        private void OnDisable() => AudioSettings.OnAudioConfigurationChanged -= OnAudioChanged;

        private void OnAudioChanged(bool deviceChanged) => Build();

        private void Build()
        {
            int rate = AudioSettings.outputSampleRate;
            if (rate <= 0) return;
            TruePeakLimiter? old = _limiter;
            if (old != null && _rate == rate && old.Latency == TruePeakLimiter.LatencyFor(rate, _settings.LookaheadMs))
            {
                old.CeilingDb = _settings.CeilingDb;
                old.ReleaseMs = _settings.ReleaseMs;
                return;
            }
            _rate = rate;
            _limiter = new TruePeakLimiter(rate, _settings.CeilingDb, _settings.LookaheadMs, _settings.ReleaseMs);
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_on) return;
            _limiter?.Process(data, channels);
        }
    }
}
