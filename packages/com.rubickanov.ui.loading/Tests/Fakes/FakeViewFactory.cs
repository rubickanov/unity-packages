using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Rubickanov.UI;

namespace Rubickanov.UI.Loading.Tests
{
    internal sealed class FakeViewFactory : IViewFactory
    {
        private readonly Dictionary<Type, IView> _preset = new();

        public void Preset<T>(IView view) where T : class, IView => _preset[typeof(T)] = view;

        public UniTask<IView> Create<T>(UILayer layer) where T : class, IView
        {
            if (!_preset.TryGetValue(typeof(T), out var view))
                throw new InvalidOperationException(
                    $"FakeViewFactory has no preset for {typeof(T).Name}.");
            return UniTask.FromResult(view);
        }

        public void Detach(IView view) { }
    }
}
