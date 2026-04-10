using System;
using NUnit.Framework;

namespace Rubickanov.Loading.Tests
{
    [TestFixture]
    public class LoadResultTests
    {
        [Test]
        public void Ok_HasSuccessTrueAndNullError()
        {
            var result = LoadResult.Ok;

            Assert.IsTrue(result.Success);
            Assert.IsNull(result.Error);
        }

        [Test]
        public void Fail_CarriesException()
        {
            var ex = new InvalidOperationException("boom");

            var result = LoadResult.Fail(ex);

            Assert.IsFalse(result.Success);
            Assert.AreSame(ex, result.Error);
        }
    }
}
