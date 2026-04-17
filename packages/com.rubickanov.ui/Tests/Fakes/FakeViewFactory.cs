using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Rubickanov.UI.Tests
{
    public class FakeViewFactory : IViewFactory
    {
        private readonly Dictionary<Type, IView> _preset = new();
        public readonly List<IView> Detached = new();

        public void Preset<T>(IView view) where T : class, IView => _preset[typeof(T)] = view;

        public UniTask<IView> Create<T>(UILayer layer) where T : class, IView
        {
            if (!_preset.TryGetValue(typeof(T), out var view))
                throw new InvalidOperationException(
                    $"FakeViewFactory has no preset for {typeof(T).Name}. Call Preset<T>(view) first.");
            return UniTask.FromResult(view);
        }

        public void Detach(IView view) => Detached.Add(view);
    }

    public class FakeViewA : FakeView { }
    public class FakeViewB : FakeView { }
    public class FakeViewC : FakeView { }
    public class FakeViewModel : ViewModelBase { }
}
