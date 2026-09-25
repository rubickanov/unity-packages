using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rubickanov.DevConsole
{
    /// <summary>Runs the commands of <see cref="BindingRegistry"/> when their key chords are pressed.</summary>
    public class CommandBindings : MonoBehaviour
    {
        private static CommandBindings? _instance;

        private readonly List<string> _pendingExecute = new();

        /// <summary>
        /// Return true to keep bindings from firing, e.g. while the game's own chat or text field has focus. The console
        /// window blocks them by itself.
        /// </summary>
        public static Func<bool>? Suppress;

        /// <summary>Ensures the singleton MonoBehaviour exists. Creates one if needed.</summary>
        public static CommandBindings EnsureExists()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("[DevConsole] CommandBindings");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<CommandBindings>();
            return _instance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            Suppress = null;
        }

        // Otherwise only the bind command creates the object, so saved bindings did nothing in a new session until
        // something else was bound
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RestoreSaved()
        {
            if (BindingRegistry.Instance.Bindings.Count > 0)
                EnsureExists();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            var bindings = BindingRegistry.Instance.Bindings;
            if (bindings.Count == 0) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Don't execute binds while console is open
            if (DevConsoleIMGUI.IsOpen) return;
            if (DevConsoleUIToolkit.Instance != null && DevConsoleUIToolkit.Instance.IsVisible) return;
            if (Suppress != null && Suppress()) return;

            // Collect first, then execute. A bound command may be `bind`/`unbind`, which mutates the
            // bindings — executing inside the foreach would throw "Collection was modified".
            _pendingExecute.Clear();
            foreach (var kvp in bindings)
            {
                if (kvp.Key.WasPressedThisFrame(keyboard))
                    _pendingExecute.Add(kvp.Value);
            }

            // Logged like typed input: a bound command that fails would otherwise do nothing, visibly or otherwise
            for (int i = 0; i < _pendingExecute.Count; i++)
                CommandRegistry.Instance.ExecuteAndLog(_pendingExecute[i]);
        }
    }
}
