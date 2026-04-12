using System;
using System.Collections.Generic;
using System.Linq;
using Rubickanov.ACS.Runtime;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.ACS.Editor
{
    [CustomEditor(typeof(EntityContext), true)]
    public class EntityContextEditor : UnityEditor.Editor
    {
        private static readonly Color ReadColor = new(0.4f, 0.75f, 0.45f);
        private static readonly Color WriteColor = new(0.9f, 0.65f, 0.25f);
        private static readonly Color DimColor = new(1f, 1f, 1f, 0.4f);

        private List<AspectInfo> _aspects = default!;
        private readonly Dictionary<string, bool> _foldouts = new();
        private RuntimeAspectDrawer _runtimeDrawer = default!;

        private void OnEnable()
        {
            _runtimeDrawer = new RuntimeAspectDrawer();
            Refresh();
        }

        private void OnDisable()
        {
            _runtimeDrawer?.Dispose();
        }

        public override bool RequiresConstantRepaint() => Application.isPlaying;

        private void Refresh()
        {
            var context = (EntityContext)target;
            var types = new List<Type>();

            foreach (var c in context.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (c is IEntityComponent)
                    types.Add(c.GetType());
            }

            types = types.Distinct().ToList();
            _aspects = AspectUsageAnalyzer.AnalyzeEntity(types);
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (_aspects != null && _aspects.Count > 0)
            {
                EditorGUILayout.Space(4);

                var headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 11,
                    normal = { textColor = DimColor }
                };
                EditorGUILayout.LabelField("ASPECTS", headerStyle);

                foreach (var aspect in _aspects)
                    DrawAspect(aspect);
            }

            // Runtime data always shows in play mode — World and other contexts may hold aspects
            // without having any IEntityComponent children to drive the static ASPECTS analysis.
            if (Application.isPlaying)
            {
                EditorGUILayout.Space(8);
                _runtimeDrawer.Draw((EntityContext)target);
            }
        }

        private void DrawAspect(AspectInfo aspect)
        {
            if (!_foldouts.ContainsKey(aspect.AspectName))
                _foldouts[aspect.AspectName] = false;

            _foldouts[aspect.AspectName] = EditorGUILayout.Foldout(
                _foldouts[aspect.AspectName], aspect.AspectName, true, EditorStyles.foldoutHeader);

            if (!_foldouts[aspect.AspectName]) return;

            EditorGUI.indentLevel++;

            foreach (var field in aspect.Fields)
                DrawField(field);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(2);
        }

        private void DrawField(AspectFieldInfo field)
        {
            var fieldStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = DimColor }
            };

            var bindingStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                richText = true,
                normal = { textColor = Color.white }
            };

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(field.FieldName, fieldStyle, GUILayout.Width(140));

            var parts = new List<string>();
            foreach (var binding in field.Bindings)
            {
                Color color = binding.IsWrite ? WriteColor : ReadColor;
                string prefix = binding.IsWrite ? "W" : "R";
                string hex = ColorUtility.ToHtmlStringRGB(color);
                parts.Add($"<color=#{hex}>{prefix}</color> {binding.ComponentName}");
            }

            EditorGUILayout.LabelField(string.Join("  ", parts), bindingStyle);
            EditorGUILayout.EndHorizontal();
        }
    }
}
