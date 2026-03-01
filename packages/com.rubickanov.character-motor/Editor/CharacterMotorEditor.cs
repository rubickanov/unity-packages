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

            SortModulesByPriority();

            for (int i = 0; i < _modules.arraySize; i++)
                DrawModule(i);

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

        private void SortModulesByPriority()
        {
            // Collect (index, priority) pairs
            var items = new List<(int index, int priority)>();
            for (int i = 0; i < _modules.arraySize; i++)
            {
                var obj = _modules.GetArrayElementAtIndex(i).managedReferenceValue;
                int p = obj is IMotorModule m ? m.Priority : 0;
                items.Add((i, p));
            }

            // Check if already sorted
            bool sorted = true;
            for (int i = 1; i < items.Count; i++)
            {
                if (items[i].priority < items[i - 1].priority)
                {
                    sorted = false;
                    break;
                }
            }
            if (sorted) return;

            // Rebuild sorted — extract values, clear, re-insert
            var objects = new List<object>();
            for (int i = 0; i < _modules.arraySize; i++)
                objects.Add(_modules.GetArrayElementAtIndex(i).managedReferenceValue);

            objects.Sort((a, b) =>
            {
                int pa = a is IMotorModule ma ? ma.Priority : 0;
                int pb = b is IMotorModule mb ? mb.Priority : 0;
                return pa.CompareTo(pb);
            });

            for (int i = 0; i < _modules.arraySize; i++)
                _modules.GetArrayElementAtIndex(i).managedReferenceValue = objects[i];

            _foldouts.Clear();
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
