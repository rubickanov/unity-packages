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
    public class LoadSceneOperation : ILoadingOperation
    {
        private readonly string _sceneName;

        public string Description => $"Loading {_sceneName}...";

        public LoadSceneOperation(string sceneName)
        {
            _sceneName = sceneName;
        }

        public async UniTask Execute(IProgress<float> progress, CancellationToken ct)
        {
            progress.Report(0f);

            var asyncOp = SceneManager.LoadSceneAsync(_sceneName, LoadSceneMode.Single);
            asyncOp.allowSceneActivation = false;

            while (asyncOp.progress < 0.9f)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report(asyncOp.progress / 0.9f);
                await UniTask.Yield(ct);
            }

            asyncOp.allowSceneActivation = true;
            await UniTask.WaitUntil(() => asyncOp.isDone, cancellationToken: ct);

            progress.Report(1f);
        }
    }
}
