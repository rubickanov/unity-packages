using NUnit.Framework;
using Rubickanov.ACS.Runtime.Netcode;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class NetworkScopeScannerTests
    {
        // As with ReplicationScanner, NetworkScopeScanner caches results per Type across
        // the test run. Each test uses its own unique nested type to avoid cross-test
        // pollution via the static cache.

        [Test]
        public void GetScope_TypeWithoutAttribute_ReturnsEverywhereAsDefault()
        {
            // Default must be Everywhere — changing this default would silently
            // disable observers/VFX on non-authority peers.
            var scope = NetworkScopeScanner.GetScope(typeof(Plain));
            Assert.AreEqual(NetworkScope.Everywhere, scope);
        }

        [Test]
        public void GetScope_TypeMarkedServerOnly_ReturnsServerOnly()
        {
            var scope = NetworkScopeScanner.GetScope(typeof(ExplicitServerOnly));
            Assert.AreEqual(NetworkScope.ServerOnly, scope);
        }

        [Test]
        public void GetScope_TypeMarkedOwnerOnly_ReturnsOwnerOnly()
        {
            var scope = NetworkScopeScanner.GetScope(typeof(ExplicitOwnerOnly));
            Assert.AreEqual(NetworkScope.OwnerOnly, scope);
        }

        [Test]
        public void GetScope_CalledTwiceOnSameType_ReturnsSameScope()
        {
            // The cache must not corrupt the returned value on the second call.
            var first = NetworkScopeScanner.GetScope(typeof(CachedScope));
            var second = NetworkScopeScanner.GetScope(typeof(CachedScope));
            Assert.AreEqual(first, second);
            Assert.AreEqual(NetworkScope.ServerOnly, second);
        }

        [Test]
        public void GetScope_DerivedType_InheritsBaseClassScopeAttribute()
        {
            // NetworkScopeAttribute is declared with Inherited = true; the scanner reads
            // it via GetCustomAttribute<...>(inherit: true). A derived class without its
            // own attribute must pick up the base class declaration.
            var scope = NetworkScopeScanner.GetScope(typeof(DerivedFromServerOnlyBase));
            Assert.AreEqual(NetworkScope.ServerOnly, scope);
        }

        [Test]
        public void GetScope_DerivedType_OverridesBaseClassScopeAttribute()
        {
            // If the derived class also has [NetworkScope(...)], its own attribute wins
            // over the base (standard GetCustomAttribute inherit=true semantics).
            var scope = NetworkScopeScanner.GetScope(typeof(DerivedOwnerOnlyOverBase));
            Assert.AreEqual(NetworkScope.OwnerOnly, scope);
        }

        // ---- Test fixtures ------------------------------------------------------

        private class Plain { }

        [NetworkScope(NetworkScope.ServerOnly)]
        private class ExplicitServerOnly { }

        [NetworkScope(NetworkScope.OwnerOnly)]
        private class ExplicitOwnerOnly { }

        [NetworkScope(NetworkScope.ServerOnly)]
        private class CachedScope { }

        [NetworkScope(NetworkScope.ServerOnly)]
        private class ServerOnlyBase { }

        private class DerivedFromServerOnlyBase : ServerOnlyBase { }

        [NetworkScope(NetworkScope.OwnerOnly)]
        private class DerivedOwnerOnlyOverBase : ServerOnlyBase { }
    }
}
