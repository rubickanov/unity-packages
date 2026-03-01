using System.Collections.Generic;

namespace Rubickanov.StateMachine
{
    public class SubStateMachine<TKey> : StateMachine<TKey>, IState where TKey : notnull
    {
        private readonly TKey _initialState;

        public SubStateMachine(TKey initialState, int capacity = 4) : base(capacity)
        {
            _initialState = initialState;
        }

        public SubStateMachine(TKey initialState, IEqualityComparer<TKey> comparer, int capacity = 4)
            : base(comparer, capacity)
        {
            _initialState = initialState;
        }

        void IState.OnEnter() => Start(_initialState);
        void IState.OnUpdate(float deltaTime) => Update(deltaTime);
        void IState.OnExit() => Stop();
    }
}
