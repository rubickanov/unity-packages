using System.Globalization;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class TryParseArgTests
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

        private class MyType
        {
            public string Value = "";
        }
    }
}
