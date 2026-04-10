using System.Threading.Tasks;
using NUnit.Framework;

namespace Rubickanov.Loading.Tests
{
    [TestFixture]
    public class NullLoadingPresenterTests
    {
        [Test]
        public async Task AllMembers_AreNoOpAndDoNotThrow()
        {
            var presenter = new NullLoadingPresenter();

            await presenter.Show();
            presenter.SetProgress(0.5f);
            presenter.SetDescription("desc");
            presenter.SetError("err");
            await presenter.WaitForInput();
            await presenter.Hide();

            Assert.Pass();
        }
    }
}
