using Cysharp.Threading.Tasks;

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
    }
}
