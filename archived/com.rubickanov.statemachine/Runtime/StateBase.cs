namespace Rubickanov.StateMachine
{
    public abstract class StateBase : IState
    {
        public virtual void OnEnter() { }
        public virtual void OnUpdate(float deltaTime) { }
        public virtual void OnExit() { }
    }
}
