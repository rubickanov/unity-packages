using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Rubickanov.GameplayTags.Editor
{
    /// <summary>
    /// Property drawer for <see cref="SerializedGameplayTag"/>. Shows dropdown button with current path.
    /// </summary>
    [CustomPropertyDrawer(typeof(SerializedGameplayTag))]
    public sealed class GameplayTagPropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var pathProperty = property.FindPropertyRelative("_path");
            var currentPath = pathProperty.stringValue;
            var displayText = string.IsNullOrEmpty(currentPath) ? "None" : currentPath;

            var labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
            var buttonRect = new Rect(
                position.x + EditorGUIUtility.labelWidth + 2,
                position.y,
                position.width - EditorGUIUtility.labelWidth - 2,
                position.height);

            EditorGUI.LabelField(labelRect, label);

            if (EditorGUI.DropdownButton(buttonRect, new GUIContent(displayText), FocusType.Keyboard))
            {
                var dropdown = new GameplayTagDropdown(new AdvancedDropdownState(), path =>
                {
                    pathProperty.stringValue = path;
                    pathProperty.serializedObject.ApplyModifiedProperties();
                });

                dropdown.Show(buttonRect);
            }

            EditorGUI.EndProperty();
        }
    }
}
