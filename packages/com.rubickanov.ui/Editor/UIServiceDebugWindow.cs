using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.UI.Editor
{
    [InitializeOnLoad]
    internal static class DebugRegistryResetHook
    {
        static DebugRegistryResetHook()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode || change == PlayModeStateChange.ExitingPlayMode)
            {
                DebugRegistry.Instances.Clear();
            }
        }
    }

    public sealed class UIServiceDebugWindow : EditorWindow
    {
        private static readonly UILayer[] LayerOrder = { UILayer.Screen, UILayer.HUD, UILayer.Popup, UILayer.Overlay };

        private readonly Dictionary<int, bool> _instanceFoldouts = new();
        private Vector2 _scroll;

        [MenuItem("Rubickanov/UI Debug")]
        public static void Open()
        {
            var window = GetWindow<UIServiceDebugWindow>();
            window.titleContent = new GUIContent("UI Debug");
            window.Show();
        }

        private void OnInspectorUpdate() => Repaint();

        private void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("UIService instances exist only in Play Mode.", MessageType.Info);
                return;
            }

            var instances = DebugRegistry.Instances;
            EditorGUILayout.LabelField($"UIService Instances: {instances.Count}", EditorStyles.boldLabel);

            if (instances.Count == 0)
            {
                EditorGUILayout.HelpBox("No UIService instances registered.", MessageType.None);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (var i = 0; i < instances.Count; i++)
            {
                DrawInstance(i, instances[i]);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawInstance(int index, UIService service)
        {
            var key = service.GetHashCode();
            _instanceFoldouts.TryGetValue(key, out var expanded);
            expanded = EditorGUILayout.Foldout(expanded || index == 0, $"Instance #{index}", true);
            _instanceFoldouts[key] = expanded;

            if (!expanded) return;

            EditorGUI.indentLevel++;
            DrawInput(service);
            DrawActiveScreen(service);
            DrawPopupStack(service);
            DrawRegisteredViews(service);
            DrawActions(service);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
        }

        private static void DrawInput(UIService service)
        {
            EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            var captured = service.PointerCaptured.CurrentValue ? "captured" : "free";
            EditorGUILayout.LabelField("Pointer", $"{captured} ({service.DebugPointerCaptureCount} held)");
            EditorGUILayout.LabelField("Back stack depth", service.DebugBackStackDepth.ToString());
            EditorGUI.indentLevel--;
        }

        private static void DrawActiveScreen(UIService service)
        {
            EditorGUILayout.LabelField("Active Screen", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            var active = service.DebugActiveScreen;
            if (active != null) DrawViewRow(active.GetType(), active);
            else EditorGUILayout.LabelField("None");
            EditorGUI.indentLevel--;
        }

        private static void DrawPopupStack(UIService service)
        {
            var stack = service.DebugPopupStack;
            EditorGUILayout.LabelField($"Popup Stack (top → bottom): {stack.Count}", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            if (stack.Count == 0)
            {
                EditorGUILayout.LabelField("Empty");
            }
            else
            {
                for (var i = stack.Count - 1; i >= 0; i--)
                {
                    DrawViewRow(stack[i].GetType(), stack[i]);
                }
            }

            EditorGUI.indentLevel--;
        }

        private static void DrawRegisteredViews(UIService service)
        {
            EditorGUILayout.LabelField($"Registered Views: {service.DebugViews.Count}", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            foreach (var layer in LayerOrder)
            {
                DrawLayerGroup(service, layer);
            }

            EditorGUI.indentLevel--;
        }

        private static void DrawLayerGroup(UIService service, UILayer layer)
        {
            var count = 0;
            foreach (var kv in service.DebugViewLayers)
            {
                if (kv.Value == layer) count++;
            }

            EditorGUILayout.LabelField($"{layer} ({count})");
            if (count == 0) return;

            EditorGUI.indentLevel++;
            foreach (var kv in service.DebugViews)
            {
                if (!service.DebugViewLayers.TryGetValue(kv.Key, out var viewLayer) || viewLayer != layer) continue;
                DrawViewRow(kv.Key, kv.Value);
            }

            EditorGUI.indentLevel--;
        }

        private static void DrawViewRow(Type viewType, View view)
        {
            var color = GUI.color;
            GUI.color = view.State switch
            {
                ViewState.Shown => Color.green,
                ViewState.Showing or ViewState.Hiding => Color.yellow,
                _ => new Color(0.6f, 0.6f, 0.6f)
            };
            EditorGUILayout.LabelField(viewType.Name, view.State.ToString());
            GUI.color = color;
        }

        private static void DrawActions(UIService service)
        {
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(service.DebugActiveScreen == null && service.DebugPopupStack.Count == 0))
            {
                if (GUILayout.Button("Hide All"))
                {
                    service.HideAll();
                }
            }
        }
    }
}
