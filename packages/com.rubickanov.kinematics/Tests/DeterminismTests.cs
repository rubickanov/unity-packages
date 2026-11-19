using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Rubickanov.Kinematics.Tests
{
    /// <summary>
    /// The pose maths on a fixed set of cases, run by Burst in its deterministic mode and by Mono: both give the same
    /// bits, and the same hash on every machine.
    /// </summary>
    public sealed class DeterminismTests
    {
        // Mac arm64 and Windows x64, Burst and Mono alike.
        private const ulong GoldenHash = 0xD200FB997F4A699EUL;

        [Test]
        public void BurstAndMonoGiveTheSameBits()
        {
            using var burst = new NativeArray<double>(PoseCasesJob.Size, Allocator.TempJob);
            using var mono = new NativeArray<double>(PoseCasesJob.Size, Allocator.TempJob);
            new PoseCasesJob { Out = burst }.Run();
            new PoseCasesJob { Out = mono }.Execute();

            Assert.AreEqual(0.0, burst[0], "the job did not run in Burst");
            Assert.AreEqual(1.0, mono[0]);
            Assert.Greater(burst[1], 10000.0, "too few values written");
            Assert.LessOrEqual(burst[1], PoseCasesJob.Size, "more values than slots");
            Assert.AreEqual(burst[1], mono[1]);
            for (var i = 2; i < (int)burst[1]; i++)
            {
                Assert.AreEqual(math.asulong(burst[i]), math.asulong(mono[i]), $"value {i}: {burst[i]} vs {mono[i]}");
            }
        }

        [Test]
        public void MatchesTheGoldenHash()
        {
            using var values = new NativeArray<double>(PoseCasesJob.Size, Allocator.TempJob);
            new PoseCasesJob { Out = values }.Run();
            ulong hash = Hash(values);
            TestContext.WriteLine($"pose cases hash {hash:X16}, {(int)values[1]} values");
            Assert.AreEqual(GoldenHash, hash, $"hash {hash:X16}");
        }

        private static ulong Hash(NativeArray<double> values)
        {
            ulong h = 14695981039346656037UL;
            for (var i = 1; i < (int)values[1]; i++)
            {
                h = (h ^ math.asulong(values[i])) * 1099511628211UL;
            }

            return h;
        }

        /// <summary>Runs every piece of the pose maths on generated cases and writes what comes out: slot 0 is 1
        /// under Mono and 0 under Burst, slot 1 the number of slots written.</summary>
        [BurstCompile(FloatMode = FloatMode.Deterministic, CompileSynchronously = true)]
        private struct PoseCasesJob : IJob
        {
            public const int Size = 40000;
            public NativeArray<double> Out;

            private int _count;
            private ulong _seed;

            public void Execute()
            {
                var managed = false;
                MarkManaged(ref managed);
                Out[0] = managed ? 1.0 : 0.0;
                _count = 2;
                _seed = 0x5EED5EEDUL;

                for (var i = 0; i < 400; i++)
                {
                    double x = Uniform(-40.0, 40.0);
                    DetMath.SinCos(x, out double s, out double c);
                    Put(s);
                    Put(c);
                    Put(DetMath.Atan2(Uniform(-3.0, 3.0), Uniform(-3.0, 3.0)));
                    Put(DetMath.Acos(Uniform(-1.0, 1.0)));
                    Put(DetMath.Asin(Uniform(-1.0, 1.0)));
                    Put(DetMath.Exp(Uniform(-30.0, 30.0)));
                    Put(Rotation.Slerp(Turn(), Turn(), Uniform(0.0, 1.0)));
                    Put(Rotation.FromTo(Vector(1.0), Vector(1.0)));
                }

                for (var i = 0; i < 300; i++)
                {
                    var leg = new TwoBoneChain(Vector(0.3) + new double3(0.0, 1.0, 0.0),
                        Vector(0.1) + new double3(0.0, 0.5, 0.05), Vector(0.1), Turn(), Turn(), Turn());
                    TwoBoneChain solved = TwoBoneIK.Solve(leg, Vector(1.2), Vector(2.0), Uniform(-0.2, 1.2),
                        Uniform(0.0, 0.1));
                    Put(solved.LowerPosition);
                    Put(solved.EndPosition);
                    Put(solved.UpperRotation);
                    Put(solved.LowerRotation);
                }

                var up = new double3(0.0, 1.0, 0.0);
                for (var i = 0; i < 300; i++)
                {
                    var ground = new FlatGround(Uniform(-0.3, 0.3));
                    double3 ankle = Vector(0.5);
                    bool found = SoleContact.Place(ref ground, ankle, ankle + Vector(0.2), up, Vector(0.1), 0.5, 0.5,
                        30.0, out SolePlacement sole);
                    Put(found ? 1.0 : 0.0);
                    Put(sole.Height);
                    Put(sole.Pitch);
                    Put(sole.Roll);
                    Put(sole.Tilt(up));
                    SoleContact.Fit(Uniform(0.0, 1.0) > 0.2, Uniform(-0.2, 0.2), Uniform(0.0, 1.0) > 0.2,
                        Uniform(-0.2, 0.2), Uniform(0.0, 0.2), 30.0, out double height, out double pitch);
                    Put(height);
                    Put(pitch);
                }

                for (var i = 0; i < 200; i++)
                {
                    var settings = new SpringSettings(Uniform(0.0, 8.0), Uniform(0.0, 3.0));
                    var scalar = new ScalarSpring();
                    var vector = new VectorSpring();
                    double dt = Uniform(0.001, 0.2);
                    for (var k = 0; k < 10; k++)
                    {
                        Put(scalar.StepAngle(Uniform(-180.0, 180.0), settings, dt));
                        Put(vector.Step(Vector(1.0), settings, dt));
                    }
                }

                var pinSettings = new LimbPinSettings(0.04, 0.04, 0.2, 0.25, 0.08, 0.15);
                for (var i = 0; i < 50; i++)
                {
                    var pin = new LimbPin();
                    double3 contact = Vector(0.1);
                    for (var k = 0; k < 40; k++)
                    {
                        contact += Vector(0.04);
                        if (k == 30)
                        {
                            pin.Release(pinSettings, contact, up);
                        }

                        Put(pin.Update(contact, Uniform(-0.02, 0.12), up, pinSettings, 1.0 / 60.0));
                    }
                }

                var lower = new LookRange(40.0, 40.0, 35.0, 45.0);
                var upper = new LookRange(40.0, 40.0, 15.0, 25.0);
                var tiltSettings = new PelvisTiltSettings(0.2, 0.5, 15.0, 0.4);
                var offset = new TurnOffset();
                for (var i = 0; i < 300; i++)
                {
                    NeckPose neck = HeadLook.Toward(Turn(), Vector(0.1), Vector(3.0), lower, upper);
                    Put(neck.Lower);
                    Put(neck.Upper);
                    Put(neck.Aim.Yaw);
                    Put(neck.Aim.Pitch);
                    PelvisTilt tilt = PelvisTilt.Solve(Uniform(-0.3, 0.3), Uniform(-0.3, 0.3), tiltSettings);
                    Put(tilt.Lift);
                    Put(tilt.Turn(new double3(0.0, 0.0, 1.0)));
                    offset.Counter(Uniform(-60.0, 60.0), Uniform(-180.0, 180.0), 150.0);
                    offset.Turn(Uniform(-30.0, 30.0));
                    offset.Settle(Uniform(0.0, 0.3));
                    Put(offset.Value);
                    Put(AimAngles.FromLocal(Vector(1.0)).Limit(75.0, 45.0, 60.0).Yaw);
                }

                Out[1] = _count;
            }

            [BurstDiscard]
            private static void MarkManaged(ref bool managed) => managed = true;

            private double Uniform(double min, double max)
            {
                _seed = _seed * 6364136223846793005UL + 1442695040888963407UL;
                double unit = (_seed >> 11) * (1.0 / 9007199254740992.0);
                return min + (max - min) * unit;
            }

            private double3 Vector(double size) =>
                new(Uniform(-size, size), Uniform(-size, size), Uniform(-size, size));

            private double4 Turn() => Rotation.AxisAngle(Vector(1.0), Uniform(-DetMath.Pi, DetMath.Pi));

            private void Put(double value)
            {
                if (_count < Size)
                {
                    Out[_count] = value;
                }

                _count++;
            }

            private void Put(double3 v)
            {
                Put(v.x);
                Put(v.y);
                Put(v.z);
            }

            private void Put(double4 q)
            {
                Put(q.x);
                Put(q.y);
                Put(q.z);
                Put(q.w);
            }
        }
    }
}
