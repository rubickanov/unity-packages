using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rubickanov.DevConsole.Commands
{
    internal static class SceneCommands
    {
        [ConsoleCommand("scene", "Show active scene info", "Scene")]
        public static void Scene()
        {
            var scene = SceneManager.GetActiveScene();
            ConsoleLog.Log($"Name: {scene.name}");
            ConsoleLog.Log($"Path: {scene.path}");
            ConsoleLog.Log($"Build Index: {scene.buildIndex}");
            ConsoleLog.Log($"Loaded: {scene.isLoaded}");
            ConsoleLog.Log($"Dirty: {scene.isDirty}");
        }

        [ConsoleCommand("scene_list", "List all loaded scenes", "Scene")]
        public static void SceneList()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                var active = scene == SceneManager.GetActiveScene() ? " (active)" : "";
                ConsoleLog.Log($"  [{i}] {scene.name} — loaded: {scene.isLoaded}{active}");
            }
        }

        [ConsoleCommand("find", "Find a GameObject by name", "Scene")]
        public static void Find(string name)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                ConsoleLog.LogError($"GameObject '{name}' not found.");
                return;
            }

            ConsoleLog.Log($"Name: {go.name}");
            ConsoleLog.Log($"Path: {GetPath(go.transform)}");
            ConsoleLog.Log($"Active: {go.activeInHierarchy}");
            ConsoleLog.Log($"Position: {go.transform.position}");
        }

        [ConsoleCommand("inspect", "Inspect a GameObject's components", "Scene")]
        public static void Inspect(string name)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                ConsoleLog.LogError($"GameObject '{name}' not found.");
                return;
            }

            var components = go.GetComponents<Component>();
            ConsoleLog.Log($"<b>{go.name}</b> — {components.Length} component(s):");

            foreach (var comp in components)
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

        [ConsoleCommand("count", "Count GameObjects (all or by component type name)", "Scene")]
        public static void Count(string type = "")
        {
            if (string.IsNullOrEmpty(type))
            {
                var total = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude).Length;
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
            {
                ConsoleLog.LogError($"Component type '{type}' not found.");
                return;
            }

            var count = UnityEngine.Object.FindObjectsByType(foundType, FindObjectsInactive.Exclude).Length;
            ConsoleLog.Log($"{foundType.Name}: {count}");
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
