using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class AnimationTests
    {
        private ManualTime _time = null!;

        [SetUp]
        public void SetUp() => _time = new ManualTime();

        [TearDown]
        public void TearDown() => _time.Dispose();

        // ── Tween ────────────────────────────────────────────────

        [Test]
        public void Run_FramesGoBy_AppliesPartOfDurationEndingOnOne()
        {
            var applied = new List<float>();

            var run = Tween.Run(1f, applied.Add, CancellationToken.None);
            _time.Step(0.25f);
            _time.Step(0.5f);
            _time.Step(0.5f);

            CollectionAssert.AreEqual(new[] { 0f, 0.25f, 0.75f, 1f }, applied);
            Assert.AreEqual(UniTaskStatus.Succeeded, run.Status);
        }

        [Test]
        public void Run_ZeroDuration_AppliesOneAtOnce()
        {
            var applied = new List<float>();

            var run = Tween.Run(0f, applied.Add, CancellationToken.None);

            CollectionAssert.AreEqual(new[] { 1f }, applied);
            Assert.AreEqual(UniTaskStatus.Succeeded, run.Status);
        }

        [Test]
        public void Run_Cancelled_StopsWhereItIs()
        {
            var applied = new List<float>();
            using var cancel = new CancellationTokenSource();

            var run = Tween.Run(1f, applied.Add, cancel.Token);
            _time.Step(0.5f);
            cancel.Cancel();
            _time.Step(1f);

            CollectionAssert.AreEqual(new[] { 0f, 0.5f }, applied);
            Assert.AreEqual(UniTaskStatus.Canceled, run.Status);
        }

        [Test]
        public void Run_ClockIsUnscaled_TimeScaleZeroStillFinishes()
        {
            var timeScale = UnityEngine.Time.timeScale;
            UnityEngine.Time.timeScale = 0f;
            try
            {
                var run = Tween.Run(0.2f, _ => { }, CancellationToken.None);
                _time.Step(0.3f);

                Assert.AreEqual(UniTaskStatus.Succeeded, run.Status);
            }
            finally
            {
                UnityEngine.Time.timeScale = timeScale;
            }
        }

        // ── Fade ─────────────────────────────────────────────────

        [Test]
        public void FadeShow_FreshTarget_FromZeroToStyledOpacity()
        {
            var fade = new FadeAnimation(0.25f);
            var target = new VisualElement();

            var show = fade.PlayShowAsync(target, CancellationToken.None);
            var atStart = target.style.opacity.value;
            _time.Step(0.125f);
            var halfway = target.style.opacity.value;
            _time.Step(0.125f);

            Assert.AreEqual(0f, atStart);
            Assert.AreEqual(0.5f, halfway, 1e-4f);
            Assert.AreEqual(UniTaskStatus.Succeeded, show.Status);
            Assert.AreEqual(StyleKeyword.Null, target.style.opacity.keyword);
        }

        [Test]
        public void FadeHide_ShownTarget_ToZero()
        {
            var fade = new FadeAnimation(0.25f);
            var target = new VisualElement();

            var hide = fade.PlayHideAsync(target, CancellationToken.None);
            _time.Step(0.25f);

            Assert.AreEqual(0f, target.style.opacity.value);
            Assert.AreEqual(UniTaskStatus.Succeeded, hide.Status);
        }

        [Test]
        public void FadeShow_DuringHide_GoesBackUpFromWhereItIs()
        {
            var fade = new FadeAnimation(0.25f);
            var target = new VisualElement();
            using var hiding = new CancellationTokenSource();
            fade.PlayHideAsync(target, hiding.Token).Forget();
            _time.Step(0.1875f);
            hiding.Cancel();

            var show = fade.PlayShowAsync(target, CancellationToken.None);
            var atStart = target.style.opacity.value;
            _time.Step(0.125f);
            var midway = show.Status;
            _time.Step(0.0625f);

            Assert.AreEqual(0.25f, atStart, 1e-4f);
            Assert.AreEqual(UniTaskStatus.Pending, midway);
            Assert.AreEqual(UniTaskStatus.Succeeded, show.Status, "three quarters of the way take three quarters of the time");
        }

        [Test]
        public async Task Show_DuringFadeHide_ViewNeverDropsBelowWhereTheHideStood()
        {
            var root = TestRoot.Create();
            using var ui = new UIService(root, new RecordingUxmlLoader().Load);
            await ui.Register<HudA>();
            var view = ui.Get<HudA>();
            view.TestAnimation = new FadeAnimation(0.25f);
            var firstShow = ui.Show<HudA, FakeViewModel>(new FakeViewModel());
            _time.Step(0.25f);
            Assert.AreEqual(UniTaskStatus.Succeeded, firstShow.Status);
            ui.HideAsync<HudA>().Forget();
            _time.Step(0.125f);

            var secondShow = ui.Show<HudA, FakeViewModel>(new FakeViewModel());
            var lowest = view.Root.style.opacity.value;
            _time.Step(0.125f);
            Assert.AreEqual(UniTaskStatus.Succeeded, secondShow.Status);
            await secondShow;

            Assert.AreEqual(0.5f, lowest, 1e-4f);
            Assert.AreEqual(ViewState.Shown, view.State);
            Assert.AreEqual(StyleKeyword.Null, view.Root.style.opacity.keyword);
        }

        [Test]
        public void FadeReset_FadedTarget_ClearsInlineOpacity()
        {
            var target = new VisualElement();
            target.style.opacity = 0.5f;

            ViewAnimations.Fade.Reset(target);

            Assert.AreEqual(StyleKeyword.Null, target.style.opacity.keyword);
        }

        [Test]
        public void FadeShow_NullTarget_Throws()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await ViewAnimations.Fade.PlayShowAsync(null!, CancellationToken.None));
        }
    }
}
