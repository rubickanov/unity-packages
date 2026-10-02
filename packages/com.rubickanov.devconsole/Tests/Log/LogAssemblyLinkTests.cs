using System.Reflection;
using NUnit.Framework;
using UnityEngine.Scripting;

namespace Rubickanov.DevConsole.Log.Tests
{
    [TestFixture]
    public class LogAssemblyLinkTests
    {
        [Test]
        public void LogAssembly_NothingReferencesIt_IsMarkedAlwaysLink()
        {
            var assembly = typeof(LogChannelProvider).Assembly;

            Assert.IsNotNull(assembly.GetCustomAttribute<AlwaysLinkAssemblyAttribute>());
        }
    }
}
