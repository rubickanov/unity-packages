#nullable enable
using System.Reflection;
using Rubickanov.Utils;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.Utils.Editor
{
    /// <summary>
    /// Custom inspector that renders <see cref="DescriptionAttribute"/> text above the default inspector
    /// for any MonoBehaviour.
    /// </summary>
    [CustomEditor(typeof(MonoBehaviour), true)]
    [CanEditMultipleObjects]
    public class ComponentDescriptionEditor : UnityEditor.Editor
    {
        private static GUIStyle? _style;

        private string? _description;

        private void OnEnable()
        {
            _description = target.GetType().GetCustomAttribute<DescriptionAttribute>()?.Description;
        }

        public override void OnInspectorGUI()
        {
            if (_description != null)
            {
                EditorGUILayout.LabelField(_description, GetStyle());
                EditorGUILayout.Space(2);
            }

            DrawDefaultInspector();
        }

        private static GUIStyle GetStyle() => _style ??= new GUIStyle(EditorStyles.miniLabel)
        {
            wordWrap = true,
            fontStyle = FontStyle.Italic,
            normal = { textColor = new Color(1f, 1f, 1f, 0.35f) }
        };
    }
}
