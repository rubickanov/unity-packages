using NUnit.Framework;

namespace Rubickanov.GAS.Tests
{
    [TestFixture]
    public class GasDiagnosticsTests
    {
        [TearDown]
        public void TearDown()
        {
            // GasDiagnostics is process-wide static state; scrub it so a handler from one
            // test never fires inside another.
            GasDiagnostics.ResetSubscribers();
        }

        [Test]
        public void ResetSubscribers_AfterSubscribing_HandlerNoLongerFires()
        {
            // Domain-Reload-off safety net: a handler registered in a previous play session
            // must not survive into the next one, where it fires into destroyed objects.
            int calls = 0;
            GasDiagnostics.Warning += _ => calls++;

            GasDiagnostics.ResetSubscribers();
            GasDiagnostics.EmitWarning("after reset");

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void EmitWarning_WithSubscriber_DeliversMessage()
        {
            string? received = null;
            GasDiagnostics.Warning += message => received = message;

            GasDiagnostics.EmitWarning("aggregation failed");

            Assert.AreEqual("aggregation failed", received);
        }

        [Test]
        public void EmitWarning_WithNoSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => GasDiagnostics.EmitWarning("nobody listening"));
        }

        [Test]
        public void Warning_TwoSubscribers_BothReceive()
        {
            // Guards the field → event change: with a plain delegate field a second consumer
            // writing `Warning = handler` silently replaced the first one's subscription.
            int first = 0, second = 0;
            GasDiagnostics.Warning += _ => first++;
            GasDiagnostics.Warning += _ => second++;

            GasDiagnostics.EmitWarning("broadcast");

            Assert.AreEqual(1, first);
            Assert.AreEqual(1, second);
        }

        [Test]
        public void Warning_UnsubscribedHandler_StopsReceiving()
        {
            int calls = 0;
            void Handler(string _) => calls++;
            GasDiagnostics.Warning += Handler;
            GasDiagnostics.Warning -= Handler;

            GasDiagnostics.EmitWarning("after unsubscribe");

            Assert.AreEqual(0, calls);
        }
    }
}
