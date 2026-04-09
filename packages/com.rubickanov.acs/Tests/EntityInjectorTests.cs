using NUnit.Framework;
using Rubickanov.ACS.Runtime;
using UnityEngine;

namespace Rubickanov.ACS.Tests
{
    [TestFixture]
    public class EntityInjectorTests
    {
        [TearDown]
        public void TearDown()
        {
            // EntityInjector.Inject is static global state — reset so tests don't leak into each other.
            EntityInjector.Inject = null;
        }

        [Test]
        public void Inject_WhenSet_DelegateReceivesGameObject()
        {
            // Arrange
            GameObject received = null;
            EntityInjector.Inject = go => received = go;
            var expected = new GameObject(nameof(EntityInjectorTests));

            try
            {
                // Act
                EntityInjector.Inject.Invoke(expected);

                // Assert
                Assert.AreSame(expected, received);
            }
            finally
            {
                Object.DestroyImmediate(expected);
            }
        }
    }
}
