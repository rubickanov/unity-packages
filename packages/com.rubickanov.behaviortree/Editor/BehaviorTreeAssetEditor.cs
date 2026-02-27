using Rubickanov.BehaviorTree.Runtime;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BehaviorTreeAsset))]
public class BehaviorTreeAssetEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("Open in Editor", GUILayout.Height(30)))
            BehaviorTreeEditorWindow.OpenAsset((BehaviorTreeAsset)target);
    }
}
