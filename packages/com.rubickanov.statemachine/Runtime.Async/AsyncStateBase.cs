using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.StateMachine
{
    public abstract class AsyncStateBase : IAsyncState
    {
        public virtual UniTask OnEnterAsync(CancellationToken ct) => UniTask.CompletedTask;
        public virtual void OnUpdate(float deltaTime) { }
        public virtual UniTask OnExitAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
