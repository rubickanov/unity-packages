using System.Reflection;
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

        private VisualElement? Tooltip => _root.Q("overlay-layer").Q(className: PopupStyle.Tooltip);

        /// <summary>Moves the mouse over <paramref name="element"/> the way the panel does, sending enter and leave events.</summary>
        private void HoverTo(VisualElement element)
        {
            var panel = _root.panel;
            MethodInfo? set = null;
            MethodInfo? commit = null;
            for (var type = panel.GetType(); type != null && (set == null || commit == null); type = type.BaseType)
            {
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var parameters = method.GetParameters();
                    if (method.Name == "SetTopElementUnderPointer" && parameters.Length == 3 &&
                        parameters[2].ParameterType == typeof(Vector2))
                        set = method;
                    if (method.Name == "CommitElementUnderPointers" && parameters.Length == 0)
                        commit = method;
                }
            }

            set!.Invoke(panel, new object[] { PointerId.mousePointerId, element, new Vector2(10f, 10f) });
            commit!.Invoke(panel, null);
        }

        private (VisualElement row, VisualElement other) AddRowWithTooltip()
        {
            var row = new VisualElement { name = "row" };
            row.Add(new Label("text"));
            var other = new VisualElement { name = "other" };
            _root.Q("screen-layer").Add(row);
            _root.Q("screen-layer").Add(other);
            row.AttachTooltip(_popups, "hint", delay: 0f);
            return (row, other);
        }

        [Test]
        public void AttachTooltip_PointerEntersThenLeaves_OpensThenCloses()
        {
            var (row, other) = AddRowWithTooltip();

            HoverTo(row.Q<Label>());
            var openedOnEnter = Tooltip != null;
            HoverTo(other);

            Assert.IsTrue(openedOnEnter);
            Assert.IsNull(Tooltip);
        }

        [Test]
        public void AttachTooltip_HoveredElementLeavesPanel_Closes()
        {
            var (row, other) = AddRowWithTooltip();
            HoverTo(row.Q<Label>());

            row.RemoveFromHierarchy();
            HoverTo(other);

            Assert.IsNull(Tooltip);
        }

        [Test]
        public void BindTextField_ChangeEventWithCompositionShown_DisplayedTextKept()
        {
            _ui.Register<TextView>().GetAwaiter().GetResult();
            var property = new ReactiveProperty<string>("ab");
            _ui.Show<TextView>(new TextViewModel(property)).GetAwaiter().GetResult();
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
