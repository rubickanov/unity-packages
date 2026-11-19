using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rubickanov.App
{
    /// <summary>
    /// Root of a game: holds the app's services, which live as long as the app, on their own <see cref="Lifecycle"/>.
    /// Made from code before the first scene loads, so no scene or prefab has to carry it. Each scene loaded in single
    /// mode gets a <see cref="SceneScope{TApp,TScene}"/> under it; an additive scene joins the one there is. The game
    /// subclasses it once, says how its services and its scene scope are made, and calls <see cref="ResetStatics"/>,
    /// <see cref="Boot"/> and <see cref="JoinActiveScene"/> from its own RuntimeInitializeOnLoadMethods (Unity does not
    /// call them on a generic type).
    /// </summary>
    /// <typeparam name="TSelf">The game's subclass.</typeparam>
    /// <typeparam name="TApp">The app's services.</typeparam>
    /// <typeparam name="TScene">One scene's services.</typeparam>
    public abstract class AppScope<TSelf, TApp, TScene> : MonoBehaviour
        where TSelf : AppScope<TSelf, TApp, TScene>
        where TApp : class
        where TScene : class
    {
        private static readonly List<Action<TApp, Lifecycle>> Extensions = new();
        private static TSelf _instance;

        private Lifecycle _life;
        private SceneScope<TApp, TScene> _game;

        /// <summary>The root scope, made on first use.</summary>
        public static TSelf Instance
        {
            get
            {
                if (_instance == null)
                {
                    var root = new GameObject(typeof(TSelf).Name);
                    DontDestroyOnLoad(root);
                    _instance = root.AddComponent<TSelf>();
                }

                return _instance;
            }
        }

        /// <summary>The app's services; null once the app has gone.</summary>
        public TApp Services { get; private set; }

        /// <summary>The services of the scene now, if it has its scope.</summary>
        public TScene Game => _game != null ? _game.Services : null;

        /// <summary>
        /// Adds services from an assembly the game does not reference, such as the dev console that builds may leave
        /// out: made after the app's own, on their lifecycle. Call it before scenes load, from AfterAssembliesLoaded.
        /// </summary>
        public static void Extend(Action<TApp, Lifecycle> install) => Extensions.Add(install);

        /// <summary>Makes the app's services on <paramref name="life"/>; the scope's transform is theirs to hang objects on.</summary>
        protected abstract TApp CreateServices(Lifecycle life);

        /// <summary>Makes the scope of a scene just loaded: <c>SceneScope&lt;…&gt;.Create&lt;GameScope&gt;(scene, app)</c>.</summary>
        protected abstract SceneScope<TApp, TScene> CreateSceneScope(Scene scene, TApp app);

        /// <summary>Forgets the last play session's extensions: call it at SubsystemRegistration (no domain reload).</summary>
        protected static void ResetStatics() => Extensions.Clear();

        /// <summary>Makes the root scope: call it at BeforeSceneLoad.</summary>
        protected static void Boot() => _ = Instance;

        /// <summary>
        /// Gives the open scene its scope: call it at AfterSceneLoad. Entering Play Mode in the Editor, the open scene is
        /// loaded before the root scope listens for loads.
        /// </summary>
        protected static void JoinActiveScene() => Instance.EnsureGame(SceneManager.GetActiveScene());

        private void Awake()
        {
            _life = new Lifecycle();
            try
            {
                Services = CreateServices(_life);
                foreach (Action<TApp, Lifecycle> install in Extensions)
                {
                    install(Services, _life);
                }
            }
            catch
            {
                // Half an app runs nothing: what was made goes at once.
                _life.Dispose();
                Services = null;
                throw;
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        // Leaving Play Mode or quitting may destroy this scope before the scene's. The scene's services go first, while
        // the app's services they let go of are still there.
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_game != null)
            {
                _game.Dispose();
            }

            _life?.Dispose();
            Services = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single)
            {
                EnsureGame(scene);
            }
        }

        // The old scene's scope went with its scene, so a destroyed one reads as null here.
        private void EnsureGame(Scene scene)
        {
            if (Services == null)
            {
                return;
            }

            if (_game == null || _game.gameObject.scene != scene)
            {
                _game = CreateSceneScope(scene, Services);
            }
        }
    }
}
