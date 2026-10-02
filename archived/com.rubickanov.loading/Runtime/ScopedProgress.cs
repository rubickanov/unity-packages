using System;

namespace Rubickanov.Loading
{
    /// <summary>
    /// Reusable mapper: takes an operation's normalized 0–1 progress and pushes
    /// <c>base + weight * value</c> to the presenter. <see cref="Reset"/> and
    /// <see cref="Invalidate"/> bump <see cref="Epoch"/> — tokens from prior
    /// operations then become no-ops, preventing a late report from overwriting
    /// progress of a later operation.
    /// </summary>
    internal sealed class ScopedProgress
    {
        private readonly Action<float> _onReport;
        private float _base;
        private float _weight;

        public int Epoch { get; private set; }

        public ScopedProgress(Action<float> onReport)
        {
            _onReport = onReport;
        }

        public void Reset(float baseProgress, float weight)
        {
            _base = baseProgress;
            _weight = weight;
            Epoch++;
        }

        public void Invalidate()
        {
            Epoch++;
        }

        public void Report(int epoch, float value)
        {
            if (epoch != Epoch)
                return;
            _onReport(_base + _weight * value);
        }
    }

    internal sealed class ScopedProgressToken : IProgress<float>
    {
        private readonly ScopedProgress _parent;
        private readonly int _epoch;

        public ScopedProgressToken(ScopedProgress parent, int epoch)
        {
            _parent = parent;
            _epoch = epoch;
        }

        public void Report(float value) => _parent.Report(_epoch, value);
    }
}
