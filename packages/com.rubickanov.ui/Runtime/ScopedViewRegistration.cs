using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Rubickanov.UI
{
    public class ScopedViewRegistration : IDisposable
    {
        private readonly IUIService _ui;
        private readonly List<Action> _cleanupActions = new();

        public ScopedViewRegistration(IUIService ui) => _ui = ui;

        public async UniTask Register<T>(UILayer layer) where T : class, IView
        {
            await _ui.Register<T>(layer);
            _cleanupActions.Add(() => _ui.Unregister<T>());
        }

        public void Dispose()
        {
            foreach (var action in _cleanupActions) action();
            _cleanupActions.Clear();
        }
    }
}
