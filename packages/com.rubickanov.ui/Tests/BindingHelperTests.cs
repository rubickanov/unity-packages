using NUnit.Framework;
using R3;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class BindingHelperTests
    {
        private UIService _ui = null!;
        private ReactiveProperty<int> _speed = null!;
        private ReactiveProperty<bool> _visible = null!;
        private ReactiveProperty<bool> _warning = null!;
        private BindingView _view = null!;

        [SetUp]
        public void SetUp()
        {
            _ui = new UIService(TestRoot.Create(), new RecordingUxmlLoader().Load);
            _speed = new ReactiveProperty<int>(1);
            _visible = new ReactiveProperty<bool>(true);
            _warning = new ReactiveProperty<bool>(false);
            _ui.Register<BindingView>().GetAwaiter().GetResult();
            _view = _ui.Get<BindingView>();
            _ui.Show<BindingView>(new BindingViewModel(_speed, _visible, _warning)).GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            _ui.Dispose();
            _speed.Dispose();
            _visible.Dispose();
            _warning.Dispose();
        }

        [Test]
        public void BindText_ValueChanges_LabelFollowsFormattedValue()
        {
            Assert.AreEqual("1 m/s", _view.Label.text);

            _speed.Value = 42;

            Assert.AreEqual("42 m/s", _view.Label.text);
        }

        [Test]
        public void BindVisible_ValueChanges_DisplayFollows()
        {
            Assert.AreEqual(DisplayStyle.Flex, _view.Box.style.display.value);

            _visible.Value = false;

            Assert.AreEqual(DisplayStyle.None, _view.Box.style.display.value);
        }

        [Test]
        public void BindClass_ValueChanges_ClassFollows()
        {
            Assert.IsFalse(_view.Box.ClassListContains("warning"));

            _warning.Value = true;

            Assert.IsTrue(_view.Box.ClassListContains("warning"));
        }

        [Test]
        public void Helpers_AfterHide_StopFollowing()
        {
            _ui.Hide<BindingView>();

            _speed.Value = 7;
            _visible.Value = false;
            _warning.Value = true;

            Assert.AreEqual("1 m/s", _view.Label.text);
            Assert.AreEqual(DisplayStyle.Flex, _view.Box.style.display.value);
            Assert.IsFalse(_view.Box.ClassListContains("warning"));
        }

        public sealed class BindingViewModel : ViewModelBase
        {
            // Exposed from a longer-lived owner: not tracked, so disposal of the view model leaves them alive.
            public ReadOnlyReactiveProperty<int> Speed { get; }
            public ReadOnlyReactiveProperty<bool> Visible { get; }
            public ReadOnlyReactiveProperty<bool> Warning { get; }

            public BindingViewModel(ReactiveProperty<int> speed, ReactiveProperty<bool> visible, ReactiveProperty<bool> warning)
            {
                Speed = speed;
                Visible = visible;
                Warning = warning;
            }
        }

        public sealed class BindingView : View<BindingViewModel>
        {
            public Label Label { get; } = new();
            public VisualElement Box { get; } = new();

            protected override string? UxmlName => null;
            protected override UILayer Layer => UILayer.HUD;

            protected override void OnInitialize()
            {
                Root.Add(Label);
                Root.Add(Box);
            }

            protected override void OnBind()
            {
                BindText(Label, ViewModel.Speed, speed => $"{speed} m/s");
                BindVisible(Box, ViewModel.Visible);
                BindClass(Box, "warning", ViewModel.Warning);
            }
        }
    }
}
