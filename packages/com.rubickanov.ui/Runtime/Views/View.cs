using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    public abstract class View
    {
        private CancellationTokenSource? _transition;

        public VisualElement Root { get; internal set; } = default!;
        public ViewState State { get; private set; }
        public bool IsVisible => State is ViewState.Showing or ViewState.Shown;

        /// <summary>
        /// Name of the UXML asset the view is built from. <c>null</c> means no UXML: the view builds its tree in
        /// <see cref="OnInitialize"/> on an empty root.
        /// </summary>
        protected virtual string? UxmlName => GetType().Name;

        /// <summary>
        /// Layer the view lives on. A view on <see cref="UILayer.Popup"/> is a popup view: registering it only loads its
        /// UXML, and every <see cref="IPopupService"/> popup showing it builds its own instance.
        /// </summary>
        protected abstract UILayer Layer { get; }

        /// <summary>
        /// Whether the view takes input while visible: its root blocks pointer events, it holds a pointer capture and
        /// its <see cref="OnBack"/> answers <see cref="IUIService.Back"/>. Screens do.
        /// </summary>
        protected virtual bool InterceptsInput => Layer is UILayer.Screen;

        /// <summary>Played on show and hide, on <see cref="AnimationTarget"/>.</summary>
        protected virtual IViewAnimation Animation => NoneAnimation.Instance;

        /// <summary>
        /// The element <see cref="Animation"/> plays on. Default: <see cref="Root"/>. A view whose root covers the screen
        /// points it at the part that moves (a strip at the bottom edge), so a wipe or squeeze runs on that part.
        /// </summary>
        protected virtual VisualElement AnimationTarget => Root;

        internal string? ResolveUxmlName() => UxmlName;
        internal UILayer ResolveLayer() => Layer;
        internal bool ResolveInterceptsInput() => InterceptsInput;
        internal UxmlCache? Uxml { get; set; }

        /// <summary>Whether this view holds a reference on <see cref="Uxml"/>, released when it is destroyed.</summary>
        internal bool OwnsUxml { get; set; }
        internal UIService? Owner { get; set; }
        internal IDisposable? PointerCapture { get; set; }
        internal IDisposable? BackHandle { get; set; }

        /// <summary>The popup showing this view as its content, or null.</summary>
        protected IPopupHandle? Popup { get; private set; }

        internal void SetPopup(IPopupHandle? popup) => Popup = popup;
        internal IViewAnimation ResolveAnimation() => Animation;
        internal VisualElement ResolveAnimationTarget() => AnimationTarget;

        internal abstract Type ViewModelType { get; }
        internal abstract ViewModelBase? BoundViewModel { get; }
        internal abstract void BindViewModel(ViewModelBase viewModel);
        internal abstract void Unbind();

        internal void Initialize() => OnInitialize();

        /// <summary>
        /// Builds the tree of a view created apart from registration (a popup's content) from UXML loaded for the
        /// registered instance, then runs <see cref="OnInitialize"/>.
        /// </summary>
        /// <param name="whyMissing">Ends the error thrown when <paramref name="uxml"/> holds no asset.</param>
        internal void InitializeFrom(UxmlCache uxml, string whyMissing)
        {
            var uxmlName = UxmlName;
            if (uxmlName == null)
                Root = new VisualElement();
            else if (uxml.Asset != null)
                Root = uxml.Asset.CloneTree();
            else
                throw new InvalidOperationException(
                    $"UXML '{uxmlName}' of {GetType().Name} is not loaded: {whyMissing}.");

            OnInitialize();
        }

        internal bool HandleBack() => OnBack();

        /// <summary>
        /// Called by <see cref="IUIService.Back"/> while this view takes input (<see cref="InterceptsInput"/>) and its
        /// handler is the top one, or while this view is the content of a popup, before the popup closes. Return true
        /// when the back press was consumed. Default: a screen returns to the previous screen of the history
        /// (<see cref="IUIService.Navigate{TView,TViewModel}"/>) and returns true, or returns false when it is the first;
        /// other views return false. The return happens after this call: an override may still read its view model
        /// after <c>base.OnBack()</c>.
        /// </summary>
        protected virtual bool OnBack() =>
            Layer == UILayer.Screen && Owner != null && Owner.NavigateBackFrom(this);

        /// <summary>
        /// Binds <paramref name="viewModel"/> (unless it is already bound) and makes the view visible. Returns the show
        /// animation, or a completed task when the view was already showing or shown.
        /// </summary>
        internal UniTask BeginShow(ViewModelBase viewModel)
        {
            var wasVisible = IsVisible;
            if (State == ViewState.Hiding)
                CancelTransition();

            if (!ReferenceEquals(BoundViewModel, viewModel))
            {
                Unbind();
                try
                {
                    BindViewModel(viewModel);
                }
                catch
                {
                    Hide();
                    throw;
                }
            }

            Root.style.display = DisplayStyle.Flex;
            Root.pickingMode = InterceptsInput ? PickingMode.Position : PickingMode.Ignore;

            if (wasVisible)
                return UniTask.CompletedTask;

            State = ViewState.Showing;
            return PlayShowAsync(StartTransition());
        }

        private async UniTask PlayShowAsync(CancellationToken ct)
        {
            try
            {
                await Animation.PlayShowAsync(AnimationTarget, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }

            if (ct.IsCancellationRequested) return;
            _transition = null;
            State = ViewState.Shown;
        }

        /// <summary>Plays the hide animation, then unbinds and disposes the view model.</summary>
        internal async UniTask HideAsync()
        {
            if (State is ViewState.Hidden or ViewState.Hiding) return;

            var ct = StartTransition();
            State = ViewState.Hiding;
            Root.pickingMode = PickingMode.Ignore;

            try
            {
                await Animation.PlayHideAsync(AnimationTarget, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                if (!ct.IsCancellationRequested) Hide();
                throw;
            }

            if (ct.IsCancellationRequested) return;
            Hide();
        }

        /// <summary>Hides at once: cancels any transition, unbinds and disposes the view model.</summary>
        internal void Hide()
        {
            CancelTransition();
            State = ViewState.Hidden;
            Root.style.display = DisplayStyle.None;
            Root.pickingMode = PickingMode.Ignore;
            Animation.Reset(AnimationTarget);
            Unbind();
        }

        /// <summary>Hides, removes the root and releases the UXML this view holds.</summary>
        internal void Destroy()
        {
            try
            {
                Hide();
            }
            finally
            {
                Root.RemoveFromHierarchy();
                Popup = null;
                ReleaseUxml();
            }
        }

        internal void ReleaseUxml()
        {
            if (!OwnsUxml) return;
            OwnsUxml = false;
            Uxml?.Release();
        }

        /// <summary>Binds and shows a view created apart from its layer (a popup's content), with no animation.</summary>
        internal void ShowDetached(ViewModelBase viewModel)
        {
            BindViewModel(viewModel);
            State = ViewState.Shown;
            Root.style.display = DisplayStyle.Flex;
        }

        private CancellationToken StartTransition()
        {
            _transition?.Cancel();
            _transition = new CancellationTokenSource();
            return _transition.Token;
        }

        private void CancelTransition()
        {
            var transition = _transition;
            _transition = null;
            transition?.Cancel();
        }

        /// <summary>Runs once, when the view's tree is built: find elements, build a code-only tree.</summary>
        protected virtual void OnInitialize() { }
    }
}
