using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Report.Tests
{
    public class OnceASessionTests
    {
        [Test]
        public void Check_SameFingerprintTwice_SecondIsSeen()
        {
            var once = new OnceASession(20, 10);

            Assert.AreEqual(OnceASession.Verdict.New, once.Check("a"));
            Assert.AreEqual(OnceASession.Verdict.Seen, once.Check("a"));
            Assert.AreEqual(OnceASession.Verdict.New, once.Check("b"));
            Assert.AreEqual(2, once.Count);
        }

        [Test]
        public void Check_OverCap_DropsNewKindsButStillKnowsOldOnes()
        {
            var once = new OnceASession(2, 10);
            once.Check("a");
            once.Check("b");

            Assert.AreEqual(OnceASession.Verdict.OverCap, once.Check("c"));
            Assert.AreEqual(OnceASession.Verdict.Seen, once.Check("a"));
            Assert.AreEqual(2, once.Count);
        }

        [Test]
        public void TakeFeedback_HasItsOwnCap_PastTheErrorsCap()
        {
            var once = new OnceASession(1, 2);
            once.Check("a");
            Assert.AreEqual(OnceASession.Verdict.OverCap, once.Check("b"));

            Assert.IsTrue(once.TakeFeedback());
            Assert.IsTrue(once.TakeFeedback());
            Assert.IsFalse(once.TakeFeedback());
        }

        [Test]
        public void BeforeSend_FromWorkerThreads_SameErrorGoesOnce()
        {
            var config = ScriptableObject.CreateInstance<ReportConfig>();
            using var reporter = new Reporter(config);
            var went = new ConcurrentBag<bool>();

            Parallel.For(0, 64, i =>
            {
                went.Add(reporter.BeforeSend(ReportTestEvents.Message($"[Water] Wave {i} lost under hull 0x{i:X8}"), null) != null);
            });

            Assert.AreEqual(1, went.Count(sent => sent));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void Fingerprint_Messages_ByFoldedTextWithoutChannelTag()
        {
            string editor = Fingerprint.Of(ReportTestEvents.Message("<b><color=#A1B2C3>[Water]</color></b> Wave 12 lost at 3.5 m")).Print;
            string player = Fingerprint.Of(ReportTestEvents.Message("14:03:09.007 f1234   E Water      Wave 7 lost at 0.25 m")).Print;
            string speaker = Fingerprint.Of(ReportTestEvents.Message("[Host] Wave 99 lost at 1 m")).Print;

            Assert.AreEqual("Wave # lost at # m", editor);
            Assert.AreEqual(editor, player);
            Assert.AreEqual(editor, speaker);
            Assert.AreNotEqual(editor, Fingerprint.Of(ReportTestEvents.Message("Sand 1 lost")).Print);
        }

        [Test]
        public void Fingerprint_Exceptions_ByTypeAndFirstFrameOfOurs()
        {
            SentryEventPair same = new(
                ReportTestEvents.Exception("System.NullReferenceException", "a", "Game.App.FrameLoop.Tick", "Game.Water.Waves.Step",
                    "UnityEngine.Transform.get_position"),
                ReportTestEvents.Exception("System.NullReferenceException", "b", "Game.App.FrameLoop.Tick", "Game.Water.Waves.Step",
                    "Rubickanov.Log.LogWriter.WriteException", "Sentry.Unity.Something.Do"));
            string first = Fingerprint.Of(same.A).Print;

            Assert.AreEqual("System.NullReferenceException|Game.Water.Waves.Step", first);
            Assert.AreEqual(first, Fingerprint.Of(same.B).Print);
            Assert.AreNotEqual(first, Fingerprint.Of(ReportTestEvents.Exception("System.NullReferenceException", "a",
                "Game.App.FrameLoop.Tick", "Game.Sand.Puffs.Step")).Print);
            Assert.AreEqual(ProblemKind.Exception, Fingerprint.Of(same.A).Kind);
            Assert.AreEqual(ProblemKind.Error, Fingerprint.Of(ReportTestEvents.Message("x")).Kind);
        }

        [Test]
        public void Fingerprint_Hang_IsOneKind()
        {
            var hang = ReportTestEvents.Exception("Sentry.Unity.Integrations.ApplicationNotRespondingException", "for 5000 ms");

            Assert.AreEqual((Fingerprint.Hang, ProblemKind.Hang), Fingerprint.Of(hang));
        }

        private readonly struct SentryEventPair
        {
            public SentryEventPair(Sentry.SentryEvent a, Sentry.SentryEvent b)
            {
                A = a;
                B = b;
            }

            public Sentry.SentryEvent A { get; }

            public Sentry.SentryEvent B { get; }
        }
    }
}
