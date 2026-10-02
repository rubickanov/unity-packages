using System;

namespace Rubickanov.StateMachine
{
    public static class StateMachineExtensions
    {
        public static StateMachine<TKey> AddState<TKey>(
            this StateMachine<TKey> stateMachine,
            TKey key,
            Action? onEnter = null,
            Action<float>? onUpdate = null,
            Action? onExit = null) where TKey : notnull
        {
            stateMachine.AddState(key, new CallbackState(onEnter, onUpdate, onExit));
            return stateMachine;
        }
    }
}
