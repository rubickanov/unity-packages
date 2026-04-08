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
        private AsyncOperation? _asyncOp;

        public string Description => $"Loading {_sceneName}...";

        public LoadSceneOperation(string sceneName)
        {
            _sceneName = sceneName;
        }

        public async UniTask Execute(IProgress<float> progress, CancellationToken ct)
        {
            progress.Report(0f);

            _asyncOp = SceneManager.LoadSceneAsync(_sceneName, LoadSceneMode.Single);
            _asyncOp.allowSceneActivation = false;

            while (_asyncOp.progress < 0.9f)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report(_asyncOp.progress / 0.9f);
                await UniTask.Yield(ct);
            }

            progress.Report(1f);
        }

        public async UniTask Activate(CancellationToken ct)
        {
            if (_asyncOp == null)
                return;

            _asyncOp.allowSceneActivation = true;
            await UniTask.WaitUntil(() => _asyncOp.isDone, cancellationToken: ct);
        }
    }
}
