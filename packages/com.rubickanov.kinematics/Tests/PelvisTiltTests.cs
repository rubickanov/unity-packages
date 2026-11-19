using NUnit.Framework;
using Unity.Mathematics;

namespace Rubickanov.Kinematics.Tests
{
    public sealed class PelvisTiltTests
    {
        private static readonly PelvisTiltSettings Settings = new(0.2, 0.5, 30.0, 0.4);

        [Test]
        public void LevelGroundLeavesThePelvis()
        {
            PelvisTilt tilt = PelvisTilt.Solve(0.0, 0.0, Settings);
            Assert.AreEqual(0.0, tilt.Lift);
            Assert.AreEqual(0.0, tilt.Roll);
        }

        [Test]
        public void RisesWithBothFeet()
        {
            PelvisTilt tilt = PelvisTilt.Solve(0.1, 0.1, Settings);
            Assert.AreEqual(0.1, tilt.Lift, 1e-12);
            Assert.AreEqual(0.0, tilt.Roll);
        }

        [Test]
        public void RaisesTheHipOverAStepWithoutStretchingEitherLeg()
        {
            PelvisTilt tilt = PelvisTilt.Solve(0.0, 0.2, Settings);
            Assert.AreEqual(22.5, tilt.Roll, 1e-9);

            double3 forward = new(0, 0, 1);
            double3 left = new double3(0, tilt.Lift, 0) + Rotation.Rotate(tilt.Turn(forward), new double3(-0.1, 0, 0));
            double3 right = new double3(0, tilt.Lift, 0) + Rotation.Rotate(tilt.Turn(forward), new double3(0.1, 0, 0));
            // The hip over the lower foot stays where the animation had it; the other rises, but less than its step.
            Assert.AreEqual(0.0, left.y, 1e-12);
            Assert.Greater(right.y, 0.05);
            Assert.Less(right.y, 0.2);
        }

        [Test]
        public void MirrorsForTheLeftFoot()
        {
            PelvisTilt right = PelvisTilt.Solve(0.0, 0.2, Settings);
            PelvisTilt left = PelvisTilt.Solve(0.2, 0.0, Settings);
            Assert.AreEqual(-right.Roll, left.Roll);
            Assert.AreEqual(right.Lift, left.Lift);
        }

        [Test]
        public void DropsToTheLowerFoot()
        {
            PelvisTilt tilt = PelvisTilt.Solve(-0.1, 0.0, Settings);
            double3 left = new double3(0, tilt.Lift, 0)
                + Rotation.Rotate(tilt.Turn(new double3(0, 0, 1)), new double3(-0.1, 0, 0));
            Assert.Greater(tilt.Roll, 0.0);
            Assert.AreEqual(-0.1, left.y, 1e-12);
        }

        [Test]
        public void KeepsWithinItsLimits()
        {
            var tight = new PelvisTiltSettings(0.2, 1.0, 8.0, 0.25);
            PelvisTilt step = PelvisTilt.Solve(0.0, 0.5, tight);
            Assert.AreEqual(8.0, step.Roll, 1e-12);
            PelvisTilt pit = PelvisTilt.Solve(-0.6, -0.6, tight);
            Assert.AreEqual(-0.25, pit.Lift, 1e-12);
        }
    }
}
