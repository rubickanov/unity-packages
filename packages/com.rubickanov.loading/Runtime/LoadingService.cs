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
        private Action<string>? _fatalErrorHandler;

        public LoadingService(ILoadingPresenter presenter, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<LoadingService>();
            _presenter = presenter;
        }

        /// <summary>
        /// Registers a handler invoked on fatal loading errors instead of the default behavior.
        /// </summary>
        public void SetFatalErrorHandler(Action<string> handler)
        {
            _fatalErrorHandler = handler;
        }

        /// <inheritdoc />
        public async UniTask Load(IReadOnlyList<ILoadingOperation> operations, CancellationToken ct = default)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _cts.Token;

            _presenter.SetDescription("Loading...");
            _presenter.SetProgress(0f);
            await _presenter.Show();

            try
            {
                int count = operations.Count;

                for (int i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
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

                    await op.Execute(progress, token);
                }

                _presenter.SetProgress(1f);
            }
            catch (OperationCanceledException)
            {
                // Silently cancel
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"Loading pipeline failed.");

                if (_fatalErrorHandler != null)
                {
                    _fatalErrorHandler(ex.Message);
                    return;
                }

                _presenter.SetError(ex.Message);
                await UniTask.Delay(2000, cancellationToken: CancellationToken.None);
            }
            finally
            {
                _presenter.Hide();
            }
        }
    }
}
