using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Report.Tests
{
    public class FeedbackLinkTests
    {
        private ReportConfig _config;
        private Reporter _reporter;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<ReportConfig>();
            _reporter = new Reporter(_config);
        }

        [TearDown]
        public void TearDown()
        {
            _reporter.Dispose();
            Object.DestroyImmediate(_config);
        }

        [Test]
        public void FeedbackOf_NoProblem_Standalone()
        {
            Assert.IsNull(_reporter.FeedbackOf("the boat sank").AssociatedEventId);
        }

        [Test]
        public void FeedbackOf_AfterErrors_LinksToTheLastThatWentNotADuplicate()
        {
            var first = ReportTestEvents.Message("Wave 1 lost");
            var second = ReportTestEvents.Exception("System.InvalidOperationException", "x", "Game.Boats.Hull.Sink");
            var duplicate = ReportTestEvents.Message("Wave 2 lost");
            Assert.IsNotNull(_reporter.BeforeSend(first, null));
            Assert.IsNotNull(_reporter.BeforeSend(second, null));
            Assert.IsNull(_reporter.BeforeSend(duplicate, null));

            Assert.AreEqual(second.EventId, _reporter.FeedbackOf("sank").AssociatedEventId);
            Assert.AreEqual(ProblemKind.Exception, _reporter.LastProblem?.Kind);
        }

        [Test]
        public void FeedbackOf_HangWhileBusy_DroppedAndNotLinked()
        {
            var error = ReportTestEvents.Message("Wave 1 lost");
            _reporter.BeforeSend(error, null);
            _reporter.Busy(true);

            Assert.IsNull(_reporter.BeforeSend(ReportTestEvents.Exception("Sentry.Unity.Integrations.ApplicationNotRespondingException", "5000 ms"), null));
            Assert.AreEqual(error.EventId, _reporter.FeedbackOf(null).AssociatedEventId);
        }

        [Test]
        public void FeedbackOf_EmptyNote_SaysSo()
        {
            Assert.AreEqual(Reporter.NoNote, _reporter.FeedbackOf("  ").Message);
            Assert.AreEqual("sank", _reporter.FeedbackOf(" sank ").Message);
        }
    }
}
