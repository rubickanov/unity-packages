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
            var priorityProp = property.FindPropertyRelative("_priority");

            var lineHeight = EditorGUIUtility.singleLineHeight;

            // Layout: [Tag 40%] [Op 20%] [Value 25%] [Priority 15%] with 3 spacings
            float totalWidth = position.width - Spacing * 3;
            float tagWidth = totalWidth * 0.40f;
            float opWidth = totalWidth * 0.20f;
            float valueWidth = totalWidth * 0.25f;
            float priorityWidth = totalWidth * 0.15f;

            float x = position.x;
            var tagRect = new Rect(x, position.y, tagWidth, lineHeight);
            x += tagWidth + Spacing;
            var opRect = new Rect(x, position.y, opWidth, lineHeight);
            x += opWidth + Spacing;
            var valueRect = new Rect(x, position.y, valueWidth, lineHeight);
            x += valueWidth + Spacing;
            var priorityRect = new Rect(x, position.y, priorityWidth, lineHeight);

            // Attribute tag dropdown
            var currentPath = attributeProp.stringValue;
            var displayText = string.IsNullOrEmpty(currentPath) ? "Select Attribute..." : currentPath;
            var tagContent = new GUIContent(displayText);
            var prevColor = GUI.color;
            if (string.IsNullOrEmpty(currentPath))
                GUI.color = new Color(1f, 0.6f, 0.6f);

            if (EditorGUI.DropdownButton(tagRect, tagContent, FocusType.Keyboard))
            {
                var dropdown = new GameplayTagDropdown(new AdvancedDropdownState(), path =>
                {
                    attributeProp.stringValue = path;
                    attributeProp.serializedObject.ApplyModifiedProperties();
                });

                dropdown.Show(tagRect);
            }
            GUI.color = prevColor;

            EditorGUI.PropertyField(opRect, operationProp, GUIContent.none);
            EditorGUI.PropertyField(valueRect, valueProp, GUIContent.none);

            var priorityLabel = new GUIContent("Pri", "Override priority. Only used by Override operation — max priority wins, ties go to last applied.");
            EditorGUIUtility.labelWidth = 24f;
            EditorGUI.PropertyField(priorityRect, priorityProp, priorityLabel);
            EditorGUIUtility.labelWidth = 0f;

            EditorGUI.EndProperty();
        }
    }
}
