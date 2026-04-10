using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Config.Tests
{
    [TestFixture]
    public class ConfigBaseTests
    {
        private readonly List<ConfigBase> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var so in _created)
                Object.DestroyImmediate(so);
            _created.Clear();
        }

        private T Create<T>() where T : ConfigBase
        {
            var so = ScriptableObject.CreateInstance<T>();
            _created.Add(so);
            return so;
        }

        [Test]
        public void Validate_DefaultImplementation_ReturnsTrue()
        {
            var config = Create<DefaultConfig>();

            Assert.IsTrue(config.Validate());
        }

        [Test]
        public void Validate_OverriddenToReturnFalse_ReturnsFalse()
        {
            var config = Create<AlwaysInvalidConfig>();

            Assert.IsFalse(config.Validate());
        }

        private class DefaultConfig : ConfigBase { }

        private class AlwaysInvalidConfig : ConfigBase
        {
            public override bool Validate() => false;
        }
    }
}
