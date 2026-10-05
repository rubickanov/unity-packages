using System;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class StatPanelTests
    {
        private VisualElement _host = null!;
        private StatPanel _panel = null!;

        [SetUp]
        public void SetUp()
        {
            _host = new VisualElement();
            _panel = new StatPanel(_host, new StatPanelOptions { Interval = 0.25f });
        }

        [TearDown]
        public void TearDown() => _panel.Dispose();

        [TestCase(10.0, StatLevel.Good)]
        [TestCase(80.0, StatLevel.Warn)]
        [TestCase(149.9, StatLevel.Warn)]
        [TestCase(150.0, StatLevel.Bad)]
        [TestCase(double.NaN, StatLevel.Plain)]
        public void Judge_LowerIsBetter_LevelByLimits(double value, StatLevel expected)
        {
            Assert.AreEqual(expected, StatLimits.LowerIsBetter(80, 150).Judge(value));
        }

        [TestCase(144.0, StatLevel.Good)]
        [TestCase(50.0, StatLevel.Warn)]
        [TestCase(30.0, StatLevel.Bad)]
        [TestCase(12.0, StatLevel.Bad)]
        public void Judge_HigherIsBetter_LevelByLimits(double value, StatLevel expected)
        {
            Assert.AreEqual(expected, StatLimits.HigherIsBetter(50, 30).Judge(value));
        }

        [Test]
        public void Judge_NoLimits_Plain()
        {
            Assert.AreEqual(StatLevel.Plain, StatLimits.None.Judge(1e9));
        }

        [Test]
        public void LowerIsBetter_WarnAboveBad_Throws()
        {
            Assert.Throws<ArgumentException>(() => StatLimits.LowerIsBetter(150, 80));
            Assert.Throws<ArgumentException>(() => StatLimits.HigherIsBetter(30, 50));
        }

        [Test]
        public void Add_Number_ShowsFormatAndUnitWithLevelClass()
        {
            double ping = 42.4;

            var row = _panel.Add("ping", "PING", () => ping, "0", " ms", StatLimits.LowerIsBetter(80, 150));

            Assert.AreEqual("42 ms", row.Text);
            Assert.AreEqual(StatLevel.Good, row.Level);
            Assert.IsTrue(row.Element.Q<Label>(className: StatRow.ValueClassName).ClassListContains("stat-row__value--good"));
            Assert.AreEqual("PING", row.Element.Q<Label>(className: StatRow.LabelClassName).text);
        }

        [Test]
        public void Tick_BeforeInterval_KeepsOldValue_AfterInterval_ReadsNew()
        {
            double ping = 40;
            var row = _panel.Add("ping", "PING", () => ping, "0", " ms", StatLimits.LowerIsBetter(80, 150));
            _panel.Tick(1f);
            ping = 200;

            _panel.Tick(0.1f);
            string before = row.Text;
            _panel.Tick(0.2f);

            Assert.AreEqual("40 ms", before);
            Assert.AreEqual("200 ms", row.Text);
            Assert.AreEqual(StatLevel.Bad, row.Level);
            var value = row.Element.Q<Label>(className: StatRow.ValueClassName);
            Assert.IsFalse(value.ClassListContains("stat-row__value--good"));
            Assert.IsTrue(value.ClassListContains("stat-row__value--bad"));
        }

        [Test]
        public void Refresh_SameText_KeepsTheString()
        {
            double fps = 60.2;
            var row = _panel.Add("fps", "FPS", () => fps);
            string first = row.Text;
            fps = 59.9;

            _panel.Refresh();

            Assert.AreSame(first, row.Text);
        }

        [Test]
        public void Add_NaN_ShowsDashPlain()
        {
            var row = _panel.Add("loss", "LOSS", () => double.NaN, "0.0", "%", StatLimits.LowerIsBetter(1, 5));

            Assert.AreEqual("-", row.Text);
            Assert.AreEqual(StatLevel.Plain, row.Level);
        }

        [Test]
        public void AddFixed_ShowsTextWithoutLabel()
        {
            var row = _panel.AddFixed("version", "", "a1b2c3d");

            Assert.AreEqual("a1b2c3d", row.Text);
            Assert.IsNull(row.Element.Q<Label>(className: StatRow.LabelClassName));
        }

        [Test]
        public void AddText_Null_ShowsDash()
        {
            string? region = null;
            var row = _panel.AddText("region", "REGION", () => region);
            region = "eu";

            string before = row.Text;
            _panel.Refresh();

            Assert.AreEqual("-", before);
            Assert.AreEqual("eu", row.Text);
        }

        [Test]
        public void Shown_NoRowShown_HidesPanel()
        {
            var fps = _panel.Add("fps", "FPS", () => 60);

            fps.Shown = false;
            DisplayStyle hidden = _panel.Root.style.display.value;
            fps.Shown = true;

            Assert.AreEqual(DisplayStyle.None, hidden);
            Assert.AreEqual(DisplayStyle.Flex, _panel.Root.style.display.value);
            Assert.AreEqual(DisplayStyle.Flex, fps.Element.style.display.value);
        }

        [Test]
        public void Refresh_HiddenRow_NotRead()
        {
            int reads = 0;
            var row = _panel.Add("fps", "FPS", () => ++reads);
            row.Shown = false;

            _panel.Refresh();

            Assert.AreEqual(1, reads);
        }

        [Test]
        public void Add_SameId_Throws()
        {
            _panel.Add("fps", "FPS", () => 60);

            Assert.Throws<ArgumentException>(() => _panel.AddFixed("fps", "", "x"));
        }

        [Test]
        public void Remove_Row_LeavesPanelAndFind()
        {
            var row = _panel.Add("ping", "PING", () => 1);

            bool removed = _panel.Remove(row);

            Assert.IsTrue(removed);
            Assert.IsNull(_panel.Find("ping"));
            Assert.IsNull(row.Element.parent);
            Assert.AreEqual(DisplayStyle.None, _panel.Root.style.display.value);
        }

        [Test]
        public void Panel_TakesNoPointer()
        {
            var row = _panel.Add("fps", "FPS", () => 60);

            Assert.AreEqual(PickingMode.Ignore, _panel.Root.pickingMode);
            row.Element.Query<VisualElement>().ForEach(e => Assert.AreEqual(PickingMode.Ignore, e.pickingMode, e.name));
        }

        [Test]
        public void Dispose_LeavesHost()
        {
            _panel.Dispose();

            Assert.AreEqual(0, _host.childCount);
        }

        [Test]
        public void FrameTimes_WindowClosed_RateAndSlowest()
        {
            var frames = new FrameTimes(0.5);
            frames.Tick(0.1);
            double before = frames.PerSecond;

            for (int i = 0; i < 3; i++)
            {
                frames.Tick(0.1);
            }

            frames.Tick(0.2);

            Assert.IsNaN(before);
            Assert.AreEqual(5 / 0.6, frames.PerSecond, 1e-9);
            Assert.AreEqual(200.0, frames.SlowestMs, 1e-9);
        }
    }
}
