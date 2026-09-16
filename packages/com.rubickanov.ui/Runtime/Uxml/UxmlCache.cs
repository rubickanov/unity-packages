using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>UXML of a registered view and of every child view it can create, with the handles to release.</summary>
    internal sealed class UxmlCache
    {
        private readonly Dictionary<Type, VisualTreeAsset?> _assets = new();
        private readonly List<IDisposable> _handles = new();

        public bool Contains(Type viewType) => _assets.ContainsKey(viewType);

        public void Add(Type viewType, VisualTreeAsset? asset) => _assets[viewType] = asset;

        public void AddHandle(IDisposable? handle)
        {
            if (handle != null) _handles.Add(handle);
        }

        public bool TryGet(Type viewType, out VisualTreeAsset? asset) => _assets.TryGetValue(viewType, out asset);

        public void Release()
        {
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
