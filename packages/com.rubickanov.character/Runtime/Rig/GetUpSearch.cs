using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.Rig
{
    /// <summary>A frame of a get-up clip near a lying body, and how to lay the clip so that frame lies as the body
    /// does.</summary>
    public struct GetUpMatch
    {
        /// <summary>The clip's place in the list searched.</summary>
        public int Entry;

        /// <summary>The frame of the clip, from 0.</summary>
        public int Frame;

        public float Cost;

        /// <summary>The heading (rad about the frame's up from its forward) a root needs for the frame to face as the
        /// body.</summary>
        public float Heading;

        /// <summary>The frame's pelvis from its root, in the root's frame.</summary>
        public Vector3 Pelvis;

        /// <summary>The root turn of <see cref="Heading"/> about +Y.</summary>
        public Quaternion Turn => Quaternion.AngleAxis(Heading * Mathf.Rad2Deg, Vector3.up);

        /// <summary>The root on the ground at <paramref name="groundHeight"/> under which the frame's pelvis is the
        /// body's at <paramref name="pelvis"/> (all in the search's frame, +Y up).</summary>
        public Vector3 Root(Vector3 pelvis, float groundHeight)
        {
            Vector3 from = Turn * Pelvis;
            return new Vector3(pelvis.x - from.x, groundHeight, pelvis.z - from.z);
        }
    }

    /// <summary>Where a clip's frames lie in <see cref="GetUpSearch"/>'s flat arrays.</summary>
    public struct GetUpClipView
    {
        public int FirstFrame;
        public int FrameCount;

        /// <summary>The last frame the search may start from (the clip stands soon after).</summary>
        public int LastStart;
    }

    /// <summary>
    /// Motion matching for one pose, as a get-up from a ragdoll is started (the "nearest frame"): every frame of the
    /// get-up clips is scored against the lying body by its joint turns, how its pelvis lies against gravity (the
    /// heading about up is free, the body's is found for each frame) and which parts bear on the ground. Ported from
    /// SPLATDOWN's Sinew Animation/MotionSearch.cs (Nearest l.56–140) in float, keeping each clip's best frame so a
    /// caller can fall back to the next clip. Frames come baked: per frame <see cref="Joints"/> joint turns (child in
    /// parent), the pelvis's turn and place in the clip root's frame (+Y up, +Z forward) and
    /// <see cref="Segments"/> segment heights over the root's ground.
    /// </summary>
    public static class GetUpSearch
    {
        public const int Segments = 12;
        public const int Joints = Segments - 1;

        // The pelvis against gravity weighs as this many joints; a part bears on the ground up to Support high, m,
        // and the support of all of them weighs as SupportWeight joints per metre squared over Support squared.
        public const float PelvisWeight = 4f;
        public const float Support = 0.25f;
        public const float SupportWeight = 1f;

        /// <summary>
        /// Each clip's frame nearest a body whose segments turn as <paramref name="turns"/> (pelvis first, in a frame
        /// with +Y up), hanging as <paramref name="parents"/> says, with segment heights <paramref name="heights"/>
        /// over the ground; into <paramref name="best"/>, cheapest first. The clips' frames are
        /// <paramref name="clipJoints"/> (<see cref="Joints"/> per frame), <paramref name="clipPelvis"/>,
        /// <paramref name="clipPelvisAt"/> and <paramref name="clipHeights"/> (<see cref="Segments"/> per frame).
        /// </summary>
        public static void Nearest(IReadOnlyList<GetUpClipView> clips, Quaternion[] clipJoints,
            Quaternion[] clipPelvis, Vector3[] clipPelvisAt, float[] clipHeights, int[] parents,
            Quaternion[] turns, float[] heights, List<GetUpMatch> best)
        {
            best.Clear();
            var joints = new Quaternion[Joints];
            for (int s = 1; s < Segments; s++)
            {
                joints[s - 1] = Quaternion.Inverse(turns[parents[s]]) * turns[s];
            }

            Quaternion pelvis = turns[0];
            var y = new Quaternion(0f, 1f, 0f, 0f);
            for (int c = 0; c < clips.Count; c++)
            {
                GetUpClipView clip = clips[c];
                var top = new GetUpMatch { Entry = c, Frame = -1, Cost = float.MaxValue };
                int last = Mathf.Min(clip.FrameCount - 1, clip.LastStart);
                for (int f = 0; f <= last; f++)
                {
                    int frame = clip.FirstFrame + f;
                    float cost = 0f;
                    for (int j = 0; j < Joints; j++)
                    {
                        float d = Quaternion.Dot(clipJoints[frame * Joints + j], joints[j]);
                        cost += 1f - d * d;
                    }

                    // The heading that best turns the frame's pelvis onto the body's: Ry(h)·c·b maximal, as
                    // cos(h/2)·(c·b) + sin(h/2)·((ŷc)·b).
                    Quaternion clipTurn = clipPelvis[frame];
                    float a = Quaternion.Dot(clipTurn, pelvis);
                    float b = Quaternion.Dot(y * clipTurn, pelvis);
                    cost += PelvisWeight * (1f - (a * a + b * b));
                    if (cost >= top.Cost)
                    {
                        continue;
                    }

                    for (int s = 0; s < Segments; s++)
                    {
                        float e = Mathf.Min(heights[s], Support) - Mathf.Min(clipHeights[frame * Segments + s], Support);
                        cost += SupportWeight * e * e / (Support * Support);
                    }

                    if (cost < top.Cost)
                    {
                        top = new GetUpMatch
                        {
                            Entry = c, Frame = f, Cost = cost, Heading = WrapPi(2f * Mathf.Atan2(b, a)),
                            Pelvis = clipPelvisAt[frame],
                        };
                    }
                }

                if (top.Frame >= 0)
                {
                    best.Add(top);
                }
            }

            best.Sort((l, r) => l.Cost.CompareTo(r.Cost));
        }

        /// <summary>
        /// The first frame a clip stands from, by its pelvis heights over the ground: from the last frame back, the
        /// first frame at 95 % of the final height after which it never sinks below 90 % (Sinew
        /// Control/GettingUp.cs:163–176).
        /// </summary>
        public static int StandFrame(float[] pelvisHeights, int first, int count)
        {
            int last = count - 1;
            float end = pelvisHeights[first + last];
            int stand = last;
            for (int f = last; f >= 0 && pelvisHeights[first + f] >= 0.9f * end; f--)
            {
                if (pelvisHeights[first + f] >= 0.95f * end)
                {
                    stand = f;
                }
            }

            return stand;
        }

        /// <summary>The nearest distance between segments a0–a1 and b0–b1.</summary>
        public static float SegmentDistance(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
        {
            Vector3 d1 = a1 - a0;
            Vector3 d2 = b1 - b0;
            Vector3 r = a0 - b0;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);
            float s;
            float t;
            if (a <= 1e-9f && e <= 1e-9f)
            {
                return r.magnitude;
            }

            if (a <= 1e-9f)
            {
                s = 0f;
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-9f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom > 1e-9f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f)
                    {
                        t = 0f;
                        s = Mathf.Clamp01(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Mathf.Clamp01((b - c) / a);
                    }
                }
            }

            return (a0 + d1 * s - (b0 + d2 * t)).magnitude;
        }

        private static float WrapPi(float a)
        {
            while (a > Mathf.PI)
            {
                a -= 2f * Mathf.PI;
            }

            while (a < -Mathf.PI)
            {
                a += 2f * Mathf.PI;
            }

            return a;
        }
    }
}
