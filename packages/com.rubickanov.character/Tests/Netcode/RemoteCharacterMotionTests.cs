using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Rubickanov.Characters.Netcode.Tests
{
    /// <summary>
    /// A remote character shown from snapshots that arrive late, unevenly, out of order or not at all still moves
    /// smoothly along its owner's path, without stalls or leaps, and replays a jump once; one that fell behind in a
    /// hitch is back on its delay soon.
    /// </summary>
    public sealed class RemoteCharacterMotionTests
    {
        private const float Speed = 5f;
        private const double SendInterval = 1.0 / 30;

        [TestCase(60f)]
        [TestCase(144f)]
        public void MovesSmoothlyOverAPoorNetwork(float frameRate)
        {
            var random = new System.Random(7);
            var body = new GameObject("RemoteBody").transform;
            try
            {
                var remote = new RemoteCharacterMotion(body, 0.1f, 5f);
                int jumps = 0;
                remote.Jumped += () => jumps++;

                // Sent every tick, the jump on its own step at 3 s; each arrives 20-80 ms later, one in 20 never.
                var inFlight = new List<(double Arrives, CharacterNetSnapshot Snapshot)>();
                for (double t = 0; t < 10; t += SendInterval)
                {
                    if (random.NextDouble() < 0.05)
                    {
                        continue;
                    }

                    var snapshot = new CharacterNetSnapshot
                    {
                        Time = t,
                        Position = PathAt(t),
                        Rotation = Quaternion.identity,
                        Aim = Vector3.forward,
                        Grounded = true,
                        Jumps = (byte)(t >= 3 ? 1 : 0),
                    };
                    inFlight.Add((t + 0.02 + 0.06 * random.NextDouble(), snapshot));
                }

                inFlight.Sort((a, b) => a.Arrives.CompareTo(b.Arrives));
                float dt = 1f / frameRate;
                int next = 0;
                Vector3 last = Vector3.zero;
                float worstError = 0f, slowest = float.PositiveInfinity, fastest = 0f;
                for (double now = 0; now < 9.5; now += dt)
                {
                    while (next < inFlight.Count && inFlight[next].Arrives <= now)
                    {
                        remote.Add(inFlight[next++].Snapshot);
                    }

                    remote.Update(dt);
                    // Judged once playback has settled behind the owner.
                    if (now > 1)
                    {
                        worstError = Mathf.Max(worstError, Vector3.Distance(body.position, PathAt(remote.Playback)));
                        float speed = Vector3.Distance(body.position, last) / dt;
                        slowest = Mathf.Min(slowest, speed);
                        fastest = Mathf.Max(fastest, speed);
                    }

                    last = body.position;
                }

                TestContext.WriteLine($"{frameRate} fps: off the path by {worstError * 100:F1} cm at most, " +
                          $"shown speed {slowest:F2}-{fastest:F2} m/s (moving at {Speed})");
                Assert.That(worstError, Is.LessThan(0.05f), "The copy stays on its owner's path.");
                Assert.That(slowest, Is.GreaterThan(Speed * 0.7f), "The copy never stalls.");
                Assert.That(fastest, Is.LessThan(Speed * 1.3f), "The copy never leaps.");
                Assert.That(jumps, Is.EqualTo(1), "The jump is replayed once.");
            }
            finally
            {
                Object.DestroyImmediate(body.gameObject);
            }
        }

        [TestCase(0.15)]
        [TestCase(0.4)]
        public void Update_FallsBehindInAHitch_CatchesUpWithinASecond(double hitch)
        {
            var body = new GameObject("RemoteBody").transform;
            try
            {
                var remote = new RemoteCharacterMotion(body, 0.1f, 5f);
                const float dt = 1f / 60;
                double sent = 0, now = 0, behindSince = -1;
                float fastest = 0f;
                Vector3 last = Vector3.zero;
                for (; now < 6; now += dt)
                {
                    while (sent <= now)
                    {
                        remote.Add(Snapshot(sent));
                        sent += SendInterval;
                    }

                    // At 3 s one frame takes the hitch on the wall clock, but the game's clock moves one frame only.
                    if (now < 3 && now + dt >= 3)
                    {
                        now += hitch;
                    }

                    remote.Update(dt);
                    if (now > 3.5 + hitch)
                    {
                        fastest = Mathf.Max(fastest, Vector3.Distance(body.position, last) / dt);
                    }

                    last = body.position;
                    // From the first frame after the hitch.
                    if (now > 3 + hitch + dt && behindSince < 0 && remote.Lead < 0.1 + SendInterval)
                    {
                        behindSince = now - 3 - hitch;
                    }
                }

                TestContext.WriteLine($"hitch {hitch * 1000:F0} ms: back on the delay after {behindSince * 1000:F0} ms, " +
                                      $"fastest after 0.5 s {fastest:F2} m/s (moving at {Speed})");
                Assert.That(behindSince, Is.InRange(0.0, 1.0), "Playback is back on its delay within a second.");
                Assert.That(fastest, Is.LessThan(Speed * 1.15f), "Half a second on, the catch-up no longer shows.");
            }
            finally
            {
                Object.DestroyImmediate(body.gameObject);
            }
        }

        private static CharacterNetSnapshot Snapshot(double time) => new()
        {
            Time = time,
            Position = PathAt(time),
            Rotation = Quaternion.identity,
            Aim = Vector3.forward,
            Grounded = true,
        };

        // Straight on at a steady speed, so the path is known at any time.
        private static Vector3 PathAt(double time) => new((float)(Speed * time), 0f, 0f);
    }
}
