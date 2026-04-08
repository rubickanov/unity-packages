using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ZLogger;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Rubickanov.Loading
{
    /// <summary>
    /// Executes a sequence of <see cref="ILoadingOperation"/>s with uniform progress distribution.
    /// Reports progress via <see cref="ILoadingPresenter"/>.
    /// </summary>
    public class LoadingService : ILoadingService
    {
        private readonly ILogger _logger;
        private readonly ILoadingPresenter _presenter;
        private CancellationTokenSource? _cts;
        private int _loadGeneration;

        public LoadingService(ILoadingPresenter presenter, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<LoadingService>();
            _presenter = presenter;
        }

        /// <inheritdoc />
        public async UniTask<LoadResult> Load(
            IReadOnlyList<ILoadingOperation> operations,
            bool waitForInput = false,
            CancellationToken ct = default)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _cts.Token;

            var generation = ++_loadGeneration;
            await _presenter.Hide();

            _presenter.SetDescription("Loading...");
            _presenter.SetProgress(0f);
            var showTask = _presenter.Show();

            try
            {
                await UniTask.WhenAll(showTask, ExecuteOperations(operations, token));

                if (waitForInput)
                    await _presenter.WaitForInput(token);

                await ActivateDeferredOperations(operations, token);

                return LoadResult.Ok;
            }
            catch (OperationCanceledException)
            {
                return LoadResult.Ok;
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"Loading pipeline failed.");
                return LoadResult.Fail(ex);
            }
            finally
            {
                if (_loadGeneration == generation)
                    await _presenter.Hide();
            }
        }

        private static async UniTask ActivateDeferredOperations(
            IReadOnlyList<ILoadingOperation> operations, CancellationToken ct)
        {
            for (int i = 0; i < operations.Count; i++)
            {
                if (operations[i] is IDeferrableOperation deferred)
                    await deferred.Activate(ct);
            }
        }

        private async UniTask ExecuteOperations(IReadOnlyList<ILoadingOperation> operations, CancellationToken ct)
        {
            int count = operations.Count;

            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var op = operations[i];
                _presenter.SetDescription(op.Description);

                float baseProgress = (float)i / count;
                float stepWeight = 1f / count;
                var capturedBase = baseProgress;
                var capturedWeight = stepWeight;
                var progress = new Progress<float>(p =>
                {
                    _presenter.SetProgress(capturedBase + capturedWeight * p);
                });

                await op.Execute(progress, ct);
            }

            _presenter.SetProgress(1f);
        }
    }
}
