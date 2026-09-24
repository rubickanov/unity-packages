using System;
using System.Collections.Generic;
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

        /// <summary>Layer the view lives on. Not used for a child view.</summary>
        protected abstract UILayer Layer { get; }

        /// <summary>Whether the view's root blocks pointer events while visible. Screens and popups do.</summary>
        protected virtual bool InterceptsInput => Layer is UILayer.Screen or UILayer.Popup;

        /// <summary>Played on show and hide.</summary>
        protected virtual IViewAnimation Animation => NoneAnimation.Instance;

        /// <summary>
        /// Child view types this view creates with <c>CreateChild</c>. Their UXML is loaded when this view registers.
        /// </summary>
        protected virtual IReadOnlyList<Type> ChildViews => Array.Empty<Type>();

        internal string? ResolveUxmlName() => UxmlName;
        internal UILayer ResolveLayer() => Layer;
        internal IReadOnlyList<Type> ResolveChildViews() => ChildViews;
        internal UxmlCache? Uxml { get; set; }
        internal UIService? Owner { get; set; }
        internal IDisposable? PointerCapture { get; set; }
        internal IDisposable? BackHandle { get; set; }

        internal abstract Type ViewModelType { get; }
        internal abstract ViewModelBase? BoundViewModel { get; }
        internal abstract void BindViewModel(ViewModelBase viewModel);
        internal abstract void Unbind();

        internal void Initialize() => OnInitialize();

        /// <summary>
        /// Builds the tree of a view created apart from registration (a child view, a popup's content) from UXML
        /// loaded for someone else, then runs <see cref="OnInitialize"/>.
        /// </summary>
        /// <param name="whyMissing">Ends the error thrown when the UXML is not in <paramref name="uxml"/>.</param>
        internal void InitializeFrom(UxmlCache? uxml, string whyMissing)
        {
            var type = GetType();
            var uxmlName = UxmlName;
            if (uxmlName == null)
                Root = new VisualElement();
            else if (uxml != null && uxml.TryGet(type, out var asset) && asset != null)
                Root = asset.CloneTree();
            else
                throw new InvalidOperationException($"UXML '{uxmlName}' of {type.Name} is not loaded: {whyMissing}.");

            Uxml = uxml;
            OnInitialize();
        }

        internal bool HandleBack() => OnBack();

        /// <summary>
        /// Called by <see cref="IUIService.Back"/> while this screen or popup is visible and its handler is the top one.
        /// Return true when the back press was consumed. Default: a popup hides itself and returns true; a screen
        /// returns to the previous screen of the history (<see cref="IUIService.Navigate{T}"/>) and returns true, or
        /// returns false when it is the first.
        /// </summary>
        protected virtual bool OnBack()
        {
            if (Owner == null) return false;

            switch (Layer)
            {
                case UILayer.Popup:
                    Owner.HideViewAsync(this).Forget();
                    return true;
                case UILayer.Screen:
                    return Owner.NavigateBackFrom(this);
                default:
                    return false;
            }
        }

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
                await Animation.PlayShowAsync(Root, ct);
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
                await Animation.PlayHideAsync(Root, ct);
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
            Animation.Reset(Root);
            Unbind();
        }

        internal void Destroy()
        {
            Hide();
            Root.RemoveFromHierarchy();
        }

        /// <summary>Binds and shows a child view created by its parent, with no animation.</summary>
        internal void ShowAsChild(ViewModelBase viewModel)
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

        protected virtual void OnInitialize() { }
    }
}
