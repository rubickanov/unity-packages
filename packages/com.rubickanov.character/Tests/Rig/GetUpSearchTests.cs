using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Rig.Tests
{
    /// <summary>
    /// The nearest-frame search (after Sinew MotionSearchTests): a baked frame shown back as a body lying in a world
    /// with tilted gravity, turned about up, is found with its clip, frame and heading, and the root it gives puts the
    /// frame's pelvis on the body's; frames past a clip's last start are left out; the stand frame follows the
    /// 95 % / 90 % rule.
    /// </summary>
    public sealed class GetUpSearchTests
    {
        private const int Segments = GetUpSearch.Segments;
        private const int Joints = GetUpSearch.Joints;
        private static readonly int[] Parents = { -1, 0, 1, 2, 2, 4, 2, 6, 0, 8, 0, 10 };

        private Quaternion[] _joints;
        private Quaternion[] _pelvis;
        private Vector3[] _pelvisAt;
        private float[] _heights;
        private List<GetUpClipView> _clips;

        [SetUp]
        public void Bake()
        {
            // Two clips of 20 and 30 frames, every frame a different pose.
            var random = new System.Random(7);
            _clips = new List<GetUpClipView>
            {
                new() { FirstFrame = 0, FrameCount = 20, LastStart = 19 },
                new() { FirstFrame = 20, FrameCount = 30, LastStart = 29 },
            };
            int frames = 50;
            _joints = new Quaternion[frames * Joints];
            _pelvis = new Quaternion[frames];
            _pelvisAt = new Vector3[frames];
            _heights = new float[frames * Segments];
            for (int f = 0; f < frames; f++)
            {
                _pelvis[f] = Random(random, 180f);
                _pelvisAt[f] = new Vector3(Next(random) * 0.3f, 0.1f + 0.9f * (f % 20) / 20f, Next(random) * 0.3f);
                for (int j = 0; j < Joints; j++)
                {
                    _joints[f * Joints + j] = Random(random, 90f);
                }

                for (int s = 0; s < Segments; s++)
                {
                    _heights[f * Segments + s] = 0.05f + 0.5f * (Next(random) + 1f);
                }
            }
        }

        [Test]
        public void BakedFrame_InATiltedWorld_IsFoundWithItsHeading([Values(0.3f, -2.4f, 3f)] float heading)
        {
            const int clip = 1;
            const int frame = 13;
            int at = _clips[clip].FirstFrame + frame;
            // The frame laid on a root of the heading, in a world whose gravity is tilted: what the ragdoll shows.
            Vector3 up = new Vector3(0.3f, 1f, -0.2f).normalized;
            Quaternion toWorld = Quaternion.Inverse(Quaternion.FromToRotation(up, Vector3.up));
            Quaternion turn = Quaternion.AngleAxis(heading * Mathf.Rad2Deg, Vector3.up);
            var world = new Quaternion[Segments];
            world[0] = toWorld * (turn * _pelvis[at]);
            for (int s = 1; s < Segments; s++)
            {
                world[s] = world[Parents[s]] * _joints[at * Joints + s - 1];
            }

            // The caller's view: gravity up, heading 0.
            Quaternion frameOf = Quaternion.FromToRotation(up, Vector3.up);
            var lying = new Quaternion[Segments];
            var heights = new float[Segments];
            for (int s = 0; s < Segments; s++)
            {
                lying[s] = frameOf * world[s];
                heights[s] = _heights[at * Segments + s];
            }

            var best = new List<GetUpMatch>();
            GetUpSearch.Nearest(_clips, _joints, _pelvis, _pelvisAt, _heights, Parents, lying, heights, best);

            Assert.That(best.Count, Is.EqualTo(2));
            Assert.That((best[0].Entry, best[0].Frame), Is.EqualTo((clip, frame)));
            Assert.That(best[0].Cost, Is.LessThan(1e-3f));
            Assert.That(Mathf.DeltaAngle(best[0].Heading * Mathf.Rad2Deg, heading * Mathf.Rad2Deg), Is.EqualTo(0f).Within(0.5f));
            // The root puts the frame's pelvis where the body's lies.
            var pelvis = new Vector3(2f, 0.4f, -1f);
            Vector3 root = best[0].Root(pelvis, 0f);
            Vector3 back = root + best[0].Turn * _pelvisAt[at];
            Assert.That(new Vector2(back.x - pelvis.x, back.z - pelvis.z).magnitude, Is.LessThan(1e-4f));
            Assert.That(best[1].Cost, Is.GreaterThan(best[0].Cost));
        }

        [Test]
        public void FramesPastTheLastStart_AreLeftOut()
        {
            int at = _clips[1].FirstFrame + 25;
            var lying = new Quaternion[Segments];
            var heights = new float[Segments];
            lying[0] = _pelvis[at];
            for (int s = 1; s < Segments; s++)
            {
                lying[s] = lying[Parents[s]] * _joints[at * Joints + s - 1];
            }

            for (int s = 0; s < Segments; s++)
            {
                heights[s] = _heights[at * Segments + s];
            }

            _clips[1] = new GetUpClipView { FirstFrame = 20, FrameCount = 30, LastStart = 20 };
            var best = new List<GetUpMatch>();
            GetUpSearch.Nearest(_clips, _joints, _pelvis, _pelvisAt, _heights, Parents, lying, heights, best);

            foreach (GetUpMatch match in best)
            {
                Assert.That(match.Entry == 0 || match.Frame <= 20, $"{match.Entry}:{match.Frame}");
            }
        }

        [Test]
        public void StandFrame_FirstAt95PercentNeverBelow90After()
        {
            float[] heights = { 0.1f, 0.2f, 0.5f, 0.85f, 0.9f, 0.96f, 0.88f, 0.97f, 1f };
            Assert.That(GetUpSearch.StandFrame(heights, 0, heights.Length), Is.EqualTo(7));
            float[] steady = { 9f, 0.1f, 0.5f, 0.96f, 0.92f, 0.97f, 1f };
            Assert.That(GetUpSearch.StandFrame(steady, 1, steady.Length - 1), Is.EqualTo(2));
        }

        private static float Next(System.Random random) => (float)(random.NextDouble() * 2.0 - 1.0);

        private static Quaternion Random(System.Random random, float degrees) =>
            Quaternion.AngleAxis(Next(random) * degrees,
                new Vector3(Next(random), Next(random), Next(random)).normalized + new Vector3(0f, 1e-3f, 0f));
    }
}
