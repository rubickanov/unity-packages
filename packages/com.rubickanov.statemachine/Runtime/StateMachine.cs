using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Rubickanov.StateMachine
{
    public class StateMachine<TKey> where TKey : notnull
    {
        private const int MaxTransitionDepth = 16;

        private readonly Dictionary<TKey, IState> _states;
        private readonly IEqualityComparer<TKey> _comparer;

        private IState? _currentState;
        private TKey _currentKey = default!;
        private bool _isStarted;
        private bool _isTransitioning;
        private bool _hasPendingTransition;
        private TKey _pendingKey = default!;
        private int _transitionDepth;

        public event Action<TKey, TKey>? StateChanged;

        public StateMachine(int capacity = 4)
            : this(EqualityComparer<TKey>.Default, capacity) { }

        public StateMachine(IEqualityComparer<TKey> comparer, int capacity = 4)
        {
            _comparer = comparer;
            _states = new Dictionary<TKey, IState>(capacity, comparer);
        }

        public TKey CurrentKey
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _currentKey;
        }

        public IState? CurrentState
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

        public void AddState(TKey key, IState state)
        {
            if (_isStarted)
                throw new InvalidOperationException("Cannot add states after the state machine has been started.");

            _states.Add(key, state);
        }

        public T? GetState<T>(TKey key) where T : class, IState
        {
            return _states.TryGetValue(key, out var state) ? state as T : null;
        }

        public T? GetCurrentState<T>() where T : class, IState
        {
            return _currentState as T;
        }

        public void Start(TKey initialState)
        {
            if (_isStarted)
                throw new InvalidOperationException("State machine has already been started. Call Stop() first.");

            if (!_states.TryGetValue(initialState, out var state))
                throw new ArgumentException($"State '{initialState}' has not been registered.", nameof(initialState));

            _isStarted = true;
            _currentKey = initialState;
            _currentState = state;
            _transitionDepth = 0;

            _isTransitioning = true;
            state.OnEnter();
            _isTransitioning = false;

            if (_hasPendingTransition)
            {
                _hasPendingTransition = false;
                PerformTransition(_pendingKey);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(float deltaTime)
        {
            _currentState?.OnUpdate(deltaTime);
        }

        public void Stop()
        {
            if (!_isStarted)
                return;

            _isStarted = false;
            _hasPendingTransition = false;

            var state = _currentState;
            _currentState = null;
            _currentKey = default!;

            state?.OnExit();
        }

        public void SetState(TKey key)
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
                return;
            }

            PerformTransition(key);
        }

        private void PerformTransition(TKey key)
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
                previousState.OnExit();

                var nextState = _states[nextKey];
                _currentKey = nextKey;
                _currentState = nextState;

                nextState.OnEnter();
                _isTransitioning = false;

                StateChanged?.Invoke(previousKey, nextKey);

                if (!_hasPendingTransition)
                {
                    _transitionDepth = 0;
                    return;
                }

                _hasPendingTransition = false;
                nextKey = _pendingKey;
            }
        }
    }
}
