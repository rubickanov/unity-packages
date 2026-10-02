using UnityEditor;
using UnityEngine;

namespace Rubickanov.GAS.Editor
{
    [CustomEditor(typeof(GameplayEffectAsset))]
    public sealed class GameplayEffectAssetEditor : UnityEditor.Editor
    {
        private SerializedProperty _duration = default!;
        private SerializedProperty _durationSeconds = default!;
        private SerializedProperty _period = default!;
        private SerializedProperty _stacking = default!;
        private SerializedProperty _modifiers = default!;
        private SerializedProperty _effectTag = default!;
        private SerializedProperty _grantedTags = default!;
        private SerializedProperty _requiredTags = default!;
        private SerializedProperty _blockedTags = default!;
        private SerializedProperty _removeEffectsWithTags = default!;

        private void OnEnable()
        {
            _duration = serializedObject.FindProperty("_duration");
            _durationSeconds = serializedObject.FindProperty("_durationSeconds");
            _period = serializedObject.FindProperty("_period");
            _stacking = serializedObject.FindProperty("_stacking");
            _modifiers = serializedObject.FindProperty("_modifiers");
            _effectTag = serializedObject.FindProperty("_effectTag");
            _grantedTags = serializedObject.FindProperty("_grantedTags");
            _requiredTags = serializedObject.FindProperty("_requiredTags");
            _blockedTags = serializedObject.FindProperty("_blockedTags");
            _removeEffectsWithTags = serializedObject.FindProperty("_removeEffectsWithTags");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Duration section
            EditorGUILayout.LabelField("Duration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_duration);

            var durationPolicy = (DurationPolicy)_duration.enumValueIndex;
            if (durationPolicy == DurationPolicy.Duration)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_durationSeconds, new GUIContent("Duration (seconds)"));
                EditorGUI.indentLevel--;
            }

            if (durationPolicy != DurationPolicy.Instant)
            {
                EditorGUILayout.PropertyField(_period, new GUIContent("Period (seconds)"));
                EditorGUILayout.PropertyField(_stacking);
            }

            EditorGUILayout.Space(8);

            // Effect Tag
            EditorGUILayout.PropertyField(_effectTag, new GUIContent("Effect Tag"));

            EditorGUILayout.Space(8);

            // Modifiers section
            EditorGUILayout.LabelField("Modifiers", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_modifiers, new GUIContent("Modifiers"), true);

            EditorGUILayout.Space(8);

            // Tags section
            EditorGUILayout.LabelField("Tags", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_grantedTags, new GUIContent("Granted Tags"));
            EditorGUILayout.PropertyField(_requiredTags, new GUIContent("Application Required Tags"));
            EditorGUILayout.PropertyField(_blockedTags, new GUIContent("Application Blocked Tags"));
            EditorGUILayout.PropertyField(_removeEffectsWithTags, new GUIContent("Remove Effects With Tags"));

            serializedObject.ApplyModifiedProperties();
        }
    }
}
