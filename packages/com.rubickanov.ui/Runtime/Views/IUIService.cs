using System;
using Cysharp.Threading.Tasks;
using R3;

namespace Rubickanov.UI
{
    public interface IUIService
    {
        /// <summary>
        /// Loads the view's UXML (and its child views') and attaches it to the layer the view declares. A popup view
        /// (<see cref="UILayer.Popup"/>) is only loaded: <see cref="IPopupService"/> builds an instance per popup.
        /// </summary>
        UniTask Register<T>() where T : View;

        /// <summary>Destroys the view, or cancels its registration while it is still loading.</summary>
        void Unregister<T>() where T : View;

        T Get<T>() where T : View;

        /// <summary>
        /// Binds <paramref name="viewModel"/> and shows the view with its animation. The view model belongs to this
        /// show: it is disposed when the view unbinds it. Showing a screen clears the screen history.
        /// </summary>
        UniTask Show<T>(ViewModelBase viewModel) where T : View;

        /// <summary>
        /// Shows the screen <typeparamref name="T"/> with a view model from <paramref name="createViewModel"/> and
        /// records it in the screen history, so <see cref="NavigateBack"/> (and <see cref="Back"/> through the
        /// screen's default <c>OnBack</c>) returns to the screen before it. The factory runs again on every return,
        /// since a view model lives for one show. A screen already in the history is returned to: everything above
        /// it is dropped.
        /// </summary>
        /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is not a screen.</exception>
        UniTask Navigate<T>(Func<ViewModelBase> createViewModel) where T : View;

        /// <summary>True while the screen history holds a screen to return to.</summary>
        bool CanNavigateBack { get; }

        /// <summary>
        /// Shows the previous screen of the history with a new view model from its factory. Returns false, changing
        /// nothing, when there is none.
        /// </summary>
        UniTask<bool> NavigateBack();

        void Hide<T>() where T : View;
        UniTask HideAsync<T>() where T : View;

        /// <summary>Hides the active screen, whichever it is, and clears the history.</summary>
        void HideScreen();
        UniTask HideScreenAsync();

        /// <summary>
        /// Asks for a free pointer. Counted: <see cref="PointerCaptured"/> stays true while any handle is alive.
        /// The visible screen, and modal or interactive popups while on screen, hold one.
        /// </summary>
        IDisposable CapturePointer();

        /// <summary>True while at least one pointer capture is held. The game maps it to its cursor state.</summary>
        ReadOnlyReactiveProperty<bool> PointerCaptured { get; }

        /// <summary>
        /// Pushes a handler for <see cref="Back"/>. The last pushed runs first, except that the visible screen's own
        /// handler always runs last; the handle removes it.
        /// </summary>
        IDisposable PushBackHandler(Func<bool> handler);

        /// <summary>
        /// Runs back handlers from the top until one returns true. Returns false when nothing consumed it.
        /// </summary>
        bool Back();
    }
}
