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

            Assert.AreEqual(LoadStatus.Ok, result.Status);
            Assert.IsTrue(result.Success);
            Assert.IsFalse(result.Cancelled);
            Assert.IsNull(result.Error);
        }

        [Test]
        public void Cancel_HasCancelledTrueAndNullError()
        {
            var result = LoadResult.Cancel;

            Assert.AreEqual(LoadStatus.Cancelled, result.Status);
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Cancelled);
            Assert.IsNull(result.Error);
        }

        [Test]
        public void Fail_CarriesException()
        {
            var ex = new InvalidOperationException("boom");

            var result = LoadResult.Fail(ex);

            Assert.AreEqual(LoadStatus.Failed, result.Status);
            Assert.IsFalse(result.Success);
            Assert.IsFalse(result.Cancelled);
            Assert.AreSame(ex, result.Error);
        }

        [Test]
        public void ToString_RendersStatusAndError()
        {
            Assert.AreEqual("LoadResult(Ok)", LoadResult.Ok.ToString());
            Assert.AreEqual("LoadResult(Cancelled)", LoadResult.Cancel.ToString());

            var ex = new InvalidOperationException("boom");
            StringAssert.Contains("Failed", LoadResult.Fail(ex).ToString());
            StringAssert.Contains("boom", LoadResult.Fail(ex).ToString());
        }
    }
}
