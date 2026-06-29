using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.EQS.Editor
{
    [CustomEditor(typeof(EQSQueryConfig))]
    public class EQSQueryConfigEditor : UnityEditor.Editor
    {
        private SerializedProperty _generator = default!;
        private SerializedProperty _tests = default!;

        private static Type[]? _generatorTypes;
        private static string[]? _generatorTypeNames;
        private static Type[]? _testTypes;
        private static string[]? _testTypeNames;

        private bool _generatorFoldout = true;
        private readonly System.Collections.Generic.HashSet<int> _testFoldouts = new();

        private void OnEnable()
        {
            _generator = serializedObject.FindProperty("_generator");
            _tests = serializedObject.FindProperty("_tests");

            if (_generatorTypes == null)
                CacheTypes();
        }

        private static void CacheTypes()
        {
            _generatorTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .Where(t => !t.IsAbstract && !t.IsInterface && typeof(EQSGenerator).IsAssignableFrom(t))
                .OrderBy(t => t.Name)
                .ToArray();
            _generatorTypeNames = _generatorTypes.Select(t => t.Name).ToArray();

            _testTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .Where(t => !t.IsAbstract && !t.IsInterface && typeof(EQSTest).IsAssignableFrom(t))
                .OrderBy(t => t.Name)
                .ToArray();
            _testTypeNames = _testTypes.Select(t => t.Name).ToArray();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawGenerator();

            EditorGUILayout.Space(8);

            DrawTests();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawGenerator()
        {
            EditorGUILayout.LabelField("Generator", EditorStyles.boldLabel);

            var genObj = _generator.managedReferenceValue;

            if (genObj == null)
            {
                EditorGUILayout.HelpBox("No generator assigned.", MessageType.Warning);
            }
            else
            {
                string typeName = genObj.GetType().Name;
                _generatorFoldout = EditorGUILayout.Foldout(_generatorFoldout, typeName, true);

                if (_generatorFoldout)
                {
                    EditorGUI.indentLevel++;
                    DrawSerializedReferenceChildren(_generator);
                    EditorGUI.indentLevel--;
                }
            }

            if (GUILayout.Button("Change Generator"))
            {
                var menu = new GenericMenu();
                for (int i = 0; i < _generatorTypes!.Length; i++)
                {
                    var type = _generatorTypes[i];
                    menu.AddItem(new GUIContent(_generatorTypeNames![i]), false, () =>
                    {
                        serializedObject.Update();
                        _generator.managedReferenceValue = Activator.CreateInstance(type);
                        serializedObject.ApplyModifiedProperties();
                    });
                }

                menu.ShowAsContext();
            }
        }

        private void DrawTests()
        {
            EditorGUILayout.LabelField("Tests", EditorStyles.boldLabel);

            if (_tests.arraySize == 0)
                EditorGUILayout.HelpBox("No tests configured.", MessageType.Warning);

            for (int i = 0; i < _tests.arraySize; i++)
                DrawTest(i);

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Add Test"))
            {
                var menu = new GenericMenu();
                for (int i = 0; i < _testTypes!.Length; i++)
                {
                    var type = _testTypes[i];
                    menu.AddItem(new GUIContent(_testTypeNames![i]), false, () =>
                    {
                        serializedObject.Update();
                        int idx = _tests.arraySize;
                        _tests.arraySize++;
                        var el = _tests.GetArrayElementAtIndex(idx);
                        el.managedReferenceValue = Activator.CreateInstance(type);
                        serializedObject.ApplyModifiedProperties();
                    });
                }

                menu.ShowAsContext();
            }
        }

        private void DrawTest(int index)
        {
            var element = _tests.GetArrayElementAtIndex(index);
            var obj = element.managedReferenceValue;
            string typeName = obj != null ? obj.GetType().Name : "(null)";
            bool expanded = _testFoldouts.Contains(index);

            EditorGUILayout.BeginHorizontal();

            expanded = EditorGUILayout.Foldout(expanded, typeName, true);
            if (expanded) _testFoldouts.Add(index); else _testFoldouts.Remove(index);

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(index == 0))
            {
                if (GUILayout.Button("\u25b2", GUILayout.Width(24)))
                {
                    _tests.MoveArrayElement(index, index - 1);
                    EditorGUILayout.EndHorizontal();
                    return;
                }
            }

            using (new EditorGUI.DisabledScope(index == _tests.arraySize - 1))
            {
                if (GUILayout.Button("\u25bc", GUILayout.Width(24)))
                {
                    _tests.MoveArrayElement(index, index + 1);
                    EditorGUILayout.EndHorizontal();
                    return;
                }
            }

            if (GUILayout.Button("\u2212", GUILayout.Width(24)))
            {
                _tests.DeleteArrayElementAtIndex(index);
                _testFoldouts.Remove(index);
                EditorGUILayout.EndHorizontal();
                return;
            }

            EditorGUILayout.EndHorizontal();

            if (expanded && obj != null)
            {
                EditorGUI.indentLevel++;
                DrawSerializedReferenceChildren(element);
                EditorGUI.indentLevel--;
            }
        }

        private static void DrawSerializedReferenceChildren(SerializedProperty property)
        {
            var end = property.GetEndProperty();
            var child = property.Copy();
            bool enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                EditorGUILayout.PropertyField(child, true);
            }
        }
    }
}
