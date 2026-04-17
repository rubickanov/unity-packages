using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rubickanov.Loading
{
    /// <summary>
    /// Loads and activates a Unity scene. Reports progress based on <see cref="AsyncOperation.progress"/>.
    /// </summary>
    public class LoadSceneOperation : ILoadingOperation, IDeferrableOperation
    {
        private readonly string _sceneName;
        private readonly LoadSceneMode _mode;
        private readonly string? _description;
        private AsyncOperation? _asyncOp;
        private bool _readyToActivate;

        public string Description => _description ?? $"Loading {_sceneName}...";

        public LoadSceneOperation(string sceneName, LoadSceneMode mode = LoadSceneMode.Single, string? description = null)
        {
            _sceneName = sceneName;
            _mode = mode;
            _description = description;
        }

        public async UniTask Execute(IProgress<float> progress, CancellationToken ct)
        {
            _readyToActivate = false;
            progress.Report(0f);

            _asyncOp = SceneManager.LoadSceneAsync(_sceneName, _mode);
            if (_asyncOp == null)
                throw new InvalidOperationException(
                    $"Scene '{_sceneName}' could not be loaded. Is it listed in Build Settings?");

            _asyncOp.allowSceneActivation = false;

            while (_asyncOp.progress < 0.9f)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report(_asyncOp.progress / 0.9f);
                await UniTask.Yield(ct);
            }

            progress.Report(1f);
            _readyToActivate = true;
        }

        public async UniTask Activate(CancellationToken ct)
        {
            if (!_readyToActivate || _asyncOp == null)
                return;

            _asyncOp.allowSceneActivation = true;
            await UniTask.WaitUntil(() => _asyncOp.isDone, cancellationToken: ct);
            _readyToActivate = false;
        }
    }
}
