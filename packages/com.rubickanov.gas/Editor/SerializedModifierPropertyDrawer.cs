using Rubickanov.GameplayTags.Editor;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Rubickanov.GAS.Editor
{
    [CustomPropertyDrawer(typeof(SerializedModifier))]
    public sealed class SerializedModifierPropertyDrawer : PropertyDrawer
    {
        private const float Spacing = 4f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var attributeProp = property.FindPropertyRelative("_attribute").FindPropertyRelative("_path");
            var operationProp = property.FindPropertyRelative("_operation");
            var valueProp = property.FindPropertyRelative("_value");

            var lineHeight = EditorGUIUtility.singleLineHeight;

            // Split into 3 columns: [Attribute tag dropdown] [Operation dropdown] [Value field]
            float totalWidth = position.width;
            float tagWidth = totalWidth * 0.45f;
            float opWidth = totalWidth * 0.25f;
            float valueWidth = totalWidth - tagWidth - opWidth - Spacing * 2;

            var tagRect = new Rect(position.x, position.y, tagWidth, lineHeight);
            var opRect = new Rect(position.x + tagWidth + Spacing, position.y, opWidth, lineHeight);
            var valueRect = new Rect(position.x + tagWidth + opWidth + Spacing * 2, position.y, valueWidth, lineHeight);

            // Attribute tag dropdown
            var currentPath = attributeProp.stringValue;
            var displayText = string.IsNullOrEmpty(currentPath) ? "Select Attribute..." : currentPath;

            if (EditorGUI.DropdownButton(tagRect, new GUIContent(displayText), FocusType.Keyboard))
            {
                var dropdown = new GameplayTagDropdown(new AdvancedDropdownState(), path =>
                {
                    attributeProp.stringValue = path;
                    attributeProp.serializedObject.ApplyModifiedProperties();
                });

                dropdown.Show(tagRect);
            }

            // Operation enum
            EditorGUI.PropertyField(opRect, operationProp, GUIContent.none);

            // Value
            EditorGUI.PropertyField(valueRect, valueProp, GUIContent.none);

            EditorGUI.EndProperty();
        }
    }
}
