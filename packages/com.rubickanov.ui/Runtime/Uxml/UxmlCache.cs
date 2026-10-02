using System;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// The UXML of a registered view with the handle to release. Counted: the registration holds one reference and
    /// every instance built apart from it (a popup's content) holds another, so the asset stays loaded until the last
    /// of them is gone.
    /// </summary>
    internal sealed class UxmlCache
    {
        private IDisposable? _handle;
        private int _references = 1;

        public UxmlCache(VisualTreeAsset? asset, IDisposable? handle)
        {
            Asset = asset;
            _handle = handle;
        }

        /// <summary>The view's tree; null for a view built in code.</summary>
        public VisualTreeAsset? Asset { get; private set; }

        public void Retain()
        {
            if (_references == 0) throw new ObjectDisposedException(nameof(UxmlCache));
            _references++;
        }

        /// <summary>Drops one reference; the last one releases the handle.</summary>
        public void Release()
        {
            if (_references == 0 || --_references > 0) return;

            var handle = _handle;
            _handle = null;
            Asset = null;
            handle?.Dispose();
        }
    }
}
