using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rubickanov.DevConsole.Commands
{
    internal static class SceneCommands
    {
        private const int MaxInspected = 10;

        [ConsoleCommand("scene", "Show active scene info", "Scene")]
        public static void Scene()
        {
            var scene = SceneManager.GetActiveScene();
            ConsoleLog.Log($"Name: {scene.name}");
            ConsoleLog.Log($"Path: {scene.path}");
            ConsoleLog.Log($"Build Index: {scene.buildIndex}");
            ConsoleLog.Log($"Loaded: {scene.isLoaded}");
#if UNITY_EDITOR
            // Unsaved changes exist only in the editor
            ConsoleLog.Log($"Dirty: {scene.isDirty}");
#endif
        }

        [ConsoleCommand("scene list", "List all loaded scenes", "Scene")]
        public static void SceneList()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                var active = scene == SceneManager.GetActiveScene() ? " (active)" : "";
                ConsoleLog.Log($"  [{i}] {scene.name} — loaded: {scene.isLoaded}{active}");
            }
        }

        [ConsoleCommand("scene load", "Load a scene from the build by name or build index, replacing the open ones or added to them", "Scene")]
        [AutoComplete(0, typeof(BuildSceneProvider))]
        public static void SceneLoad(string scene, bool additive = false)
        {
            var index = FindBuildIndex(scene);
            if (index < 0)
                throw new CommandException(
                    $"No scene '{scene}' in the build. Available: {string.Join(", ", BuildSceneProvider.Names())}");

            SceneManager.LoadScene(index, additive ? LoadSceneMode.Additive : LoadSceneMode.Single);
            ConsoleLog.Log($"Loading {BuildSceneProvider.NameAt(index)}{(additive ? " additively" : "")}.");
        }

        [ConsoleCommand("scene reload", "Load the active scene again", "Scene")]
        public static void SceneReload()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex < 0)
                throw new CommandException($"'{scene.name}' is not in the build, so it cannot be loaded again.");

            SceneManager.LoadScene(scene.buildIndex);
            ConsoleLog.Log($"Reloading {scene.name}.");
        }

        [ConsoleCommand("inspect", "Show every GameObject with this name or path, inactive ones too: path, state, position, components", "Scene")]
        public static void Inspect(string name)
        {
            var matches = new List<Transform>();
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (Matches(t, name)) matches.Add(t);
            }

            if (matches.Count == 0)
                throw new CommandException($"No GameObject named '{name}'.");

            matches.Sort((a, b) => string.CompareOrdinal(GetPath(a), GetPath(b)));
            for (int i = 0; i < matches.Count && i < MaxInspected; i++)
                LogObject(matches[i].gameObject);

            if (matches.Count > MaxInspected)
                ConsoleLog.Log($"…and {matches.Count - MaxInspected} more. Give more of the path to narrow it down.");
        }

        // A name is compared with the object's own name, a path (with /) with the end of its hierarchy path
        internal static bool Matches(Transform t, string name)
        {
            if (name.IndexOf('/') < 0)
                return string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase);

            var path = GetPath(t);
            return path.EndsWith(name, StringComparison.OrdinalIgnoreCase) &&
                   (path.Length == name.Length || path[path.Length - name.Length - 1] == '/');
        }

        private static void LogObject(GameObject go)
        {
            var state = go.activeInHierarchy ? "active" : go.activeSelf ? "parent inactive" : "inactive";
            ConsoleLog.Log($"<b>{GetPath(go.transform)}</b> ({state}, scene {go.scene.name}) at {go.transform.position}");

            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null)
                {
                    ConsoleLog.LogWarning("  (Missing Script)");
                    continue;
                }

                var enabled = comp is Behaviour b ? (b.enabled ? " [ON]" : " [OFF]") : "";
                ConsoleLog.Log($"  {comp.GetType().Name}{enabled}");
            }
        }

        [ConsoleCommand("count", "Count GameObjects, or those with a component type; inactive ones only when asked", "Scene")]
        public static void Count(string type = "", bool includeInactive = false)
        {
            var inactive = includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
            if (string.IsNullOrEmpty(type))
            {
                var total = UnityEngine.Object.FindObjectsByType<GameObject>(inactive).Length;
                ConsoleLog.Log($"Total GameObjects: {total}");
                return;
            }

            // Search for type by name across all loaded assemblies
            Type? foundType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch { continue; }

                for (int i = 0; i < types.Length; i++)
                {
                    var t = types[i];
                    if (string.Equals(t.Name, type, StringComparison.OrdinalIgnoreCase) &&
                        typeof(Component).IsAssignableFrom(t))
                    {
                        foundType = t;
                        break;
                    }
                }
                if (foundType != null) break;
            }

            if (foundType == null)
                throw new CommandException($"Component type '{type}' not found.");

            var count = UnityEngine.Object.FindObjectsByType(foundType, inactive).Length;
            ConsoleLog.Log($"{foundType.Name}: {count}");
        }

        private static int FindBuildIndex(string scene)
        {
            var total = SceneManager.sceneCountInBuildSettings;
            if (int.TryParse(scene, out var index))
                return index >= 0 && index < total ? index : -1;

            for (int i = 0; i < total; i++)
            {
                if (string.Equals(BuildSceneProvider.NameAt(i), scene, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        private static string GetPath(Transform t)
        {
            var path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
    }
}
