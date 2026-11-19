using NUnit.Framework;

namespace Rubickanov.Report.Tests
{
    public class ReportStartTests
    {
        [TestCase("agent", false, false, "playtest", ExpectedResult = "agent")]
        [TestCase("  release ", true, true, "playtest", ExpectedResult = "release")]
        [TestCase(null, true, false, "release", ExpectedResult = "agent")]
        [TestCase(null, false, true, "playtest", ExpectedResult = "agent")]
        [TestCase(null, false, false, "playtest", ExpectedResult = "playtest")]
        [TestCase("", false, false, " release ", ExpectedResult = "release")]
        [TestCase(null, false, false, "", ExpectedResult = "playtest")]
        public string Pick_FirstThatFits(string asked, bool batch, bool automated, string own) =>
            ReportEnvironment.Pick(asked, batch, automated, own);

        [Test]
        public void Allowed_OnlyOnAndAsked()
        {
            Assert.IsFalse(ReportStart.Allowed(false, true));
            Assert.IsFalse(ReportStart.Allowed(true, false));
            Assert.IsTrue(ReportStart.Allowed(true, true));
        }
    }
}
