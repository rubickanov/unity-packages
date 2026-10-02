using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.StateMachine
{
    public class AsyncSubStateMachine<TKey> : AsyncStateMachine<TKey>, IAsyncState where TKey : notnull
    {
        private readonly TKey _initialState;

        public AsyncSubStateMachine(TKey initialState, int capacity = 4) : base(capacity)
        {
            _initialState = initialState;
        }

        public AsyncSubStateMachine(TKey initialState, IEqualityComparer<TKey> comparer, int capacity = 4)
            : base(comparer, capacity)
        {
            _initialState = initialState;
        }

        UniTask IAsyncState.OnEnterAsync(CancellationToken ct) => StartAsync(_initialState, ct);
        void IAsyncState.OnUpdate(float deltaTime) => Update(deltaTime);
        UniTask IAsyncState.OnExitAsync(CancellationToken ct) => StopAsync(ct);
    }
}
