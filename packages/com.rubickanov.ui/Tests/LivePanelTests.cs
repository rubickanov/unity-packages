using System;
using System.Threading.Tasks;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Rubickanov.UI.Tests
{
    /// <summary>Behaviour that needs a real panel: events are dispatched only to elements on one.</summary>
    [TestFixture]
    public class LivePanelTests
    {
        private GameObject _document = null!;
        private PanelSettings _settings = null!;
        private VisualElement _root = null!;
        private UIService _ui = null!;
        private PopupHost _popups = null!;

        [SetUp]
        public void SetUp()
        {
            // A UIDocument gets a runtime panel in edit mode too.
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _document = new GameObject("live-panel");
            var document = _document.AddComponent<UIDocument>();
            document.panelSettings = _settings;
            _root = TestRoot.Create();
            document.rootVisualElement.Add(_root);
            _ui = new UIService(_root, new RecordingUxmlLoader().Load);
            _popups = new PopupHost(_root, _ui);
        }

        [TearDown]
        public void TearDown()
        {
            _popups.Dispose();
            _ui.Dispose();
            Object.DestroyImmediate(_document);
            Object.DestroyImmediate(_settings);
        }

        /// <summary>A pointer press on <paramref name="element"/>, dispatched by the panel.</summary>
        private static void PressOn(VisualElement element)
        {
            using var press = PointerDownEvent.GetPooled();
            press.target = element;
            element.SendEvent(press);
        }

        [Test]
        public async Task ClickOutside_PressOnBackdrop_ClosesAsClickOutside()
        {
            var popup = _popups.Create().Content(() => new Label("modal")).Modal()
                .CloseOn(PopupCloseTriggers.ClickOutside).Open();

            PressOn(popup.Panel.parent);

            Assert.IsFalse(popup.IsOpen);
            Assert.AreEqual(PopupCloseReason.ClickOutside, (await popup.Result).Reason);
        }

        [Test]
        public void ClickOutside_PressInsidePanel_StaysOpen()
        {
            var label = new Label("modal");
            var popup = _popups.Create().Content(() => label).Modal().CloseOn(PopupCloseTriggers.ClickOutside).Open();

            PressOn(label);

            Assert.IsTrue(popup.IsOpen);
        }

        [Test]
        public void ModalWithoutClickOutside_PressOnBackdrop_StaysOpen()
        {
            var popup = _popups.Create().Content(() => new Label("modal")).Modal().Open();

            PressOn(popup.Panel.parent);

            Assert.IsTrue(popup.IsOpen);
        }

        // Off a panel a field sends no ChangeEvent: picks need the live panel.
        [Test]
        public async Task BindDropdown_TwoWay_IndexFollowsPropertyAndPick()
        {
            await _ui.Register<BindingHelperTests.DropdownView>();
            var view = _ui.Get<BindingHelperTests.DropdownView>();
            var vm = new BindingHelperTests.DropdownViewModel(1);
            await _ui.Show<BindingHelperTests.DropdownView, BindingHelperTests.DropdownViewModel>(vm);

            var appliedAtBind = view.Quality.index;
            vm.Quality.Value = 2;
            var followedProperty = view.Quality.index;
            view.Quality.index = 0;

            Assert.AreEqual(1, appliedAtBind);
            Assert.AreEqual(2, followedProperty);
            Assert.AreEqual(0, vm.Quality.Value);
            CollectionAssert.AreEqual(BindingHelperTests.DropdownView.Choices, view.Quality.choices);
        }

        [Test]
        public async Task BindDropdown_OneWay_InitialIndexThenCallbackOnPick()
        {
            await _ui.Register<BindingHelperTests.DropdownView>();
            var view = _ui.Get<BindingHelperTests.DropdownView>();
            var vm = new BindingHelperTests.DropdownViewModel(0);
            await _ui.Show<BindingHelperTests.DropdownView, BindingHelperTests.DropdownViewModel>(vm);

            var initial = view.Language.index;
            view.Language.index = 1;

            Assert.AreEqual(2, initial);
            CollectionAssert.AreEqual(new[] { 1 }, vm.Picked);
        }

        [Test]
        public async Task BindDropdown_AfterHide_PickNoLongerReachesViewModel()
        {
            await _ui.Register<BindingHelperTests.DropdownView>();
            var view = _ui.Get<BindingHelperTests.DropdownView>();
            var vm = new BindingHelperTests.DropdownViewModel(0);
            await _ui.Show<BindingHelperTests.DropdownView, BindingHelperTests.DropdownViewModel>(vm);

            _ui.Hide<BindingHelperTests.DropdownView>();
            view.Language.index = 0;

            CollectionAssert.IsEmpty(vm.Picked);
        }

        [Test]
        public void BindTextField_ChangeEventWithCompositionShown_DisplayedTextKept()
        {
            _ui.Register<TextView>().GetAwaiter().GetResult();
            var property = new ReactiveProperty<string>("ab");
            _ui.Show<TextView, TextViewModel>(new TextViewModel(property)).GetAwaiter().GetResult();
            var field = _ui.Get<TextView>().Field;
            var text = field.Q<TextElement>();
            field.SetValueWithoutNotify("abc");
            // An IME composition shows text the value does not hold yet.
            ((INotifyValueChanged<string>)text).SetValueWithoutNotify("abcあ");

            using (var change = ChangeEvent<string>.GetPooled("ab", "abc"))
            {
                change.target = field;
                field.SendEvent(change);
            }

            Assert.AreEqual("abc", property.Value);
            Assert.AreEqual("abcあ", text.text);
        }

        public sealed class TextViewModel : ViewModelBase
        {
            public readonly ReactiveProperty<string> Text;
            public TextViewModel(ReactiveProperty<string> text) => Text = text;
        }

        public sealed class TextView : View<TextViewModel>
        {
            public TextField Field { get; private set; } = null!;

            protected override string? UxmlName => null;
            protected override UILayer Layer => UILayer.HUD;

            protected override void OnInitialize()
            {
                Field = new TextField();
                Root.Add(Field);
            }

            protected override void OnBind() => BindTextField(Field, ViewModel.Text);
        }
    }
}
