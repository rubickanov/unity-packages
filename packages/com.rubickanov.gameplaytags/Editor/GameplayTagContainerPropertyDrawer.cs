using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Rubickanov.GameplayTags.Editor
{
    /// <summary>
    /// Property drawer for <see cref="SerializedGameplayTagContainer"/>. Shows tag list with add/remove buttons.
    /// </summary>
    [CustomPropertyDrawer(typeof(SerializedGameplayTagContainer))]
    public sealed class GameplayTagContainerPropertyDrawer : PropertyDrawer
    {
        private const float ButtonWidth = 20f;
        private const float Spacing = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var pathsProperty = property.FindPropertyRelative("_paths");
            var count = pathsProperty.arraySize;

            // Header + each tag line + "Add Tag" button
            var lines = 1 + count + 1;
            return lines * (EditorGUIUtility.singleLineHeight + Spacing);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var pathsProperty = property.FindPropertyRelative("_paths");
            var lineHeight = EditorGUIUtility.singleLineHeight;
            var y = position.y;

            // Header
            var headerRect = new Rect(position.x, y, position.width, lineHeight);
            EditorGUI.LabelField(headerRect, label, EditorStyles.boldLabel);
            y += lineHeight + Spacing;

            // Tag entries
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel++;

            for (var i = 0; i < pathsProperty.arraySize; i++)
            {
                var entryRect = new Rect(position.x, y, position.width, lineHeight);
                var labelRect = new Rect(
                    entryRect.x + EditorGUI.indentLevel * 15f,
                    entryRect.y,
                    entryRect.width - ButtonWidth - Spacing - EditorGUI.indentLevel * 15f,
                    lineHeight);
                var removeRect = new Rect(
                    entryRect.xMax - ButtonWidth,
                    entryRect.y,
                    ButtonWidth,
                    lineHeight);

                var element = pathsProperty.GetArrayElementAtIndex(i);
                var path = element.stringValue;

                EditorGUI.LabelField(labelRect, string.IsNullOrEmpty(path) ? "None" : path);

                if (GUI.Button(removeRect, "\u2212")) // minus sign
                {
                    pathsProperty.DeleteArrayElementAtIndex(i);
                    pathsProperty.serializedObject.ApplyModifiedProperties();
                    break;
                }

                y += lineHeight + Spacing;
            }

            // Add Tag button
            var addRect = new Rect(
                position.x + EditorGUI.indentLevel * 15f,
                y,
                position.width - EditorGUI.indentLevel * 15f,
                lineHeight);

            if (GUI.Button(addRect, "Add Tag"))
            {
                var dropdown = new GameplayTagDropdown(new AdvancedDropdownState(), path =>
                {
                    if (string.IsNullOrEmpty(path))
                        return;

                    // Duplicate prevention
                    for (var i = 0; i < pathsProperty.arraySize; i++)
                    {
                        if (pathsProperty.GetArrayElementAtIndex(i).stringValue == path)
                            return;
                    }

                    pathsProperty.InsertArrayElementAtIndex(pathsProperty.arraySize);
                    pathsProperty.GetArrayElementAtIndex(pathsProperty.arraySize - 1).stringValue = path;
                    pathsProperty.serializedObject.ApplyModifiedProperties();
                });

                dropdown.Show(addRect);
            }

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }
}
