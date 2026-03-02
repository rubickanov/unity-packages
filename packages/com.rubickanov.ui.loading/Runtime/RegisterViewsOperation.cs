using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Rubickanov.Loading;

namespace Rubickanov.UI.Loading
{
    public class RegisterViewsOperation : ILoadingOperation
    {
        private readonly SceneViewScopeService _scopeService;
        private readonly List<Func<ScopedViewRegistration, UniTask>> _registrations = new();

        public string Description => "Loading UI...";

        public RegisterViewsOperation(SceneViewScopeService scopeService)
        {
            _scopeService = scopeService;
        }

        public RegisterViewsOperation Add<T>(UILayer layer) where T : class, IView
        {
            _registrations.Add(scope => scope.Register<T>(layer));
            return this;
        }

        public async UniTask Execute(IProgress<float> progress, CancellationToken ct)
        {
            progress.Report(0f);

            var scope = _scopeService.Begin();

            for (int i = 0; i < _registrations.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await _registrations[i](scope);
                progress.Report((float)(i + 1) / _registrations.Count);
            }
        }
    }
}
