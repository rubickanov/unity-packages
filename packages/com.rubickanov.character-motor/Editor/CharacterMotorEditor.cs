using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.Motor.Editor
{
    [CustomEditor(typeof(CharacterMotor))]
    public class CharacterMotorEditor : UnityEditor.Editor
    {
        private SerializedProperty _bodyType;
        private SerializedProperty _groundMask;
        private SerializedProperty _modules;

        private static Type[] _moduleTypes;
        private static string[] _moduleTypeNames;

        private readonly HashSet<int> _foldouts = new();

        private void OnEnable()
        {
            _bodyType = serializedObject.FindProperty("_bodyType");
            _groundMask = serializedObject.FindProperty("_groundMask");
            _modules = serializedObject.FindProperty("_modules");

            if (_moduleTypes == null)
                CacheModuleTypes();
        }

        private static void CacheModuleTypes()
        {
            _moduleTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IMotorModule).IsAssignableFrom(t))
                .OrderBy(t => t.Name)
                .ToArray();

            _moduleTypeNames = _moduleTypes.Select(t => t.Name).ToArray();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_bodyType);
            EditorGUILayout.PropertyField(_groundMask);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Modules", EditorStyles.boldLabel);

            for (int i = 0; i < _modules.arraySize; i++)
                DrawModule(i);

            // Check priority order
            bool outOfOrder = false;
            int lastPriority = int.MinValue;
            for (int i = 0; i < _modules.arraySize; i++)
            {
                var obj = _modules.GetArrayElementAtIndex(i).managedReferenceValue;
                if (obj is IMotorModule m)
                {
                    if (m.Priority < lastPriority)
                    {
                        outOfOrder = true;
                        break;
                    }
                    lastPriority = m.Priority;
                }
            }
            if (outOfOrder)
                EditorGUILayout.HelpBox("Modules are not sorted by priority. Serialized order does not affect execution — the simulation sorts by Priority at runtime.", MessageType.Info);

            EditorGUILayout.Space(4);
            DrawAddButton();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawModule(int index)
        {
            var element = _modules.GetArrayElementAtIndex(index);
            var obj = element.managedReferenceValue;
            string typeName = obj != null ? obj.GetType().Name : "(null)";
            if (obj is IMotorModule module)
                typeName = $"{typeName}  (Priority: {module.Priority})";
            bool expanded = _foldouts.Contains(index);

            EditorGUILayout.BeginHorizontal();

            expanded = EditorGUILayout.Foldout(expanded, typeName, true);
            if (expanded) _foldouts.Add(index); else _foldouts.Remove(index);

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(index == 0))
            {
                if (GUILayout.Button("\u25b2", GUILayout.Width(24)))
                {
                    _modules.MoveArrayElement(index, index - 1);
                    EditorGUILayout.EndHorizontal();
                    return;
                }
            }

            using (new EditorGUI.DisabledScope(index == _modules.arraySize - 1))
            {
                if (GUILayout.Button("\u25bc", GUILayout.Width(24)))
                {
                    _modules.MoveArrayElement(index, index + 1);
                    EditorGUILayout.EndHorizontal();
                    return;
                }
            }

            if (GUILayout.Button("\u2212", GUILayout.Width(24)))
            {
                _modules.DeleteArrayElementAtIndex(index);
                _foldouts.Remove(index);
                EditorGUILayout.EndHorizontal();
                return;
            }

            EditorGUILayout.EndHorizontal();

            if (expanded && obj != null)
            {
                EditorGUI.indentLevel++;
                var end = element.GetEndProperty();
                var child = element.Copy();
                bool enter = true;
                while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
                {
                    enter = false;
                    EditorGUILayout.PropertyField(child, true);
                }
                EditorGUI.indentLevel--;
            }
        }

        private HashSet<Type> GetExistingModuleTypes()
        {
            var set = new HashSet<Type>();
            for (int i = 0; i < _modules.arraySize; i++)
            {
                var obj = _modules.GetArrayElementAtIndex(i).managedReferenceValue;
                if (obj != null) set.Add(obj.GetType());
            }
            return set;
        }

        private void DrawAddButton()
        {
            if (_moduleTypes == null || _moduleTypes.Length == 0)
            {
                EditorGUILayout.HelpBox("No IMotorModule implementations found.", MessageType.Warning);
                return;
            }

            if (GUILayout.Button("Add Module"))
            {
                var existing = GetExistingModuleTypes();
                var menu = new GenericMenu();
                for (int i = 0; i < _moduleTypes.Length; i++)
                {
                    var type = _moduleTypes[i];
                    if (existing.Contains(type))
                    {
                        menu.AddDisabledItem(new GUIContent(_moduleTypeNames[i]));
                    }
                    else
                    {
                        menu.AddItem(new GUIContent(_moduleTypeNames[i]), false, () =>
                        {
                            serializedObject.Update();
                            int idx = _modules.arraySize;
                            _modules.arraySize++;
                            var el = _modules.GetArrayElementAtIndex(idx);
                            el.managedReferenceValue = Activator.CreateInstance(type);
                            serializedObject.ApplyModifiedProperties();
                        });
                    }
                }
                menu.ShowAsContext();
            }
        }
    }
}
