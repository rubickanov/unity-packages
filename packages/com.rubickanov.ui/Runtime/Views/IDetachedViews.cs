using System;

namespace Rubickanov.UI
{
    /// <summary>
    /// Builds instances of registered views apart from their layer, for <see cref="PopupHost"/> to show as popup
    /// content. <see cref="UIService"/> is one.
    /// </summary>
    internal interface IDetachedViews
    {
        /// <summary>
        /// A new instance of the registered view <paramref name="viewType"/>, bound to <paramref name="viewModel"/> and
        /// shown, for the caller to add and destroy.
        /// </summary>
        View CreateDetached(Type viewType, ViewModelBase viewModel);
    }
}
