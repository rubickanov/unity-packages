using NUnit.Framework;
using Unity.Mathematics;

namespace Rubickanov.Kinematics.Tests
{
    public sealed class SoleContactTests
    {
        private const double Length = 0.15;
        private const double MaxAngle = 30.0;

        [Test]
        public void LiesAlongAGentleSlope()
        {
            Assert.IsTrue(SoleContact.Fit(true, 0.1, true, 0.15, Length, MaxAngle, out double height, out double pitch));
            Assert.AreEqual(0.1, height, 1e-5);
            Assert.AreEqual(18.43, pitch, 0.01);
        }

        [Test]
        public void StandsOnTheBallAboveAStepUp()
        {
            Assert.IsTrue(SoleContact.Fit(true, 0.0, true, 0.18, Length, MaxAngle, out double height, out double pitch));
            Assert.AreEqual(0.18, height, 1e-5);
            Assert.AreEqual(0.0, pitch);
        }

        [Test]
        public void KeepsTheHeelOnTheStepAboveADropAhead()
        {
            Assert.IsTrue(SoleContact.Fit(true, 0.0, true, -0.18, Length, MaxAngle, out double height,
                out double pitch));
            Assert.AreEqual(0.0, height, 1e-5);
            Assert.AreEqual(0.0, pitch);
        }

        [Test]
        public void RestsOnWhicheverPointHasGround()
        {
            Assert.IsTrue(SoleContact.Fit(false, 0.0, true, 0.05, Length, MaxAngle, out double height,
                out double pitch));
            Assert.AreEqual(0.05, height, 1e-5);
            Assert.AreEqual(0.0, pitch);

            Assert.IsTrue(SoleContact.Fit(true, -0.1, false, 0.0, Length, MaxAngle, out height, out pitch));
            Assert.AreEqual(-0.1, height, 1e-5);
            Assert.AreEqual(0.0, pitch);
        }

        [Test]
        public void StaysLevelWithoutAFootLength()
        {
            Assert.IsTrue(SoleContact.Fit(true, 0.02, true, 0.04, 0.0, MaxAngle, out double height, out double pitch));
            Assert.AreEqual(0.04, height, 1e-5);
            Assert.AreEqual(0.0, pitch);
        }

        [Test]
        public void FindsNothingWithoutGround()
        {
            Assert.IsFalse(SoleContact.Fit(false, 0.0, false, 0.0, Length, MaxAngle, out double height,
                out double pitch));
            Assert.AreEqual(0.0, height);
            Assert.AreEqual(0.0, pitch);
        }

        [Test]
        public void PlacesTheSoleOnFlatGroundAtItsHeight()
        {
            var ground = new FlatGround(0.3);
            var up = new double3(0, 1, 0);
            Assert.IsTrue(SoleContact.Place(ref ground, new double3(1, 0.4, 2), new double3(1, 0.38, 2.15), up,
                double3.zero, 0.5, 0.5, MaxAngle, out SolePlacement sole));
            Assert.AreEqual(0.3, sole.Height, 1e-12);
            Assert.AreEqual(0.0, sole.Pitch, 1e-12);
            Assert.AreEqual(0.0, sole.Roll, 1e-12);
            Assert.IsFalse(sole.OnEdge);
            Assert.AreEqual(1.0, sole.Forward.z, 1e-12);
            Assert.Less(Rotation.Angle(Rotation.Identity, sole.Tilt(up)), 1e-12);
        }

        [Test]
        public void FindsNoFloorOutOfReach()
        {
            var ground = new FlatGround(-2.0);
            Assert.IsFalse(SoleContact.Place(ref ground, new double3(0, 0.1, 0), new double3(0, 0.1, 0.15),
                new double3(0, 1, 0), double3.zero, 0.5, 0.5, MaxAngle, out _));
        }
    }
}
