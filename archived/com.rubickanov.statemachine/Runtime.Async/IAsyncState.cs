using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.StateMachine
{
    public interface IAsyncState
    {
        UniTask OnEnterAsync(CancellationToken ct);
        void OnUpdate(float deltaTime);
        UniTask OnExitAsync(CancellationToken ct);
    }
}
