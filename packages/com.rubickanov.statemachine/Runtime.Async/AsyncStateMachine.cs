using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.StateMachine
{
    public class AsyncStateMachine<TKey> where TKey : notnull
    {
        private const int MaxTransitionDepth = 16;

        private readonly Dictionary<TKey, IAsyncState> _states;

        private IAsyncState? _currentState;
        private TKey _currentKey = default!;
        private bool _isStarted;
        private bool _isTransitioning;
        private bool _hasPendingTransition;
        private TKey _pendingKey = default!;
        private int _transitionDepth;

        public event Action<TKey, TKey>? StateChanged;

        public AsyncStateMachine(int capacity = 4)
        {
            _states = new Dictionary<TKey, IAsyncState>(capacity);
        }

        public AsyncStateMachine(IEqualityComparer<TKey> comparer, int capacity = 4)
        {
            _states = new Dictionary<TKey, IAsyncState>(capacity, comparer);
        }

        public TKey CurrentKey
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (!_isStarted)
                    throw new InvalidOperationException("State machine has not been started.");
                return _currentKey;
            }
        }

        public IAsyncState? CurrentState
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _currentState;
        }

        public bool IsStarted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _isStarted;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsInState(TKey key)
        {
            return _isStarted && EqualityComparer<TKey>.Default.Equals(_currentKey, key);
        }

        public void AddState(TKey key, IAsyncState state)
        {
            if (_isStarted)
                throw new InvalidOperationException("Cannot add states after the state machine has been started.");

            _states.Add(key, state);
        }

        public T? GetState<T>(TKey key) where T : class, IAsyncState
        {
            return _states.TryGetValue(key, out var state) ? state as T : null;
        }

        public async UniTask StartAsync(TKey initialState, CancellationToken ct = default)
        {
            if (_isStarted)
                throw new InvalidOperationException("State machine has already been started. Call StopAsync() first.");

            if (!_states.TryGetValue(initialState, out var state))
                throw new ArgumentException($"State '{initialState}' has not been registered.", nameof(initialState));

            _isStarted = true;
            _currentKey = initialState;
            _currentState = state;
            _transitionDepth = 0;

            _isTransitioning = true;
            await state.OnEnterAsync(ct);
            _isTransitioning = false;

            if (_hasPendingTransition)
            {
                _hasPendingTransition = false;
                await PerformTransitionAsync(_pendingKey, ct);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(float deltaTime)
        {
            _currentState?.OnUpdate(deltaTime);
        }

        public async UniTask StopAsync(CancellationToken ct = default)
        {
            if (!_isStarted)
                return;

            _isStarted = false;
            _hasPendingTransition = false;

            var state = _currentState;
            _currentState = null;
            _currentKey = default!;

            if (state != null)
                await state.OnExitAsync(ct);
        }

        public async UniTask SetStateAsync(TKey key, CancellationToken ct = default)
        {
            if (!_isStarted)
                throw new InvalidOperationException("State machine has not been started.");

            if (!_states.ContainsKey(key))
                throw new ArgumentException($"State '{key}' has not been registered.", nameof(key));

            if (_isTransitioning)
            {
                _hasPendingTransition = true;
                _pendingKey = key;
                return;
            }

            await PerformTransitionAsync(key, ct);
        }

        private async UniTask PerformTransitionAsync(TKey key, CancellationToken ct)
        {
            var nextKey = key;

            while (true)
            {
                _transitionDepth++;
                if (_transitionDepth > MaxTransitionDepth)
                {
                    _transitionDepth = 0;
                    _hasPendingTransition = false;
                    throw new InvalidOperationException(
                        $"Maximum transition depth ({MaxTransitionDepth}) exceeded. Possible infinite loop detected.");
                }

                var previousKey = _currentKey;
                var previousState = _currentState!;

                _isTransitioning = true;
                await previousState.OnExitAsync(ct);

                var nextState = _states[nextKey];
                _currentKey = nextKey;
                _currentState = nextState;

                await nextState.OnEnterAsync(ct);
                _isTransitioning = false;

                StateChanged?.Invoke(previousKey, nextKey);

                if (!_hasPendingTransition)
                {
                    _transitionDepth = 0;
                    return;
                }

                _hasPendingTransition = false;
                nextKey = _pendingKey;
                _transitionDepth = 0;
            }
        }
    }
}
