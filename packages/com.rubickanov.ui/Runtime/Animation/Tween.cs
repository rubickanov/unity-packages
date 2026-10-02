using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Rubickanov.UI
{
    /// <summary>
    /// The frame loop under view animations: drives a value from 0 to 1 over unscaled time, so animations run at any
    /// <c>Time.timeScale</c>, slow motion and pause included.
    /// </summary>
    public static class Tween
    {
        /// <summary>Seconds the tweens run on. Unscaled time; tests set their own clock.</summary>
        internal static Func<float> Clock = () => Time.unscaledTime;

        /// <summary>Waits for the next frame. Tests step frames by hand.</summary>
        internal static Func<CancellationToken, UniTask> NextFrame =
            ct => UniTask.Yield(PlayerLoopTiming.Update, ct);

        /// <summary>
        /// Calls <paramref name="apply"/> once a frame with the part of <paramref name="seconds"/> gone by, 0 to 1, and
        /// last with exactly 1. A zero or negative duration applies 1 at once. Cancelling stops it where it is.
        /// </summary>
        /// <example><code>
        /// await Tween.Run(0.25f, t => strip.style.scale = new Scale(new Vector2(1f, t)), ct);
        /// </code></example>
        public static async UniTask Run(float seconds, Action<float> apply, CancellationToken ct)
        {
            if (apply == null) throw new ArgumentNullException(nameof(apply));

            var start = Clock();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var t = seconds > 0f ? (Clock() - start) / seconds : 1f;
                if (t >= 1f)
                {
                    apply(1f);
                    return;
                }

                apply(t < 0f ? 0f : t);
                await NextFrame(ct);
            }
        }
    }
}
