using NUnit.Framework;

namespace Rubickanov.Kinematics.Tests
{
    public sealed class TurnOffsetTests
    {
        [Test]
        public void KeepsStillWhileBodyTurns()
        {
            var offset = new TurnOffset();
            offset.Counter(30.0, 30.0, 150.0);
            Assert.AreEqual(-30.0, offset.Value, 1e-4);
            Assert.AreEqual(60.0, offset.ErrorTo(30.0), 1e-4);
        }

        [Test]
        public void IsCarriedAlongPastMaxLag()
        {
            var offset = new TurnOffset();
            offset.Counter(-100.0, 0.0, 150.0);
            offset.Counter(-60.0, 0.0, 150.0);
            Assert.AreEqual(100.0, offset.Value, 1e-4);
        }

        [Test]
        public void StillCountersWhenThatReducesTheLag()
        {
            var offset = new TurnOffset();
            offset.Turn(170.0);
            offset.Counter(20.0, 0.0, 150.0);
            Assert.AreEqual(150.0, offset.Value, 1e-4);
        }

        [Test]
        public void WrapsAroundBehind()
        {
            var offset = new TurnOffset();
            offset.Turn(170.0);
            offset.Turn(20.0);
            Assert.AreEqual(-170.0, offset.Value, 1e-4);
            Assert.AreEqual(-10.0, offset.ErrorTo(180.0), 1e-4);
        }

        [Test]
        public void SettlesTheShortWay()
        {
            var offset = new TurnOffset();
            offset.Turn(-170.0);
            offset.Settle(0.5);
            Assert.AreEqual(-85.0, offset.Value, 1e-4);
            offset.Settle(1.0);
            Assert.AreEqual(0.0, offset.Value, 1e-4);
        }
    }
}
