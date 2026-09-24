using System;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// Loads the UXML asset for a view by name. The returned handle is disposed when the view is destroyed. A null
    /// asset fails the view's registration; a null handle releases nothing.
    /// </summary>
    public delegate UniTask<(VisualTreeAsset? asset, IDisposable? handle)> UxmlLoader(string name);
}
