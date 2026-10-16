using NUnit.Framework;

namespace Rubickanov.Rig.Tests
{
    public sealed class TurnOffsetTests
    {
        [Test]
        public void KeepsStillWhileBodyTurns()
        {
            var offset = new TurnOffset();
            offset.Counter(30f, 30f, 150f);
            Assert.AreEqual(-30f, offset.Value, 1e-4f);
            Assert.AreEqual(60f, offset.ErrorTo(30f), 1e-4f);
        }

        [Test]
        public void IsCarriedAlongPastMaxLag()
        {
            var offset = new TurnOffset();
            offset.Counter(-100f, 0f, 150f);
            offset.Counter(-60f, 0f, 150f);
            Assert.AreEqual(100f, offset.Value, 1e-4f);
        }

        [Test]
        public void StillCountersWhenThatReducesTheLag()
        {
            var offset = new TurnOffset();
            offset.Turn(170f);
            offset.Counter(20f, 0f, 150f);
            Assert.AreEqual(150f, offset.Value, 1e-4f);
        }

        [Test]
        public void WrapsAroundBehind()
        {
            var offset = new TurnOffset();
            offset.Turn(170f);
            offset.Turn(20f);
            Assert.AreEqual(-170f, offset.Value, 1e-4f);
            Assert.AreEqual(-10f, offset.ErrorTo(180f), 1e-4f);
        }

        [Test]
        public void SettlesTheShortWay()
        {
            var offset = new TurnOffset();
            offset.Turn(-170f);
            offset.Settle(0.5f);
            Assert.AreEqual(-85f, offset.Value, 1e-4f);
            offset.Settle(1f);
            Assert.AreEqual(0f, offset.Value, 1e-4f);
        }
    }
}
