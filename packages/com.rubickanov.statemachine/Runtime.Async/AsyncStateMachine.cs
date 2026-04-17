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
        private readonly IEqualityComparer<TKey> _comparer;

        private IAsyncState? _currentState;
        private TKey _currentKey = default!;
        private bool _isStarted;
        private bool _isTransitioning;
        private bool _hasPendingTransition;
        private TKey _pendingKey = default!;
        private CancellationToken _pendingCancellationToken;
        private int _transitionDepth;

        public event Action<TKey, TKey>? StateChanged;

        public AsyncStateMachine(int capacity = 4)
            : this(EqualityComparer<TKey>.Default, capacity) { }

        public AsyncStateMachine(IEqualityComparer<TKey> comparer, int capacity = 4)
        {
            _comparer = comparer;
            _states = new Dictionary<TKey, IAsyncState>(capacity, comparer);
        }

        public TKey CurrentKey
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _currentKey;
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

        public bool HasPendingTransition
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _hasPendingTransition;
        }

        public TKey PendingKey
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _hasPendingTransition ? _pendingKey : default!;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsInState(TKey key)
        {
            return _isStarted && _comparer.Equals(_currentKey, key);
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

        public T? GetCurrentState<T>() where T : class, IAsyncState
        {
            return _currentState as T;
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
                var pendingKey = _pendingKey;
                var pendingCt = _pendingCancellationToken;
                _hasPendingTransition = false;
                _pendingCancellationToken = default;
                await PerformTransitionAsync(pendingKey, pendingCt);
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
            _pendingCancellationToken = default;

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

            if (_comparer.Equals(_currentKey, key))
                return;

            if (_isTransitioning)
            {
                _hasPendingTransition = true;
                _pendingKey = key;
                _pendingCancellationToken = ct;
                return;
            }

            await PerformTransitionAsync(key, ct);
        }

        private async UniTask PerformTransitionAsync(TKey key, CancellationToken ct)
        {
            var nextKey = key;
            var nextCt = ct;

            while (true)
            {
                _transitionDepth++;
                if (_transitionDepth > MaxTransitionDepth)
                {
                    _transitionDepth = 0;
                    _hasPendingTransition = false;
                    _pendingCancellationToken = default;
                    throw new InvalidOperationException(
                        $"Maximum transition depth ({MaxTransitionDepth}) exceeded. Possible infinite loop detected.");
                }

                var previousKey = _currentKey;
                var previousState = _currentState!;

                _isTransitioning = true;
                await previousState.OnExitAsync(nextCt);

                var nextState = _states[nextKey];
                _currentKey = nextKey;
                _currentState = nextState;

                await nextState.OnEnterAsync(nextCt);
                _isTransitioning = false;

                StateChanged?.Invoke(previousKey, nextKey);

                if (!_hasPendingTransition)
                {
                    _transitionDepth = 0;
                    return;
                }

                _hasPendingTransition = false;
                nextKey = _pendingKey;
                nextCt = _pendingCancellationToken;
                _pendingCancellationToken = default;
            }
        }
    }
}
