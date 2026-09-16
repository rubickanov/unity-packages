using System;
using Cysharp.Threading.Tasks;
using R3;

namespace Rubickanov.UI
{
    public interface IUIService
    {
        /// <summary>Loads the view's UXML (and its child views') and attaches it to the layer the view declares.</summary>
        UniTask Register<T>() where T : View;

        /// <summary>Destroys the view, or cancels its registration while it is still loading.</summary>
        void Unregister<T>() where T : View;

        T Get<T>() where T : View;

        /// <summary>
        /// Binds <paramref name="viewModel"/> and shows the view with its animation. The view model belongs to this
        /// show: it is disposed when the view unbinds it.
        /// </summary>
        UniTask Show<T>(ViewModelBase viewModel) where T : View;

        void Hide<T>() where T : View;
        UniTask HideAsync<T>() where T : View;

        void HideTop();
        UniTask HideTopAsync();
        void HideAll();
        UniTask HideAllAsync();

        /// <summary>
        /// Asks for a free pointer. Counted: <see cref="PointerCaptured"/> stays true while any handle is alive.
        /// Screen and popup views, and modal or interactive popups, hold one while visible.
        /// </summary>
        IDisposable CapturePointer();

        /// <summary>True while at least one pointer capture is held. The game maps it to its cursor state.</summary>
        ReadOnlyReactiveProperty<bool> PointerCaptured { get; }

        /// <summary>Pushes a handler for <see cref="Back"/>. The last pushed runs first; the handle removes it.</summary>
        IDisposable PushBackHandler(Func<bool> handler);

        /// <summary>
        /// Runs back handlers from the top until one returns true. Returns false when nothing consumed it.
        /// </summary>
        bool Back();
    }
}
