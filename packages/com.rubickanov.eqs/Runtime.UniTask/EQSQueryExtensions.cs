using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.EQS
{
    public static class EQSQueryExtensions
    {
        public static async UniTask<EQSQueryResult> RunAsync(
            this EQSQuery query, EQSQueryContext context,
            float budgetMs = 0.5f, CancellationToken cancellationToken = default)
        {
            query.Start(context);

            while (!query.Tick(budgetMs))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.Yield(cancellationToken);
            }

            return query.GetResult();
        }
    }
}
