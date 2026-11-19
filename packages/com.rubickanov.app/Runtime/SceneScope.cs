using System;
using Rubickanov.Log;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rubickanov.App
{
    /// <summary>
    /// Holds the services of one scene under <see cref="AppScope{TSelf,TApp,TScene}"/>, on their own
    /// <see cref="Lifecycle"/>. Made in the scene each time a scene loads in single mode, after its objects' Awake, and
    /// destroyed with it, so nothing carries over into the next scene. The game subclasses it once and says how the
    /// services are made.
    /// </summary>
    /// <typeparam name="TApp">The app's services.</typeparam>
    /// <typeparam name="TScene">The scene's services.</typeparam>
    public abstract class SceneScope<TApp, TScene> : MonoBehaviour
        where TApp : class
        where TScene : class
    {
        private static readonly LogChannel Log = LogChannel.Get("App");

        private Lifecycle _life;

        /// <summary>The scene's services; null once the scene has gone, or if they failed to be made.</summary>
        public TScene Services { get; private set; }

        /// <summary>Makes the scope of type <typeparamref name="TScope"/> in <paramref name="scene"/> and its services.</summary>
        public static TScope Create<TScope>(Scene scene, TApp app) where TScope : SceneScope<TApp, TScene>
        {
            var scopeObject = new GameObject(typeof(TScope).Name);
            SceneManager.MoveGameObjectToScene(scopeObject, scene);
            var scope = scopeObject.AddComponent<TScope>();
            scope._life = new Lifecycle();
            try
            {
                scope.Services = scope.CreateServices(app, scene, scope._life);
            }
            catch (Exception exception)
            {
                // Half a scene's services run nothing: what was made goes at once. The scope stays, empty, so the
                // scene is not set up twice.
                scope._life.Dispose();
                Log.Exception(exception);
                scope.OnFailed(app, scene);
            }

            return scope;
        }

        /// <summary>Lets the scene's services go now, before the scene itself.</summary>
        public void Dispose()
        {
            _life?.Dispose();
            Services = null;
        }

        /// <summary>Makes the scene's services on <paramref name="life"/>.</summary>
        protected abstract TScene CreateServices(TApp app, Scene scene, Lifecycle life);

        /// <summary>The services could not be made (the exception is logged, what was made is gone): a way out.</summary>
        protected virtual void OnFailed(TApp app, Scene scene)
        {
        }

        private void OnDestroy() => Dispose();
    }
}
