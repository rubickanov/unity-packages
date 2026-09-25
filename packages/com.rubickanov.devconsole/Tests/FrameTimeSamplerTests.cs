using NUnit.Framework;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class FrameTimeSamplerTests
    {
        [SetUp]
        public void SetUp() => FrameTimeSampler.Clear();

        [TearDown]
        public void TearDown() => FrameTimeSampler.Clear();

        [Test]
        public void Stats_CoverOnlyTheLastWindow()
        {
            // An old slow frame, then steady 40 fps ending in one 20 fps hitch. Frames are taken newest first until
            // they add up to the window: the hitch plus 38 frames make 1 s, past 0.99 s
            FrameTimeSampler.Record(1f);
            for (int i = 0; i < 60; i++) FrameTimeSampler.Record(0.025f);
            FrameTimeSampler.Record(0.05f);

            Assert.IsTrue(FrameTimeSampler.TryGetStats(0.99f, out var average, out var min, out var max, out var frames));
            Assert.AreEqual(39, frames);
            Assert.AreEqual(39f, average, 0.01f);
            Assert.AreEqual(20f, min, 0.01f);
            Assert.AreEqual(40f, max, 0.01f);
        }

        [Test]
        public void Stats_NothingRecorded_IsFalse()
        {
            Assert.IsFalse(FrameTimeSampler.TryGetStats(1f, out _, out _, out _, out _));
        }
    }
}
