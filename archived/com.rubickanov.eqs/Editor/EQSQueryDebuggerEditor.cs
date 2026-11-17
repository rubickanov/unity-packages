using UnityEditor;
using UnityEngine;

namespace Rubickanov.EQS.Editor
{
    [CustomEditor(typeof(EQSQueryDebugger))]
    public class EQSQueryDebuggerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var debugger = (EQSQueryDebugger)target;

            EditorGUILayout.Space(8);

            if (GUILayout.Button("Run Query"))
            {
                debugger.RunQuery();
                SceneView.RepaintAll();
            }

            var result = debugger.LastResult;
            EditorGUILayout.LabelField("Generated Items", debugger.GeneratedCount.ToString());
            EditorGUILayout.LabelField("Scored Items", result.Items?.Count.ToString() ?? "0");

            if (result.TryGetBest(out var best))
            {
                EditorGUILayout.LabelField("Best Score", best.Score.ToString("F3"));
                EditorGUILayout.LabelField("Best Position", best.Position.ToString("F2"));
            }
        }
    }
}
