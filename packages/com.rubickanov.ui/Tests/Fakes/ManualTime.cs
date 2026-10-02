using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.UI.Tests
{
    /// <summary>
    /// Puts <see cref="Tween"/> on a clock and frames the test moves: <see cref="Step"/> advances the clock and runs
    /// one frame of every tween waiting for it. Dispose to put the real ones back.
    /// </summary>
    public sealed class ManualTime : IDisposable
    {
        private readonly Func<float> _clock;
        private readonly Func<CancellationToken, UniTask> _nextFrame;
        private List<UniTaskCompletionSource> _waiting = new();

        public float Now { get; private set; } = 64f;

        public ManualTime()
        {
            _clock = Tween.Clock;
            _nextFrame = Tween.NextFrame;
            Tween.Clock = () => Now;
            Tween.NextFrame = Wait;
        }

        public void Step(float seconds)
        {
            Now += seconds;
            var waiting = _waiting;
            _waiting = new List<UniTaskCompletionSource>();
            foreach (var frame in waiting) frame.TrySetResult();
        }

        public void Dispose()
        {
            Tween.Clock = _clock;
            Tween.NextFrame = _nextFrame;
        }

        private UniTask Wait(CancellationToken ct)
        {
            var frame = new UniTaskCompletionSource();
            _waiting.Add(frame);
            return frame.Task.AttachExternalCancellation(ct);
        }
    }
}
