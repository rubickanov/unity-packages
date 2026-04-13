using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rubickanov.ACS.Runtime;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.ACS.Editor
{
    [CustomEditor(typeof(MonoEntity), true)]
    public class MonoEntityEditor : UnityEditor.Editor
    {
        private static readonly Color ReadColor = new(0.4f, 0.75f, 0.45f);
        private static readonly Color WriteColor = new(0.9f, 0.65f, 0.25f);
        private static readonly Color DimColor = new(1f, 1f, 1f, 0.4f);

        // Precomputed once: binding-badge hex color strings.
        private static readonly string ReadHex = ColorUtility.ToHtmlStringRGB(ReadColor);
        private static readonly string WriteHex = ColorUtility.ToHtmlStringRGB(WriteColor);

        // Lazy-init styles: EditorStyles.* is not guaranteed to be valid at type-init time.
        private static GUIStyle? _headerStyle;
        private static GUIStyle? _fieldStyle;
        private static GUIStyle? _bindingStyle;

        private static GUIStyle HeaderStyle => _headerStyle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11,
            normal = { textColor = DimColor }
        };

        private static GUIStyle FieldStyle => _fieldStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = DimColor }
        };

        private static GUIStyle BindingStyle => _bindingStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            richText = true,
            normal = { textColor = Color.white }
        };

        [InitializeOnLoadMethod]
        private static void ResetStyles()
        {
            _headerStyle = null;
            _fieldStyle = null;
            _bindingStyle = null;
        }

        private List<AspectInfo> _aspects = default!;
        private readonly Dictionary<string, bool> _foldouts = new();
        // Keyed by reference identity of AspectFieldInfo.Bindings; same list instance is reused each repaint.
        private readonly Dictionary<List<FieldBinding>, string> _bindingLabelCache = new();
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
            var context = (MonoEntity)target;
            var types = new List<Type>();

            foreach (var c in context.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (c is IEntityComponent)
                    types.Add(c.GetType());
            }

            types = types.Distinct().ToList();
            _aspects = AspectUsageAnalyzer.AnalyzeEntity(types);

            RebuildBindingLabelCache();
        }

        private void RebuildBindingLabelCache()
        {
            _bindingLabelCache.Clear();
            if (_aspects == null) return;

            var sb = new StringBuilder();
            foreach (var aspect in _aspects)
            {
                foreach (var field in aspect.Fields)
                {
                    sb.Clear();
                    bool first = true;
                    foreach (var binding in field.Bindings)
                    {
                        if (!first) sb.Append("  ");
                        first = false;
                        string hex = binding.IsWrite ? WriteHex : ReadHex;
                        string prefix = binding.IsWrite ? "W" : "R";
                        sb.Append("<color=#").Append(hex).Append('>').Append(prefix).Append("</color> ")
                          .Append(binding.ComponentName);
                    }
                    _bindingLabelCache[field.Bindings] = sb.ToString();
                }
            }
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (_aspects != null && _aspects.Count > 0)
            {
                EditorGUILayout.Space(4);

                EditorGUILayout.LabelField("ASPECTS", HeaderStyle);

                foreach (var aspect in _aspects)
                    DrawAspect(aspect);
            }

            // Runtime data always shows in play mode — World and other contexts may hold aspects
            // without having any IEntityComponent children to drive the static ASPECTS analysis.
            if (Application.isPlaying)
            {
                EditorGUILayout.Space(8);
                _runtimeDrawer.Draw((MonoEntity)target);
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
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(field.FieldName, FieldStyle, GUILayout.Width(140));

            if (!_bindingLabelCache.TryGetValue(field.Bindings, out string? label))
                label = string.Empty;

            EditorGUILayout.LabelField(label, BindingStyle);
            EditorGUILayout.EndHorizontal();
        }
    }
}
