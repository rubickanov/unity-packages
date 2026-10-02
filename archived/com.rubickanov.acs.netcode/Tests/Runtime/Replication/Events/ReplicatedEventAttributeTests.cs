using System;
using System.Reflection;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime.Netcode;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class ReplicatedEventAttributeTests
    {
        // Aspect-shaped fixture that exercises every attribute-property default and override
        // path. Fields are only accessed via reflection in the tests below — their types
        // and names are load-bearing (the scanner inspects attributes on real aspect fields).
        private sealed class Aspect
        {
            [ReplicatedEvent]
            public readonly Subject<int> Default = new();

            [ReplicatedEvent(Authority = AuthorityMode.Owner)]
            public readonly Subject<int> OwnerAuth = new();

            [ReplicatedEvent(Reliability = Reliability.Unreliable)]
            public readonly Subject<int> UnreliableDelivery = new();

            [ReplicatedEvent(Authority = AuthorityMode.Owner, Reliability = Reliability.Unreliable)]
            public readonly Subject<int> OwnerUnreliable = new();
        }

        private static ReplicatedEventAttribute GetAttribute(string fieldName)
        {
            var field = typeof(Aspect).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(field, $"Aspect must declare field {fieldName}.");
            var attr = field!.GetCustomAttribute<ReplicatedEventAttribute>();
            Assert.IsNotNull(attr, $"Field {fieldName} must carry [ReplicatedEvent].");
            return attr!;
        }

        [Test]
        public void DefaultCtor_AuthorityIsServer_ReliabilityIsReliable()
        {
            var attr = GetAttribute(nameof(Aspect.Default));

            Assert.AreEqual(AuthorityMode.Server, attr.Authority);
            Assert.AreEqual(Reliability.Reliable, attr.Reliability);
        }

        [Test]
        public void AuthorityOverride_OwnerAuth_AppliesWithoutTouchingReliability()
        {
            var attr = GetAttribute(nameof(Aspect.OwnerAuth));

            Assert.AreEqual(AuthorityMode.Owner, attr.Authority);
            Assert.AreEqual(Reliability.Reliable, attr.Reliability);
        }

        [Test]
        public void ReliabilityOverride_Unreliable_AppliesWithoutTouchingAuthority()
        {
            var attr = GetAttribute(nameof(Aspect.UnreliableDelivery));

            Assert.AreEqual(AuthorityMode.Server, attr.Authority);
            Assert.AreEqual(Reliability.Unreliable, attr.Reliability);
        }

        [Test]
        public void BothOverrides_OwnerUnreliable_BothApplied()
        {
            var attr = GetAttribute(nameof(Aspect.OwnerUnreliable));

            Assert.AreEqual(AuthorityMode.Owner, attr.Authority);
            Assert.AreEqual(Reliability.Unreliable, attr.Reliability);
        }

        [Test]
        public void AttributeUsage_RestrictsToFieldTarget()
        {
            var usage = typeof(ReplicatedEventAttribute)
                .GetCustomAttribute<AttributeUsageAttribute>();

            Assert.IsNotNull(usage, "[ReplicatedEvent] must declare [AttributeUsage].");
            Assert.AreEqual(AttributeTargets.Field, usage!.ValidOn);
        }
    }
}
