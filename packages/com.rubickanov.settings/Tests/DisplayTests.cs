using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Settings.Tests
{
    /// <summary>What the display settings put on the screen.</summary>
    public sealed class DisplayTests
    {
        [Test]
        public void TargetFrameRate_CapCountsOnlyWithoutVsync()
        {
            Assert.That(DisplayApplier.TargetFrameRate(true, 60), Is.EqualTo(-1));
            Assert.That(DisplayApplier.TargetFrameRate(false, 60), Is.EqualTo(60));
            Assert.That(DisplayApplier.TargetFrameRate(false, 0), Is.EqualTo(-1));
        }

        [Test]
        public void Resolve_SizePastTheDesktopAndNativeWindow_FallBackToWhatFits()
        {
            var desktop = new ScreenSize(1920, 1080);
            var offered = new[] { new ScreenSize(1280, 720), new ScreenSize(1920, 1080), new ScreenSize(3840, 2160) };
            const FullScreenMode Windowed = FullScreenMode.Windowed;
            const FullScreenMode Full = FullScreenMode.FullScreenWindow;

            Assert.That(DisplayApplier.Resolve(new ScreenSize(1280, 720), Windowed, desktop, desktop, false, offered),
                Is.EqualTo(new ScreenSize(1280, 720)), "a size that fits is kept");
            Assert.That(DisplayApplier.Resolve(new ScreenSize(3840, 2160), Full, desktop, desktop, false, offered),
                Is.EqualTo(desktop), "a size past the desktop is native");
            Assert.That(DisplayApplier.Resolve(ScreenSize.Native, Full, desktop, desktop, false, offered), Is.EqualTo(desktop));
            Assert.That(DisplayApplier.Resolve(ScreenSize.Native, Windowed, desktop, desktop, false, offered),
                Is.EqualTo(new ScreenSize(1280, 720)), "a window from full screen is under the desktop");
            Assert.That(DisplayApplier.Resolve(ScreenSize.Native, Windowed, desktop, desktop, false, new ScreenSize[0]),
                Is.EqualTo(new ScreenSize(1536, 864)));
            Assert.That(DisplayApplier.Resolve(ScreenSize.Native, Windowed, desktop, new ScreenSize(1000, 600), true, offered),
                Is.EqualTo(new ScreenSize(1000, 600)), "a window that is one already stays as it is");
        }

        [Test]
        public void Distinct_ScreenSizes_ComeOnceEachSmallestFirst()
        {
            IReadOnlyList<ScreenSize> sizes = ScreenModes.Distinct(new[]
            {
                new ScreenSize(1920, 1080), new ScreenSize(1280, 720), new ScreenSize(1920, 1080), new ScreenSize(0, 0),
            });

            Assert.That(sizes, Is.EqualTo(new[] { new ScreenSize(1280, 720), new ScreenSize(1920, 1080) }));
        }

        [Test]
        public void TryParse_SizeTexts_ReadBothWays()
        {
            Assert.That(ScreenSize.TryParse("1920x1080", out ScreenSize size) && size == new ScreenSize(1920, 1080), Is.True);
            Assert.That(ScreenSize.TryParse("1280 × 720", out size) && size == new ScreenSize(1280, 720), Is.True);
            Assert.That(ScreenSize.TryParse("NATIVE", out size) && size.IsNative, Is.True);
            Assert.That(ScreenSize.TryParse("big", out _), Is.False);
            Assert.That(new ScreenSize(1920, 1080).ToString(), Is.EqualTo("1920x1080"));
        }

        [Test]
        public void Scale_Steps_DoubleEveryTwoAndAHalf()
        {
            Assert.That(LookSettings.Scale(5), Is.EqualTo(1f));
            Assert.That(LookSettings.Scale(0), Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(LookSettings.Scale(10), Is.EqualTo(4f).Within(1e-4f));
        }
    }
}
