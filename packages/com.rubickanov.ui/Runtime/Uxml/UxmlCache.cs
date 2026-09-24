using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// UXML of a registered view and of every child view it can create, with the handles to release. Counted: the
    /// registration holds one reference and every instance built apart from it (a popup's content) holds another, so
    /// the assets stay loaded until the last of them is gone.
    /// </summary>
    internal sealed class UxmlCache
    {
        private readonly Dictionary<Type, VisualTreeAsset?> _assets = new();
        private readonly List<IDisposable> _handles = new();
        private int _references = 1;

        public bool Contains(Type viewType) => _assets.ContainsKey(viewType);

        public void Add(Type viewType, VisualTreeAsset? asset) => _assets[viewType] = asset;

        public void AddHandle(IDisposable? handle)
        {
            if (handle != null) _handles.Add(handle);
        }

        public bool TryGet(Type viewType, out VisualTreeAsset? asset) => _assets.TryGetValue(viewType, out asset);

        public void Retain()
        {
            if (_references == 0) throw new ObjectDisposedException(nameof(UxmlCache));
            _references++;
        }

        /// <summary>Drops one reference; the last one releases the handles.</summary>
        public void Release()
        {
            if (_references == 0 || --_references > 0) return;

            List<Exception>? errors = null;
            for (int i = _handles.Count - 1; i >= 0; i--)
            {
                try { _handles[i].Dispose(); }
                catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }
            }
            _handles.Clear();
            _assets.Clear();

            if (errors != null)
                throw new AggregateException(errors);
        }
    }
}
