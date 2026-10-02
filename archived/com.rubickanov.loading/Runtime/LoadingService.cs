using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// <para>
    /// Not thread-safe. Call <see cref="Load"/> from a single thread (typically Unity's main thread).
    /// A new <see cref="Load"/> call cancels any in-flight one — the earlier call resolves as
    /// <see cref="LoadResult.Ok"/> (reentry cancel is considered normal), while external
    /// <see cref="CancellationToken"/> cancellation resolves as <see cref="LoadResult.Cancel"/>.
    /// </para>
    /// </summary>
    public class LoadingService : ILoadingService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly ILoadingPresenter _presenter;
        private readonly string _defaultDescription;
        private CancellationTokenSource? _cts;
        private int _loadGeneration;
        private bool _disposed;

        public LoadingService(
            ILoadingPresenter presenter,
            ILoggerFactory loggerFactory,
            string defaultDescription = "Loading...")
        {
            _logger = loggerFactory.CreateLogger<LoadingService>();
            _presenter = presenter;
            _defaultDescription = defaultDescription;
        }

        /// <inheritdoc />
        public async UniTask<LoadResult> Load(
            IReadOnlyList<ILoadingOperation> operations,
            bool waitForInput = false,
            CancellationToken ct = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LoadingService));

            if (operations.Count == 0)
                return LoadResult.Ok;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _cts.Token;

            var generation = ++_loadGeneration;
            await _presenter.Hide();

            _presenter.SetDescription(_defaultDescription);
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
                // External cancel (caller's ct) vs reentry cancel (a newer Load started):
                // reentry cancel bumps _loadGeneration, so generation != _loadGeneration.
                if (_loadGeneration != generation)
                    return LoadResult.Ok;

                return ct.IsCancellationRequested ? LoadResult.Cancel : LoadResult.Ok;
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"Loading pipeline failed.");
                _presenter.SetError(ex.Message);
                return LoadResult.Fail(ex);
            }
            finally
            {
                // Only the latest Load owns _cts; a newer Load has already cancelled+replaced it.
                // Dispose ours so its registration on the caller's ct doesn't linger until the
                // next Load/Dispose.
                if (_loadGeneration == generation)
                {
                    await _presenter.Hide();
                    _cts?.Dispose();
                    _cts = null;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
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
            var scoped = new ScopedProgress(_presenter.SetProgress);

            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var op = operations[i];
                _presenter.SetDescription(op.Description);

                float baseProgress = (float)i / count;
                float stepWeight = 1f / count;
                scoped.Reset(baseProgress, stepWeight);
                _presenter.SetProgress(baseProgress);

                var token = new ScopedProgressToken(scoped, scoped.Epoch);
                var sw = Stopwatch.StartNew();
                _logger.ZLogDebug($"Loading op [{i + 1}/{count}]: {op.Description}");
                try
                {
                    await op.Execute(token, ct);
                }
                finally
                {
                    scoped.Invalidate();
                    sw.Stop();
                    _logger.ZLogDebug($"Op done [{i + 1}/{count}]: {op.Description} ({sw.ElapsedMilliseconds} ms)");
                }
            }

            _presenter.SetProgress(1f);
        }
    }
}
