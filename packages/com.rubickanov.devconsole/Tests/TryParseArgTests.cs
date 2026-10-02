using System.Globalization;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public partial class TryParseArgTests
    {
        private CommandRegistry _registry = null!;

        [SetUp]
        public void SetUp() => _registry = new CommandRegistry();

        [Test]
        public void TryParseArg_String_ReturnsInputUnchanged()
        {
            Assert.IsTrue(_registry.TryParseArg("hello", typeof(string), out var result));
            Assert.AreEqual("hello", result);
        }

        [Test]
        public void TryParseArg_Int_ParsesDecimal()
        {
            Assert.IsTrue(_registry.TryParseArg("42", typeof(int), out var result));
            Assert.AreEqual(42, result);
        }

        [Test]
        public void TryParseArg_Float_UsesInvariantCulture()
        {
            var prev = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
            try
            {
                Assert.IsTrue(_registry.TryParseArg("1.5", typeof(float), out var result));
                Assert.AreEqual(1.5f, (float)result!);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = prev;
            }
        }

        [Test]
        public void TryParseArg_Bool_ParsesTrueFalse()
        {
            Assert.IsTrue(_registry.TryParseArg("true", typeof(bool), out var truthy));
            Assert.AreEqual(true, truthy);

            Assert.IsTrue(_registry.TryParseArg("false", typeof(bool), out var falsy));
            Assert.AreEqual(false, falsy);
        }

        [Test]
        public void TryParseArg_Enum_IsCaseInsensitive()
        {
            Assert.IsTrue(_registry.TryParseArg("monday", typeof(System.DayOfWeek), out var result));
            Assert.AreEqual(System.DayOfWeek.Monday, result);
        }

        [Test]
        public void TryParseArg_Vector3WithSpacesAroundCommas_ParsesCorrectly()
        {
            Assert.IsTrue(_registry.TryParseArg("1, 2, 3", typeof(Vector3), out var result));
            Assert.AreEqual(new Vector3(1, 2, 3), result);
        }

        [Test]
        public void TryParseArg_InvalidInt_ReturnsFalse()
        {
            Assert.IsFalse(_registry.TryParseArg("not-a-number", typeof(int), out var result));
            Assert.IsNull(result);
        }

        [Test]
        public void TryParseArg_CustomParser_TakesPrecedenceOverBuiltin()
        {
            _registry.RegisterParser<int>(_ => (true, 999));

            Assert.IsTrue(_registry.TryParseArg("1", typeof(int), out var result));
            Assert.AreEqual(999, result);
        }

        [Test]
        public void TryParseArg_CustomParserFails_StaysFalseDoesNotFallthrough()
        {
            _registry.RegisterParser<int>(_ => (false, 0));

            Assert.IsFalse(_registry.TryParseArg("42", typeof(int), out var result));
            Assert.AreEqual(0, result);
        }

        [Test]
        public void TryParseArg_CustomTypeWithRegisteredParser_ParsesViaDelegate()
        {
            _registry.RegisterParser<MyType>(input => (true, new MyType { Value = input }));

            Assert.IsTrue(_registry.TryParseArg("hello", typeof(MyType), out var result));
            Assert.AreEqual("hello", ((MyType)result!).Value);
        }

        [TestCase("abc", typeof(int))]
        [TestCase("99999999999", typeof(int))]
        [TestCase("1.2.3", typeof(float))]
        [TestCase("-1", typeof(ulong))]
        [TestCase("x", typeof(long))]
        [TestCase("yes", typeof(bool))]
        [TestCase("Purple", typeof(KeyCode))]
        [TestCase("1,x,3", typeof(Vector3))]
        public void TryParseArg_InvalidInput_ReturnsFalse(string input, System.Type type)
        {
            Assert.IsFalse(_registry.TryParseArg(input, type, out _));
        }

        [TestCase("Space", KeyCode.Space)]
        [TestCase("space", KeyCode.Space)]
        [TestCase("32", KeyCode.Space)]
        public void TryParseArg_EnumNameOrNumber_Parses(string input, KeyCode expected)
        {
            Assert.IsTrue(_registry.TryParseArg(input, typeof(KeyCode), out var result));
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void TryParseArg_FlagsEnumCommaList_CombinesTheValues()
        {
            Assert.IsTrue(_registry.TryParseArg("ctrl,shift", typeof(KeyModifiers), out var result));
            Assert.AreEqual(KeyModifiers.Ctrl | KeyModifiers.Shift, result);
        }

        [Test]
        public void TryParseArg_FloatWithThousandsSeparator_ParsesAsBefore()
        {
            Assert.IsTrue(_registry.TryParseArg("1,000.5", typeof(float), out var result));
            Assert.AreEqual(1000.5f, (float)result!, 1e-3f);
        }

        [Test]
        public void TryParseArg_CustomParserThrows_ReturnsFalse()
        {
            _registry.RegisterParser<MyType>(_ => throw new System.InvalidOperationException("no players loaded"));

            Assert.IsFalse(_registry.TryParseArg("bob", typeof(MyType), out _));
        }

        [Test]
        public void Execute_CustomParserThrows_ReturnsAnErrorInsteadOfThrowing()
        {
            _registry.RegisterParser<MyType>(_ => throw new System.InvalidOperationException("no players loaded"));
            _registry.RegisterTarget(new Kick());

            var result = _registry.Execute("kick bob");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("bob", result.Message ?? "");
        }

        private class MyType
        {
            public string Value = "";
        }

        private partial class Kick
        {
            [ConsoleCommand("kick")]
            public void Run(MyType player) { }
        }
    }
}
