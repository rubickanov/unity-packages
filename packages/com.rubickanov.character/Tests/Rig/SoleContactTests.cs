using NUnit.Framework;

namespace Rubickanov.Rig.Tests
{
    public sealed class SoleContactTests
    {
        private const float Length = 0.15f;
        private const float MaxAngle = 30f;

        [Test]
        public void LiesAlongAGentleSlope()
        {
            Assert.IsTrue(SoleContact.Fit(0.1f, 0.15f, Length, MaxAngle, out float height, out float pitch));
            Assert.AreEqual(0.1f, height, 1e-5f);
            Assert.AreEqual(18.43f, pitch, 0.01f);
        }

        [Test]
        public void StandsOnTheBallAboveAStepUp()
        {
            Assert.IsTrue(SoleContact.Fit(0f, 0.18f, Length, MaxAngle, out float height, out float pitch));
            Assert.AreEqual(0.18f, height, 1e-5f);
            Assert.AreEqual(0f, pitch);
        }

        [Test]
        public void KeepsTheHeelOnTheStepAboveADropAhead()
        {
            Assert.IsTrue(SoleContact.Fit(0f, -0.18f, Length, MaxAngle, out float height, out float pitch));
            Assert.AreEqual(0f, height, 1e-5f);
            Assert.AreEqual(0f, pitch);
        }

        [Test]
        public void RestsOnWhicheverPointHasGround()
        {
            Assert.IsTrue(SoleContact.Fit(null, 0.05f, Length, MaxAngle, out float height, out float pitch));
            Assert.AreEqual(0.05f, height, 1e-5f);
            Assert.AreEqual(0f, pitch);

            Assert.IsTrue(SoleContact.Fit(-0.1f, null, Length, MaxAngle, out height, out pitch));
            Assert.AreEqual(-0.1f, height, 1e-5f);
            Assert.AreEqual(0f, pitch);
        }

        [Test]
        public void StaysLevelWithoutAFootLength()
        {
            Assert.IsTrue(SoleContact.Fit(0.02f, 0.04f, 0f, MaxAngle, out float height, out float pitch));
            Assert.AreEqual(0.04f, height, 1e-5f);
            Assert.AreEqual(0f, pitch);
        }

        [Test]
        public void FindsNothingWithoutGround()
        {
            Assert.IsFalse(SoleContact.Fit(null, null, Length, MaxAngle, out float height, out float pitch));
            Assert.AreEqual(0f, height);
            Assert.AreEqual(0f, pitch);
        }
    }
}
