using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.TestTools;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class StrippingTests
    {
        [Test]
        public void ConsoleCommandAttribute_IsAPreserveAttribute_SoStrippingKeepsCommands()
        {
            Assert.IsTrue(typeof(PreserveAttribute).IsAssignableFrom(typeof(ConsoleCommandAttribute)));
        }

        [Test]
        public void ConsoleCommandAttribute_RequiresItsUsages_SoStrippingKeepsItOnCommands()
        {
            Assert.IsNotNull(typeof(ConsoleCommandAttribute).GetCustomAttribute<RequireAttributeUsagesAttribute>());
        }

        [Test]
        public void Providers_AGameCanNameInAutoComplete_AreMarkedPreserve()
        {
            var unmarked = NameableProviders(typeof(IAutoCompleteProvider).Assembly)
                .Where(type => type.GetCustomAttribute<PreserveAttribute>() == null)
                .Select(type => type.Name)
                .ToList();

            Assert.IsEmpty(unmarked, "Providers without [Preserve]: " + string.Join(", ", unmarked));
        }

        [Test]
        public void RegisterTarget_ProviderWithoutTheConstructorItsArgumentsNeed_ErrorNamesPreserve()
        {
            var registry = new CommandRegistry();
            LogAssert.Expect(LogType.Error,
                new Regex(@"Failed to create provider NoStringConstructorProvider: .*\[Preserve\]"));

            registry.RegisterTarget(new NeedsMissingConstructor());

            Assert.IsTrue(registry.Execute("needsmissing x").Success);
        }

        // Public providers whose constructors an [AutoComplete] attribute can call: no arguments, or only strings.
        private static IEnumerable<Type> NameableProviders(Assembly assembly)
        {
            return assembly.GetTypes().Where(type =>
                type.IsPublic && !type.IsAbstract && typeof(IAutoCompleteProvider).IsAssignableFrom(type) &&
                type.GetConstructors().Any(ctor => ctor.GetParameters().All(p =>
                    p.ParameterType == typeof(string) || p.ParameterType == typeof(string[]))));
        }

        private sealed class NeedsMissingConstructor
        {
            [ConsoleCommand("needsmissing")]
            [AutoComplete(0, typeof(NoStringConstructorProvider), "an argument it cannot take")]
            public string Run(string value) => value;
        }

        private sealed class NoStringConstructorProvider : IAutoCompleteProvider
        {
            public string Hint => "<x>";

            public void GetSuggestions(string partial, List<string> results)
            {
            }
        }
    }
}
