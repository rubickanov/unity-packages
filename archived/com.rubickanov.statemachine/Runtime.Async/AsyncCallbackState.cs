using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.StateMachine
{
    public sealed class AsyncCallbackState : IAsyncState
    {
        private readonly Func<CancellationToken, UniTask>? _onEnterAsync;
        private readonly Action<float>? _onUpdate;
        private readonly Func<CancellationToken, UniTask>? _onExitAsync;

        public AsyncCallbackState(
            Func<CancellationToken, UniTask>? onEnterAsync = null,
            Action<float>? onUpdate = null,
            Func<CancellationToken, UniTask>? onExitAsync = null)
        {
            _onEnterAsync = onEnterAsync;
            _onUpdate = onUpdate;
            _onExitAsync = onExitAsync;
        }

        public UniTask OnEnterAsync(CancellationToken ct) =>
            _onEnterAsync?.Invoke(ct) ?? UniTask.CompletedTask;

        public void OnUpdate(float deltaTime) => _onUpdate?.Invoke(deltaTime);

        public UniTask OnExitAsync(CancellationToken ct) =>
            _onExitAsync?.Invoke(ct) ?? UniTask.CompletedTask;
    }
}
