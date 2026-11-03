using System;
using System.Collections.Generic;

namespace Rubickanov.Rig
{
    /// <summary>
    /// A locomotion loop's strides from its feet: per frame the reach (left foot minus right along the root's forward,
    /// m), the frames each foot reaches farthest ahead (it touches down: a stride marker), and the frame of a loop
    /// whose stance matches a shown pose. Markers are Sinew's <c>ClipMarkers.FindMarkers</c>/<c>Passes</c>
    /// (wipeout Assets/Sinew/Animation/ClipMarkers.cs:219–305), its frame constants turned into seconds (it sampled at
    /// 120 Hz).
    /// </summary>
    public static class Strides
    {
        // Feet reach their farthest no closer than this, s, at least this share of the farthest reach, and in a
        // stride each foot gets at least this share of the leg (hip to knee to ankle) ahead: Sinew's 0.3 m on its
        // adult's ~0.87 m leg (an adult's walk reaches 0.6-0.8 m, standing and landing stances under 0.3 m).
        // Sinew ClipMarkers.cs:25–27 (24 frames at 120 Hz; a reach in metres, here scaled to the character).
        public const float MarkerSpacing = 0.2f;
        public const float MarkerReach = 0.3f;
        public const float StrideReach = 0.34f;

        /// <summary>
        /// Frames where the left foot reaches farthest ahead, one a stride: peaks at least <paramref name="stride"/>
        /// (m) ahead and <paramref name="spacing"/> frames apart; peaks with no right foot as far ahead between them are
        /// one stride, the farthest kept. A clip where the feet never pass each other that far (standing, a landing,
        /// a strafe) has none. The right foot's markers are those of the negated reach.
        /// </summary>
        public static int[] FindMarkers(IReadOnlyList<float> reach, bool loop, int spacing, float stride)
        {
            int n = reach.Count;
            float most = 0f;
            for (int i = 0; i < n; i++)
            {
                most = Math.Max(most, reach[i]);
            }

            var peaks = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (reach[i] < stride || reach[i] < MarkerReach * most)
                {
                    continue;
                }

                bool peak = true;
                for (int k = -spacing; k <= spacing && peak; k++)
                {
                    int j = loop ? ((i + k) % n + n) % n : i + k;
                    if (k == 0 || j < 0 || j >= n)
                    {
                        continue;
                    }

                    peak = reach[j] < reach[i] || (reach[j] == reach[i] && j > i);
                }

                if (peak)
                {
                    peaks.Add(i);
                }
            }

            var found = new List<int>();
            foreach (int i in peaks)
            {
                if (found.Count > 0 && !Passes(reach, found[^1], i, stride))
                {
                    if (reach[i] > reach[found[^1]])
                    {
                        found[^1] = i;
                    }

                    continue;
                }

                found.Add(i);
            }

            if (loop && found.Count > 1 && !Passes(reach, found[^1], found[0] + n, stride))
            {
                // The last and the first are one stride round the loop.
                if (reach[found[^1]] > reach[found[0]])
                {
                    found[0] = found[^1];
                    found.Sort();
                }

                found.RemoveAt(found.Count - 1);
            }

            // A lone peak is a stride only if the right foot passes ahead somewhere in the clip.
            if (found.Count == 1 && !Passes(reach, 0, loop ? n : n - 1, stride))
            {
                found.Clear();
            }

            return found.ToArray();
        }

        /// <summary>The right foot reaches <paramref name="stride"/> ahead of the left between two frames (indices
        /// past the end wrap round).</summary>
        private static bool Passes(IReadOnlyList<float> reach, int from, int to, float stride)
        {
            int n = reach.Count;
            for (int i = from; i <= to; i++)
            {
                if (reach[i % n] <= -stride)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The frame of a loop (<paramref name="reach"/> per frame, its left and right markers) in the stance of the
        /// foot that bears, nearest <paramref name="shownReach"/>; -1 without markers of both feet. The left foot's
        /// stance runs from its marker (it touches down ahead) to the right foot's next one, the reach falling all
        /// the way; the right's the other way round. The bearing foot is the one that moves back against the
        /// other: a falling reach is the left's stance.
        /// </summary>
        public static int StanceFrame(IReadOnlyList<float> reach, IReadOnlyList<int> left, IReadOnlyList<int> right,
            float shownReach, bool leftBears)
        {
            int n = reach.Count;
            if (n == 0 || left.Count == 0 || right.Count == 0)
            {
                return -1;
            }

            IReadOnlyList<int> from = leftBears ? left : right;
            IReadOnlyList<int> to = leftBears ? right : left;
            int best = -1;
            float bestError = float.MaxValue;
            foreach (int start in from)
            {
                // The other foot's next marker round the loop.
                int end = int.MaxValue;
                foreach (int m in to)
                {
                    int ahead = m > start ? m : m + n;
                    end = Math.Min(end, ahead);
                }

                for (int f = start; f <= end; f++)
                {
                    int frame = f % n;
                    float error = Math.Abs(reach[frame] - shownReach);
                    if (error < bestError)
                    {
                        bestError = error;
                        best = frame;
                    }
                }
            }

            return best;
        }
    }
}
