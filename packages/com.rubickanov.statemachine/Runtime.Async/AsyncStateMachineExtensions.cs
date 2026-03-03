using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.StateMachine
{
    public static class AsyncStateMachineExtensions
    {
        public static AsyncStateMachine<TKey> AddState<TKey>(
            this AsyncStateMachine<TKey> stateMachine,
            TKey key,
            Func<CancellationToken, UniTask>? onEnterAsync = null,
            Action<float>? onUpdate = null,
            Func<CancellationToken, UniTask>? onExitAsync = null) where TKey : notnull
        {
            stateMachine.AddState(key, new AsyncCallbackState(onEnterAsync, onUpdate, onExitAsync));
            return stateMachine;
        }
    }
}
