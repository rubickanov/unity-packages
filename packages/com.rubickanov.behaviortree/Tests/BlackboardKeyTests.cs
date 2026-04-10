using NUnit.Framework;
using Rubickanov.BehaviorTree.Runtime;

namespace Rubickanov.BehaviorTree.Tests
{
    [TestFixture]
    public class BlackboardKeyTests
    {
        [Test]
        public void Constructor_SetsName()
        {
            var key = new BlackboardKey<int>("health");

            Assert.AreEqual("health", key.Name);
        }

        [Test]
        public void ToString_ReturnsName()
        {
            var key = new BlackboardKey<float>("speed");

            Assert.AreEqual("speed", key.ToString());
        }
    }
}
